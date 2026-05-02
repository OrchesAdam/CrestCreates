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
    private readonly Mock<TenantInitializationOrchestrator> _orchestratorMock;
    private readonly Mock<ITenantInitializationStore> _storeMock;
    private readonly TenantAppService _tenantAppService;

    public TenantAppServiceTests()
    {
        _tenantManagerMock = new Mock<ITenantManager>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _orchestratorMock = new Mock<TenantInitializationOrchestrator>(
            Mock.Of<ITenantDatabaseInitializer>(),
            Mock.Of<ITenantMigrationRunner>(),
            Mock.Of<ITenantDataSeeder>(),
            Mock.Of<ITenantSettingDefaultsSeeder>(),
            Mock.Of<ITenantFeatureDefaultsSeeder>(),
            Mock.Of<ITenantInitializationStore>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<TenantInitializationOrchestrator>>());
        _storeMock = new Mock<ITenantInitializationStore>();
        _tenantAppService = new TenantAppService(
            _tenantManagerMock.Object,
            _tenantRepositoryMock.Object,
            _orchestratorMock.Object,
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

        _orchestratorMock
            .Setup(o => o.InitializeAsync(
                It.IsAny<TenantInitializationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantInitializationResult.Succeeded(
                Guid.NewGuid().ToString("N"),
                Array.Empty<TenantInitializationStep>()));

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
}
