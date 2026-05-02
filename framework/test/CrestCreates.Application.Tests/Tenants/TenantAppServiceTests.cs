using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantAppServiceTests
{
    private readonly Mock<ITenantManager> _tenantManagerMock;
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly Mock<ITenantInitializationStore> _storeMock;
    private readonly TenantAppService _tenantAppService;

    public TenantAppServiceTests()
    {
        _tenantManagerMock = new Mock<ITenantManager>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _storeMock = new Mock<ITenantInitializationStore>();

        var orchestrator = CreateSuccessfulOrchestrator();
        _tenantAppService = new TenantAppService(
            _tenantManagerMock.Object,
            _tenantRepositoryMock.Object,
            orchestrator,
            _storeMock.Object);
    }

    [Fact]
    public async Task CreateAsync_ReturnsMappedTenantDto()
    {
        var tenant = new Tenant(Guid.NewGuid(), "host")
        {
            DisplayName = "Host Tenant",
            IsActive = true,
            CreationTime = DateTime.UtcNow
        };
        tenant.SetDefaultConnectionString("Server=.;Database=HostDb;");

        _tenantRepositoryMock
            .Setup(repository => repository.FindByNameAsync("HOST", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        _tenantManagerMock
            .Setup(manager => manager.CreateAsync(
                "host",
                "Host Tenant",
                "Server=.;Database=HostDb;"))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(repository => repository.InsertAsync(tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(repository => repository.UpdateAsync(tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await _tenantAppService.CreateAsync(new CreateTenantDto
        {
            Name = "host",
            DisplayName = "Host Tenant",
            DefaultConnectionString = "Server=.;Database=HostDb;"
        });

        result.Id.Should().Be(tenant.Id);
        result.Name.Should().Be("host");
        result.DisplayName.Should().Be("Host Tenant");
        result.DefaultConnectionString.Should().Be("Server=.;Database=HostDb;");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetListAsync_WithIsActiveFilter_ReturnsFilteredTenants()
    {
        var activeTenant = new Tenant(Guid.NewGuid(), "host")
        {
            DisplayName = "Host Tenant",
            IsActive = true,
            CreationTime = DateTime.UtcNow
        };
        var inactiveTenant = new Tenant(Guid.NewGuid(), "archived")
        {
            DisplayName = "Archived Tenant",
            IsActive = false,
            CreationTime = DateTime.UtcNow
        };

        _tenantRepositoryMock
            .Setup(repository => repository.GetListWithDetailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { inactiveTenant, activeTenant });

        var result = await _tenantAppService.GetListAsync(true);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("host");
    }

    private static TenantInitializationOrchestrator CreateSuccessfulOrchestrator()
    {
        var storeMock = new Mock<ITenantInitializationStore>();
        var correlationId = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var record = new TenantInitializationRecord(
            Guid.NewGuid(), tenantId, 1, correlationId);

        storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        storeMock
            .Setup(s => s.UpdateAsync(
                It.IsAny<TenantInitializationRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dbInitMock = new Mock<ITenantDatabaseInitializer>();
        dbInitMock
            .Setup(d => d.InitializeAsync(
                It.IsAny<TenantInitializationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());

        var migrationMock = new Mock<ITenantMigrationRunner>();
        migrationMock
            .Setup(m => m.RunAsync(
                It.IsAny<TenantInitializationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantMigrationResult.Succeeded());

        var seederMock = new Mock<ITenantDataSeeder>();
        seederMock
            .Setup(s => s.SeedAsync(
                It.IsAny<TenantInitializationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSeedResult.Succeeded());

        var settingsMock = new Mock<ITenantSettingDefaultsSeeder>();
        settingsMock
            .Setup(s => s.SeedAsync(
                It.IsAny<TenantInitializationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSettingDefaultsResult.Succeeded());

        var featuresMock = new Mock<ITenantFeatureDefaultsSeeder>();
        featuresMock
            .Setup(f => f.SeedAsync(
                It.IsAny<TenantInitializationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantFeatureDefaultsResult.Succeeded());

        var loggerMock = new Mock<ILogger<TenantInitializationOrchestrator>>();

        return new TenantInitializationOrchestrator(
            dbInitMock.Object,
            migrationMock.Object,
            seederMock.Object,
            settingsMock.Object,
            featuresMock.Object,
            storeMock.Object,
            loggerMock.Object);
    }
}
