using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared;
using CrestCreates.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantManagerTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly TenantManager _tenantManager;

    public TenantManagerTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        var logger = Mock.Of<ILogger<TenantManager>>();
        _tenantManager = new TenantManager(
            _tenantRepositoryMock.Object,
            logger);
    }

    [Fact]
    public async Task CreateAsync_WithValidInput_ReturnsTenantWithCorrectProperties()
    {
        // TenantManager.CreateAsync is now a pure aggregate factory — no I/O.
        // Repository operations (uniqueness check, insert) moved to TenantAppService.
        var result = await _tenantManager.CreateAsync(
            "host",
            "Host Tenant",
            "Server=.;Database=HostDb;");

        result.Name.Should().Be("host");
        result.NormalizedName.Should().Be("HOST");
        result.DisplayName.Should().Be("Host Tenant");
        result.IsActive.Should().BeTrue();
        result.GetDefaultConnectionString().Should().Be("Server=.;Database=HostDb;");
        result.InitializationStatus.Should().Be(TenantInitializationStatus.Pending);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_CreatesTenantDirectly()
    {
        // TenantManager.CreateAsync no longer checks for duplicates;
        // the duplicate check has moved to TenantAppService.CreateAsync.
        // The manager simply creates a tenant instance.
        var result = await _tenantManager.CreateAsync(
            "host",
            "Host Tenant",
            null);

        result.Name.Should().Be("host");
        result.NormalizedName.Should().Be("HOST");
        result.DisplayName.Should().Be("Host Tenant");
        result.IsActive.Should().BeTrue();
        result.GetDefaultConnectionString().Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithEmptyConnectionString_RemovesDefaultConnectionString()
    {
        var tenant = new Tenant(Guid.NewGuid(), "host")
        {
            DisplayName = "Old Name"
        };
        tenant.SetDefaultConnectionString("Server=.;Database=OldDb;");

        _tenantRepositoryMock
            .Setup(repository => repository.FindByNameAsync("host", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _tenantRepositoryMock
            .Setup(repository => repository.UpdateAsync(tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await _tenantManager.UpdateAsync(
            "host",
            "New Name",
            "   ");

        result.DisplayName.Should().Be("New Name");
        result.GetDefaultConnectionString().Should().BeNull();
        tenant.LastModificationTime.Should().NotBeNull();
    }
}
