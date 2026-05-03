using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.Domain.Shared.Settings;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantSeederScopeTests
{
    private static TenantInitializationContext CreateContext()
        => new()
        {
            TenantId = Guid.NewGuid(),
            TenantName = "test-tenant",
            ConnectionString = "Server=.;Database=TestDb;",
            CorrelationId = Guid.NewGuid().ToString("N")
        };

    [Fact]
    public async Task SettingDefaultsSeeder_ShouldResolveManagerFromNewScope()
    {
        var definitionManager = new Mock<ISettingDefinitionManager>();
        definitionManager.Setup(d => d.GetAll()).Returns(Array.Empty<SettingDefinition>());

        var scopeProvider = new Mock<IServiceProvider>();
        var scope = new Mock<IServiceScope>();
        var scopedServiceProvider = new Mock<IServiceProvider>();
        var settingManager = new Mock<ISettingManager>();

        scope.Setup(s => s.ServiceProvider).Returns(scopedServiceProvider.Object);
        scopeProvider.Setup(p => p.CreateScope()).Returns(scope.Object);
        scopedServiceProvider
            .Setup(p => p.GetService(typeof(ISettingManager)))
            .Returns(settingManager.Object);

        var seeder = new TenantSettingDefaultsSeeder(
            definitionManager.Object,
            scopeProvider.Object,
            Mock.Of<ILogger<TenantSettingDefaultsSeeder>>());

        var result = await seeder.SeedAsync(CreateContext());

        result.Success.Should().BeTrue();
        scopeProvider.Verify(p => p.CreateScope(), Times.Once,
            "TenantSettingDefaultsSeeder must create a new scope to pick up ICurrentTenant context");
        scopedServiceProvider.Verify(p => p.GetService(typeof(ISettingManager)), Times.AtLeastOnce,
            "ISettingManager must be resolved from the new scope, not the root provider");
    }

    [Fact]
    public async Task FeatureDefaultsSeeder_ShouldResolveManagerFromNewScope()
    {
        var definitionManager = new Mock<IFeatureDefinitionManager>();
        definitionManager.Setup(d => d.GetAll()).Returns(Array.Empty<FeatureDefinition>());

        var scopeProvider = new Mock<IServiceProvider>();
        var scope = new Mock<IServiceScope>();
        var scopedServiceProvider = new Mock<IServiceProvider>();
        var featureManager = new Mock<IFeatureManager>();

        scope.Setup(s => s.ServiceProvider).Returns(scopedServiceProvider.Object);
        scopeProvider.Setup(p => p.CreateScope()).Returns(scope.Object);
        scopedServiceProvider
            .Setup(p => p.GetService(typeof(IFeatureManager)))
            .Returns(featureManager.Object);

        var seeder = new TenantFeatureDefaultsSeeder(
            definitionManager.Object,
            scopeProvider.Object,
            Mock.Of<ILogger<TenantFeatureDefaultsSeeder>>());

        var result = await seeder.SeedAsync(CreateContext());

        result.Success.Should().BeTrue();
        scopeProvider.Verify(p => p.CreateScope(), Times.Once,
            "TenantFeatureDefaultsSeeder must create a new scope to pick up ICurrentTenant context");
        scopedServiceProvider.Verify(p => p.GetService(typeof(IFeatureManager)), Times.AtLeastOnce,
            "IFeatureManager must be resolved from the new scope, not the root provider");
    }
}
