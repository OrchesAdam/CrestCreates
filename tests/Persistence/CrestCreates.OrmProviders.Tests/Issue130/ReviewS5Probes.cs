using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.MultiTenancy;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.OrmProviders.Tests.Issue130;

[Collection("ModelFilterRegistry")]
public sealed class ReviewS5Probes
{
    // Dedicated context/tenant pair keeps the generated model's captured tenant stable across
    // cases; this collection serializes registry tests and tenant mutations.
    private static readonly Issue130MutableTenant Tenant = new("tenant-a");

    private static DbContextOptions<ReviewS5DbContext> Options(Issue130SqliteFile db) =>
        new DbContextOptionsBuilder<ReviewS5DbContext>()
            .UseSqlite(db.ConnectionString)
            .AddInterceptors(new MultiTenancyInterceptor(Tenant))
            .Options;

    private static ICrestRepositoryBase<TEntity, long> Repository<TEntity>(ReviewS5DbContext context, bool platformBase)
        where TEntity : class, IEntity<long> => platformBase
        ? new ReviewS5EfRepository<TEntity>(new EfCoreDbContextAdapter(context), Tenant)
        : new EfCoreRepository<TEntity, long>(new EfCoreDbContextAdapter(context), null, Tenant);

    [Theory]
    [InlineData(false, "single-control")]
    [InlineData(true, "single-control")]
    [InlineData(false, "batch-update")]
    [InlineData(true, "batch-update")]
    [InlineData(false, "batch-delete")]
    [InlineData(true, "batch-delete")]
    public async Task EF_unstamped_tenant_batch_writes_must_not_bypass_persisted_ownership(bool platformBase, string operation)
    {
        using var db = new Issue130SqliteFile();
        Tenant.Id = "tenant-a";
        var options = Options(db);
        await using (var seed = new ReviewS5DbContext(options, Tenant))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.OptionalTenantEntities.Add(new Issue130OptionalTenantEntity { Id = 1, TenantId = "tenant-a", Name = "original" });
            await seed.SaveChangesAsync();
        }

        var control = operation == "single-control";
        if (!control) Tenant.Id = "tenant-b";
        await using var context = new ReviewS5DbContext(options, Tenant);
        // Prove the database row is hidden by the actual generated EF model. Checking this via
        // the platform repository would mask the separate read-filter finding.
        if (!control) Assert.Empty(await context.OptionalTenantEntities.AsNoTracking().ToListAsync());
        var repo = Repository<Issue130OptionalTenantEntity>(context, platformBase);
        var submitted = new Issue130OptionalTenantEntity { Id = 1, TenantId = Tenant.Id, Name = "updated" };
        var error = await Record.ExceptionAsync(async () =>
        {
            if (control) await repo.UpdateAsync(submitted);
            else if (operation == "batch-update") await repo.UpdateRangeAsync(new[] { submitted });
            else await repo.DeleteRangeAsync(new[] { submitted });
        });
        var persisted = db.Read("S1_OptionalTenant", 1);
        if (control)
        {
            Assert.Null(error);
            Assert.Equal("tenant-a", persisted.Text("TenantId"));
            Assert.Equal("updated", persisted.Text("Name"));
        }
        else
        {
            Assert.True(persisted.Exists, "tenant-b batch write removed tenant-a's hidden row");
            Assert.Equal("tenant-a", persisted.Text("TenantId"));
            Assert.Equal("original", persisted.Text("Name"));
            Assert.NotNull(error);
        }
    }

    [Theory]
    [InlineData(false, "optional-tenant")]
    [InlineData(true, "optional-tenant")]
    [InlineData(false, "soft-delete")]
    [InlineData(true, "soft-delete")]
    public async Task Ordinary_EF_repository_reads_must_preserve_generated_model_filters(bool platformBase, string filter)
    {
        using var db = new Issue130SqliteFile();
        Tenant.Id = "tenant-a";
        var options = Options(db);
        await using (var seed = new ReviewS5DbContext(options, Tenant))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.OptionalTenantEntities.AddRange(
                new Issue130OptionalTenantEntity { Id = 1, TenantId = "tenant-a", Name = "own" },
                new Issue130OptionalTenantEntity { Id = 2, TenantId = "tenant-b", Name = "foreign" },
                new Issue130OptionalTenantEntity { Id = 3, TenantId = null, Name = "shared" });
            seed.SoftOnly.AddRange(new Issue130SoftOnly(1, "live"), new Issue130SoftOnly(2, "deleted") { IsDeleted = true });
            await seed.SaveChangesAsync();
        }
        await using var context = new ReviewS5DbContext(options, Tenant);
        if (filter == "optional-tenant")
        {
            var expected = new long[] { 1, 3 };
            Assert.Equal(expected, await context.OptionalTenantEntities.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Id).ToArrayAsync());
            var rows = await Repository<Issue130OptionalTenantEntity>(context, platformBase).GetListAsync();
            Assert.Equal(expected, rows.Select(e => e.Id).OrderBy(id => id).ToArray());
        }
        else
        {
            Assert.Equal(new long[] { 1 }, await context.SoftOnly.AsNoTracking().Select(e => e.Id).ToArrayAsync());
            var rows = await Repository<Issue130SoftOnly>(context, platformBase).GetListAsync();
            Assert.Equal(new long[] { 1 }, rows.Select(e => e.Id).OrderBy(id => id).ToArray());
        }
    }
}

public sealed class ReviewS5EfRepository<TEntity> : EfCoreRepositoryBase<TEntity, long>
    where TEntity : class, IEntity<long>
{
    public ReviewS5EfRepository(CrestCreates.DbContextProvider.Abstract.IDataBaseContext context, ICurrentTenant tenant)
        : base(context, tenant, new DataFilterState()) { }
}

public sealed class ReviewS5DbContext : DbContext
{
    private readonly ICurrentTenant _tenant;
    public ReviewS5DbContext(DbContextOptions<ReviewS5DbContext> options) : this(options, new Issue130MutableTenant(null)) { }
    public ReviewS5DbContext(DbContextOptions<ReviewS5DbContext> options, ICurrentTenant tenant) : base(options) => _tenant = tenant;
    public DbSet<Issue130OptionalTenantEntity> OptionalTenantEntities => Set<Issue130OptionalTenantEntity>();
    public DbSet<Issue130SoftOnly> SoftOnly => Set<Issue130SoftOnly>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Issue130OptionalTenantEntity>(e => { e.ToTable("S1_OptionalTenant"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); });
        modelBuilder.Entity<Issue130SoftOnly>(e => { e.ToTable(Issue130Tables.SoftOnly); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); });
        SoftDeleteFilterConfiguration.ApplyAll(modelBuilder);
        TenantFilterConfiguration.ApplyAll(modelBuilder, _tenant);
    }
}
