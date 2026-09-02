using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Localization.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
