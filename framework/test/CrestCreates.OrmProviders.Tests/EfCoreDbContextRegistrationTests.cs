using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.EFCore.Configuration;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Interceptors;
using CrestCreates.OrmProviders.EFCore.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class EfCoreDbContextRegistrationTests
{
    [Fact]
    public void AddCrestCreatesEfCoreDbContext_Wires_Provider_Contributor_And_Interceptors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(new FakeCurrentTenant("tenant-1"));
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser("9b2f4dc0-3c3f-4f1b-9d4f-7854c5e4f6c1"));
        services.AddSingleton<IEfCoreDbContextOptionsContributor>(
            new DelegateEfCoreDbContextOptionsContributor(options => options.UseSqlite("Data Source=:memory:")));
        services.AddCrestCreatesEfCoreDbContext();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CrestCreatesDbContext>>();
        var context = scope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();

        Assert.Equal("tenant-1", context.CurrentTenantId);
        Assert.Contains(options.Extensions, extension =>
            extension.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
        Assert.IsType<AuditInterceptor>(scope.ServiceProvider.GetRequiredService<AuditInterceptor>());
        Assert.IsType<MultiTenancyInterceptor>(scope.ServiceProvider.GetRequiredService<MultiTenancyInterceptor>());
        Assert.Same(context, scope.ServiceProvider.GetRequiredService<IEntityFrameworkCoreDbContext>());
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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public string UserName => "tester";

        public bool IsAuthenticated => true;

        public string TenantId => "tenant-1";

        public string[] Roles => Array.Empty<string>();

        public Guid? OrganizationId => null;

        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();

        public int DataScopeValue => 0;

        public bool IsSuperAdmin => false;

        public string FindClaimValue(string claimType) => string.Empty;

        public string[] FindClaimValues(string claimType) => Array.Empty<string>();

        public bool IsInRole(string roleName) => false;

        public bool IsInOrganization(Guid orgId) => false;
    }

    [Fact]
    public async Task CrestCreatesDbContext_OnModelCreating_Should_ApplyGeneratedTenantFilters_WhenRegistryHasRegistrations()
    {
        TenantFilterRegistryStore.Clear();

        try
        {
            var applyAllCalls = 0;
            TenantFilterRegistryStore.Register((modelBuilder, currentTenant) =>
            {
                applyAllCalls++;
                modelBuilder.Entity<Role>().HasQueryFilter(entity => entity.TenantId == currentTenant.Id);
            });

            var context = new CrestCreatesDbContext(
                new DbContextOptionsBuilder<CrestCreatesDbContext>().Options,
                new FakeCurrentTenant("tenant-1"));
            var modelBuilder = new ModelBuilder(new ConventionSet());

            InvokeOnModelCreating(context, modelBuilder);

            Assert.Equal(1, applyAllCalls);
            Assert.NotNull(modelBuilder.Model.FindEntityType(typeof(Role))?.GetQueryFilter());
        }
        finally
        {
            TenantFilterRegistryStore.Clear();
        }
    }

    [Fact]
    public void CrestCreatesDbContext_OnModelCreating_Should_NotThrow_WhenNoTenantFilterRegistrationsExist()
    {
        TenantFilterRegistryStore.Clear();

        var context = new CrestCreatesDbContext(
            new DbContextOptionsBuilder<CrestCreatesDbContext>().Options,
            new FakeCurrentTenant("tenant-1"));
        var modelBuilder = new ModelBuilder(new ConventionSet());

        Assert.False(TenantFilterRegistryStore.HasRegistrations);
        InvokeOnModelCreating(context, modelBuilder);
        Assert.Null(modelBuilder.Model.FindEntityType(typeof(Role))?.GetQueryFilter());
    }

    [Fact]
    public async Task CrestCreatesDbContext_Should_Save_AuditLog_ExtraProperties()
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new CrestCreatesDbContext(options);
        await context.Database.OpenConnectionAsync();

        try
        {
            await context.Database.EnsureCreatedAsync();

            var auditLog = new AuditLog(Guid.NewGuid())
            {
                Duration = 123,
                ExecutionTime = DateTime.UtcNow,
                CreationTime = DateTime.UtcNow,
                Status = 0,
                ExtraProperties = new Dictionary<string, object>
                {
                    ["source"] = "test",
                    ["attempt"] = 2
                }
            };

            context.AuditLogs.Add(auditLog);
            await context.SaveChangesAsync();

            var saved = await context.AuditLogs.AsNoTracking().SingleAsync();

            Assert.Equal("test", saved.ExtraProperties["source"]?.ToString());
            Assert.Equal("2", saved.ExtraProperties["attempt"]?.ToString());
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static void InvokeOnModelCreating(CrestCreatesDbContext context, ModelBuilder modelBuilder)
    {
        typeof(CrestCreatesDbContext)
            .GetMethod("OnModelCreating", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, new object[] { modelBuilder });
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

