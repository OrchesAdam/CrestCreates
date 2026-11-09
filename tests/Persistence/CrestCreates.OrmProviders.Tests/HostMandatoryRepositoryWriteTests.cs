using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// Host administration of explicitly owned mandatory rows must survive the repository ownership
/// guard and still reject empty ownership, ownership migration and tenant A writing tenant B.
/// </summary>
[Collection("ModelFilterRegistry")]
public class HostMandatoryRepositoryWriteTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    [Fact]
    public async Task Host_updates_and_deletes_an_explicit_mandatory_row_through_the_real_repository()
    {
        var databaseName = Guid.NewGuid();
        var hostTenant = HostTenant();

        await using (var seedContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, hostTenant.Object), hostTenant.Object))
        {
            seedContext.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", TenantA));
            await seedContext.SaveChangesAsync();
        }

        await using (var mutateContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, hostTenant.Object), hostTenant.Object))
        {
            var repository = new UserRepository(
                new EfCoreDbContextAdapter(mutateContext), hostTenant.Object, new DataFilterState());
            var user = await repository.GetQueryableUnfiltered().SingleAsync();
            user.Email = "alice.updated@example.com";

            await repository.UpdateAsync(user);

            var persisted = await repository.GetQueryableUnfiltered().AsNoTracking().SingleAsync();
            persisted.Email.Should().Be("alice.updated@example.com");
            persisted.TenantId.Should().Be(TenantA);
        }

        await using (var deleteContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, hostTenant.Object), hostTenant.Object))
        {
            var repository = new UserRepository(
                new EfCoreDbContextAdapter(deleteContext), hostTenant.Object, new DataFilterState());
            var user = await repository.GetQueryableUnfiltered().SingleAsync();

            await repository.DeleteAsync(user);

            (await repository.GetQueryableUnfiltered().CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task Repository_rejects_ownership_migration_and_foreign_tenant_writes()
    {
        var databaseName = Guid.NewGuid();
        var hostTenant = HostTenant();

        await using (var seedContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, hostTenant.Object), hostTenant.Object))
        {
            seedContext.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", TenantA));
            seedContext.Users.Add(new User(Guid.NewGuid(), "bob", "bob@example.com", TenantB));
            await seedContext.SaveChangesAsync();
        }

        await using (var hostContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, hostTenant.Object), hostTenant.Object))
        {
            var repository = new UserRepository(
                new EfCoreDbContextAdapter(hostContext), hostTenant.Object, new DataFilterState());
            var user = await repository.GetQueryableUnfiltered().SingleAsync(item => item.UserName == "alice");
            user.TenantId = TenantB;

            var act = async () => await repository.UpdateAsync(user);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*change tenant ownership*");
        }

        var tenantA = new Mock<ICurrentTenant>();
        tenantA.SetupGet(tenant => tenant.Id).Returns(TenantA);
        await using (var tenantContext = new CrestCreatesDbContext(
                         CreateOptions(databaseName, tenantA.Object), tenantA.Object))
        {
            var repository = new UserRepository(
                new EfCoreDbContextAdapter(tenantContext), tenantA.Object, new DataFilterState());
            var user = await repository.GetQueryableUnfiltered().SingleAsync(item => item.UserName == "bob");
            user.Email = "bob.updated@example.com";

            var act = async () => await repository.UpdateAsync(user);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*current tenant*");
        }

        await using (var verify = new CrestCreatesDbContext(
                         CreateOptions(databaseName, hostTenant.Object), hostTenant.Object))
        {
            var users = await verify.Users.IgnoreQueryFilters().OrderBy(user => user.UserName).ToListAsync();
            users.Should().HaveCount(2);
            users.Single(user => user.UserName == "alice").TenantId.Should().Be(TenantA);
            users.Single(user => user.UserName == "bob").Email.Should().Be("bob@example.com");
        }
    }

    private static Mock<ICurrentTenant> HostTenant()
    {
        var hostTenant = new Mock<ICurrentTenant>();
        hostTenant.SetupGet(tenant => tenant.Id).Returns((string?)null);
        return hostTenant;
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
