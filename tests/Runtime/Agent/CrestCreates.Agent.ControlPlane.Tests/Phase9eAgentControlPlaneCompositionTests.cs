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
        var keyedMutation = new Mock<IMutationControlPlaneTools>().Object;
        var keyedActivation = new Mock<IActivationControlPlaneTools>().Object;
        var keyedFacade = new Mock<IAgentControlPlaneToolService>().Object;
        services.AddSingleton(readOnlyOverride.Object);
        services.AddSingleton(facadeOverride.Object);
        services.AddKeyedSingleton<IMutationControlPlaneTools>("keyed-only", keyedMutation);
        services.AddKeyedSingleton<IActivationControlPlaneTools>("keyed-only", keyedActivation);
        services.AddKeyedSingleton<IAgentControlPlaneToolService>("keyed-facade", keyedFacade);
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
        provider.GetRequiredKeyedService<IMutationControlPlaneTools>("keyed-only").Should().BeSameAs(keyedMutation);
        provider.GetRequiredKeyedService<IActivationControlPlaneTools>("keyed-only").Should().BeSameAs(keyedActivation);
        provider.GetRequiredKeyedService<IAgentControlPlaneToolService>("keyed-facade").Should().BeSameAs(keyedFacade);
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

    [Fact]
    public void AddAgentControlPlane_Should_RejectSingletonConsumerOfScopedFacadeDuringValidation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAgentControlPlaneToolService>(_ => Mock.Of<IAgentControlPlaneToolService>());
        services.AddAgentControlPlane();
        services.AddSingleton<SingletonToolConsumer>();

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        act.Should().Throw<AggregateException>()
            .WithMessage("*Cannot consume scoped service*IReadOnlyControlPlaneTools*SingletonToolConsumer*");
    }

    [Fact]
    public async Task AddAgentControlPlane_Should_PreserveLateUnkeyedNarrowOverride()
    {
        var services = new ServiceCollection();
        services.AddAgentControlPlane();
        var lateOverride = new Mock<IReadOnlyControlPlaneTools>();
        lateOverride.Setup(service => service.GetTopologySummaryAsync(
                It.IsAny<AgentToolInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentToolResult<TopologySummaryResult>.Success(new TopologySummaryResult
            {
                TotalNodeCount = 0,
                TotalEdgeCount = 0,
                NodeCountsByKind = new Dictionary<DescriptorKind, int>(),
                EdgeCountsByKind = new Dictionary<RelationshipKind, int>(),
                TopologyDiagnostics = []
            }));
        services.AddSingleton(lateOverride.Object);

        using var provider = services.BuildServiceProvider();
        var context = TestContext();

        provider.GetRequiredService<IReadOnlyControlPlaneTools>().Should().BeSameAs(lateOverride.Object);
        await provider.GetRequiredService<IReadOnlyControlPlaneTools>().GetTopologySummaryAsync(context);

        lateOverride.Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAgentControlPlane_Should_IsolateAndDisposeScopedFacadesPerScope()
    {
        var services = new ServiceCollection();
        var facades = new List<Mock<IAgentControlPlaneToolService>>();
        services.AddScoped<IAgentControlPlaneToolService>(_ =>
        {
            var facade = new Mock<IAgentControlPlaneToolService>();
            facade.As<IDisposable>();
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
            facades.Add(facade);
            return facade.Object;
        });
        services.AddAgentControlPlane();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var context = TestContext();
        IReadOnlyControlPlaneTools firstTools;
        IReadOnlyControlPlaneTools secondTools;

        using (var firstScope = provider.CreateScope())
        {
            firstTools = firstScope.ServiceProvider.GetRequiredService<IReadOnlyControlPlaneTools>();
            await firstTools.GetTopologySummaryAsync(context);
            await firstScope.ServiceProvider.GetRequiredService<IReadOnlyControlPlaneTools>().GetTopologySummaryAsync(context);
        }

        using (var secondScope = provider.CreateScope())
        {
            secondTools = secondScope.ServiceProvider.GetRequiredService<IReadOnlyControlPlaneTools>();
            await secondTools.GetTopologySummaryAsync(context);
        }

        facades.Should().HaveCount(2);
        facades[0].As<IDisposable>().Verify(service => service.Dispose(), Times.Once);
        facades[1].As<IDisposable>().Verify(service => service.Dispose(), Times.Once);
        facades[0].Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Exactly(2));
        facades[1].Verify(service => service.GetTopologySummaryAsync(context, It.IsAny<CancellationToken>()), Times.Once);
        firstTools.Should().NotBeSameAs(secondTools);
    }

    private static AgentToolInvocationContext TestContext() => new()
    {
        TenantId = "tenant",
        ActorId = "actor",
        ActorKind = AgentToolActorKind.Human,
        CorrelationId = "correlation",
        ToolName = AgentToolName.GetTopologySummary,
        InvocationSource = AgentToolInvocationSource.Direct
    };

    private sealed class SingletonToolConsumer(IReadOnlyControlPlaneTools tools)
    {
        public IReadOnlyControlPlaneTools Tools { get; } = tools;
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
