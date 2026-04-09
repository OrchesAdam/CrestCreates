using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Identity;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Identity;

public class RoleAppServiceTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly RoleAppService _roleAppService;

    public RoleAppServiceTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _currentTenantMock = new Mock<ICurrentTenant>();
        _currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns(string.Empty);
        _roleAppService = new RoleAppService(_roleRepositoryMock.Object, _currentTenantMock.Object);
    }

    [Fact]
    public async Task CreateAsync_WhenRoleAlreadyExists_ThrowsInvalidOperationException()
    {
        _roleRepositoryMock
            .Setup(repository => repository.FindByNameAsync("Admin", "tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role(Guid.NewGuid(), "Admin", "tenant-a"));

        var action = async () => await _roleAppService.CreateAsync(new CreateIdentityRoleDto
        {
            Name = "Admin",
            TenantId = "tenant-a"
        });

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
    }

    [Fact]
    public async Task SetActiveAsync_UpdatesRoleState()
    {
        var roleId = Guid.NewGuid();
        var role = new Role(roleId, "Admin", "tenant-a")
        {
            IsActive = true
        };

        _roleRepositoryMock
            .Setup(repository => repository.GetAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _roleRepositoryMock
            .Setup(repository => repository.UpdateAsync(role, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        await _roleAppService.SetActiveAsync(roleId, false);

        role.IsActive.Should().BeFalse();
        role.LastModificationTime.Should().NotBeNull();
    }

    [Fact]
    public async Task GetListAsync_WithoutTenantId_UsesCurrentTenant()
    {
        _currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns("tenant-a");
        _roleRepositoryMock
            .Setup(repository => repository.GetListAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Role, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.Generic.List<Role>
            {
                new(Guid.NewGuid(), "Admin", "tenant-a")
            });

        var result = await _roleAppService.GetListAsync();

        result.Should().ContainSingle();
        result[0].TenantId.Should().Be("tenant-a");
    }
}
