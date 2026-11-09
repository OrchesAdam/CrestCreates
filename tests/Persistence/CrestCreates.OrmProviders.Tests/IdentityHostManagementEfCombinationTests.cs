using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Identity;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Security.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// Authorized host user management must reach a real EF save instead of dying at the tenancy
/// boundary: the AppService gate, the real repository and the save interceptor all participate.
/// </summary>
[Collection("ModelFilterRegistry")]
public class IdentityHostManagementEfCombinationTests
{
    private const string TenantA = "tenant-a";

    [Fact]
    public async Task Authorized_host_creates_and_a_tenant_admin_updates_a_user_through_ef()
    {
        var databaseName = Guid.NewGuid();
        var hostTenant = new Mock<ICurrentTenant>();
        hostTenant.SetupGet(tenant => tenant.Id).Returns((string?)null);
        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker
            .Setup(checker => checker.CheckAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var featureChecker = new Mock<IFeatureChecker>();
        featureChecker
            .Setup(checker => checker.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.Id).Returns(Guid.NewGuid().ToString());
        currentUser.SetupGet(user => user.IsSuperAdmin).Returns(false);
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(hasher => hasher.HashPassword(It.IsAny<string>())).Returns("hashed-password");

        Guid createdUserId;
        await using (var hostContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, hostTenant.Object), hostTenant.Object))
        {
            var hostService = new UserAppService(
                new UserRepository(new EfCoreDbContextAdapter(hostContext), hostTenant.Object, new DataFilterState()),
                Mock.Of<IRoleRepository>(),
                Mock.Of<IUserRoleRepository>(),
                passwordHasher.Object,
                Mock.Of<IPasswordPolicyValidator>(),
                hostTenant.Object,
                currentUser.Object,
                featureChecker.Object,
                Mock.Of<IIdentitySecurityLogWriter>(),
                permissionChecker.Object);

            var created = await hostService.CreateAsync(new CreateIdentityUserDto
            {
                UserName = "alice",
                Email = "alice@example.com",
                Password = "P@ssw0rd!",
                TenantId = TenantA
            });

            created.TenantId.Should().Be(TenantA);
            createdUserId = created.Id;
        }

        await using (var verify = new CrestCreatesDbContext(CreateOptions(databaseName, hostTenant.Object)))
        {
            var persisted = await verify.Users.IgnoreQueryFilters().SingleAsync();
            persisted.Id.Should().Be(createdUserId);
            persisted.TenantId.Should().Be(TenantA);
            persisted.Email.Should().Be("alice@example.com");
        }

        var tenantA = new Mock<ICurrentTenant>();
        tenantA.SetupGet(tenant => tenant.Id).Returns(TenantA);
        var tenantAdmin = new Mock<ICurrentUser>();
        tenantAdmin.SetupGet(user => user.IsAuthenticated).Returns(true);
        tenantAdmin.SetupGet(user => user.Id).Returns(Guid.NewGuid().ToString());
        tenantAdmin.SetupGet(user => user.IsSuperAdmin).Returns(false);

        await using (var tenantContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, tenantA.Object), tenantA.Object))
        {
            Assert.Equal(1, await tenantContext.Users.IgnoreQueryFilters().CountAsync());
            Assert.Equal(1, await tenantContext.Users.CountAsync());
            var tenantRepository = new UserRepository(
                new EfCoreDbContextAdapter(tenantContext), tenantA.Object, new DataFilterState());
            Assert.NotNull(await tenantRepository.GetAsync(createdUserId));
            var tenantService = new UserAppService(
                tenantRepository,
                Mock.Of<IRoleRepository>(),
                Mock.Of<IUserRoleRepository>(),
                passwordHasher.Object,
                Mock.Of<IPasswordPolicyValidator>(),
                tenantA.Object,
                tenantAdmin.Object,
                featureChecker.Object,
                Mock.Of<IIdentitySecurityLogWriter>(),
                permissionChecker.Object);

            var updated = await tenantService.UpdateAsync(
                createdUserId,
                new UpdateIdentityUserDto { Email = "alice.updated@example.com" });

            updated.Email.Should().Be("alice.updated@example.com");
            updated.TenantId.Should().Be(TenantA);
        }

        await using (var verify = new CrestCreatesDbContext(CreateOptions(databaseName, hostTenant.Object)))
        {
            var persisted = await verify.Users.IgnoreQueryFilters().SingleAsync();
            persisted.Email.Should().Be("alice.updated@example.com");
            persisted.TenantId.Should().Be(TenantA);
        }
    }

    private static DbContextOptions<CrestCreatesDbContext> CreateOptions(
        Guid databaseName,
        ICurrentTenant currentTenant)
    {
        return new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"{databaseName:N}")
            .AddInterceptors(new MultiTenancyInterceptor(currentTenant))
            .ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>()
            .Options;
    }
}
