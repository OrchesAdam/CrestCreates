using System;
using System.Linq;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Data.FreeSql.Interceptors;
using CrestCreates.Data.FreeSql.Repositories;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Data.SqlSugar.Interceptors;
using CrestCreates.Data.SqlSugar.Repositories;
using Microsoft.EntityFrameworkCore;
using SqlSugar;

namespace CrestCreates.OrmProviders.Tests.Issue130;

internal static class Issue130EfBed
{
    public static DbContextOptions<Issue130EfDbContext> Options(
        Issue130SqliteFile database,
        Issue130CurrentUser user,
        Issue130MutableTenant tenant)
    {
        return new DbContextOptionsBuilder<Issue130EfDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(
                new AuditInterceptor(user),
                new MultiTenancyInterceptor(tenant))
            .Options;
    }

    public static void EnsureCreated(Issue130SqliteFile database, Issue130CurrentUser user, Issue130MutableTenant tenant)
    {
        using var context = new Issue130EfDbContext(Options(database, user, tenant));
        context.Database.EnsureCreated();
    }

    public static EfCoreRepository<TEntity, long> Repository<TEntity>(
        Issue130EfDbContext context,
        CrestCreates.MultiTenancy.Abstract.ICurrentTenant? tenant = null)
        where TEntity : class, CrestCreates.Domain.Shared.Entities.IEntity<long>
    {
        return new EfCoreRepository<TEntity, long>(new EfCoreDbContextAdapter(context), null, tenant);
    }
}

public sealed class Issue130EfDbContext : DbContext
{
    public Issue130EfDbContext(DbContextOptions<Issue130EfDbContext> options)
        : base(options)
    {
    }

    public DbSet<Issue130SoftStamped> SoftStamped => Set<Issue130SoftStamped>();

    public DbSet<Issue130SoftOnly> SoftOnly => Set<Issue130SoftOnly>();

    public DbSet<Issue130Plain> Plain => Set<Issue130Plain>();

    public DbSet<Issue130TenantEntity> TenantEntities => Set<Issue130TenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        SoftDeleteFilterConfiguration.ApplyAll(modelBuilder);

        modelBuilder.Entity<Issue130SoftStamped>(entity =>
        {
            entity.ToTable(Issue130Tables.SoftStamped);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<Issue130SoftOnly>(entity =>
        {
            entity.ToTable(Issue130Tables.SoftOnly);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Issue130Plain>(entity =>
        {
            entity.ToTable(Issue130Tables.Plain);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Issue130TenantEntity>(entity =>
        {
            entity.ToTable(Issue130Tables.Tenant);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        });
    }
}

public sealed class Issue130EfTenantDbContext : DbContext
{
    private readonly CrestCreates.MultiTenancy.Abstract.ICurrentTenant _tenant;

    public Issue130EfTenantDbContext(DbContextOptions<Issue130EfTenantDbContext> options)
        : this(options, new Issue130MutableTenant(null))
    {
    }

    public Issue130EfTenantDbContext(
        DbContextOptions<Issue130EfTenantDbContext> options,
        CrestCreates.MultiTenancy.Abstract.ICurrentTenant tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Issue130TenantEntity> TenantEntities => Set<Issue130TenantEntity>();

    public DbSet<Issue130OptionalTenantEntity> OptionalTenantEntities => Set<Issue130OptionalTenantEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        SoftDeleteFilterConfiguration.ApplyAll(modelBuilder);
        CrestCreates.Data.EFCore.MultiTenancy.TenantFilterConfiguration.ApplyAll(modelBuilder, _tenant);

        modelBuilder.Entity<Issue130TenantEntity>(entity =>
        {
            entity.ToTable(Issue130Tables.Tenant);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Issue130OptionalTenantEntity>(entity =>
        {
            entity.ToTable("S1_OptionalTenant");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });
    }
}

internal static class Issue130FreeSqlBed
{
    public static IFreeSql Create(
        Issue130SqliteFile database,
        Issue130CurrentUserProvider userProvider,
        Issue130MutableTenant? tenant)
    {
        var freeSql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, database.ConnectionString)
            .Build();

        freeSql.CodeFirst.SyncStructure<Issue130SoftStamped>();
        freeSql.CodeFirst.SyncStructure<Issue130SoftOnly>();
        freeSql.CodeFirst.SyncStructure<Issue130Plain>();
        freeSql.CodeFirst.SyncStructure<Issue130TenantEntity>();
        freeSql.CodeFirst.SyncStructure<Issue130MappedSoftStamped>();
        freeSql.CodeFirst.SyncStructure<Issue130MappedSoftOnly>();
        freeSql.CodeFirst.SyncStructure<Issue130ReservedSoftStamped>();
        freeSql.CodeFirst.SyncStructure<Issue130ReservedSoftOnly>();
        freeSql.CodeFirst.SyncStructure<Issue130OptionalTenantEntity>();
        freeSql.CodeFirst.SyncStructure<Issue130TenantOnlyEntity>();

        freeSql.ConfigureAuditInterceptor(userProvider, tenant);
        return freeSql;
    }
}

public sealed class EfCoreRepositoryBaseProbe : EfCoreRepositoryBase<Issue130SoftStamped, long>
{
    public EfCoreRepositoryBaseProbe(DbContext context)
        : base(
            new EfCoreDbContextAdapter(context),
            new Issue130MutableTenant(null),
            new CrestCreates.Domain.DataFilter.DataFilterState())
    {
    }
}

public sealed class Issue130FreeSqlRepository<TEntity> : FreeSqlRepository<TEntity, long>
    where TEntity : class, CrestCreates.Domain.Shared.Entities.IEntity<long>
{
    public Issue130FreeSqlRepository(
        FreeSqlUnitOfWorkManager unitOfWorkManager,
        ICurrentUserProvider? currentUserProvider = null,
        CrestCreates.MultiTenancy.Abstract.ICurrentTenant? currentTenant = null)
        : base(unitOfWorkManager, null, currentUserProvider, currentTenant)
    {
    }

    public override IQueryable<TEntity> GetQueryableUnfiltered()
        => throw new NotSupportedException("Issue 130 baselines never call the unfiltered entry point.");
}

internal static class Issue130SqlSugarBed
{
    public static SqlSugarClient Create(
        Issue130SqliteFile database,
        Issue130CurrentUserProvider userProvider,
        Issue130MutableTenant? tenant)
    {
        var client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = database.ConnectionString,
            IsAutoCloseConnection = false
        });

        client.Ado.Open();
        client.CodeFirst.InitTables(
            typeof(Issue130SoftStamped),
            typeof(Issue130SoftOnly),
            typeof(Issue130Plain),
            typeof(Issue130TenantEntity),
            typeof(Issue130MappedSoftStamped),
            typeof(Issue130MappedSoftOnly),
            typeof(Issue130ReservedSoftStamped),
            typeof(Issue130ReservedSoftOnly),
            typeof(Issue130OptionalTenantEntity),
            typeof(Issue130TenantOnlyEntity));

        client.ConfigureAuditInterceptor(userProvider, tenant);
        return client;
    }
}

public sealed class Issue130SqlSugarRepository<TEntity> : SqlSugarRepository<TEntity, long>
    where TEntity : class, CrestCreates.Domain.Shared.Entities.IEntity<long>, new()
{
    public Issue130SqlSugarRepository(
        ISqlSugarClient client,
        ICurrentUserProvider? currentUserProvider = null,
        CrestCreates.MultiTenancy.Abstract.ICurrentTenant? currentTenant = null)
        : base(client, null, currentUserProvider, currentTenant)
    {
    }

    public override IQueryable<TEntity> GetQueryableUnfiltered()
        => throw new NotSupportedException("Issue 130 baselines never call the unfiltered entry point.");
}
