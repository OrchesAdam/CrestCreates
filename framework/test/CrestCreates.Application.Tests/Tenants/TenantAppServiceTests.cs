using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantAppServiceTests
{
    private readonly Mock<ITenantManager> _tenantManagerMock;
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly TenantAppService _tenantAppService;

    public TenantAppServiceTests()
    {
        _tenantManagerMock = new Mock<ITenantManager>();
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _tenantAppService = new TenantAppService(
            _tenantManagerMock.Object,
            _tenantRepositoryMock.Object);
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

        _tenantManagerMock
            .Setup(manager => manager.CreateAsync(
                "host",
                "Host Tenant",
                "Server=.;Database=HostDb;",
                It.IsAny<CancellationToken>()))
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
