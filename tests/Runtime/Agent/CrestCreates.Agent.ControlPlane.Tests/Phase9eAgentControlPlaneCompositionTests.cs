using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Localization.Services;
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
                && descriptor.ImplementationFactory != null,
                "the default composition must register an unkeyed facade alias for {0}", toolInterface.Name);
        }
    }

    [Fact]
    public void AddAgentControlPlane_Should_ResolveNarrowInterfacesAsFacadeAliases_AndPreserveHostOverrides()
    {
        var services = new ServiceCollection();
        var readOnlyOverride = new Mock<IReadOnlyControlPlaneTools>().Object;
        var facadeOverride = new Mock<IAgentControlPlaneToolService>().Object;
        services.AddSingleton(readOnlyOverride);
        services.AddSingleton(facadeOverride);
        services.AddKeyedSingleton<IMutationControlPlaneTools>("keyed-only", new Mock<IMutationControlPlaneTools>().Object);
        services.AddKeyedSingleton<IActivationControlPlaneTools>("keyed-only", new Mock<IActivationControlPlaneTools>().Object);
        services.AddAgentControlPlane();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IReadOnlyControlPlaneTools>().Should().BeSameAs(readOnlyOverride);
        provider.GetRequiredService<IMutationControlPlaneTools>().Should().BeSameAs(facadeOverride);
        provider.GetRequiredService<IActivationControlPlaneTools>().Should().BeSameAs(facadeOverride);
        provider.GetRequiredKeyedService<IMutationControlPlaneTools>("keyed-only").Should().NotBeSameAs(facadeOverride);
        provider.GetRequiredKeyedService<IActivationControlPlaneTools>("keyed-only").Should().NotBeSameAs(facadeOverride);
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
