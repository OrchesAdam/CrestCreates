using System.Collections.Concurrent;
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

#pragma warning disable CC1001 // RefValidation: descriptor not registered — tests use mock registries

public sealed class CapabilityEndpointIntegrationTests : IDisposable
{
    public CapabilityEndpointIntegrationTests()
    {
        CapabilityEndpointBindingRegistry.Reset();
        ClearDescriptorProviderRegistry();
    }

    public void Dispose()
    {
        CapabilityEndpointBindingRegistry.Reset();
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
