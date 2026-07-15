using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

[CollectionDefinition("DynamicApiStaticState")]
public sealed class DynamicApiStaticStateCollection;

#pragma warning disable CC1001 // RefValidation: descriptor not registered — tests use mock registries

[Collection("DynamicApiStaticState")]
public sealed class CapabilityEndpointIntegrationTests : IDisposable
{
    public CapabilityEndpointIntegrationTests()
    {
        CapabilityEndpointBindingRegistry.Reset();
        CapabilityEndpointResultContractRegistration.Reset();
        ClearDescriptorProviderRegistry();
    }

    public void Dispose()
    {
        CapabilityEndpointBindingRegistry.Reset();
        CapabilityEndpointResultContractRegistration.Reset();
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

    /// <summary>
    /// Creates a simple IDescriptorProvider that returns the given descriptors.
    /// </summary>
    private static IDescriptorProvider<CapabilityEndpointDescriptor> CreateProvider(
        params CapabilityEndpointDescriptor[] descriptors)
    {
        var mock = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        mock.Setup(p => p.GetDescriptors()).Returns(descriptors);
        return mock.Object;
    }

    /// <summary>
    /// Creates a ServiceCollection with the minimum services needed, registers mocks,
    /// calls AddCrestCapabilityEndpoints, and builds the provider.
    /// </summary>
    private static (IServiceProvider ServiceProvider, Mock<ICapabilityDispatcher> DispatcherMock)
        BuildServices(Mock<ICapabilityRegistry> capRegistryMock)
    {
        var dispatcherMock = new Mock<ICapabilityDispatcher>();

        var services = new ServiceCollection();
        services.AddSingleton(capRegistryMock.Object);
        services.AddSingleton(dispatcherMock.Object);
        services.AddCrestCapabilityEndpoints();

        return (services.BuildServiceProvider(), dispatcherMock);
    }

    [Fact]
    public void MapCrestCapabilityEndpoints_MapsActiveEndpoints()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-1",
            Name = "Test Capability",
            Version = 1,
            State = DescriptorState.Active,
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-1",
            Name = "Test EP 1",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-1", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/test"
        };

        // Register provider in the static descriptor registry
        var provider = CreateProvider(endpointDescriptor);
        DescriptorProviderRegistry.Register(provider);

        // Register binding contract
        var binding = new CapabilityEndpointBindingContract(
            EndpointId: "ep-1",
            EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null));
        CapabilityEndpointBindingRegistry.Register(binding);

        // Mock capability registry to return the referenced capability
        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-1", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-1")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var (sp, _) = BuildServices(capRegistryMock);

        var endpoints = new DefaultEndpointRouteBuilder(sp);

        // Act
        endpoints.MapCrestCapabilityEndpoints();

        // Assert
        endpoints.DataSources.Should().HaveCount(1);
    }

    [Fact]
    public void MapCrestCapabilityEndpoints_SkipsInactiveDescriptors()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-2",
            Name = "Test Capability 2",
            Version = 1,
            State = DescriptorState.Active
        };

        var activeDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-active",
            Name = "Active EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-2", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/v1/active"
        };

        var inactiveDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-draft",
            Name = "Draft EP",
            Version = 1,
            State = DescriptorState.Draft,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-2", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/v1/draft"
        };

        // Register both as providers
        var provider1 = CreateProvider(activeDescriptor);
        var provider2 = CreateProvider(inactiveDescriptor);
        DescriptorProviderRegistry.Register(provider1);
        DescriptorProviderRegistry.Register(provider2);

        // Register binding for the active one (inactive also registered to show it's the state filter, not binding)
        var binding = new CapabilityEndpointBindingContract(
            EndpointId: "ep-active",
            EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null));
        CapabilityEndpointBindingRegistry.Register(binding);

        // Simulate having both in the registry but only active ones mapped
        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-2", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-2")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var (sp, _) = BuildServices(capRegistryMock);

        var endpoints = new DefaultEndpointRouteBuilder(sp);

        // Act
        endpoints.MapCrestCapabilityEndpoints();

        // Assert: only the active descriptor should be mapped
        endpoints.DataSources.Should().HaveCount(1);
    }

    [Fact]
    public void MapCrestCapabilityEndpoints_FailsClosed_WhenBindingMissing()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-3",
            Name = "Test Capability 3",
            Version = 1,
            State = DescriptorState.Active
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-no-binding",
            Name = "No Binding EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-3", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/no-binding"
        };

        var provider = CreateProvider(endpointDescriptor);
        DescriptorProviderRegistry.Register(provider);

        // Intentionally do NOT register a binding — fails-closed

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-3", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-3")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var (sp, _) = BuildServices(capRegistryMock);

        var endpoints = new DefaultEndpointRouteBuilder(sp);

        // Act
        var act = () => endpoints.MapCrestCapabilityEndpoints();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ep-no-binding*1*");
    }

    /// <summary>
    /// Verifies the pipeline fails closed when the referenced capability does not exist
    /// in the ICapabilityRegistry. The validator catches this during EnsureBuilt(),
    /// throwing RegistryValidationException before the mapping phase even begins.
    /// </summary>
    [Fact]
    public void MapCrestCapabilityEndpoints_FailsClosed_WhenCapabilityNotFound()
    {
        // Arrange
        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-no-cap",
            Name = "No Cap EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("missing-cap", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/no-cap"
        };

        var provider = CreateProvider(endpointDescriptor);
        DescriptorProviderRegistry.Register(provider);

        // Register binding even though capability is missing — test that validation fails first
        var binding = new CapabilityEndpointBindingContract(
            EndpointId: "ep-no-cap",
            EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null));
        CapabilityEndpointBindingRegistry.Register(binding);

        // Capability registry returns null/empty for all resolution paths — validator catches this
        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((CapabilityDescriptor?)null);
        capRegistryMock.Setup(r => r.GetById(It.IsAny<string>()))
            .Returns((CapabilityDescriptor?)null);
        capRegistryMock.Setup(r => r.GetAll())
            .Returns(Array.Empty<CapabilityDescriptor>());

        var (sp, _) = BuildServices(capRegistryMock);

        var endpoints = new DefaultEndpointRouteBuilder(sp);

        // Act
        var act = () => endpoints.MapCrestCapabilityEndpoints();

        // Assert — validator catches missing capability during EnsureBuilt and fails closed
        act.Should().Throw<RegistryValidationException>()
            .WithMessage("*missing-cap*v1*");
    }

    [Fact]
    public void AddCrestCapabilityEndpoints_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // We also need ICapabilityRegistry for the validator to resolve
        var capRegistryMock = new Mock<ICapabilityRegistry>();
        services.AddSingleton(capRegistryMock.Object);

        // Act
        services.AddCrestCapabilityEndpoints();
        var sp = services.BuildServiceProvider();

        // Assert — core services should be registered
        sp.GetService<ICapabilityEndpointRegistry>().Should().NotBeNull();
        sp.GetService<IRegistryValidationEngine<CapabilityEndpointDescriptor>>().Should().NotBeNull();
        sp.GetService<CapabilityEndpointRegistryBootstrapper>().Should().NotBeNull();
        sp.GetService<CapabilityEndpointOptions>().Should().NotBeNull();

        // Multi-registration services
        var validators = sp.GetServices<IRegistryValidator<CapabilityEndpointDescriptor>>().ToList();
        validators.Should().ContainSingle()
            .Which.Should().BeOfType<CapabilityEndpointDescriptorValidator>();

        var extractors = sp.GetServices<IDescriptorRelationshipExtractor>().ToList();
        extractors.Should().ContainSingle()
            .Which.Should().BeOfType<CapabilityEndpointRelationshipExtractor>();
    }

    [Fact]
    public async Task Endpoint_Delegate_InvokesDispatcher()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-delegate",
            Name = "Delegate Test Cap",
            Version = 1,
            State = DescriptorState.Active
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-delegate",
            Name = "Delegate EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-delegate", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/delegate"
        };

        var provider = CreateProvider(endpointDescriptor);
        DescriptorProviderRegistry.Register(provider);

        var binding = new CapabilityEndpointBindingContract(
            EndpointId: "ep-delegate",
            EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(new { payload = "test-input" }));
        CapabilityEndpointBindingRegistry.Register(binding);

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-delegate", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-delegate")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var dispatcherMock = new Mock<ICapabilityDispatcher>();
        dispatcherMock
            .Setup(d => d.DispatchAsync(
                capability,
                InvocationSource.Http,
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Success(
                new { result = "ok" }, TimeSpan.FromMilliseconds(10)));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(capRegistryMock.Object);
        services.AddSingleton(dispatcherMock.Object);
        services.AddCrestCapabilityEndpoints();
        var sp = services.BuildServiceProvider();

        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();

        // Get the mapped route endpoint
        var dataSource = endpoints.DataSources.Should().ContainSingle().Subject;
        var routeEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Should().ContainSingle().Subject;

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-123",
            RequestServices = sp
        };

        // Act — invoke the endpoint delegate directly (bypassing routing)
        await routeEndpoint.RequestDelegate!(httpContext);

        // Assert — dispatcher was invoked with the correct parameters
        dispatcherMock.Verify(
            d => d.DispatchAsync(
                capability,
                InvocationSource.Http,
                It.Is<object?>(input =>
                    input != null &&
                    MatchPayload(input, "test-input")),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                httpContext.RequestAborted),
            Times.Once);
    }

    [Fact]
    public async Task Endpoint_WithResultContract_ReturnsCustomResult()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-rc",
            Name = "RC Cap",
            Version = 1,
            State = DescriptorState.Active
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-rc",
            Name = "RC EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-rc", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/rc"
        };

        // Register provider + binding
        var providerMock = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        providerMock.Setup(p => p.GetDescriptors()).Returns(new[] { endpointDescriptor });
        DescriptorProviderRegistry.Register(providerMock.Object);
        CapabilityEndpointBindingRegistry.Register(new CapabilityEndpointBindingContract(
            EndpointId: "ep-rc", EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null)));

        // Register a result contract that returns a custom envelope
        CapabilityEndpointResultContractRegistration.Register("ep-rc", 1, (ctx, httpContext) =>
        {
            // Simulate legacy DynamicApiResponse wrapping
            return Results.Ok(new { code = 200, message = "操作成功", data = ctx.Output });
        });

        // Mock dispatcher to return success
        var dispatcherMock = new Mock<ICapabilityDispatcher>();
        dispatcherMock
            .Setup(d => d.DispatchAsync(
                capability,
                InvocationSource.Http,
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Success(
                new { name = "test" }, TimeSpan.FromMilliseconds(5)));

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-rc", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-rc")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(capRegistryMock.Object);
        services.AddSingleton(dispatcherMock.Object);
        services.AddCrestCapabilityEndpoints();
        var sp = services.BuildServiceProvider();

        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();

        var dataSource = endpoints.DataSources.Should().ContainSingle().Subject;
        var routeEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Should().ContainSingle().Subject;

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-rc",
            RequestServices = sp
        };

        // Act
        await routeEndpoint.RequestDelegate!(httpContext);

        // Assert — the custom result contract was used (status code 200)
        httpContext.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Endpoint_WithoutResultContract_UsesDefaultMapper()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-default",
            Name = "Default Map Cap",
            Version = 1,
            State = DescriptorState.Active
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-default",
            Name = "Default EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-default", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/default"
        };

        // Register provider + binding — but NO result contract registration
        var providerMock2 = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        providerMock2.Setup(p => p.GetDescriptors()).Returns(new[] { endpointDescriptor });
        DescriptorProviderRegistry.Register(providerMock2.Object);
        CapabilityEndpointBindingRegistry.Register(new CapabilityEndpointBindingContract(
            EndpointId: "ep-default", EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null)));

        // Mock dispatcher to return success
        var expectedOutput = new { name = "default-out" };
        var dispatcherMock = new Mock<ICapabilityDispatcher>();
        dispatcherMock
            .Setup(d => d.DispatchAsync(
                capability,
                InvocationSource.Http,
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Success(
                expectedOutput, TimeSpan.FromMilliseconds(5)));

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-default", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-default")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(capRegistryMock.Object);
        services.AddSingleton(dispatcherMock.Object);
        services.AddCrestCapabilityEndpoints();
        var sp = services.BuildServiceProvider();

        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();

        var dataSource = endpoints.DataSources.Should().ContainSingle().Subject;
        var routeEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Should().ContainSingle().Subject;

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-default",
            RequestServices = sp
        };

        // Act
        await routeEndpoint.RequestDelegate!(httpContext);

        // Assert — default mapper used (200 + JSON)
        httpContext.Response.StatusCode.Should().Be(200);
    }

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

    /// <summary>
    /// Helper to check the payload property of an anonymous object without using
    /// null-propagating operators (which are not allowed in expression tree lambdas).
    /// </summary>
    private static bool MatchPayload(object? input, string expected)
    {
        if (input is null) return false;
        var prop = input.GetType().GetProperty("payload");
        if (prop is null) return false;
        var value = prop.GetValue(input);
        if (value is null) return false;
        return value.ToString() == expected;
    }
}

#pragma warning restore CC1001

#pragma warning disable CC1001 // RefValidation: descriptor not registered — tests use mock registries

/// <summary>
/// E2E integration tests for the full compatibility projection chain:
/// HTTP request → generated binding → ICapabilityDispatcher → CapabilityPipeline-style resolution →
/// ICapabilityContextAwareHandlerInvoker → original service method → legacy-compatible DynamicApiResponse.
/// </summary>
[Collection("DynamicApiStaticState")]
public sealed class CompatibilityProjectionEndToEndTests : IDisposable
{
    private readonly FakeBookAppService _bookService;

    public CompatibilityProjectionEndToEndTests()
    {
        _bookService = new FakeBookAppService();
        CapabilityEndpointBindingRegistry.Reset();
        CapabilityEndpointResultContractRegistration.Reset();
        CapabilityHandlerResolverProvider.Reset();
        ClearDescriptorProviderRegistry();
    }

    public void Dispose()
    {
        CapabilityEndpointBindingRegistry.Reset();
        CapabilityEndpointResultContractRegistration.Reset();
        CapabilityHandlerResolverProvider.Reset();
        ClearDescriptorProviderRegistry();
    }

    private static void ClearDescriptorProviderRegistry()
    {
        var field = typeof(DescriptorProviderRegistry).GetField("_providers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var providers = (ConcurrentBag<object>)field!.GetValue(null)!;
        providers.Clear();
    }

    /// <summary>
    /// Builds a ServiceProvider with a mock ICapabilityDispatcher that resolves handlers
    /// from the static CapabilityHandlerResolverProvider and invokes them — simulating the
    /// behaviour of CapabilityPipeline without requiring all middleware dependencies.
    /// </summary>
    private IServiceProvider BuildServiceProvider(
        Mock<ICapabilityRegistry> capRegistryMock,
        out Mock<ICapabilityDispatcher> dispatcherMock)
    {
        var spCapture = new ServiceCollection();
        IServiceProvider? capturedSp = null;

        dispatcherMock = new Mock<ICapabilityDispatcher>();
        dispatcherMock
            .Setup(d => d.DispatchAsync(
                It.IsAny<CapabilityDescriptor>(),
                It.IsAny<InvocationSource>(),
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .Returns((CapabilityDescriptor desc, InvocationSource src, object? input,
                Action<CapabilityExecutionContext>? configureContext, CancellationToken ct) =>
                InvokeThroughHandlerResolverAsync(desc, input, configureContext, ct, capturedSp!));

        spCapture.AddLogging();
        spCapture.AddSingleton(capRegistryMock.Object);
        spCapture.AddSingleton(dispatcherMock.Object);
        spCapture.AddSingleton(_bookService);
        spCapture.AddCrestCapabilityEndpoints();
        capturedSp = spCapture.BuildServiceProvider();

        return capturedSp;
    }

    /// <summary>
    /// Simulates what CapabilityPipeline does: resolve the handler via the static resolver,
    /// check for ICapabilityContextAwareHandlerInvoker, build an execution context,
    /// and invoke the handler.
    /// </summary>
    private static async Task<CapabilityExecutionResult> InvokeThroughHandlerResolverAsync(
        CapabilityDescriptor desc,
        object? input,
        Action<CapabilityExecutionContext>? configureContext,
        CancellationToken ct,
        IServiceProvider sp)
    {
        var resolver = CapabilityHandlerResolverProvider.GetResolver();
        var invoker = resolver.Resolve(desc.Id);
        if (invoker is null)
        {
            return CapabilityExecutionResult.Failure(
                "HANDLER_NOT_FOUND",
                $"No handler registered for capability '{desc.Id}'.",
                TimeSpan.Zero);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var context = new CapabilityExecutionContext
        {
            CapabilityId = desc.Id,
            CapabilityName = desc.Name,
            CapabilityVersion = desc.Version,
            CapabilityContractHash = "e2e-test-hash",
            Input = input,
            CancellationToken = ct,
            ServiceProvider = sp
        };
        configureContext?.Invoke(context);

        object? output;
        if (invoker is ICapabilityContextAwareHandlerInvoker contextAware)
        {
            output = await contextAware.InvokeAsync(context, ct);
        }
        else
        {
            output = await invoker.InvokeAsync(input, ct);
        }

        return CapabilityExecutionResult.Success(
            output,
            DateTimeOffset.UtcNow - startedAt);
    }

    /// <summary>
    /// Helper that sets up all the registrations for a single compatibility endpoint:
    /// capability descriptor + endpoint descriptor + binding + result contract + invoker.
    /// </summary>
    private static void SetupCompatibilityEndpoint(
        CapabilityDescriptor capability,
        string endpointId,
        CapabilityEndpointHttpMethod httpMethod,
        string routePattern,
        Func<HttpContext, CancellationToken, ValueTask<object?>> bindInput,
        Func<EndpointExecutionContext, HttpContext, object> resultMapper,
        ICapabilityHandlerInvoker invoker)
    {
        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = endpointId,
            Name = $"Compat EP {endpointId}",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capability.Id, capability.Version),
            HttpMethod = httpMethod,
            RoutePattern = routePattern
        };

        var providerMock = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        providerMock.Setup(p => p.GetDescriptors()).Returns(new[] { endpointDescriptor });
        DescriptorProviderRegistry.Register(providerMock.Object);

        CapabilityEndpointBindingRegistry.Register(new CapabilityEndpointBindingContract(
            EndpointId: endpointId,
            EndpointVersion: 1,
            BindInputAsync: bindInput));

        CapabilityEndpointResultContractRegistration.Register(endpointId, 1, resultMapper);

        CapabilityHandlerResolverProvider.Register(capability.Id, invoker);
    }

    /// <summary>
    /// Maps endpoints, gets the route endpoint, and invokes it with a configured HttpContext.
    /// Returns the response body as a string for further assertions.
    /// </summary>
    private static async Task<(HttpContext Context, string ResponseBody)> InvokeEndpointAsync(
        IServiceProvider sp,
        string requestMethod,
        object? requestBody,
        string routePattern)
    {
        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();

        var dataSource = endpoints.DataSources.Should().ContainSingle().Subject;
        var routeEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Should().ContainSingle().Subject;

        var responseBodyStream = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
            RequestServices = sp,
            Request =
            {
                Method = requestMethod,
                Path = routePattern,
                ContentType = "application/json",
                Body = requestBody is not null
                    ? new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(requestBody)))
                    : Stream.Null
            },
            Response =
            {
                Body = responseBodyStream
            }
        };

        if (requestBody is not null)
        {
            httpContext.Request.ContentLength = httpContext.Request.Body.Length;
        }

        await routeEndpoint.RequestDelegate!(httpContext);

        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(responseBodyStream).ReadToEndAsync();

        return (httpContext, body);
    }

    // ─────────────────────────────
    //  Helper types (simulating generated code)
    // ─────────────────────────────

    public class FakeBookAppService
    {
        public Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken ct = default)
        {
            return Task.FromResult(new BookDto { Id = Guid.NewGuid(), Title = input.Title });
        }

        public Task<BookDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            if (id == Guid.Empty) return Task.FromResult<BookDto?>(null);
            return Task.FromResult<BookDto?>(new BookDto { Id = id, Title = "Test Book" });
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<BookDto>> GetAllAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new List<BookDto>
            {
                new() { Id = Guid.NewGuid(), Title = "Book1" },
                new() { Id = Guid.NewGuid(), Title = "Book2" }
            });
        }
    }

    public class BookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
    }

    public class CreateBookDto
    {
        public string Title { get; set; } = "";
    }

    // Envelope types (simulate generated code for multi-param methods)
    public class CreateBookEnvelope
    {
        public CreateBookDto Input { get; set; } = new();
    }

    public class GetByIdEnvelope
    {
        public Guid Id { get; set; }
    }

    public class DeleteEnvelope
    {
        public Guid Id { get; set; }
    }

    // Invoker types (simulate generated AppServiceCompatibilityInvokerEmitter output)
    public class BookAppService_Create_CompatibilityInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public string HandlerId => "compat.appservice.book.create";

        public async Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => await InvokeAsync(null!, ct); // Not called when context-aware path is used

        public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<FakeBookAppService>();
            var envelope = (CreateBookEnvelope?)context.Input;
            return await service.CreateAsync(envelope!.Input, ct);
        }
    }

    public class BookAppService_GetById_CompatibilityInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public string HandlerId => "compat.appservice.book.getById";

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(null);

        public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<FakeBookAppService>();
            var envelope = (GetByIdEnvelope?)context.Input;
            return await service.GetByIdAsync(envelope!.Id, ct);
        }
    }

    public class BookAppService_Delete_CompatibilityInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public string HandlerId => "compat.appservice.book.delete";

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(null);

        public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<FakeBookAppService>();
            var envelope = (DeleteEnvelope?)context.Input;
            await service.DeleteAsync(envelope!.Id, ct);
            return null; // void return → null output
        }
    }

    public class BookAppService_GetAll_CompatibilityInvoker : ICapabilityContextAwareHandlerInvoker
    {
        public string HandlerId => "compat.appservice.book.getAll";

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(null);

        public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            var service = context.ServiceProvider.GetRequiredService<FakeBookAppService>();
            return await service.GetAllAsync(ct);
        }
    }

    // ─────────────────────────────
    //  Tests
    // ─────────────────────────────

    [Fact]
    public async Task CreateAsync_Post_Returns200_WithDynamicApiResponseWrappingBookDto()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "compat.appservice.book.create",
            Name = "Create Book",
            Version = 1,
            State = DescriptorState.Active
        };

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("compat.appservice.book.create", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("compat.appservice.book.create")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var sp = BuildServiceProvider(capRegistryMock, out _);

        SetupCompatibilityEndpoint(
            capability,
            endpointId: "ep-book-create",
            httpMethod: CapabilityEndpointHttpMethod.Post,
            routePattern: "/api/v1/books",
            bindInput: (ctx, ct) =>
                ValueTask.FromResult<object?>(new CreateBookEnvelope
                {
                    Input = new CreateBookDto { Title = "E2E Test Book" }
                }),
            resultMapper: (ctx, _) =>
            {
                if (ctx.Succeeded)
                    return Results.Ok(new DynamicApiResponse<BookDto>
                    {
                        Code = StatusCodes.Status200OK,
                        Message = "操作成功",
                        Data = (BookDto?)ctx.Output
                    });
                return Results.StatusCode(500);
            },
            invoker: new BookAppService_Create_CompatibilityInvoker());

        var (httpContext, body) = await InvokeEndpointAsync(sp, "POST", null, "/api/v1/books");

        // Assert
        httpContext.Response.StatusCode.Should().Be(200);

        // Deserialize as untyped JSON first for inspection, then as typed
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().Be("操作成功");
        doc.RootElement.TryGetProperty("data", out var dataProp).Should().BeTrue();
        dataProp.GetProperty("title").GetString().Should().Be("E2E Test Book");
        dataProp.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GetByIdAsync_Get_Found_Returns200_WithDynamicApiResponseWrappingBookDto()
    {
        // Arrange
        var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var capability = new CapabilityDescriptor
        {
            Id = "compat.appservice.book.getById",
            Name = "Get Book By Id",
            Version = 1,
            State = DescriptorState.Active
        };

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("compat.appservice.book.getById", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("compat.appservice.book.getById")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var sp = BuildServiceProvider(capRegistryMock, out _);

        SetupCompatibilityEndpoint(
            capability,
            endpointId: "ep-book-getById",
            httpMethod: CapabilityEndpointHttpMethod.Get,
            routePattern: "/api/v1/books/get-by-id",
            bindInput: (ctx, ct) =>
                ValueTask.FromResult<object?>(new GetByIdEnvelope { Id = bookId }),
            resultMapper: (ctx, _) =>
            {
                if (!ctx.Succeeded)
                    return Results.StatusCode(500);

                // Simulate WrapGetResult<T> behaviour: null → 404, non-null → 200
                if (ctx.Output is null)
                {
                    return Results.NotFound(new DynamicApiResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Message = "资源不存在"
                    });
                }

                return Results.Ok(new DynamicApiResponse<BookDto>
                {
                    Code = StatusCodes.Status200OK,
                    Message = "操作成功",
                    Data = (BookDto?)ctx.Output
                });
            },
            invoker: new BookAppService_GetById_CompatibilityInvoker());

        var (httpContext, body) = await InvokeEndpointAsync(sp, "GET", null, "/api/v1/books/get-by-id");

        // Assert
        httpContext.Response.StatusCode.Should().Be(200);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().Be("操作成功");
        doc.RootElement.TryGetProperty("data", out var dataProp).Should().BeTrue();
        dataProp.GetProperty("title").GetString().Should().Be("Test Book");
        dataProp.GetProperty("id").GetGuid().Should().Be(bookId);
    }

    [Fact]
    public async Task GetByIdAsync_Get_NotFound_Returns404_WithDynamicApiResponse()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "compat.appservice.book.getById",
            Name = "Get Book By Id",
            Version = 1,
            State = DescriptorState.Active
        };

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("compat.appservice.book.getById", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("compat.appservice.book.getById")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var sp = BuildServiceProvider(capRegistryMock, out _);

        SetupCompatibilityEndpoint(
            capability,
            endpointId: "ep-book-getById-nf",
            httpMethod: CapabilityEndpointHttpMethod.Get,
            routePattern: "/api/v1/books/get-by-id-notfound",
            bindInput: (ctx, ct) =>
                ValueTask.FromResult<object?>(new GetByIdEnvelope { Id = Guid.Empty }),
            resultMapper: (ctx, _) =>
            {
                if (!ctx.Succeeded)
                    return Results.StatusCode(500);

                if (ctx.Output is null)
                {
                    return Results.NotFound(new DynamicApiResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Message = "资源不存在"
                    });
                }

                return Results.Ok(new DynamicApiResponse<BookDto>
                {
                    Code = StatusCodes.Status200OK,
                    Message = "操作成功",
                    Data = (BookDto?)ctx.Output
                });
            },
            invoker: new BookAppService_GetById_CompatibilityInvoker());

        var (httpContext, body) = await InvokeEndpointAsync(sp, "GET", null, "/api/v1/books/get-by-id-notfound");

        // Assert
        httpContext.Response.StatusCode.Should().Be(404);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(404);
        doc.RootElement.GetProperty("message").GetString().Should().Be("资源不存在");
        doc.RootElement.TryGetProperty("data", out _).Should().BeFalse("NotFound should not include Data");
    }

    [Fact]
    public async Task DeleteAsync_Delete_Returns200_WithDynamicApiResponseVoid()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "compat.appservice.book.delete",
            Name = "Delete Book",
            Version = 1,
            State = DescriptorState.Active
        };

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("compat.appservice.book.delete", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("compat.appservice.book.delete")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var sp = BuildServiceProvider(capRegistryMock, out _);

        SetupCompatibilityEndpoint(
            capability,
            endpointId: "ep-book-delete",
            httpMethod: CapabilityEndpointHttpMethod.Delete,
            routePattern: "/api/v1/books/delete-by-id",
            bindInput: (ctx, ct) =>
                ValueTask.FromResult<object?>(new DeleteEnvelope { Id = Guid.NewGuid() }),
            resultMapper: (ctx, _) =>
            {
                if (ctx.Succeeded)
                {
                    return Results.Ok(new DynamicApiResponse
                    {
                        Code = StatusCodes.Status200OK,
                        Message = "操作成功"
                    });
                }
                return Results.StatusCode(500);
            },
            invoker: new BookAppService_Delete_CompatibilityInvoker());

        var (httpContext, body) = await InvokeEndpointAsync(sp, "DELETE", null, "/api/v1/books/delete-by-id");

        // Assert
        httpContext.Response.StatusCode.Should().Be(200);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().Be("操作成功");
        doc.RootElement.TryGetProperty("data", out _).Should().BeFalse("void result should not have Data property");
    }

    [Fact]
    public async Task GetAllAsync_Get_NoParams_Returns200_WithDynamicApiResponseWrappingList()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "compat.appservice.book.getAll",
            Name = "Get All Books",
            Version = 1,
            State = DescriptorState.Active
        };

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("compat.appservice.book.getAll", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("compat.appservice.book.getAll")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var sp = BuildServiceProvider(capRegistryMock, out _);

        SetupCompatibilityEndpoint(
            capability,
            endpointId: "ep-book-getAll",
            httpMethod: CapabilityEndpointHttpMethod.Get,
            routePattern: "/api/v1/books",
            bindInput: (ctx, ct) =>
                ValueTask.FromResult<object?>(null!), // No input params
            resultMapper: (ctx, _) =>
            {
                if (!ctx.Succeeded)
                    return Results.StatusCode(500);

                return Results.Ok(new DynamicApiResponse<List<BookDto>>
                {
                    Code = StatusCodes.Status200OK,
                    Message = "操作成功",
                    Data = (List<BookDto>?)ctx.Output
                });
            },
            invoker: new BookAppService_GetAll_CompatibilityInvoker());

        var (httpContext, body) = await InvokeEndpointAsync(sp, "GET", null, "/api/v1/books");

        // Assert
        httpContext.Response.StatusCode.Should().Be(200);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetInt32().Should().Be(200);
        doc.RootElement.GetProperty("message").GetString().Should().Be("操作成功");
        doc.RootElement.TryGetProperty("data", out var dataProp).Should().BeTrue();
        dataProp.GetArrayLength().Should().Be(2);
        dataProp[0].GetProperty("title").GetString().Should().Be("Book1");
         dataProp[1].GetProperty("title").GetString().Should().Be("Book2");
     }

    /// <summary>
    /// Verifies that a compatibility result contract does NOT swallow pipeline failures.
    /// When the pipeline returns RATE_LIMIT_EXCEEDED, the custom result mapper must be
    /// bypassed and the default failure mapper must produce 429.
    /// Uses RATE_LIMIT_EXCEEDED instead of UNAUTHORIZED because Results.Forbid()
    /// requires authentication middleware which is not available in unit test context.
    /// </summary>
    [Fact]
    public async Task CompatibilityResultContract_PipelineFailure_NotSwallowed()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-rl",
            Name = "RL Cap",
            Version = 1,
            State = DescriptorState.Active
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-rl",
            Name = "RL EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-rl", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/rl"
        };

        var providerMock = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        providerMock.Setup(p => p.GetDescriptors()).Returns(new[] { endpointDescriptor });
        DescriptorProviderRegistry.Register(providerMock.Object);
        CapabilityEndpointBindingRegistry.Register(new CapabilityEndpointBindingContract(
            EndpointId: "ep-rl", EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null)));

        // Register a result contract that would wrap as DynamicApiResponse on success
        CapabilityEndpointResultContractRegistration.Register("ep-rl", 1, (ctx, httpContext) =>
        {
            // This should NOT be called for failures
            return Results.Ok(new { code = 200, message = "操作成功", data = ctx.Output });
        });

        // Mock dispatcher to return RATE_LIMIT_EXCEEDED failure
        var dispatcherMock = new Mock<ICapabilityDispatcher>();
        dispatcherMock
            .Setup(d => d.DispatchAsync(
                capability,
                InvocationSource.Http,
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Failure(
                "RATE_LIMIT_EXCEEDED", "Rate limit exceeded.", TimeSpan.FromMilliseconds(1)));

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-rl", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-rl")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(capRegistryMock.Object);
        services.AddSingleton(dispatcherMock.Object);
        services.AddCrestCapabilityEndpoints();
        var sp = services.BuildServiceProvider();

        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();

        var dataSource = endpoints.DataSources.Should().ContainSingle().Subject;
        var routeEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Should().ContainSingle().Subject;

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-rl",
            RequestServices = sp
        };

        // Act
        await routeEndpoint.RequestDelegate!(httpContext);

        // Assert — failure must NOT be swallowed by the result contract.
        // RATE_LIMIT_EXCEEDED maps to 429 via CapabilityEndpointResultMapper.
        httpContext.Response.StatusCode.Should().Be(429,
            "RATE_LIMIT_EXCEEDED failure must produce 429, not 200 OK with success envelope");
    }

    /// <summary>
    /// Verifies that a compatibility result contract does NOT swallow validation failures.
    /// When the pipeline returns CAPABILITY_VALIDATION_FAILED, the custom result mapper
    /// must be bypassed and the default failure mapper must produce 400 Problem.
    /// </summary>
    [Fact]
    public async Task CompatibilityResultContract_ValidationFailure_Returns400()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-val",
            Name = "Val Cap",
            Version = 1,
            State = DescriptorState.Active
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-val",
            Name = "Val EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-val", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/v1/val"
        };

        var providerMock = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        providerMock.Setup(p => p.GetDescriptors()).Returns(new[] { endpointDescriptor });
        DescriptorProviderRegistry.Register(providerMock.Object);
        CapabilityEndpointBindingRegistry.Register(new CapabilityEndpointBindingContract(
            EndpointId: "ep-val", EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null)));

        CapabilityEndpointResultContractRegistration.Register("ep-val", 1, (ctx, httpContext) =>
            Results.Ok(new { code = 200, message = "操作成功", data = ctx.Output }));

        // Mock dispatcher to return validation failure
        var dispatcherMock = new Mock<ICapabilityDispatcher>();
        dispatcherMock
            .Setup(d => d.DispatchAsync(
                capability,
                InvocationSource.Http,
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Failure(
                "CAPABILITY_VALIDATION_FAILED", "Input validation failed.", TimeSpan.FromMilliseconds(1)));

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-val", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-val")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(capRegistryMock.Object);
        services.AddSingleton(dispatcherMock.Object);
        services.AddCrestCapabilityEndpoints();
        var sp = services.BuildServiceProvider();

        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();

        var dataSource = endpoints.DataSources.Should().ContainSingle().Subject;
        var routeEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Should().ContainSingle().Subject;

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-val",
            RequestServices = sp
        };

        // Act
        await routeEndpoint.RequestDelegate!(httpContext);

        // Assert — validation failure must produce 400, not 200 OK
        httpContext.Response.StatusCode.Should().Be(400,
            "CAPABILITY_VALIDATION_FAILED must produce 400 Problem, not 200 OK with success envelope");
    }

    /// <summary>
    /// Verifies that a compatibility result contract does NOT produce 404 for
    /// GET + HANDLER_NOT_FOUND. The failure must be mapped by the default mapper
    /// (500 Problem), not by the compatibility WrapGetResult(null) which would
    /// incorrectly produce 404 "资源不存在".
    /// </summary>
    [Fact]
    public async Task CompatibilityResultContract_HandlerNotFound_Returns500Not404()
    {
        // Arrange
        var capability = new CapabilityDescriptor
        {
            Id = "test-cap-hnf",
            Name = "HNF Cap",
            Version = 1,
            State = DescriptorState.Active
        };

        var endpointDescriptor = new CapabilityEndpointDescriptor
        {
            Id = "ep-hnf",
            Name = "HNF EP",
            Version = 1,
            State = DescriptorState.Active,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("test-cap-hnf", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Get,
            RoutePattern = "/api/v1/hnf"
        };

        var providerMock = new Mock<IDescriptorProvider<CapabilityEndpointDescriptor>>();
        providerMock.Setup(p => p.GetDescriptors()).Returns(new[] { endpointDescriptor });
        DescriptorProviderRegistry.Register(providerMock.Object);
        CapabilityEndpointBindingRegistry.Register(new CapabilityEndpointBindingContract(
            EndpointId: "ep-hnf", EndpointVersion: 1,
            BindInputAsync: (ctx, ct) => ValueTask.FromResult<object?>(null)));

        // Register a GET result contract that would produce 404 for null output
        CapabilityEndpointResultContractRegistration.Register("ep-hnf", 1, (ctx, httpContext) =>
        {
            // This simulates WrapGetResult — would return 404 for null output
            if (ctx.Output is null)
                return Results.NotFound(new { code = 404, message = "资源不存在" });
            return Results.Ok(new { code = 200, message = "操作成功", data = ctx.Output });
        });

        // Mock dispatcher to return HANDLER_NOT_FOUND failure
        var dispatcherMock = new Mock<ICapabilityDispatcher>();
        dispatcherMock
            .Setup(d => d.DispatchAsync(
                capability,
                InvocationSource.Http,
                It.IsAny<object?>(),
                It.IsAny<Action<CapabilityExecutionContext>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityExecutionResult.Failure(
                "HANDLER_NOT_FOUND", "No handler registered.", TimeSpan.FromMilliseconds(1)));

        var capRegistryMock = new Mock<ICapabilityRegistry>();
        capRegistryMock.Setup(r => r.GetByVersion("test-cap-hnf", 1)).Returns(capability);
        capRegistryMock.Setup(r => r.GetById("test-cap-hnf")).Returns(capability);
        capRegistryMock.Setup(r => r.GetAll()).Returns(new[] { capability });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(capRegistryMock.Object);
        services.AddSingleton(dispatcherMock.Object);
        services.AddCrestCapabilityEndpoints();
        var sp = services.BuildServiceProvider();

        var endpoints = new DefaultEndpointRouteBuilder(sp);
        endpoints.MapCrestCapabilityEndpoints();

        var dataSource = endpoints.DataSources.Should().ContainSingle().Subject;
        var routeEndpoint = dataSource.Endpoints.OfType<RouteEndpoint>().Should().ContainSingle().Subject;

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-hnf",
            RequestServices = sp
        };

        // Act
        await routeEndpoint.RequestDelegate!(httpContext);

        // Assert — HANDLER_NOT_FOUND must produce 500 (Problem), NOT 404
        httpContext.Response.StatusCode.Should().Be(500,
            "HANDLER_NOT_FOUND must produce 500 Problem, not 404 from WrapGetResult(null)");
    }

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
