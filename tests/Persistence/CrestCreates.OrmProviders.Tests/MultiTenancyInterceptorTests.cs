using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

[Collection("ModelFilterRegistry")]
public class MultiTenancyInterceptorTests
{
    [Fact]
    public async Task SavingChanges_Added_User_WithEmptyTenantId_ShouldSetCurrentTenantId()
    {
        var options = CreateOptions(Guid.NewGuid(), "tenant-1");

        await using var context = new CrestCreatesDbContext(options);
        var user = new User(Guid.NewGuid(), "alice", "alice@example.com", string.Empty);
        context.Users.Add(user);

        await context.SaveChangesAsync();

        Assert.Equal("tenant-1", user.TenantId);
    }

    [Fact]
    public async Task SavingChanges_ModifiedAndDeleted_User_ShouldValidateCurrentTenantId()
    {
        var databaseName = Guid.NewGuid();

        await using (var seedContext = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-1")))
        {
            seedContext.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", string.Empty));
            await seedContext.SaveChangesAsync();
        }

        await using (var modifyContext = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-2")))
        {
            // This test targets the write interceptor; the generated tenant filter (S3) correctly
            // hides the tenant-1 row from normal reads, so load it via the explicit test bypass.
            var user = await modifyContext.Users.IgnoreQueryFilters().SingleAsync();
            user.Email = "updated@example.com";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => modifyContext.SaveChangesAsync());

            Assert.Contains("tenant-1", exception.Message);
            Assert.Contains("tenant-2", exception.Message);
        }

        await using (var deleteContext = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-2")))
        {
            var user = await deleteContext.Users.IgnoreQueryFilters().SingleAsync();
            deleteContext.Users.Remove(user);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());

            Assert.Contains("tenant-1", exception.Message);
            Assert.Contains("tenant-2", exception.Message);
        }
    }

    [Fact]
    public async Task SavingChanges_Host_Added_MandatoryTenantWithoutTenantId_IsRejectedBeforeExecution()
    {
        var databaseName = Guid.NewGuid();
        await using (var context = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            context.Users.Add(new User(Guid.NewGuid(), "hostless", "hostless@example.com", string.Empty));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

            Assert.Contains("no tenant in current context", exception.Message);
        }

        await AssertNoUsersAsync(databaseName);
    }

    [Fact]
    public async Task SavingChanges_Tenant_Added_MandatoryTenantForAnotherTenant_IsRejectedBeforeExecution()
    {
        var databaseName = Guid.NewGuid();
        await using (var context = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-a")))
        {
            context.Users.Add(new User(Guid.NewGuid(), "foreign", "foreign@example.com", "tenant-b"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

            Assert.Contains("tenant-b", exception.Message);
            Assert.Contains("tenant-a", exception.Message);
        }

        await AssertNoUsersAsync(databaseName);
    }

    [Fact]
    public async Task SavingChanges_Host_Added_MandatoryTenantWithExplicitTenant_IsAllowed()
    {
        var databaseName = Guid.NewGuid();
        await using (var context = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            context.Users.Add(new User(Guid.NewGuid(), "seeded", "seeded@example.com", "tenant-a"));

            await context.SaveChangesAsync();
        }

        await using var verify = new CrestCreatesDbContext(CreateOptions(databaseName, null));
        var persisted = await verify.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("tenant-a", persisted.TenantId);
    }

    [Fact]
    public async Task SavingChanges_Tenant_CannotChangePersistedTenantOwnership()
    {
        var databaseName = Guid.NewGuid();
        await using (var seedContext = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            seedContext.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", "tenant-a"));
            seedContext.Users.Add(new User(Guid.NewGuid(), "bob", "bob@example.com", "tenant-b"));
            await seedContext.SaveChangesAsync();
        }

        await using (var moveToB = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-a")))
        {
            var user = await moveToB.Users.IgnoreQueryFilters().SingleAsync(u => u.UserName == "alice");
            user.TenantId = "tenant-b";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => moveToB.SaveChangesAsync());
            Assert.Contains("change tenant ownership", exception.Message);
        }

        await using (var stealA = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-a")))
        {
            var user = await stealA.Users.IgnoreQueryFilters().SingleAsync(u => u.UserName == "bob");
            user.TenantId = "tenant-a";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => stealA.SaveChangesAsync());
            Assert.Contains("change tenant ownership", exception.Message);
        }
    }

    [Fact]
    public async Task SavingChanges_Host_CanWriteSharedOptionalTenantRows()
    {
        var databaseName = Guid.NewGuid();
        await using (var context = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null)))
        {
            context.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), Name = "shared" });
            await context.SaveChangesAsync();
        }

        await using (var modify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null)))
        {
            var entity = await modify.Entities.SingleAsync();
            entity.Name = "updated";
            await modify.SaveChangesAsync();
        }

        await using var verify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        var persisted = await verify.Entities.SingleAsync();
        Assert.Equal("updated", persisted.Name);
        Assert.True(string.IsNullOrEmpty(persisted.TenantId));
    }

    [Fact]
    public async Task SavingChanges_Tenant_Added_OptionalTenantForAnotherTenant_IsRejected()
    {
        var databaseName = Guid.NewGuid();
        await using var context = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a"));
        context.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), TenantId = "tenant-b" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("tenant-b", exception.Message);
        Assert.Contains("tenant-a", exception.Message);
    }

    [Fact]
    public async Task SavingChanges_Host_ModifyingOptionalRowOwnedByTenant_IsRejected()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            seed.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "owned" });
            await seed.SaveChangesAsync();
        }

        await using var host = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        var entity = await host.Entities.IgnoreQueryFilters().SingleAsync();
        entity.Name = "host-edit";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SaveChangesAsync());
        Assert.Contains("tenant-a", exception.Message);
    }

    [Fact]
    public async Task SavingChanges_Host_CanModifyAndDeleteMandatoryRowsWithExplicitUnchangedTenant()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            seed.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", "tenant-a"));
            await seed.SaveChangesAsync();
        }

        await using (var modify = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            var user = await modify.Users.IgnoreQueryFilters().SingleAsync();
            user.Email = "alice.updated@example.com";

            await modify.SaveChangesAsync();
        }

        await using (var verify = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            var persisted = await verify.Users.IgnoreQueryFilters().SingleAsync();
            Assert.Equal("alice.updated@example.com", persisted.Email);
            Assert.Equal("tenant-a", persisted.TenantId);
        }

        await using (var delete = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            var user = await delete.Users.IgnoreQueryFilters().SingleAsync();
            delete.Users.Remove(user);

            await delete.SaveChangesAsync();
        }

        await AssertNoUsersAsync(databaseName);
    }

    [Fact]
    public async Task SavingChanges_Tenant_CanModifyAndDeleteItsOwnMandatoryRows()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            seed.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", "tenant-a"));
            await seed.SaveChangesAsync();
        }

        await using (var modify = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-a")))
        {
            var user = await modify.Users.IgnoreQueryFilters().SingleAsync();
            user.Email = "alice.updated@example.com";

            await modify.SaveChangesAsync();
        }

        await using (var delete = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-a")))
        {
            var user = await delete.Users.IgnoreQueryFilters().SingleAsync();
            delete.Users.Remove(user);

            await delete.SaveChangesAsync();
        }

        await AssertNoUsersAsync(databaseName);
    }

    [Fact]
    public async Task SavingChanges_Host_CannotChangeMandatoryOwnershipAndNothingIsPersisted()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            seed.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", "tenant-a"));
            await seed.SaveChangesAsync();
        }

        await using (var mutate = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            var user = await mutate.Users.IgnoreQueryFilters().SingleAsync();
            user.Email = "changed@example.com";
            user.TenantId = "tenant-b";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mutate.SaveChangesAsync());
            Assert.Contains("change tenant ownership", exception.Message);
        }

        await using var verify = new CrestCreatesDbContext(CreateOptions(databaseName, null));
        var persisted = await verify.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("alice@example.com", persisted.Email);
        Assert.Equal("tenant-a", persisted.TenantId);
    }

    [Fact]
    public async Task SavingChanges_Host_CannotDeleteMandatoryRowWhileChangingOwnership()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            seed.Users.Add(new User(Guid.NewGuid(), "alice", "alice@example.com", "tenant-a"));
            await seed.SaveChangesAsync();
        }

        await using (var delete = new CrestCreatesDbContext(CreateOptions(databaseName, null)))
        {
            var user = await delete.Users.IgnoreQueryFilters().SingleAsync();
            user.TenantId = "tenant-b";
            delete.Users.Remove(user);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => delete.SaveChangesAsync());
            Assert.Contains("change tenant ownership", exception.Message);
        }

        await using var verify = new CrestCreatesDbContext(CreateOptions(databaseName, null));
        var persisted = await verify.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("tenant-a", persisted.TenantId);
    }

    [Fact]
    public async Task SavingChanges_Tenant_CanModifyAndDeleteItsOwnOptionalRow()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            seed.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "owned" });
            await seed.SaveChangesAsync();
        }

        await using (var modify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            var entity = await modify.Entities.SingleAsync();
            entity.Name = "updated";

            await modify.SaveChangesAsync();
        }

        await using (var delete = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            var entity = await delete.Entities.SingleAsync();
            delete.Entities.Remove(entity);

            await delete.SaveChangesAsync();
        }

        await using var verify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        Assert.Equal(0, await verify.Entities.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SavingChanges_Host_CanDeleteSharedOptionalRow()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null)))
        {
            seed.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), Name = "shared" });
            await seed.SaveChangesAsync();
        }

        await using (var delete = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null)))
        {
            var entity = await delete.Entities.SingleAsync();
            delete.Entities.Remove(entity);

            await delete.SaveChangesAsync();
        }

        await using var verify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        Assert.Equal(0, await verify.Entities.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SavingChanges_Host_CannotDeleteOptionalRowOwnedByTenant()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            seed.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "owned" });
            await seed.SaveChangesAsync();
        }

        await using (var host = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null)))
        {
            var entity = await host.Entities.IgnoreQueryFilters().SingleAsync();
            host.Entities.Remove(entity);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SaveChangesAsync());
            Assert.Contains("tenant-a", exception.Message);
        }

        await using var verify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        Assert.Equal(1, await verify.Entities.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SavingChanges_SharedOptionalRow_CannotBecomeTenantOwned()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null)))
        {
            seed.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), Name = "shared" });
            await seed.SaveChangesAsync();
        }

        await using (var tenant = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            var entity = await tenant.Entities.IgnoreQueryFilters().SingleAsync();
            entity.TenantId = "tenant-a";
            entity.Name = "claimed";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tenant.SaveChangesAsync());
            Assert.Contains("change tenant ownership", exception.Message);
            Assert.Contains("<shared>", exception.Message);
        }

        await using var verify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        var persisted = await verify.Entities.IgnoreQueryFilters().SingleAsync();
        Assert.True(string.IsNullOrEmpty(persisted.TenantId));
        Assert.Equal("shared", persisted.Name);
    }

    [Fact]
    public async Task SavingChanges_TenantOwnedOptionalRow_CannotBecomeShared()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            seed.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "owned" });
            await seed.SaveChangesAsync();
        }

        await using (var tenant = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            var entity = await tenant.Entities.SingleAsync();
            entity.TenantId = string.Empty;
            entity.Name = "released";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tenant.SaveChangesAsync());
            Assert.Contains("change tenant ownership", exception.Message);
        }

        await using var verify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        var persisted = await verify.Entities.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("tenant-a", persisted.TenantId);
        Assert.Equal("owned", persisted.Name);
    }

    [Fact]
    public async Task SavingChanges_OptionalRow_CannotMoveToAnotherTenant()
    {
        var databaseName = Guid.NewGuid();
        await using (var seed = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            seed.Entities.Add(new OptionalTenantEntity { Id = Guid.NewGuid(), TenantId = "tenant-a", Name = "owned" });
            await seed.SaveChangesAsync();
        }

        await using (var tenant = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, "tenant-a")))
        {
            var entity = await tenant.Entities.SingleAsync();
            entity.TenantId = "tenant-b";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tenant.SaveChangesAsync());
            Assert.Contains("change tenant ownership", exception.Message);
        }

        await using var verify = new OptionalTenantDbContext(CreateOptionalOptions(databaseName, null));
        var persisted = await verify.Entities.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("tenant-a", persisted.TenantId);
    }

    private static async Task AssertNoUsersAsync(Guid databaseName)
    {
        await using var verify = new CrestCreatesDbContext(CreateOptions(databaseName, null));
        Assert.Equal(0, await verify.Users.IgnoreQueryFilters().CountAsync());
    }

    private static DbContextOptions<CrestCreatesDbContext> CreateOptions(Guid databaseName, string? tenantId)
    {
        return new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"{databaseName:N}")
            .AddInterceptors(new MultiTenancyInterceptor(new FakeCurrentTenant(tenantId)))
            .Options;
    }

    private static DbContextOptions<OptionalTenantDbContext> CreateOptionalOptions(Guid databaseName, string? tenantId)
    {
        return new DbContextOptionsBuilder<OptionalTenantDbContext>()
            .UseInMemoryDatabase($"optional-{databaseName:N}")
            .AddInterceptors(new MultiTenancyInterceptor(new FakeCurrentTenant(tenantId)))
            .Options;
    }

    public sealed class OptionalTenantDbContext : DbContext
    {
        public OptionalTenantDbContext(DbContextOptions<OptionalTenantDbContext> options)
            : base(options)
        {
        }

        public DbSet<OptionalTenantEntity> Entities => Set<OptionalTenantEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OptionalTenantEntity>().HasKey(entity => entity.Id);
        }
    }

    public sealed class OptionalTenantEntity : CrestCreates.Data.EFCore.MultiTenancy.IMultiTenant
    {
        public Guid Id { get; set; }

        public string TenantId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public FakeCurrentTenant(string? tenantId)
        {
            Id = tenantId;
            Tenant = tenantId is null ? null : new FakeTenantInfo(tenantId);
        }

        public ITenantInfo? Tenant { get; }

        public string? Id { get; }

        public Task<IDisposable> ChangeAsync(string tenantId)
        {
            return Task.FromResult<IDisposable>(new NoopDisposable());
        }

        public IDisposable Change(ITenantInfo tenant)
        {
            throw new NotImplementedException();
        }

        public void SetTenantId(string tenantId)
        {
        }
    }

    private sealed class FakeTenantInfo : ITenantInfo
    {
        public FakeTenantInfo(string name)
        {
            Id = name;
            Name = name;
            ConnectionString = "Data Source=:memory:";
        }

        public string Id { get; }

        public string Name { get; }

        public string? ConnectionString { get; }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
