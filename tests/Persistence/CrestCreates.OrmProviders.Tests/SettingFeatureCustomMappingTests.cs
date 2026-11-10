using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Application.Settings;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.Domain.Shared.Settings;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

[Collection("ModelFilterRegistry")]
public class SettingFeatureCustomMappingTests : IDisposable
{
    private const string SettingName = "Sample.Global";
    private const string FeatureName = "Sample.Feature";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"crest-custom-mapping-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Remapped_setting_uses_the_model_table_and_index_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateRemappedContext(connection);
        await context.Database.EnsureCreatedAsync();

        var entityType = context.Model.FindEntityType(typeof(SettingValue))!;
        entityType.GetTableName().Should().Be("AppSettings");
        entityType.GetIndexes().Select(index => index.GetDatabaseName()).Should().Contain(
            "IX_AppSettings_Name_Scope_ProviderKey",
            "the conflict target must come from the live model, not the default table name");

        (await TableExistsAsync(context, "AppSettings")).Should().BeTrue();
        (await TableExistsAsync(context, "SettingValues")).Should().BeFalse();
    }

    [Fact]
    public async Task Remapped_setting_converges_without_a_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateRemappedContext(connection);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new SettingRepository(new EfCoreDbContextAdapter(context));

        await realRepository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "winner", false, null));

        var manager = CreateSettingManager(CreateStaleSettingRepository(realRepository));

        await manager.SetGlobalAsync(SettingName, "loser");

        var rows = await context.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
    }

    [Fact]
    public async Task Remapped_setting_converges_inside_an_explicit_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateRemappedContext(connection);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new SettingRepository(new EfCoreDbContextAdapter(context));

        await realRepository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "winner", false, null));

        await using var transaction = await context.Database.BeginTransactionAsync();
        var manager = CreateSettingManager(CreateStaleSettingRepository(realRepository));

        await manager.SetGlobalAsync(SettingName, "loser");

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        var rows = await context.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
    }

    [Fact]
    public async Task Remapped_setting_converges_inside_a_formal_unit_of_work()
    {
        using var provider = BuildRemappedUnitOfWorkProvider();
        await SeedSettingAsync(provider, "winner");

        using (var scope = provider.CreateScope())
        {
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<RemappedContext>();
            var manager = CreateSettingManager(
                CreateStaleSettingRepository(new SettingRepository(dbContext)));

            await using var unitOfWork = unitOfWorkManager.BeginScope();
            await unitOfWork.StartAsync();
            await manager.SetGlobalAsync(SettingName, "loser");
            await unitOfWork.CompleteAsync();
            unitOfWork.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
        }

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<RemappedContext>();
        var rows = await verifyContext.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
    }

    [Fact]
    public async Task Remapped_feature_converges_without_a_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateRemappedContext(connection);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new FeatureRepository(new EfCoreDbContextAdapter(context));

        await realRepository.InsertAsync(new FeatureValue(
            Guid.NewGuid(), FeatureName, FeatureScope.Global, string.Empty, "winner", null));

        var manager = CreateFeatureManager(CreateStaleFeatureRepository(realRepository));

        await manager.SetGlobalAsync(FeatureName, "loser");

        var rows = await context.FeatureValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
    }

    [Fact]
    public async Task Remapped_feature_converges_inside_an_explicit_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateRemappedContext(connection);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new FeatureRepository(new EfCoreDbContextAdapter(context));

        await realRepository.InsertAsync(new FeatureValue(
            Guid.NewGuid(), FeatureName, FeatureScope.Global, string.Empty, "winner", null));

        await using var transaction = await context.Database.BeginTransactionAsync();
        var manager = CreateFeatureManager(CreateStaleFeatureRepository(realRepository));

        await manager.SetGlobalAsync(FeatureName, "loser");

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        var rows = await context.FeatureValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
    }

    [Fact]
    public async Task Remapped_feature_converges_inside_a_formal_unit_of_work()
    {
        using var provider = BuildRemappedUnitOfWorkProvider();
        await SeedFeatureAsync(provider, "winner");

        using (var scope = provider.CreateScope())
        {
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<RemappedContext>();
            var manager = CreateFeatureManager(
                CreateStaleFeatureRepository(new FeatureRepository(dbContext)));

            await using var unitOfWork = unitOfWorkManager.BeginScope();
            await unitOfWork.StartAsync();
            await manager.SetGlobalAsync(FeatureName, "loser");
            await unitOfWork.CompleteAsync();
            unitOfWork.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
        }

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<RemappedContext>();
        var rows = await verifyContext.FeatureValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
    }

    [Theory]
    [InlineData("OtherSettingValues")]
    [InlineData("SettingValuesExtra")]
    [InlineData("MySettingValues")]
    public async Task Similar_tenant_table_conflicts_are_not_setting_convergence(string tenantTable)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateSimilarTableContext(connection, tenantTable);
        await context.Database.EnsureCreatedAsync();

        context.Tenants.Add(new Tenant(Guid.NewGuid(), "duplicate-name"));
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Tenants.Add(new Tenant(Guid.NewGuid(), "duplicate-name"));

        var realRepository = new SettingRepository(new EfCoreDbContextAdapter(context));
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue?)null);
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));

        var manager = CreateSettingManager(repository.Object);

        var act = async () => await manager.SetGlobalAsync(SettingName, "value");

        await act.Should().ThrowAsync<DbUpdateException>(
            $"a unique violation on '{tenantTable}' must keep its original attribution");
        repository.Verify(
            repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.ChangeTracker.Entries<SettingValue>().Single().State.Should().Be(
            EntityState.Added,
            "the unrelated setting insert must not be detached by a foreign-table conflict");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Similar_tenant_table_conflicts_are_not_feature_convergence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateSimilarTableContext(connection, "MyFeatureValues");
        await context.Database.EnsureCreatedAsync();

        context.Tenants.Add(new Tenant(Guid.NewGuid(), "duplicate-name"));
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Tenants.Add(new Tenant(Guid.NewGuid(), "duplicate-name"));

        var realRepository = new FeatureRepository(new EfCoreDbContextAdapter(context));
        var repository = new Mock<IFeatureRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                FeatureName, FeatureScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeatureValue?)null);
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((FeatureValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));

        var manager = CreateFeatureManager(repository.Object);

        var act = async () => await manager.SetGlobalAsync(FeatureName, "value");

        await act.Should().ThrowAsync<DbUpdateException>();
        repository.Verify(
            repo => repo.UpdateAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.ChangeTracker.Entries<FeatureValue>().Single().State.Should().Be(EntityState.Added);

        await transaction.RollbackAsync();
    }

    public sealed class RemappedContext : CrestCreatesDbContext
    {
        public RemappedContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
        }

        public RemappedContext(DbContextOptions<RemappedContext> options)
            : base(Rebase(options))
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SettingValue>().ToTable("AppSettings");
            modelBuilder.Entity<FeatureValue>().ToTable("AppFeatures");
        }

        private static DbContextOptions<CrestCreatesDbContext> Rebase(DbContextOptions<RemappedContext> options)
        {
            return new DbContextOptions<CrestCreatesDbContext>(
                options.Extensions.ToDictionary(extension => extension.GetType()));
        }
    }

    public sealed class SimilarTableContext : CrestCreatesDbContext
    {
        public SimilarTableContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
        }

        public SimilarTableContext(DbContextOptions<SimilarTableContext> options)
            : base(Rebase(options))
        {
        }

        public string TenantTable { get; set; } = "OtherSettingValues";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Tenant>().ToTable(TenantTable);
        }

        private static DbContextOptions<CrestCreatesDbContext> Rebase(DbContextOptions<SimilarTableContext> options)
        {
            return new DbContextOptions<CrestCreatesDbContext>(
                options.Extensions.ToDictionary(extension => extension.GetType()));
        }
    }

    private sealed class SimilarTableModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            return context is SimilarTableContext similar
                ? new SimilarTableModelCacheKey(context.GetType(), similar.TenantTable, designTime)
                : new SimilarTableModelCacheKey(context.GetType(), null, designTime);
        }

        public object Create(DbContext context) => Create(context, false);
    }

    private sealed record SimilarTableModelCacheKey(Type ContextType, string? TenantTable, bool DesignTime);

    private static RemappedContext CreateRemappedContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RemappedContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>()
            .Options;
        return new RemappedContext(options);
    }

    private static SimilarTableContext CreateSimilarTableContext(
        SqliteConnection connection,
        string tenantTable)
    {
        var options = new DbContextOptionsBuilder<SimilarTableContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, SimilarTableModelCacheKeyFactory>()
            .Options;
        return new SimilarTableContext(options) { TenantTable = tenantTable };
    }

    private static async Task<bool> TableExistsAsync(DbContext context, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    private ServiceProvider BuildRemappedUnitOfWorkProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new RemappedContext(
            new DbContextOptionsBuilder<RemappedContext>()
                .UseSqlite($"Data Source={_databasePath}")
                .ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>()
                .Options));
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<RemappedContext>());
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp => sp.GetRequiredService<RemappedContext>());
        services.AddSingleton(Mock.Of<ICurrentTenant>());
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<RemappedContext>().Database.EnsureCreated();
        return provider;
    }

    private static async Task SeedSettingAsync(ServiceProvider provider, string value)
    {
        using var scope = provider.CreateScope();
        var repository = new SettingRepository(scope.ServiceProvider.GetRequiredService<IDataBaseContext>());
        await repository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, value, false, null));
    }

    private static async Task SeedFeatureAsync(ServiceProvider provider, string value)
    {
        using var scope = provider.CreateScope();
        var repository = new FeatureRepository(scope.ServiceProvider.GetRequiredService<IDataBaseContext>());
        await repository.InsertAsync(new FeatureValue(
            Guid.NewGuid(), FeatureName, FeatureScope.Global, string.Empty, value, null));
    }

    private static ISettingRepository CreateStaleSettingRepository(SettingRepository realRepository)
    {
        var findCalls = 0;
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .Returns(async () => findCalls++ == 0
                ? null
                : await realRepository.FindAsync(SettingName, SettingScope.Global, string.Empty, null));
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.UpdateAsync(entity, token));
        return repository.Object;
    }

    private static IFeatureRepository CreateStaleFeatureRepository(FeatureRepository realRepository)
    {
        var findCalls = 0;
        var repository = new Mock<IFeatureRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                FeatureName, FeatureScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .Returns(async () => findCalls++ == 0
                ? null
                : await realRepository.FindAsync(FeatureName, FeatureScope.Global, string.Empty, null));
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((FeatureValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((FeatureValue entity, CancellationToken token) => realRepository.UpdateAsync(entity, token));
        return repository.Object;
    }

    private static SettingManager CreateSettingManager(ISettingRepository repository)
    {
        var definition = new SettingDefinition(
            SettingName, "Sample", scopes: SettingScope.Global, valueType: SettingValueType.String);
        var definitions = new Mock<ISettingDefinitionManager>();
        definitions.Setup(manager => manager.GetOrNull(SettingName)).Returns(definition);
        definitions.Setup(manager => manager.GetAll()).Returns(new[] { definition });
        var cache = new Mock<ICrestCacheService>();
        var invalidator = new SettingCacheInvalidator(cache.Object, new SettingCacheKeyContributor());

        return new SettingManager(
            definitions.Object,
            repository,
            Mock.Of<ISettingStore>(),
            Mock.Of<ISettingEncryptionService>(),
            new SettingValueTypeConverter(),
            invalidator);
    }

    private static FeatureManager CreateFeatureManager(IFeatureRepository repository)
    {
        var definition = new FeatureDefinition(
            FeatureName, "Sample", scopes: FeatureScope.Global, valueType: FeatureValueType.String);
        var definitions = new Mock<IFeatureDefinitionManager>();
        definitions.Setup(manager => manager.GetOrNull(FeatureName)).Returns(definition);
        definitions.Setup(manager => manager.GetAll()).Returns(new[] { definition });
        var cache = new Mock<ICrestCacheService>();
        var invalidator = new FeatureCacheInvalidator(cache.Object, new FeatureCacheKeyContributor());

        return new FeatureManager(
            definitions.Object,
            repository,
            Mock.Of<IFeatureStore>(),
            new FeatureValueTypeConverter(),
            invalidator,
            Mock.Of<IFeatureAuditRecorder>());
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;
    }
}
