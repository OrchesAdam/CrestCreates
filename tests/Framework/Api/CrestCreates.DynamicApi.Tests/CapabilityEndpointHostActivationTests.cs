using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

#pragma warning disable CC1001 // RefValidation: descriptor not registered — tests use mock registries

/// <summary>
/// Contract tests for the per-host ExplicitHost activation surface
/// (<see cref="CapabilityEndpointHostActivation"/>, <see cref="CapabilityEndpointContribution"/>,
/// <see cref="CapabilityEndpointRegistryBootstrapper"/> and the contribution DI entry point).
/// Contributions are hand-built here because the generated wiring is a later step.
/// </summary>
[Collection("DynamicApiStaticState")]
public sealed class CapabilityEndpointHostActivationTests : IDisposable
{
    public CapabilityEndpointHostActivationTests()
    {
        ResetStaticState();
    }

    public void Dispose()
    {
        ResetStaticState();
    }

    private static void ResetStaticState()
    {
        CapabilityEndpointBindingRegistry.Reset();
        CapabilityEndpointResultContractRegistration.Reset();
        CapabilityHandlerResolverProvider.Reset();
        CapabilityEndpointJsonContractRegistry.Reset();
        ClearDescriptorProviderRegistry();
    }

    /// <summary>
    /// Clears the static DescriptorProviderRegistry between tests to ensure isolation.
    /// </summary>
    private static void ClearDescriptorProviderRegistry()
    {
        var field = typeof(DescriptorProviderRegistry).GetField("_providers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var providers = (ConcurrentBag<object>)field!.GetValue(null)!;
        providers.Clear();
    }

    [Fact]
    public void HostActivation_AnswersBodyTypes_WithLegacyFallback()
    {
        // Arrange
        var capRegistryMock = CreateCapabilityRegistryMock(CreateCapability());
        CapabilityEndpointJsonContractRegistry.RegisterBodyType(typeof(LegacyBodyTypeMarker));

        var services = new ServiceCollection();
        services.AddSingleton(capRegistryMock.Object);
        services.AddCrestCapabilityEndpoints();
        services.AddCrestCapabilityEndpointContribution(new BodyTypeContribution
        {
            OwnerAssemblyName = "HostA.Generated",
            BodyTypes = [typeof(ContributedBodyTypeMarker)]
        });

        var sp = services.BuildServiceProvider();

        // Act
        var activation = sp.GetRequiredService<CapabilityEndpointHostActivation>();
        var bodyTypes = activation.GetBodyTypes();

        // Assert — host contribution first, legacy static registry as fallback
        bodyTypes.Should().Contain(typeof(ContributedBodyTypeMarker))
            .And.Contain(typeof(LegacyBodyTypeMarker));
    }

    [Fact]
    public void Map_ContributedEndpoint_IsHostIsolated_AndLeavesProcessStaticsUntouched()
    {
        // Arrange
        var capability = CreateCapability();
        var capRegistryMock = CreateCapabilityRegistryMock(capability);
        var dispatcherMock = CreateDispatcherMock();

        var contribution = CreateContribution<HostAContribution>(
            ownerAssembly: "HostA.Generated",
            descriptor: CreateEndpoint("ep-contributed", route: "/api/v1/host-a"),
            binding: CreateBinding("ep-contributed", "from-host-a-binding"),
            resultMarker: "host-a");

        var (hostA, endpointsA) = BuildHost(capRegistryMock, dispatcherMock, contribution);
        var (hostB, endpointsB) = BuildHost(capRegistryMock, dispatcherMock);

        // Act — host A maps the contributed endpoint, host B has no contribution
        endpointsA.MapCrestCapabilityEndpoints();
        endpointsB.MapCrestCapabilityEndpoints();

        // Assert — host A mapped exactly the contributed endpoint
        var endpointA = GetEndpoints(endpointsA).Should().ContainSingle().Subject;
        endpointA.RoutePattern.RawText.Should().Be("/api/v1/host-a");

        // host B neither maps nor validates the contribution (per-host isolation)
        endpointsB.DataSources.Should().BeEmpty();
        hostB.GetRequiredService<ICapabilityEndpointRegistry>()
            .GetById("ep-contributed").Should().BeNull();

        // The DI-contributed path never writes to process-global static registries
        CapabilityEndpointBindingRegistry.Find("ep-contributed", 1).Should().BeNull();
        hostB.GetRequiredService<ICapabilityEndpointResultContractRegistry>()
            .TryGetResultMapper("ep-contributed", 1).Should().BeNull();

        // Act — invoke host A's contributed endpoint
        var (context, body) = Invoke(hostA, endpointsA, "/api/v1/host-a");

        // Assert — the contributed binding and result mapper were used
        context.Response.StatusCode.Should().Be(200);
        body.Should().Contain("host-a");
        dispatcherMock.Verify(
            d => d.DispatchAsync(
                It.Is<CapabilityDescriptor>(c => c.Id == "test-cap"),
                InvocationSource.Http,
                It.Is<object?>(input => MatchPayload(input, "from-host-a-binding")),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void AddCrestCapabilityEndpointContribution_IsIdempotent_PerContributionType()
    {
        var services = new ServiceCollection();

        services.AddCrestCapabilityEndpointContribution(new IdempotentContribution());
        services.AddCrestCapabilityEndpointContribution(new IdempotentContribution());

        services.Where(d => d.ServiceType == typeof(CapabilityEndpointContribution))
            .Should().ContainSingle(
                "repeated Add* calls for the same generated contribution type must be idempotent");
    }

    [Fact]
    public void RequiredIdentityMissing_FailsDeterministically_WithOwnerActionType()
    {
        // Arrange
        var capability = CreateCapability();
        var siblingCapability = CreateCapability("sibling-cap");
        var capRegistryMock = CreateCapabilityRegistryMock(capability, siblingCapability);
        var dispatcherMock = CreateDispatcherMock();

        var contribution = CreateContribution<LibraryContribution>(
            ownerAssembly: "LibraryFeature.Generated",
            descriptor: CreateEndpoint("ep-a", route: "/api/v1/ep-a"),
            binding: CreateBinding("ep-a"),
            resultMarker: "ep-a",
            requiredIdentities:
            [
                new CapabilityEndpointRequiredIdentity(
                    IdentityId: "ep-sibling",
                    Owner: "PaymentFeature.Api",
                    Action: "Create",
                    TypeName: "Book")
            ]);

        var (hostA, endpointsA) = BuildHost(capRegistryMock, dispatcherMock, contribution);

        // Act
        var act = () => endpointsA.MapCrestCapabilityEndpoints();

        // Assert — deterministic failure naming owner, action and type, and nothing was mapped
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ep-sibling*")
            .WithMessage("*PaymentFeature.Api*")
            .WithMessage("*Create*")
            .WithMessage("*Book*")
            .WithMessage("*LibraryFeature.Generated*");
        endpointsA.DataSources.Should().BeEmpty();

        // Act — second attempt on a fresh host after the missing contribution is added
        var siblingContribution = CreateContribution<PaymentContribution>(
            ownerAssembly: "PaymentFeature.Generated",
            descriptor: CreateEndpoint(
                "ep-sibling", capabilityId: "sibling-cap", route: "/api/v1/ep-sibling"),
            binding: CreateBinding("ep-sibling"),
            resultMarker: "ep-sibling");

        var (hostB, endpointsB) = BuildHost(
            capRegistryMock, dispatcherMock, contribution, siblingContribution);

        var retry = () => endpointsB.MapCrestCapabilityEndpoints();

        // Assert — no permanent poisoning; both endpoints are mapped
        retry.Should().NotThrow();
        GetEndpoints(endpointsB).Select(e => e.RoutePattern.RawText)
            .Should().BeEquivalentTo("/api/v1/ep-a", "/api/v1/ep-sibling");
        hostB.GetRequiredService<ICapabilityEndpointRegistry>()
            .GetById("ep-sibling").Should().NotBeNull();
    }

    [Fact]
    public void ContributedCapability_MissingFromAuthoritativeRegistry_FailsClosed()
    {
        // #129 S2.3: a host that activates a generated contribution but composes its capability
        // registry without that contribution must fail before mapping, never drop the endpoints.
        var contributed = CreateCapability("contributed-cap");
        var contribution = new MissingCapabilityContribution
        {
            OwnerAssemblyName = "MissingCapability.Generated",
            CapabilityDescriptors = [contributed],
            EndpointDescriptors =
            [
                CreateEndpoint("ep-missing-cap", capabilityId: contributed.Id, route: "/api/v1/missing-cap")
            ],
            Bindings = [CreateBinding("ep-missing-cap")],
            Handlers = [CreateHandler(contributed.Id)],
            ResultMappers = [CreateResultBinding("ep-missing-cap", "missing-cap")],
        };
        var (_, endpoints) = BuildHost(CreateCapabilityRegistryMock(), CreateDispatcherMock(), contribution);

        var act = () => endpoints.MapCrestCapabilityEndpoints();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*contributed-cap*")
            .WithMessage("*authoritative registry*");
        endpoints.DataSources.Should().BeEmpty();
    }

    [Fact]
    public void SecondMapCall_DoesNotDoubleMap()
    {
        // Arrange
        var capability = CreateCapability();
        var capRegistryMock = CreateCapabilityRegistryMock(capability);
        var dispatcherMock = CreateDispatcherMock();

        var contribution = CreateContribution<SingleMapContribution>(
            ownerAssembly: "HostA.Generated",
            descriptor: CreateEndpoint("ep-once", route: "/api/v1/once"),
            binding: CreateBinding("ep-once"),
            resultMarker: "once");

        var (_, endpoints) = BuildHost(capRegistryMock, dispatcherMock, contribution);

        // Act
        endpoints.MapCrestCapabilityEndpoints();
        endpoints.MapCrestCapabilityEndpoints();

        // Assert — the route table stays stable; repeated maps are idempotent by (endpointId, version)
        GetEndpoints(endpoints).Should().ContainSingle();
        endpoints.DataSources.Should().ContainSingle();
    }

    [Fact]
    public void ContributedEndpoint_WithoutResultMapper_FailsClosed()
    {
        // Arrange
        var capability = CreateCapability();
        var capRegistryMock = CreateCapabilityRegistryMock(capability);
        var dispatcherMock = CreateDispatcherMock();

        var descriptor = CreateEndpoint("ep-no-mapper", route: "/api/v1/no-mapper");
        var contribution = new NoMapperContribution
        {
            OwnerAssemblyName = "Incomplete.Generated",
            EndpointDescriptors = [descriptor],
            Bindings = [CreateBinding("ep-no-mapper")],
            Handlers = [CreateHandler("test-cap")]
            // ResultMappers intentionally missing — fail closed, no generic output fallback
        };

        var (_, endpoints) = BuildHost(capRegistryMock, dispatcherMock, contribution);

        // Act
        var act = () => endpoints.MapCrestCapabilityEndpoints();

        // Assert — deterministic rejection naming the endpoint and the owning assembly
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ep-no-mapper*")
            .WithMessage("*Incomplete.Generated*")
            .WithMessage("*result mapper*");
        endpoints.DataSources.Should().BeEmpty();
    }

    [Fact]
    public void ContributedEndpoint_WithoutHandler_FailsClosed()
    {
        // Arrange
        var capability = CreateCapability();
        var capRegistryMock = CreateCapabilityRegistryMock(capability);
        var dispatcherMock = CreateDispatcherMock();

        var descriptor = CreateEndpoint("ep-no-handler", route: "/api/v1/no-handler");
        var contribution = new NoHandlerContribution
        {
            OwnerAssemblyName = "Incomplete.Generated",
            EndpointDescriptors = [descriptor],
            Bindings = [CreateBinding("ep-no-handler")],
            ResultMappers = [CreateResultBinding("ep-no-handler", "no-handler")]
            // Handlers intentionally missing — the invoker is a required per-action contract
        };

        var (_, endpoints) = BuildHost(capRegistryMock, dispatcherMock, contribution);

        // Act
        var act = () => endpoints.MapCrestCapabilityEndpoints();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ep-no-handler*")
            .WithMessage("*test-cap*")
            .WithMessage("*handler invoker*");
        endpoints.DataSources.Should().BeEmpty();
    }

    [Fact]
    public void FailedRegistryBuild_IsNotCached_LaterAttemptRetries()
    {
        // Arrange — a broken legacy static provider poisons the first build
        var capability = CreateCapability();
        var capRegistryMock = CreateCapabilityRegistryMock(capability);
        var dispatcherMock = CreateDispatcherMock();

        var badDescriptor = CreateEndpoint(
            "ep-bad", capabilityId: "missing-cap", route: "/api/v1/bad");
        RegisterStaticProvider(badDescriptor);

        var contribution = CreateContribution<RetryContribution>(
            ownerAssembly: "HostA.Generated",
            descriptor: CreateEndpoint("ep-good", route: "/api/v1/good"),
            binding: CreateBinding("ep-good"),
            resultMarker: "good");

        var (sp, endpoints) = BuildHost(capRegistryMock, dispatcherMock, contribution);
        var registry = (CapabilityEndpointRegistry)sp.GetRequiredService<ICapabilityEndpointRegistry>();

        // Act — first attempt fails the authoritative build
        var firstAttempt = () => endpoints.MapCrestCapabilityEndpoints();

        // Assert — failure is deterministic and the registry recorded the failed attempt
        firstAttempt.Should().Throw<RegistryValidationException>()
            .WithMessage("*missing-cap*");
        registry.State.Should().Be(RegistryState.Failed);

        // Act — repair the static input, retry on the same host
        ClearDescriptorProviderRegistry();
        var retry = () => endpoints.MapCrestCapabilityEndpoints();

        // Assert — a failed build is not cached as permanently failed; the host recovers
        retry.Should().NotThrow();
        registry.State.Should().Be(RegistryState.Built);
        GetEndpoints(endpoints).Should().ContainSingle()
            .Which.RoutePattern.RawText.Should().Be("/api/v1/good");
    }

    [Fact]
    public void HostContribution_TakesPrecedence_OverLegacyStaticSources()
    {
        // Arrange — the legacy static provider keeps the descriptor, the host contribution owns the binding
        var capability = CreateCapability();
        var capRegistryMock = CreateCapabilityRegistryMock(capability);
        var dispatcherMock = CreateDispatcherMock();

        RegisterStaticProvider(CreateEndpoint("ep-legacy", route: "/api/v1/legacy"));

        var contribution = new LegacyBindingContribution
        {
            OwnerAssemblyName = "HostA.Generated",
            Bindings = [CreateBinding("ep-legacy", "from-host-contribution")],
            ResultMappers = [CreateResultBinding("ep-legacy", "host-precedence")]
        };

        var (sp, endpoints) = BuildHost(capRegistryMock, dispatcherMock, contribution);

        endpoints.MapCrestCapabilityEndpoints();

        // Act
        var (context, body) = Invoke(sp, endpoints, "/api/v1/legacy");

        // Assert — the host contribution's binding and mapper won over the legacy sources
        context.Response.StatusCode.Should().Be(200);
        body.Should().Contain("host-precedence");
        dispatcherMock.Verify(
            d => d.DispatchAsync(
                It.IsAny<CapabilityDescriptor>(),
                InvocationSource.Http,
                It.Is<object?>(input => MatchPayload(input, "from-host-contribution")),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────
    //  Contribution/build helpers (simulating generated code)
    // ─────────────────────────────

    private static CapabilityDescriptor CreateCapability(string id = "test-cap")
        => new()
        {
            Id = id,
            Name = $"Capability {id}",
            Version = 1,
            State = DescriptorState.Active
        };

    private static CapabilityEndpointDescriptor CreateEndpoint(
        string id,
        string capabilityId = "test-cap",
        string route = "/api/v1/contributed",
        CapabilityEndpointHttpMethod httpMethod = CapabilityEndpointHttpMethod.Post)
        => new()
        {
            Id = id,
            Name = id,
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, 1),
            HttpMethod = httpMethod,
            RoutePattern = route,
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

    private static TContribution CreateContribution<TContribution>(
        string ownerAssembly,
        CapabilityEndpointDescriptor descriptor,
        CapabilityEndpointBindingContract binding,
        string resultMarker,
        CapabilityEndpointRequiredIdentity[]? requiredIdentities = null)
        where TContribution : CapabilityEndpointContribution, new()
        => new()
        {
            OwnerAssemblyName = ownerAssembly,
            EndpointDescriptors = [descriptor],
            Bindings = [binding],
            Handlers = [CreateHandler(descriptor.Capability.Id)],
            ResultMappers = [CreateResultBinding(descriptor.Id, resultMarker)],
            RequiredIdentityIds = requiredIdentities ?? []
        };

    private static CapabilityEndpointBindingContract CreateBinding(
        string endpointId,
        string payload = "contributed-input")
        => new(endpointId, 1, (ctx, ct) => ValueTask.FromResult<object?>(new { payload }));

    private static CapabilityEndpointResultBinding CreateResultBinding(string endpointId, string marker)
        => new(endpointId, 1, (ctx, _) => Results.Ok(new { code = 200, marker, data = ctx.Output }));

    private static CapabilityEndpointHandlerBinding CreateHandler(string capabilityId)
        => new(capabilityId, new TestHandlerInvoker());

    private static Mock<ICapabilityRegistry> CreateCapabilityRegistryMock(
        params CapabilityDescriptor[] capabilities)
    {
        var mock = new Mock<ICapabilityRegistry>();
        foreach (var capability in capabilities)
        {
            mock.Setup(r => r.GetByVersion(capability.Id, capability.Version)).Returns(capability);
            mock.Setup(r => r.GetById(capability.Id)).Returns(capability);
        }

        mock.Setup(r => r.GetAll()).Returns(capabilities);
        return mock;
    }

    private static Mock<ICapabilityDispatcher> CreateDispatcherMock()
    {
        var mock = new Mock<ICapabilityDispatcher>();
        mock.Setup(d => d.DispatchAsync(
                It.IsAny<CapabilityDescriptor>(),
                It.IsAny<InvocationSource>(),
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Success(
                new { name = "dispatched" }, TimeSpan.FromMilliseconds(1)));
        return mock;
    }

    private static void RegisterStaticProvider(params CapabilityEndpointDescriptor[] descriptors)
    {
        var mock = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        mock.Setup(p => p.GetDescriptors()).Returns(descriptors);
        DescriptorProviderRegistry.Register(mock.Object);
    }

    private static (IServiceProvider ServiceProvider, IEndpointRouteBuilder Endpoints) BuildHost(
        Mock<ICapabilityRegistry> capabilityRegistry,
        Mock<ICapabilityDispatcher> dispatcher,
        params CapabilityEndpointContribution[] contributions)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(capabilityRegistry.Object);
        services.AddSingleton(dispatcher.Object);
        services.AddCrestCapabilityEndpoints();

        foreach (var contribution in contributions)
        {
            services.AddCrestCapabilityEndpointContribution(contribution);
        }

        var sp = services.BuildServiceProvider();
        return (sp, new DefaultEndpointRouteBuilder(sp));
    }

    private static IReadOnlyList<RouteEndpoint> GetEndpoints(IEndpointRouteBuilder endpoints)
        => endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();

    private static (HttpContext Context, string Body) Invoke(
        IServiceProvider sp,
        IEndpointRouteBuilder endpoints,
        string routePattern)
    {
        var routeEndpoint = GetEndpoints(endpoints)
            .Should().ContainSingle(e => e.RoutePattern.RawText == routePattern).Subject;

        var responseBodyStream = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
            RequestServices = sp,
            Request =
            {
                Method = "POST",
                Path = routePattern,
                ContentType = "application/json",
                Body = Stream.Null
            },
            Response =
            {
                Body = responseBodyStream
            }
        };

        routeEndpoint.RequestDelegate!(httpContext).GetAwaiter().GetResult();

        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var body = new StreamReader(responseBodyStream).ReadToEnd();

        return (httpContext, body);
    }

    private static bool MatchPayload(object? input, string expected)
    {
        if (input is null) return false;
        var prop = input.GetType().GetProperty("payload");
        if (prop is null) return false;
        var value = prop.GetValue(input);
        if (value is null) return false;
        return value.ToString() == expected;
    }

    private sealed class TestHandlerInvoker : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(new { handled = true });
    }

    // Hand-built contributions simulating the generated per-assembly contribution classes.
    // TryAddEnumerable de-duplicates by implementation type, so each host-local contribution
    // needs its own concrete type.
    private sealed class IdempotentContribution : CapabilityEndpointContribution;

    private sealed class HostAContribution : CapabilityEndpointContribution;

    private sealed class LibraryContribution : CapabilityEndpointContribution;

    private sealed class PaymentContribution : CapabilityEndpointContribution;

    private sealed class SingleMapContribution : CapabilityEndpointContribution;

    private sealed class RetryContribution : CapabilityEndpointContribution;

    private sealed class LegacyBindingContribution : CapabilityEndpointContribution;

    private sealed class NoMapperContribution : CapabilityEndpointContribution;

    private sealed class NoHandlerContribution : CapabilityEndpointContribution;

    private sealed class BodyTypeContribution : CapabilityEndpointContribution;

    private sealed class MissingCapabilityContribution : CapabilityEndpointContribution;

    private sealed class ContributedBodyTypeMarker;

    private sealed class LegacyBodyTypeMarker;

    /// <summary>
    /// Minimal IEndpointRouteBuilder implementation for testing.
    /// Does not provide real HTTP routing — only captures data sources.
    /// </summary>
    private sealed class DefaultEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public DefaultEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            DataSources = new List<EndpointDataSource>();
        }

        public IServiceProvider ServiceProvider { get; }

        public ICollection<EndpointDataSource> DataSources { get; }

        public IApplicationBuilder CreateApplicationBuilder()
        {
            throw new NotSupportedException();
        }
    }
}

#pragma warning restore CC1001
