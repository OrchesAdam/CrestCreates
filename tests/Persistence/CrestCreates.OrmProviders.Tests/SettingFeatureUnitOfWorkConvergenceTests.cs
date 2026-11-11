using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Application.Settings;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Data.Abstractions;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// Natural-key convergence through the formal unit-of-work transaction boundary. A deferred
/// insert would surface the provider unique violation only at the final commit, where the
/// manager retry is unreachable; these tests lock the flush/re-read recovery to the statement
/// boundary and prove early flushes are still discarded by a rollback.
/// </summary>
public class SettingFeatureUnitOfWorkConvergenceTests : IDisposable
{
    private const string SettingName = "Sample.Global";
    private const string FeatureName = "Sample.Feature";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"crest-setting-uow-{Guid.NewGuid():N}.db");

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

    [Fact]
    public async Task Setting_converges_inside_a_formal_unit_of_work_and_commits_business_writes_once()
    {
        using var provider = BuildProvider();
        await SeedSettingAsync(provider, "winner");
        var businessTenantId = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            var manager = CreateSettingManager(CreateStaleRepository(dbContext, out var findCalls));

            await using (var unitOfWork = unitOfWorkManager.BeginScope())
            {
                await unitOfWork.StartAsync();
                dbContext.Tenants.Add(new Tenant(businessTenantId, "convergence-tenant"));

                await manager.SetGlobalAsync(SettingName, "loser");

                await unitOfWork.CompleteAsync();
                unitOfWork.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
            }

            findCalls().Should().Be(2, "the stale first read must be followed by a winner re-read");
        }

        using (var verifyScope = provider.CreateScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            var rows = await dbContext.SettingValues.AsNoTracking().ToListAsync();
            rows.Should().ContainSingle();
            rows[0].Value.Should().Be("loser", "the loser must converge on the single persisted natural-key row");
            (await dbContext.Tenants.FindAsync(businessTenantId)).Should().NotBeNull(
                "the unrelated business write committed with the same unit of work exactly once");
        }
    }

    [Fact]
    public async Task Setting_convergence_is_discarded_when_the_unit_of_work_rolls_back()
    {
        using var provider = BuildProvider();
        await SeedSettingAsync(provider, "winner");

        using (var scope = provider.CreateScope())
        {
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            var manager = CreateSettingManager(CreateStaleRepository(dbContext, out _));

            await using (var unitOfWork = unitOfWorkManager.BeginScope())
            {
                await unitOfWork.StartAsync();
                await manager.SetGlobalAsync(SettingName, "rolled-back");
                await unitOfWork.RollbackAsync();
            }
        }

        using (var verifyScope = provider.CreateScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            var rows = await dbContext.SettingValues.AsNoTracking().ToListAsync();
            rows.Should().ContainSingle();
            rows[0].Value.Should().Be("winner", "the savepoint-protected flush inside the transaction must not commit early");
        }
    }

    [Fact]
    public async Task Feature_converges_inside_a_formal_unit_of_work()
    {
        using var provider = BuildProvider();
        await SeedFeatureAsync(provider, "winner");

        using (var scope = provider.CreateScope())
        {
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            var repository = CreateStaleFeatureRepository(dbContext, out var findCalls);
            var manager = CreateFeatureManager(repository);

            await using (var unitOfWork = unitOfWorkManager.BeginScope())
            {
                await unitOfWork.StartAsync();
                await manager.SetGlobalAsync(FeatureName, "loser");
                await unitOfWork.CompleteAsync();
            }

            findCalls().Should().Be(2);
        }

        using (var verifyScope = provider.CreateScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<CrestCreatesDbContext>();
            var rows = await dbContext.FeatureValues.AsNoTracking().ToListAsync();
            rows.Should().ContainSingle();
            rows[0].Value.Should().Be("loser");
        }
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CrestCreatesDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
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

    private static async Task SeedFeatureAsync(ServiceProvider provider, string value)
    {
        using var scope = provider.CreateScope();
        var repository = new FeatureRepository(scope.ServiceProvider.GetRequiredService<IDataBaseContext>());
        await repository.InsertAsync(new FeatureValue(
            Guid.NewGuid(), FeatureName, FeatureScope.Global, string.Empty, value, null));
    }

    private static ISettingRepository CreateStaleRepository(
        CrestCreatesDbContext dbContext,
        out Func<int> findCalls)
    {
        var realRepository = new SettingRepository(dbContext);
        var calls = 0;
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                It.IsAny<string>(), It.IsAny<SettingScope>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string name, SettingScope settingScope, string providerKey, string? tenantId, CancellationToken token) =>
            {
                calls++;
                return calls == 1
                    ? Task.FromResult<SettingValue?>(null)
                    : realRepository.FindAsync(name, settingScope, providerKey, tenantId, token);
            });
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue entity, CancellationToken token) => realRepository.UpdateAsync(entity, token));

        findCalls = () => calls;
        return repository.Object;
    }

    private static IFeatureRepository CreateStaleFeatureRepository(
        CrestCreatesDbContext dbContext,
        out Func<int> findCalls)
    {
        var realRepository = new FeatureRepository(dbContext);
        var calls = 0;
        var repository = new Mock<IFeatureRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                It.IsAny<string>(), It.IsAny<FeatureScope>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string name, FeatureScope featureScope, string providerKey, string? tenantId, CancellationToken token) =>
            {
                calls++;
                return calls == 1
                    ? Task.FromResult<FeatureValue?>(null)
                    : realRepository.FindAsync(name, featureScope, providerKey, tenantId, token);
            });
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((FeatureValue entity, CancellationToken token) => realRepository.InsertAsync(entity, token));
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((FeatureValue entity, CancellationToken token) => realRepository.UpdateAsync(entity, token));

        findCalls = () => calls;
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

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;
    }
}
