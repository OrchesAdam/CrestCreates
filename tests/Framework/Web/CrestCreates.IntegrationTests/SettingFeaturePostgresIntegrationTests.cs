using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Settings;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using LibraryManagement.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// Real PostgreSQL evidence for the R3-12 host-null uniqueness contract: the migration must be
/// discoverable, must refuse duplicate rows without recording success, and natural-key Set must
/// converge inside explicit and formal-UoW transactions. SQLite-only evidence is not extrapolated.
/// </summary>
public sealed class SettingFeaturePostgresIntegrationTests : IAsyncLifetime
{
    private const string SettingName = "Sample.Global";
    private const string FeatureName = "Sample.Feature";
    private const string HostNullMigration = "20261109090000_R3_12_SettingHostNullUnique";
    private const string InitialMigration = "20260506065811_InitialCreate";
    private const string HostNullIndex = "IX_SettingValues_Name_Scope_ProviderKey";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("crest_setting_feature_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Library_migration_creates_and_drops_the_host_null_unique_index()
    {
        var schema = await CreateSchemaAsync();
        using var context = CreateLibraryContext(schema);

        await context.Database.MigrateAsync();

        (await context.Database.GetAppliedMigrationsAsync()).Should().Contain(
            HostNullMigration,
            "the migration must be part of the normal EF upgrade chain");
        (await ReadHostNullIndexDefinitionAsync(schema)).Should().Contain(
            "\"TenantId\" IS NULL",
            "the partial index is what makes host-null natural keys unique on PostgreSQL");

        await context.Database.MigrateAsync(InitialMigration);

        (await ReadHostNullIndexDefinitionAsync(schema)).Should().BeNull(
            "Down must drop the partial unique index");

        await context.Database.MigrateAsync();

        (await ReadHostNullIndexDefinitionAsync(schema)).Should().NotBeNull(
            "a later upgrade must be able to reapply the migration");
    }

    [Fact]
    public async Task Library_migration_refuses_duplicate_host_null_rows_without_recording_success()
    {
        var schema = await CreateSchemaAsync();
        using (var context = CreateLibraryContext(schema))
        {
            await context.Database.MigrateAsync(InitialMigration);
        }

        await InsertDuplicateHostNullSettingsAsync(schema);

        using (var context = CreateLibraryContext(schema))
        {
            var act = async () => await context.Database.MigrateAsync();

            var exception = await act.Should().ThrowAsync<Exception>();
            exception.Which.ToString().Should().Contain(
                "R3-12",
                "the duplicate diagnostic must name the reconciliation rule without printing setting values");
        }

        (await ReadAppliedMigrationsAsync(schema)).Should().NotContain(
            HostNullMigration,
            "a failed duplicate check must not be recorded as an applied migration");
        (await ReadHostNullIndexDefinitionAsync(schema)).Should().BeNull(
            "the unique index must not be created while duplicates still exist");
    }

    [Fact]
    public async Task Setting_converges_inside_a_postgres_transaction()
    {
        var schema = await CreateSchemaAsync();
        await using var context = CreateCrestContext(schema);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new SettingRepository(context);

        var winner = new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "winner", false, null);
        await realRepository.InsertAsync(winner);

        await using var transaction = await context.Database.BeginTransactionAsync();
        var findCalls = 0;
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => findCalls++ == 0 ? null : winner);
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.UpdateAsync(entity, token));

        var manager = CreateSettingManager(repository.Object);

        await manager.SetGlobalAsync(SettingName, "loser");

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        var rows = await context.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle("the partial unique index must keep one row for the host-null natural key");
        rows[0].Value.Should().Be("loser", "the loser must converge on the persisted row");
        findCalls.Should().Be(2, "the stale read must be followed by a winner re-read");
    }

    [Fact]
    public async Task Setting_converges_with_a_custom_table_mapping_on_postgres()
    {
        var schema = await CreateSchemaAsync();
        await using var context = CreateRemappedContext(schema);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new SettingRepository(context);

        context.Model.FindEntityType(typeof(SettingValue))!.GetTableName().Should().Be("AppSettings");
        (await ReadIndexDefinitionAsync(schema, "SettingValues", HostNullIndex)).Should().BeNull();
        (await ReadIndexDefinitionAsync(schema, "AppSettings", "IX_AppSettings_Name_Scope_ProviderKey"))
            .Should().Contain("\"TenantId\" IS NULL");

        await realRepository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "winner", false, null));

        var findCalls = 0;
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .Returns(async () => findCalls++ == 0 ? null : await realRepository.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null));
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.UpdateAsync(entity, token));

        await CreateSettingManager(repository.Object).SetGlobalAsync(SettingName, "loser");

        var rows = await context.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
        findCalls.Should().Be(2);
    }

    [Fact]
    public async Task Feature_converges_with_a_custom_table_mapping_inside_a_postgres_transaction()
    {
        var schema = await CreateSchemaAsync();
        await using var context = CreateRemappedContext(schema);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new FeatureRepository(context);

        await realRepository.InsertAsync(new CrestCreates.Domain.Features.FeatureValue(
            Guid.NewGuid(), FeatureName, CrestCreates.Domain.Shared.Features.FeatureScope.Global,
            string.Empty, "winner", null));

        await using var transaction = await context.Database.BeginTransactionAsync();
        var findCalls = 0;
        var repository = new Mock<CrestCreates.Domain.Features.IFeatureRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                FeatureName, CrestCreates.Domain.Shared.Features.FeatureScope.Global,
                string.Empty, null, It.IsAny<CancellationToken>()))
            .Returns(async () => findCalls++ == 0 ? null : await realRepository.FindAsync(
                FeatureName, CrestCreates.Domain.Shared.Features.FeatureScope.Global,
                string.Empty, null));
        repository
            .Setup(repo => repo.InsertAsync(
                It.IsAny<CrestCreates.Domain.Features.FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((CrestCreates.Domain.Features.FeatureValue entity, CancellationToken token) =>
                realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(
                It.IsAny<CrestCreates.Domain.Features.FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((CrestCreates.Domain.Features.FeatureValue entity, CancellationToken token) =>
                realRepository.UpdateAsync(entity, token));

        await CreateFeatureManager(repository.Object).SetGlobalAsync(FeatureName, "loser");

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        var rows = await context.FeatureValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
        findCalls.Should().Be(2);
    }

    [Fact]
    public async Task Similar_table_conflict_is_not_setting_convergence_on_postgres()
    {
        var schema = await CreateSchemaAsync();
        await using var context = CreateSimilarTableContext(schema);
        await context.Database.EnsureCreatedAsync();

        context.Tenants.Add(new CrestCreates.Domain.Permission.Tenant(Guid.NewGuid(), "duplicate-name"));
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Tenants.Add(new CrestCreates.Domain.Permission.Tenant(Guid.NewGuid(), "duplicate-name"));

        var realRepository = new SettingRepository(context);
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue?)null);
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));

        var act = async () => await CreateSettingManager(repository.Object)
            .SetGlobalAsync(SettingName, "value");

        await act.Should().ThrowAsync<DbUpdateException>(
            "a PostgreSQL unique violation on OtherSettingValues must keep its original attribution");
        repository.Verify(
            repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.ChangeTracker.Entries<SettingValue>().Single().State.Should().Be(EntityState.Added);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Two_postgres_writers_that_both_read_empty_converge_to_one_row()
    {
        var schema = await CreateSchemaAsync();
        await using var firstContext = CreateCrestContext(schema);
        await firstContext.Database.EnsureCreatedAsync();
        await using var secondContext = CreateCrestContext(schema);

        var firstRepository = new SettingRepository(firstContext);
        var secondRepository = new SettingRepository(secondContext);

        // Deterministic barrier: both writers observe an empty natural key before either writes.
        (await firstRepository.FindAsync(SettingName, SettingScope.Global, string.Empty, null))
            .Should().BeNull();
        (await secondRepository.FindAsync(SettingName, SettingScope.Global, string.Empty, null))
            .Should().BeNull();

        await CreateSettingManager(CreateStaleThenRealRepository(firstRepository))
            .SetGlobalAsync(SettingName, "winner-a");
        await CreateSettingManager(CreateStaleThenRealRepository(secondRepository))
            .SetGlobalAsync(SettingName, "loser-b");

        await using var verifyContext = CreateCrestContext(schema);
        var rows = await verifyContext.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle(
            "the real PostgreSQL partial unique index must permit only one host-null natural-key row");
        rows[0].Value.Should().Be("loser-b", "the loser must re-read the committed winner and update it");
    }

    [Fact]
    public async Task Setting_converges_inside_a_postgres_unit_of_work()
    {
        var schema = await CreateSchemaAsync();
        using var provider = BuildUnitOfWorkProvider(schema);
        await SeedSettingAsync(provider, "winner");

        using (var scope = provider.CreateScope())
        {
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            var realRepository = new SettingRepository(dbContext);
            var findCalls = 0;
            var repository = new Mock<ISettingRepository>();
            repository
                .Setup(repo => repo.FindAsync(
                    It.IsAny<string>(), It.IsAny<SettingScope>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string name, SettingScope settingScope, string providerKey, string? tenantId, CancellationToken token) =>
                {
                    findCalls++;
                    return findCalls == 1
                        ? Task.FromResult<SettingValue?>(null)
                        : realRepository.FindAsync(name, settingScope, providerKey, tenantId, token);
                });
            repository
                .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
                .Returns((SettingValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));
            repository
                .Setup(repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
                .Returns((SettingValue entity, CancellationToken token) => realRepository.UpdateAsync(entity, token));

            await using var unitOfWork = unitOfWorkManager.BeginScope();
            await unitOfWork.StartAsync();
            await CreateSettingManager(repository.Object).SetGlobalAsync(SettingName, "loser");
            await unitOfWork.CompleteAsync();
            unitOfWork.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
            findCalls.Should().Be(2);
        }

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
        var rows = await verifyContext.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
    }

    [Fact]
    public async Task Feature_converges_inside_a_postgres_transaction()
    {
        var schema = await CreateSchemaAsync();
        await using var context = CreateCrestContext(schema);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new FeatureRepository(context);

        var winner = new CrestCreates.Domain.Features.FeatureValue(
            Guid.NewGuid(), FeatureName, CrestCreates.Domain.Shared.Features.FeatureScope.Global,
            string.Empty, "winner", null);
        await realRepository.InsertAsync(winner);

        await using var transaction = await context.Database.BeginTransactionAsync();
        var findCalls = 0;
        var repository = new Mock<CrestCreates.Domain.Features.IFeatureRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                FeatureName, CrestCreates.Domain.Shared.Features.FeatureScope.Global,
                string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => findCalls++ == 0 ? null : winner);
        repository
            .Setup(repo => repo.InsertAsync(
                It.IsAny<CrestCreates.Domain.Features.FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((CrestCreates.Domain.Features.FeatureValue entity, CancellationToken token) =>
                realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(
                It.IsAny<CrestCreates.Domain.Features.FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((CrestCreates.Domain.Features.FeatureValue entity, CancellationToken token) =>
                realRepository.UpdateAsync(entity, token));

        var manager = CreateFeatureManager(repository.Object);

        await manager.SetGlobalAsync(FeatureName, "loser");

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        var rows = await context.FeatureValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("loser");
        findCalls.Should().Be(2);
    }

    private async Task<string> CreateSchemaAsync()
    {
        var schema = $"sf_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA "{schema}";""";
        await command.ExecuteNonQueryAsync();
        return schema;
    }

    private string SchemaConnectionString(string schema)
        => $"{_postgres.GetConnectionString()};Search Path={schema}";

    private LibraryDbContext CreateLibraryContext(string schema)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseNpgsql(SchemaConnectionString(schema))
            .Options;
        return new LibraryDbContext(options);
    }

    private CrestCreatesDbContext CreateCrestContext(string schema)
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseNpgsql(SchemaConnectionString(schema))
            .Options;
        return new CrestCreatesDbContext(options);
    }

    private RemappedPostgresContext CreateRemappedContext(string schema)
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseNpgsql(SchemaConnectionString(schema))
            .Options;
        return new RemappedPostgresContext(options);
    }

    private SimilarPostgresContext CreateSimilarTableContext(string schema)
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseNpgsql(SchemaConnectionString(schema))
            .Options;
        return new SimilarPostgresContext(options);
    }

    private sealed class RemappedPostgresContext : CrestCreatesDbContext
    {
        public RemappedPostgresContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SettingValue>().ToTable("AppSettings");
            modelBuilder.Entity<CrestCreates.Domain.Features.FeatureValue>().ToTable("AppFeatures");
        }
    }

    private sealed class SimilarPostgresContext : CrestCreatesDbContext
    {
        public SimilarPostgresContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CrestCreates.Domain.Permission.Tenant>().ToTable("OtherSettingValues");
        }
    }

    private ServiceProvider BuildUnitOfWorkProvider(string schema)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CrestCreatesDbContext>(options => options.UseNpgsql(SchemaConnectionString(schema)));
        services.AddScoped<IEntityFrameworkCoreDbContext>(sp => sp.GetRequiredService<CrestCreatesDbContext>());
        services.AddScoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>());
        services.AddSingleton(Mock.Of<ICurrentTenant>());
        services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
        services.AddUnitOfWork();
        services.AddEfCoreUnitOfWork();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>().Database.EnsureCreated();
        return provider;
    }

    private static async Task SeedSettingAsync(ServiceProvider provider, string value)
    {
        using var scope = provider.CreateScope();
        var repository = new SettingRepository(scope.ServiceProvider.GetRequiredService<IDataBaseContext>());
        await repository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, value, false, null));
    }

    private async Task InsertDuplicateHostNullSettingsAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(SchemaConnectionString(schema));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "SettingValues"
                ("Id", "Name", "Value", "ProviderType", "Scope", "ProviderKey", "TenantId",
                 "IsEncrypted", "CreationTime", "ConcurrencyStamp")
            VALUES
                (@first, 'Sample.Global', 'first', 'Global', 1, '', NULL, false, now(), 'stamp-1'),
                (@second, 'Sample.Global', 'second', 'Global', 1, '', NULL, false, now(), 'stamp-2');
            """;
        command.Parameters.AddWithValue("first", Guid.NewGuid());
        command.Parameters.AddWithValue("second", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private Task<string?> ReadHostNullIndexDefinitionAsync(string schema)
    {
        return ReadIndexDefinitionAsync(schema, "SettingValues", HostNullIndex);
    }

    private async Task<string?> ReadIndexDefinitionAsync(string schema, string table, string indexName)
    {
        await using var connection = new NpgsqlConnection(SchemaConnectionString(schema));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = current_schema()
              AND tablename = @table
              AND indexname = @indexName;
            """;
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("indexName", indexName);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task<List<string>> ReadAppliedMigrationsAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(SchemaConnectionString(schema));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";""";
        await using var reader = await command.ExecuteReaderAsync();
        var migrations = new List<string>();
        while (await reader.ReadAsync())
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }

    private static ISettingRepository CreateStaleThenRealRepository(SettingRepository realRepository)
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

    private static CrestCreates.Application.Features.FeatureManager CreateFeatureManager(
        CrestCreates.Domain.Features.IFeatureRepository repository)
    {
        var definition = new CrestCreates.Domain.Shared.Features.FeatureDefinition(
            FeatureName, "Sample",
            scopes: CrestCreates.Domain.Shared.Features.FeatureScope.Global,
            valueType: CrestCreates.Domain.Shared.Features.FeatureValueType.String);
        var definitions = new Mock<CrestCreates.Domain.Features.IFeatureDefinitionManager>();
        definitions.Setup(manager => manager.GetOrNull(FeatureName)).Returns(definition);
        definitions.Setup(manager => manager.GetAll()).Returns(new[] { definition });
        var cache = new Mock<ICrestCacheService>();
        var invalidator = new FeatureCacheInvalidator(cache.Object, new FeatureCacheKeyContributor());

        return new CrestCreates.Application.Features.FeatureManager(
            definitions.Object,
            repository,
            Mock.Of<CrestCreates.Domain.Features.IFeatureStore>(),
            new CrestCreates.Application.Features.FeatureValueTypeConverter(),
            invalidator,
            Mock.Of<CrestCreates.Application.Features.IFeatureAuditRecorder>());
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
