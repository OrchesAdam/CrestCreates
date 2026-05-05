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

        var scopedProvider = new Mock<IServiceProvider>();
        var scope = new Mock<IServiceScope>();
        var settingManager = new Mock<ISettingManager>();

        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);
        scopedProvider
            .Setup(p => p.GetService(typeof(ISettingManager)))
            .Returns(settingManager.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);

        var scopeProvider = new Mock<IServiceProvider>();
        scopeProvider.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

        var seeder = new TenantSettingDefaultsSeeder(
            definitionManager.Object,
            scopeProvider.Object,
            Mock.Of<ILogger<TenantSettingDefaultsSeeder>>());

        var result = await seeder.SeedAsync(CreateContext());

        result.Success.Should().BeTrue();
        scopeProvider.Verify(p => p.GetService(typeof(IServiceScopeFactory)), Times.AtLeastOnce,
            "TenantSettingDefaultsSeeder must create a new scope to pick up ICurrentTenant context");
        scopedProvider.Verify(p => p.GetService(typeof(ISettingManager)), Times.AtLeastOnce,
            "ISettingManager must be resolved from the new scope, not the root provider");
    }

    [Fact]
    public async Task FeatureDefaultsSeeder_ShouldResolveManagerFromNewScope()
    {
        var definitionManager = new Mock<IFeatureDefinitionManager>();
        definitionManager.Setup(d => d.GetAll()).Returns(Array.Empty<FeatureDefinition>());

        var scopedProvider = new Mock<IServiceProvider>();
        var scope = new Mock<IServiceScope>();
        var featureManager = new Mock<IFeatureManager>();

        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);
        scopedProvider
            .Setup(p => p.GetService(typeof(IFeatureManager)))
            .Returns(featureManager.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);

        var scopeProvider = new Mock<IServiceProvider>();
        scopeProvider.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

        var seeder = new TenantFeatureDefaultsSeeder(
            definitionManager.Object,
            scopeProvider.Object,
            Mock.Of<ILogger<TenantFeatureDefaultsSeeder>>());

        var result = await seeder.SeedAsync(CreateContext());

        result.Success.Should().BeTrue();
        scopeProvider.Verify(p => p.GetService(typeof(IServiceScopeFactory)), Times.AtLeastOnce,
            "TenantFeatureDefaultsSeeder must create a new scope to pick up ICurrentTenant context");
        scopedProvider.Verify(p => p.GetService(typeof(IFeatureManager)), Times.AtLeastOnce,
            "IFeatureManager must be resolved from the new scope, not the root provider");
    }

    [Fact]
    public async Task FeatureDefaultsSeeder_ShouldBeIdempotent()
    {
        var definitionManager = new Mock<IFeatureDefinitionManager>();
        definitionManager.Setup(x => x.GetAll()).Returns(new[]
        {
            new FeatureDefinition(
                "Identity.UserCreationEnabled",
                "Identity",
                displayName: "User creation",
                description: "Allows creating users",
                defaultValue: "true",
                valueType: FeatureValueType.Bool,
                scopes: FeatureScope.Global | FeatureScope.Tenant)
        });

        var featureManager = new Mock<IFeatureManager>();
        featureManager
            .Setup(x => x.GetScopedValueOrNullAsync(
                It.IsAny<string>(),
                It.IsAny<FeatureScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeatureValueEntry
            {
                Name = "Identity.UserCreationEnabled",
                Value = "true",
                Scope = FeatureScope.Tenant,
                ProviderKey = "tenant-1",
                TenantId = "tenant-1"
            });

        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider.Setup(x => x.GetService(typeof(IFeatureManager))).Returns(featureManager.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(x => x.ServiceProvider).Returns(scopedProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);

        var rootProvider = new Mock<IServiceProvider>();
        rootProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

        var seeder = new TenantFeatureDefaultsSeeder(
            definitionManager.Object,
            rootProvider.Object,
            Mock.Of<ILogger<TenantFeatureDefaultsSeeder>>());

        var result = await seeder.SeedAsync(new TenantInitializationContext
        {
            TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            TenantName = "tenant-1",
            CorrelationId = "test",
            ConnectionString = null
        });

        result.Success.Should().BeTrue();
        featureManager.Verify(x => x.SetTenantAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
