using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Application.Settings;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.Domain.Shared.Settings;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

[Collection("ModelFilterRegistry")]
public class SettingFeatureUniquenessTests
{
    private const string SettingName = "Sample.Global";
    private const string FeatureName = "Sample.Feature";

    [Fact]
    public void Setting_host_null_natural_key_has_a_filtered_unique_index()
    {
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseInMemoryDatabase($"model-{Guid.NewGuid():N}")
            .Options;
        using var context = new CrestCreatesDbContext(options);

        var setting = context.Model.FindEntityType(typeof(SettingValue))!;
        var hostNullIndex = setting.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "Name", "Scope", "ProviderKey" }));

        hostNullIndex.IsUnique.Should().BeTrue();
        hostNullIndex.GetFilter().Should().Contain("TenantId").And.Contain("IS NULL");

        var feature = context.Model.FindEntityType(typeof(FeatureValue))!;
        feature.GetIndexes().Count(index => index.IsUnique).Should().Be(
            3,
            "the existing Feature full and filtered unique constraints must be preserved");
    }

    [Fact]
    public async Task Concurrent_set_converges_to_one_row_after_a_unique_violation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var realRepository = new SettingRepository(new EfCoreDbContextAdapter(context));

        var winner = new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "winner", false, null);
        await realRepository.InsertAsync(winner);

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

        var rows = await context.SettingValues.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle("the unique index must keep one row for the host-null natural key");
        rows[0].Value.Should().Be("loser", "the losing writer must converge on the persisted row");
        findCalls.Should().Be(2, "the first read is stale and the retry re-reads the winner");
    }

    [Fact]
    public async Task Distinct_natural_keys_do_not_collide_across_partitions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = new SettingRepository(new EfCoreDbContextAdapter(context));

        await repository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "global", false, null));
        await repository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.Tenant, "tenant-a", "tenant", false, "tenant-a"));
        await repository.InsertAsync(new SettingValue(
            Guid.NewGuid(), SettingName, SettingScope.User, "user-1", "user", false, null));

        (await context.SettingValues.AsNoTracking().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Non_unique_database_errors_are_not_treated_as_a_concurrency_win()
    {
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue?)null);
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database offline"));

        var manager = CreateSettingManager(repository.Object);

        var act = async () => await manager.SetGlobalAsync(SettingName, "value");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*offline*");
        repository.Verify(
            repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Unique_violation_without_a_visible_winner_is_rethrown()
    {
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue?)null);
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CrestUniqueConstraintException(nameof(SettingValue), new InvalidOperationException()));

        var manager = CreateSettingManager(repository.Object);

        var act = async () => await manager.SetGlobalAsync(SettingName, "value");

        await act.Should().ThrowAsync<CrestUniqueConstraintException>();
        repository.Verify(
            repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Feature_set_converges_after_a_unique_violation()
    {
        var existing = new FeatureValue(
            Guid.NewGuid(), FeatureName, FeatureScope.Global, string.Empty, "old", null);
        var findCalls = 0;
        var repository = new Mock<IFeatureRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                FeatureName, FeatureScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => findCalls++ == 0 ? null : existing);
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CrestUniqueConstraintException(nameof(FeatureValue), new InvalidOperationException()));
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeatureValue entity, CancellationToken _) => entity);

        var manager = CreateFeatureManager(repository.Object);

        await manager.SetGlobalAsync(FeatureName, "new");

        existing.Value.Should().Be("new");
        repository.Verify(
            repo => repo.UpdateAsync(existing, It.IsAny<CancellationToken>()),
            Times.Once);
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

    private static CrestCreatesDbContext CreateContext(SqliteConnection connection)
    {
        var hostTenant = new Mock<ICurrentTenant>();
        hostTenant.SetupGet(tenant => tenant.Id).Returns((string?)null);
        var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>()
            .Options;

        return new CrestCreatesDbContext(options, hostTenant.Object);
    }
}
