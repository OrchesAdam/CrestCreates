using System;
using System.Linq;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class ConcurrencyTokenConfigurationTests
{
    [Fact]
    public void CrestCreatesDbContext_AllIHasConcurrencyStampEntities_HaveConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"token-check-{Guid.NewGuid():N}")
            .Options;

        using var context = new CrestCreatesDbContext(options);
        var model = context.Model;

        var hasConcurrencyEntities = model.GetEntityTypes()
            .Where(et => typeof(IHasConcurrencyStamp).IsAssignableFrom(et.ClrType))
            .ToList();

        foreach (var entityType in hasConcurrencyEntities)
        {
            var stampProp = entityType.FindProperty(nameof(IHasConcurrencyStamp.ConcurrencyStamp));
            Assert.NotNull(stampProp);
            Assert.True(stampProp.IsConcurrencyToken,
                $"{entityType.ClrType.Name}.ConcurrencyStamp must be IsConcurrencyToken. "
                + "Did you forget to call modelBuilder.ConfigureConcurrencyStamp()?");
        }
    }

    [Fact]
    public void ModelBuilderExtensions_ConfigureConcurrencyStamp_Works()
    {
        var options = new DbContextOptionsBuilder<ConcurrencyTestDbContext>()
            .UseInMemoryDatabase($"ext-check-{Guid.NewGuid():N}")
            .Options;

        using var context = new ConcurrencyTestDbContext(options);
        var model = context.Model;
        var entityType = model.FindEntityType(typeof(ConcurrencyTestEntity));
        Assert.NotNull(entityType);
        var stampProp = entityType!.FindProperty(nameof(IHasConcurrencyStamp.ConcurrencyStamp));
        Assert.NotNull(stampProp);
        Assert.True(stampProp.IsConcurrencyToken);
    }
}

public class ConcurrencyTestEntity : AuditedEntity<Guid> { public string Name { get; set; } = ""; }

public class ConcurrencyTestDbContext : DbContext
{
    public ConcurrencyTestDbContext(DbContextOptions<ConcurrencyTestDbContext> options) : base(options) { }
    public DbSet<ConcurrencyTestEntity> TestEntities { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConcurrencyTestEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
        });
        modelBuilder.ConfigureConcurrencyStamp();
    }
}
