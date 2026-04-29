using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.EFCore.Configuration;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

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
            var user = await modifyContext.Users.SingleAsync();
            user.Email = "updated@example.com";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => modifyContext.SaveChangesAsync());

            Assert.Contains("tenant-1", exception.Message);
            Assert.Contains("tenant-2", exception.Message);
        }

        await using (var deleteContext = new CrestCreatesDbContext(CreateOptions(databaseName, "tenant-2")))
        {
            var user = await deleteContext.Users.SingleAsync();
            deleteContext.Users.Remove(user);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());

            Assert.Contains("tenant-1", exception.Message);
            Assert.Contains("tenant-2", exception.Message);
        }
    }

    private static DbContextOptions<CrestCreatesDbContext> CreateOptions(Guid databaseName, string tenantId)
    {
        return new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"{databaseName:N}")
            .AddInterceptors(new MultiTenancyInterceptor(new FakeCurrentTenant(tenantId)))
            .Options;
    }

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public FakeCurrentTenant(string tenantId)
        {
            Id = tenantId;
            Tenant = new FakeTenantInfo(tenantId);
        }

        public ITenantInfo Tenant { get; }

        public string Id { get; }

        public Task<IDisposable> ChangeAsync(string tenantId)
        {
            return Task.FromResult<IDisposable>(new NoopDisposable());
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
