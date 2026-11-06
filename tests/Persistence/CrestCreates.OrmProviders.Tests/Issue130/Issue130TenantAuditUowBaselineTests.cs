using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.FreeSql.Extensions;
using CrestCreates.Data.FreeSql.Interceptors;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Data.SqlSugar.Interceptors;
using CrestCreates.Data.SqlSugar.Modules;
using CrestCreates.Data.SqlSugar.UnitOfWork;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

/// <summary>
/// #130 S1 tenant (T), audit (A), transaction/cancellation (U) baselines. The tenant red cases
/// prove that detached writes are not validated against persisted tenant facts, and that SDK
/// filters capture the tenant when they are configured instead of resolving it per operation.
/// </summary>
public sealed class Issue130TenantAuditUowBaselineTests : IDisposable
{
    private readonly Issue130SqliteFile _database = new();
    private readonly Issue130CurrentUser _user = new();
    private readonly Issue130CurrentUserProvider _userProvider = new();
    private readonly Issue130MutableTenant _tenant = new("tenant-a");

    public void Dispose() => _database.Dispose();

    private void InsertTenantRow(long id, string tenantId, string name, string stamp = "stamp-1")
    {
        _database.Execute($"""
            INSERT INTO "{Issue130Tables.Tenant}"
                ("Id","TenantId","Name","CreationTime","CreatorId","LastModificationTime","LastModifierId","ConcurrencyStamp")
            VALUES ({id}, {Issue130SqliteFile.Quote(tenantId)}, {Issue130SqliteFile.Quote(name)},
                    '2026-01-01 00:00:00', NULL, NULL, NULL, {Issue130SqliteFile.Quote(stamp)});
            """);
    }

    private void InsertAuditedRow(long id, string name)
    {
        _database.Execute($"""
            INSERT INTO "{Issue130Tables.SoftStamped}"
                ("Id","Name","CreationTime","CreatorId","LastModificationTime","LastModifierId",
                 "ConcurrencyStamp","IsDeleted","DeletionTime","DeleterId")
            VALUES ({id}, {Issue130SqliteFile.Quote(name)}, '2026-01-01 00:00:00',
                    '22222222-2222-2222-2222-222222222222', NULL, NULL, 'stamp-{id}', 0, NULL, NULL);
            """);
    }

    // ---------------------------------------------------------------- Tenant reads

    [Fact]
    public async Task T_FreeSql_stable_tenant_filter_isolates_rows()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "a");
        InsertTenantRow(2, "tenant-b", "b");
        var repository = new Issue130FreeSqlRepository<Issue130TenantEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        var rows = await repository.GetListAsync();

        Assert.Single(rows);
        Assert.Equal("tenant-a", rows[0].TenantId);
    }

    [Fact]
    public async Task T_FreeSql_filter_must_resolve_the_current_tenant_per_operation()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "a");
        InsertTenantRow(2, "tenant-b", "b");
        var repository = new Issue130FreeSqlRepository<Issue130TenantEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        _tenant.Id = "tenant-b";

        var rows = await repository.GetListAsync();

        Assert.Single(rows);
        Assert.Equal("tenant-b", rows[0].TenantId);
    }

    [Fact]
    public async Task T_SqlSugar_stable_tenant_filter_isolates_rows()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "a");
        InsertTenantRow(2, "tenant-b", "b");
        var repository = new Issue130SqlSugarRepository<Issue130TenantEntity>(client, _userProvider, _tenant);

        var rows = await repository.GetListAsync();

        Assert.Single(rows);
        Assert.Equal("tenant-a", rows[0].TenantId);
    }

    [Fact]
    public async Task T_SqlSugar_filter_must_resolve_the_current_tenant_per_operation()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "a");
        InsertTenantRow(2, "tenant-b", "b");
        var repository = new Issue130SqlSugarRepository<Issue130TenantEntity>(client, _userProvider, _tenant);

        _tenant.Id = "tenant-b";

        var rows = await repository.GetListAsync();

        Assert.Single(rows);
        Assert.Equal("tenant-b", rows[0].TenantId);
    }

    // ---------------------------------------------------------------- Detached writes

    [Fact]
    public async Task T_EfCore_forged_detached_update_must_not_change_another_tenants_row()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertTenantRow(1, "tenant-a", "original");
        _tenant.Id = "tenant-b";

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130TenantEntity>(context, _tenant);
        var forged = new Issue130TenantEntity(1, "tenant-b", "forged") { ConcurrencyStamp = "stamp-1" };

        var exception = await Record.ExceptionAsync(() => repository.UpdateAsync(forged));

        Assert.NotNull(exception);
        var row = _database.Read(Issue130Tables.Tenant, 1);
        Assert.Equal("tenant-a", row.Text("TenantId"));
        Assert.Equal("original", row.Text("Name"));
    }

    [Fact]
    public async Task T_EfCore_cross_tenant_id_delete_is_rejected_by_the_save_interceptor()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertTenantRow(1, "tenant-a", "original");
        _tenant.Id = "tenant-b";

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130TenantEntity>(context, _tenant);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(1));

        Assert.Equal(1, _database.Count(Issue130Tables.Tenant));
    }

    [Fact]
    public async Task T_FreeSql_forged_detached_update_must_not_change_another_tenants_row()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "original");
        _tenant.Id = "tenant-b";
        var repository = new Issue130FreeSqlRepository<Issue130TenantEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);
        var forged = new Issue130TenantEntity(1, "tenant-b", "forged") { ConcurrencyStamp = "stamp-1" };

        var exception = await Record.ExceptionAsync(() => repository.UpdateAsync(forged));

        Assert.NotNull(exception);
        var row = _database.Read(Issue130Tables.Tenant, 1);
        Assert.Equal("tenant-a", row.Text("TenantId"));
        Assert.Equal("original", row.Text("Name"));
    }

    [Fact]
    public async Task T_FreeSql_cross_tenant_id_delete_must_not_remove_the_row()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "original");
        _tenant.Id = "tenant-b";
        var repository = new Issue130FreeSqlRepository<Issue130TenantEntity>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        var exception = await Record.ExceptionAsync(() => repository.DeleteAsync(1));

        Assert.NotNull(exception);
        var row = _database.Read(Issue130Tables.Tenant, 1);
        Assert.True(row.Exists);
        Assert.Equal("tenant-a", row.Text("TenantId"));
        Assert.Equal("original", row.Text("Name"));
    }

    [Fact]
    public async Task T_SqlSugar_forged_detached_update_must_not_change_another_tenants_row()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "original");
        _tenant.Id = "tenant-b";
        var repository = new Issue130SqlSugarRepository<Issue130TenantEntity>(client, _userProvider, _tenant);
        var forged = new Issue130TenantEntity(1, "tenant-b", "forged") { ConcurrencyStamp = "stamp-1" };

        var exception = await Record.ExceptionAsync(() => repository.UpdateAsync(forged));

        Assert.NotNull(exception);
        var row = _database.Read(Issue130Tables.Tenant, 1);
        Assert.Equal("tenant-a", row.Text("TenantId"));
        Assert.Equal("original", row.Text("Name"));
    }

    [Fact]
    public async Task T_SqlSugar_cross_tenant_id_delete_must_not_remove_the_row()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        InsertTenantRow(1, "tenant-a", "original");
        _tenant.Id = "tenant-b";
        var repository = new Issue130SqlSugarRepository<Issue130TenantEntity>(client, _userProvider, _tenant);

        var exception = await Record.ExceptionAsync(() => repository.DeleteAsync(1));

        Assert.NotNull(exception);
        var row = _database.Read(Issue130Tables.Tenant, 1);
        Assert.True(row.Exists);
        Assert.Equal("tenant-a", row.Text("TenantId"));
        Assert.Equal("original", row.Text("Name"));
    }

    // ---------------------------------------------------------------- Audit readback

    [Fact]
    public async Task A01_EfCore_insert_persists_creation_audit()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);

        await repository.InsertAsync(new Issue130SoftStamped(1, "audited"));

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.NotNull(row.DateTimeValue("CreationTime"));
        Assert.Equal(Guid.Parse(_user.Id), row.GuidValue("CreatorId"));
        Assert.Null(row.DateTimeValue("LastModificationTime"));
    }

    [Fact]
    public async Task A01_FreeSql_insert_persists_creation_audit()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        await repository.InsertAsync(new Issue130SoftStamped(1, "audited"));

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.NotNull(row.DateTimeValue("CreationTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("CreatorId"));
        Assert.Null(row.DateTimeValue("LastModificationTime"));
    }

    [Fact]
    public async Task A01_SqlSugar_insert_persists_creation_audit()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client, _userProvider, _tenant);

        await repository.InsertAsync(new Issue130SoftStamped(1, "audited"));

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.NotNull(row.DateTimeValue("CreationTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("CreatorId"));
        Assert.Null(row.DateTimeValue("LastModificationTime"));
    }

    [Fact]
    public async Task A02_EfCore_update_uses_the_current_user_and_preserves_creation_fields()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertAuditedRow(1, "original");

        await using var context = new Issue130EfDbContext(Issue130EfBed.Options(_database, _user, _tenant));
        var repository = Issue130EfBed.Repository<Issue130SoftStamped>(context);
        var loaded = await repository.GetAsync(1);
        _user.Id = "33333333-3333-3333-3333-333333333333";
        loaded!.Name = "updated";

        await repository.UpdateAsync(loaded);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), row.GuidValue("CreatorId"));
        Assert.Equal(DateTime.Parse("2026-01-01 00:00:00"), row.DateTimeValue("CreationTime"));
        Assert.Equal(Guid.Parse(_user.Id), row.GuidValue("LastModifierId"));
        Assert.NotNull(row.DateTimeValue("LastModificationTime"));
    }

    [Fact]
    public async Task A02_FreeSql_update_uses_the_current_user_and_preserves_creation_fields()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        InsertAuditedRow(1, "original");
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);
        var loaded = await repository.GetAsync(1);
        _userProvider.UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        loaded!.Name = "updated";

        await repository.UpdateAsync(loaded);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), row.GuidValue("CreatorId"));
        Assert.Equal(DateTime.Parse("2026-01-01 00:00:00"), row.DateTimeValue("CreationTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("LastModifierId"));
        Assert.NotNull(row.DateTimeValue("LastModificationTime"));
    }

    [Fact]
    public async Task A02_SqlSugar_update_uses_the_current_user_and_preserves_creation_fields()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        InsertAuditedRow(1, "original");
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client, _userProvider, _tenant);
        var loaded = await repository.GetAsync(1);
        _userProvider.UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        loaded!.Name = "updated";

        await repository.UpdateAsync(loaded);

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), row.GuidValue("CreatorId"));
        Assert.Equal(DateTime.Parse("2026-01-01 00:00:00"), row.DateTimeValue("CreationTime"));
        Assert.Equal(_userProvider.UserId, row.GuidValue("LastModifierId"));
        Assert.NotNull(row.DateTimeValue("LastModificationTime"));
    }

    // ---------------------------------------------------------------- Unit of work and cancellation

    [Fact]
    public async Task U01_EfCore_stale_stamp_delete_reports_conflict_at_complete()
    {
        Issue130EfBed.EnsureCreated(_database, _user, _tenant);
        InsertAuditedRow(1, "stamped");

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddDbContext<Issue130EfDbContext>(options => options
            .UseSqlite(_database.ConnectionString)
            .AddInterceptors(new AuditInterceptor(_user), new MultiTenancyInterceptor(_tenant)));
        services.AddScoped<CrestCreates.DbContextProvider.Abstract.IDataBaseContext>(
            provider => new CrestCreates.Data.EFCore.DbContexts.EfCoreDbContextAdapter(
                provider.GetRequiredService<Issue130EfDbContext>()));
        services.AddScoped(provider => new EfCoreRepository<Issue130SoftStamped, long>(
            provider.GetRequiredService<CrestCreates.DbContextProvider.Abstract.IDataBaseContext>()));
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var repository = scope.ServiceProvider.GetRequiredService<EfCoreRepository<Issue130SoftStamped, long>>();

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        await repository.DeleteAsync(1, "stale-stamp");

        await Assert.ThrowsAsync<CrestConcurrencyException>(() => unitOfWork.CompleteAsync());

        var row = _database.Read(Issue130Tables.SoftStamped, 1);
        Assert.Equal(0, row.Integer("IsDeleted"));
        Assert.Equal("stamp-1", row.Text("ConcurrencyStamp"));
    }

    [Fact]
    public async Task U01_FreeSql_two_repositories_follow_the_unit_of_work_and_abandon_hides_both_writes()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddFreeSqlWithUow(freeSql, repositoryAssembly: null);
        services.AddScoped<Issue130FreeSqlRepository<Issue130SoftStamped>>();

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var first = scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>();
            var second = scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await first.InsertAsync(new Issue130SoftStamped(1, "first"));
            await second.InsertAsync(new Issue130SoftStamped(2, "second"));
        }

        Assert.Equal(0, _database.Count(Issue130Tables.SoftStamped));
    }

    [Fact]
    public async Task U01_SqlSugar_two_repositories_follow_the_unit_of_work_and_abandon_hides_both_writes()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddScoped<ISqlSugarClient>(_ => client);
        services.AddScoped<SqlSugarUnitOfWork>();
        services.AddUnitOfWorkProvider(
            OrmProvider.SqlSugar,
            static provider => provider.GetRequiredService<SqlSugarUnitOfWork>(),
            supportsRequiresNew: false,
            capabilities: new UnitOfWorkProviderCapabilities
            {
                PromptTerminationOnAbandon = true,
                DiscardUncommittedOnAbandon = true
            });
        services.AddScoped<Issue130SqlSugarRepository<Issue130SoftStamped>>();

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var first = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();
            var second = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await first.InsertAsync(new Issue130SoftStamped(1, "first"));
            await second.InsertAsync(new Issue130SoftStamped(2, "second"));
        }

        Assert.Equal(0, _database.Count(Issue130Tables.SoftStamped));
    }

    [Fact]
    public async Task U01_FreeSql_complete_persists_both_repository_writes()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddFreeSqlWithUow(freeSql, repositoryAssembly: null);
        services.AddScoped<Issue130FreeSqlRepository<Issue130SoftStamped>>();

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var first = scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>();
            var second = scope.ServiceProvider.GetRequiredService<Issue130FreeSqlRepository<Issue130SoftStamped>>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await first.InsertAsync(new Issue130SoftStamped(1, "first"));
            await second.InsertAsync(new Issue130SoftStamped(2, "second"));
            await unitOfWork.CompleteAsync();
        }

        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped));
    }

    [Fact]
    public async Task U01_SqlSugar_complete_persists_both_repository_writes()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(new Issue130NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddScoped<ISqlSugarClient>(_ => client);
        services.AddScoped<SqlSugarUnitOfWork>();
        services.AddUnitOfWorkProvider(
            OrmProvider.SqlSugar,
            static provider => provider.GetRequiredService<SqlSugarUnitOfWork>(),
            supportsRequiresNew: false,
            capabilities: new UnitOfWorkProviderCapabilities
            {
                PromptTerminationOnAbandon = true,
                DiscardUncommittedOnAbandon = true
            });
        services.AddScoped<Issue130SqlSugarRepository<Issue130SoftStamped>>();

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var first = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();
            var second = scope.ServiceProvider.GetRequiredService<Issue130SqlSugarRepository<Issue130SoftStamped>>();

            await using var unitOfWork = manager.BeginScope();
            await unitOfWork.StartAsync();
            await first.InsertAsync(new Issue130SoftStamped(1, "first"));
            await second.InsertAsync(new Issue130SoftStamped(2, "second"));
            await unitOfWork.CompleteAsync();
        }

        Assert.Equal(2, _database.Count(Issue130Tables.SoftStamped));
    }

    [Fact]
    public void U03_SqlSugar_module_must_register_the_client_interface_used_by_repositories()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=unused;"
            })
            .Build();
        var services = new ServiceCollection();
        new SqlSugarOrmModule(configuration).RegisterOrmServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<ISqlSugarClient>());
    }

    [Fact]
    public void U03_SqlSugar_module_must_not_register_a_process_wide_client()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=unused;"
            })
            .Build();
        var services = new ServiceCollection();
        new SqlSugarOrmModule(configuration).RegisterOrmServices(services);

        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<SqlSugarClient>(),
            second.ServiceProvider.GetRequiredService<SqlSugarClient>());
    }

    [Fact]
    public async Task U07_FreeSql_repository_honors_a_pre_cancelled_token()
    {
        var freeSql = Issue130FreeSqlBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130FreeSqlRepository<Issue130SoftStamped>(
            new FreeSqlUnitOfWorkManager(freeSql), _userProvider, _tenant);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetListAsync(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task U07_SqlSugar_repository_honors_a_pre_cancelled_token()
    {
        var client = Issue130SqlSugarBed.Create(_database, _userProvider, _tenant);
        var repository = new Issue130SqlSugarRepository<Issue130SoftStamped>(client, _userProvider, _tenant);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetListAsync(new CancellationToken(canceled: true)));
    }
}

internal sealed class Issue130NoOpDomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
        => Task.CompletedTask;
}
