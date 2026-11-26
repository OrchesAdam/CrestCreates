using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Localization.Services;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public sealed class Phase9eAgentControlPlaneCompositionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AddAgentControlPlane_Overloads_Should_ResolveLocalizedCatalogConsistently(int overload)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalizationService>(new KeyReturningLocalizationService("zh-CN"));

        switch (overload)
        {
            case 0:
                services.AddAgentControlPlane();
                break;
            case 1:
                services.AddAgentControlPlane(AgentToolAuthorizationOptions.ProductionDefaults);
                break;
            case 2:
                services.AddAgentControlPlane(AgentToolAuthorizationPolicy.ProductionDefaults);
                break;
        }

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDescriptorReviewMessageTemplateCatalog>()
            .Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DiagnosticCount"] = "3"
                })
            .Should().Be("草稿验证通过，共有 3 条诊断。");
    }

    [Fact]
    public void AddAgentControlPlane_WithoutLocalization_Should_ResolveStableEnglishCatalog()
    {
        var services = new ServiceCollection();
        services.AddAgentControlPlane();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDescriptorReviewMessageTemplateCatalog>()
            .Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DiagnosticCount"] = "3"
                })
            .Should().Be("Draft validation passed with 3 diagnostics.");
    }

    [Fact]
    public void AddAgentControlPlane_Should_RegisterNarrowInterfacesAsFacadeAliases()
    {
        var services = new ServiceCollection();
        services.AddAgentControlPlane();
        var toolInterfaces = new[]
        {
            typeof(IReadOnlyControlPlaneTools),
            typeof(IMutationControlPlaneTools),
            typeof(IActivationControlPlaneTools)
        };

        foreach (var toolInterface in toolInterfaces)
        {
            services.Should().ContainSingle(descriptor =>
                descriptor.ServiceType == toolInterface
                && !descriptor.IsKeyedService
                && descriptor.ImplementationType == typeof(ControlPlaneToolInterfaceForwarder)
                && descriptor.Lifetime == ServiceLifetime.Transient,
                "the default composition must register an unkeyed forwarding alias for {0}", toolInterface.Name);
        }
    }

    [Fact]
    public async Task AddAgentControlPlane_Should_ResolveNarrowInterfacesAsFacadeAliases_AndPreserveHostOverrides()
    {
        var services = new ServiceCollection();
        var readOnlyOverride = new Mock<IReadOnlyControlPlaneTools>();
        readOnlyOverride.Setup(service => service.GetTopologySummaryAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<TopologySummaryResult>.Success(new TopologySummaryResult
            {
                TotalNodeCount = 0,
                TotalEdgeCount = 0,
                NodeCountsByKind = new Dictionary<DescriptorKind, int>(),
                EdgeCountsByKind = new Dictionary<RelationshipKind, int>(),
                TopologyDiagnostics = []
            }));
        var facadeOverride = new Mock<IAgentControlPlaneToolService>();
        facadeOverride.Setup(service => service.GetTopologySummaryAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<TopologySummaryResult>.Success(new TopologySummaryResult
            {
                TotalNodeCount = 0,
                TotalEdgeCount = 0,
                NodeCountsByKind = new Dictionary<DescriptorKind, int>(),
                EdgeCountsByKind = new Dictionary<RelationshipKind, int>(),
                TopologyDiagnostics = []
            }));
        facadeOverride.Setup(service => service.GetDescriptorDraftAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentToolResult<AgentDescriptorDraftDto>)null!);
        facadeOverride.Setup(service => service.GetActivationRequestStatusAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentToolResult<ActivationRequest>)null!);
        services.AddSingleton(readOnlyOverride.Object);
        services.AddSingleton(facadeOverride.Object);
        services.AddKeyedSingleton<IMutationControlPlaneTools>("keyed-only", new Mock<IMutationControlPlaneTools>().Object);
        services.AddKeyedSingleton<IActivationControlPlaneTools>("keyed-only", new Mock<IActivationControlPlaneTools>().Object);
        services.AddAgentControlPlane();

        using var provider = services.BuildServiceProvider();
        var context = new AgentToolInvocationContext
        {
            TenantId = "tenant",
            ActorId = "actor",
            ActorKind = AgentToolActorKind.Human,
            CorrelationId = "correlation",
            ToolName = AgentToolName.GetTopologySummary,
            InvocationSource = AgentToolInvocationSource.Direct
        };

        provider.GetRequiredService<IReadOnlyControlPlaneTools>().Should().BeSameAs(readOnlyOverride.Object);
        await provider.GetRequiredService<IReadOnlyControlPlaneTools>().GetTopologySummaryAsync(context);
        await provider.GetRequiredService<IMutationControlPlaneTools>().GetDescriptorDraftAsync(context, "draft");
        await provider.GetRequiredService<IActivationControlPlaneTools>().GetActivationRequestStatusAsync(context, "request");

        readOnlyOverride.Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        facadeOverride.Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Never);
        facadeOverride.Verify(service => service.GetDescriptorDraftAsync(context, "draft", It.IsAny<CancellationToken>()), Times.Once);
        facadeOverride.Verify(service => service.GetActivationRequestStatusAsync(context, "request", It.IsAny<CancellationToken>()), Times.Once);
        provider.GetRequiredKeyedService<IMutationControlPlaneTools>("keyed-only").Should().NotBeSameAs(facadeOverride.Object);
        provider.GetRequiredKeyedService<IActivationControlPlaneTools>("keyed-only").Should().NotBeSameAs(facadeOverride.Object);
    }

    [Fact]
    public async Task AddAgentControlPlane_Should_ResolveScopedFacadeThroughAliases()
    {
        var services = new ServiceCollection();
        var facade = new Mock<IAgentControlPlaneToolService>();
        facade.Setup(service => service.GetTopologySummaryAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<TopologySummaryResult>.Success(new TopologySummaryResult
            {
                TotalNodeCount = 0,
                TotalEdgeCount = 0,
                NodeCountsByKind = new Dictionary<DescriptorKind, int>(),
                EdgeCountsByKind = new Dictionary<RelationshipKind, int>(),
                TopologyDiagnostics = []
            }));
        services.AddScoped<IAgentControlPlaneToolService>(_ => facade.Object);
        services.AddAgentControlPlane();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var context = new AgentToolInvocationContext
        {
            TenantId = "tenant",
            ActorId = "actor",
            ActorKind = AgentToolActorKind.Human,
            CorrelationId = "correlation",
            ToolName = AgentToolName.GetTopologySummary,
            InvocationSource = AgentToolInvocationSource.Direct
        };

        await scope.ServiceProvider.GetRequiredService<IReadOnlyControlPlaneTools>()
            .GetTopologySummaryAsync(context);

        facade.Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAgentControlPlane_Should_ResolveLateScopedFacadeOverrideThroughAliases()
    {
        var services = new ServiceCollection();
        services.AddAgentControlPlane();
        var facade = new Mock<IAgentControlPlaneToolService>();
        facade.Setup(service => service.GetTopologySummaryAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<TopologySummaryResult>.Success(new TopologySummaryResult
            {
                TotalNodeCount = 0,
                TotalEdgeCount = 0,
                NodeCountsByKind = new Dictionary<DescriptorKind, int>(),
                EdgeCountsByKind = new Dictionary<RelationshipKind, int>(),
                TopologyDiagnostics = []
            }));
        services.AddScoped<IAgentControlPlaneToolService>(_ => facade.Object);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var context = new AgentToolInvocationContext
        {
            TenantId = "tenant",
            ActorId = "actor",
            ActorKind = AgentToolActorKind.Human,
            CorrelationId = "correlation",
            ToolName = AgentToolName.GetTopologySummary,
            InvocationSource = AgentToolInvocationSource.Direct
        };

        await scope.ServiceProvider.GetRequiredService<IReadOnlyControlPlaneTools>()
            .GetTopologySummaryAsync(context);

        facade.Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAgentControlPlane_Should_KeepTransientFacadePerAliasResolution()
    {
        var services = new ServiceCollection();
        var facade = new Mock<IAgentControlPlaneToolService>();
        facade.Setup(service => service.GetTopologySummaryAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<TopologySummaryResult>.Success(new TopologySummaryResult
            {
                TotalNodeCount = 0,
                TotalEdgeCount = 0,
                NodeCountsByKind = new Dictionary<DescriptorKind, int>(),
                EdgeCountsByKind = new Dictionary<RelationshipKind, int>(),
                TopologyDiagnostics = []
            }));
        var resolutions = 0;
        services.AddTransient<IAgentControlPlaneToolService>(_ =>
        {
            resolutions++;
            return facade.Object;
        });
        services.AddAgentControlPlane();

        using var provider = services.BuildServiceProvider();
        var context = new AgentToolInvocationContext
        {
            TenantId = "tenant",
            ActorId = "actor",
            ActorKind = AgentToolActorKind.Human,
            CorrelationId = "correlation",
            ToolName = AgentToolName.GetTopologySummary,
            InvocationSource = AgentToolInvocationSource.Direct
        };
        var tools = provider.GetRequiredService<IReadOnlyControlPlaneTools>();

        await tools.GetTopologySummaryAsync(context);
        await tools.GetTopologySummaryAsync(context);

        resolutions.Should().Be(1);
        facade.Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void AddAgentControlPlane_Should_NotDisposeHostFacadeMoreThanOnce()
    {
        var services = new ServiceCollection();
        var facade = new Mock<IAgentControlPlaneToolService>();
        var disposableFacade = facade.As<IDisposable>();
        services.AddSingleton<IAgentControlPlaneToolService>(_ => facade.Object);
        services.AddAgentControlPlane();

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<IAgentControlPlaneToolService>().Should().BeSameAs(facade.Object);
            _ = provider.GetRequiredService<IReadOnlyControlPlaneTools>();
            _ = provider.GetRequiredService<IMutationControlPlaneTools>();
            _ = provider.GetRequiredService<IActivationControlPlaneTools>();
        }

        disposableFacade.Verify(service => service.Dispose(), Times.Once);
    }

    private sealed class KeyReturningLocalizationService(string currentCulture) : ILocalizationService
    {
        public string CurrentCulture { get; } = currentCulture;
        public string GetString(string key) => key;
        public string GetString(string key, params object[] arguments) => key;
        public string GetString(string key, string cultureName) => key;
        public string GetString(string key, string cultureName, params object[] arguments) => key;
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, params object[] arguments) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments) => Task.FromResult<string?>(key);
        public IDisposable ChangeCulture(string cultureName) => throw new NotSupportedException();
        public Task<IDisposable> ChangeCultureAsync(string cultureName) => throw new NotSupportedException();
    }
}
