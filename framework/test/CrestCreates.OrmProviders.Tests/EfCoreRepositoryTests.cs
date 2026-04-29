using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class EfCoreRepositoryTests
{
    [Fact]
    public async Task TenantRepository_FindByNameAsync_Should_LoadConnectionStrings()
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"tenant-include-{Guid.NewGuid():N}")
            .Options;

        var tenantId = Guid.NewGuid();

        await using (var seedContext = new CrestCreatesDbContext(options))
        {
            var tenant = new Tenant(tenantId, "Tenant A");
            tenant.AddConnectionString("Default", "Server=.\\Test;Database=TenantA;");
            seedContext.Add(tenant);
            await seedContext.SaveChangesAsync();
        }

        await using var queryContext = new CrestCreatesDbContext(options);
        var repository = new TenantRepository(
            new EfCoreDbContextAdapter(queryContext),
            Mock.Of<ICurrentTenant>(),
            new DataFilterState());

        var result = await repository.FindByNameAsync("Tenant A");

        Assert.NotNull(result);
        Assert.NotNull(result!.ConnectionStrings);
        Assert.Single(result.ConnectionStrings);
        Assert.Equal("Server=.\\Test;Database=TenantA;", result.ConnectionStrings[0].Value);
    }

    [Fact]
    public async Task EfCoreQueryableBuilder_ThenInclude_Should_LoadNestedReferenceNavigation()
    {
        var options = new DbContextOptionsBuilder<TestIncludeDbContext>()
            .UseInMemoryDatabase($"then-include-{Guid.NewGuid():N}")
            .Options;

        await using (var seedContext = new TestIncludeDbContext(options))
        {
            var address = new IncludeAddress(Guid.NewGuid())
            {
                Line1 = "Main Street"
            };

            var customer = new IncludeCustomer(Guid.NewGuid())
            {
                AddressId = address.Id,
                Address = address
            };

            var invoice = new IncludeInvoice(Guid.NewGuid())
            {
                CustomerId = customer.Id,
                Customer = customer
            };

            seedContext.Add(invoice);
            await seedContext.SaveChangesAsync();
        }

        await using var queryContext = new TestIncludeDbContext(options);
        var builder = new EfCoreDbContextAdapter(queryContext).Queryable<IncludeInvoice>();

        var result = await builder
            .Include(invoice => invoice.Customer)
            .ThenInclude<IncludeCustomer, IncludeAddress>(customer => customer.Address)
            .FirstAsync();

        Assert.NotNull(result.Customer);
        Assert.NotNull(result.Customer.Address);
        Assert.Equal("Main Street", result.Customer.Address.Line1);
    }

    [Fact]
    public async Task EfCoreRepository_GetQueryableUnfiltered_Should_IgnoreQueryFilters()
    {
        var options = new DbContextOptionsBuilder<TestFilteredDbContext>()
            .UseInMemoryDatabase($"query-filter-{Guid.NewGuid():N}")
            .Options;

        await using (var seedContext = new TestFilteredDbContext(options))
        {
            seedContext.AddRange(
                new FilteredEntity(Guid.NewGuid()) { Name = "Visible", IsDeleted = false },
                new FilteredEntity(Guid.NewGuid()) { Name = "Hidden", IsDeleted = true });
            await seedContext.SaveChangesAsync();
        }

        await using var queryContext = new TestFilteredDbContext(options);
        var repository = new EfCoreRepository<FilteredEntity, Guid>(new EfCoreDbContextAdapter(queryContext));

        Assert.Equal(1, repository.GetQueryable().Count());
        Assert.Equal(2, repository.GetQueryableUnfiltered().Count());
    }

    public sealed class TestIncludeDbContext : DbContext
    {
        public TestIncludeDbContext(DbContextOptions<TestIncludeDbContext> options)
            : base(options)
        {
        }

        public DbSet<IncludeInvoice> Invoices => Set<IncludeInvoice>();
        public DbSet<IncludeCustomer> Customers => Set<IncludeCustomer>();
        public DbSet<IncludeAddress> Addresses => Set<IncludeAddress>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IncludeInvoice>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.HasOne(e => e.Customer)
                    .WithMany()
                    .HasForeignKey(e => e.CustomerId);
            });

            modelBuilder.Entity<IncludeCustomer>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.HasOne(e => e.Address)
                    .WithMany()
                    .HasForeignKey(e => e.AddressId);
            });

            modelBuilder.Entity<IncludeAddress>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
            });
        }
    }

    public sealed class TestFilteredDbContext : DbContext
    {
        public TestFilteredDbContext(DbContextOptions<TestFilteredDbContext> options)
            : base(options)
        {
        }

        public DbSet<FilteredEntity> Entities => Set<FilteredEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FilteredEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.HasQueryFilter(e => !e.IsDeleted);
            });
        }
    }

    public sealed class IncludeInvoice : Entity<Guid>
    {
        public IncludeInvoice()
        {
        }

        public IncludeInvoice(Guid id)
        {
            Id = id;
        }

        public Guid CustomerId { get; set; }
        public IncludeCustomer Customer { get; set; } = null!;
    }

    public sealed class IncludeCustomer : Entity<Guid>
    {
        public IncludeCustomer()
        {
        }

        public IncludeCustomer(Guid id)
        {
            Id = id;
        }

        public Guid AddressId { get; set; }
        public IncludeAddress Address { get; set; } = null!;
    }

    public sealed class IncludeAddress : Entity<Guid>
    {
        public IncludeAddress()
        {
        }

        public IncludeAddress(Guid id)
        {
            Id = id;
        }

        public string Line1 { get; set; } = string.Empty;
    }

    public sealed class FilteredEntity : Entity<Guid>
    {
        public FilteredEntity()
        {
        }

        public FilteredEntity(Guid id)
        {
            Id = id;
        }

        public string Name { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }
}
