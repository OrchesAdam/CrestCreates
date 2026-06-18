using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Permissions;
using CrestCreates.Application.Permissions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Permissions;

public class PermissionGrantAppServiceTests
{
    [Fact]
    public async Task GetUserEffectivePermissionsAsync_WithTenantId_OnlyUsesRolesInCurrentTenant()
    {
        var permissionGrantManagerMock = new Mock<IPermissionGrantManager>();
        var roleRepositoryMock = new Mock<IRoleRepository>();
        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns("tenant-a");

        roleRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new List<Role>
            {
                new()
                {
                    Name = "Librarian",
                    TenantId = "tenant-a"
                },
                new()
                {
                    Name = "Auditor",
                    TenantId = "tenant-b"
                }
            });

        permissionGrantManagerMock
            .Setup(manager => manager.GetEffectivePermissionsAsync(
                "11111111-1111-1111-1111-111111111111",
                It.Is<IEnumerable<string>>(roles => roles.SequenceEqual(new[] { "Librarian" })),
                "tenant-a",
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new[] { "Books.View" });

        var service = new PermissionGrantAppService(
            permissionGrantManagerMock.Object,
            roleRepositoryMock.Object,
            currentTenantMock.Object);

        var result = await service.GetUserEffectivePermissionsAsync(
            "11111111-1111-1111-1111-111111111111",
            "tenant-a");

        result.Should().BeEquivalentTo(new UserEffectivePermissionsDto
        {
            UserId = "11111111-1111-1111-1111-111111111111",
            TenantId = "tenant-a",
            Permissions = new[] { "Books.View" }
        });
    }
}
