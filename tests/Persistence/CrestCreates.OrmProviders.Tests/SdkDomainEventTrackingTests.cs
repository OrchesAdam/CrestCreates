using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.FreeSql.Extensions;
using CrestCreates.Data.FreeSql.Repositories;
using CrestCreates.Data.FreeSql.UnitOfWork;
using CrestCreates.Data.SqlSugar.Extensions;
using CrestCreates.Data.SqlSugar.Repositories;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.UnitOfWork;
using FluentAssertions;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;
using KernelUnitOfWorkManager = CrestCreates.Data.Abstractions.IUnitOfWorkManager;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// R3-11：SDK 仓储写入经受管 UoW 跟踪强类型领域事件；提交后发布一次，回滚不发布，
/// 子 scope 不提前发布，通知失败保留已提交事实与事件队列。
/// </summary>
public class SdkDomainEventTrackingTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "crest-r3-11-" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        foreach (var suffix in new string[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task FreeSql_insert_publishes_only_after_commit()
    {
        var publisher = new RecordingPublisher();
        using var provider = BuildFreeSqlProvider(publisher);
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<KernelUnitOfWorkManager>();
        var repository = new EventFreeSqlRepository(
            scope.ServiceProvider.GetRequiredService<FreeSqlUnitOfWorkManager>(), manager);

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        var entity = NewEntity("free-commit");
        await repository.InsertAsync(entity);

        publisher.Events.Should().BeEmpty("events must not be published before the commit");

        await unitOfWork.CompleteAsync();

        publisher.Events.Should().ContainSingle()
            .Which.Should().BeOfType<TestDomainEvent>().Which.Source.Should().Be("free-commit");
        ((IHasDomainEvents)entity).DomainEvents.Should().BeEmpty("a successful publish clears the entity queue");
    }

    [Fact]
    public async Task FreeSql_rollback_does_not_publish_and_abandon_discards_tracking()
    {
        var publisher = new RecordingPublisher();
        using var provider = BuildFreeSqlProvider(publisher);
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<KernelUnitOfWorkManager>();
        var repository = new EventFreeSqlRepository(
            scope.ServiceProvider.GetRequiredService<FreeSqlUnitOfWorkManager>(), manager);

        await using (var unitOfWork = manager.BeginScope())
        {
            await unitOfWork.StartAsync();
            await repository.InsertAsync(NewEntity("free-rollback"));
            await unitOfWork.RollbackAsync();
        }

        publisher.Events.Should().BeEmpty("a rolled back write must not notify domain events");
    }

    [Fact]
    public async Task FreeSql_tracks_the_same_entity_once_across_multiple_writes()
    {
        var publisher = new RecordingPublisher();
        using var provider = BuildFreeSqlProvider(publisher);
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<KernelUnitOfWorkManager>();
        var repository = new EventFreeSqlRepository(
            scope.ServiceProvider.GetRequiredService<FreeSqlUnitOfWorkManager>(), manager);

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        var entity = NewEntity("free-once");
        await repository.InsertAsync(entity);
        entity.AddDomainEvent(new TestDomainEvent("updated"));
        await repository.UpdateAsync(entity);

        await unitOfWork.CompleteAsync();

        publisher.Events.Should().HaveCount(2, "one tracking entry publishes both accumulated events exactly once");
        publisher.Events.Select(domainEvent => ((TestDomainEvent)domainEvent).Source)
            .Should().Equal("free-once", "updated");
    }

    [Fact]
    public async Task FreeSql_joined_scope_does_not_publish_before_the_owner_completes()
    {
        var publisher = new RecordingPublisher();
        using var provider = BuildFreeSqlProvider(publisher);
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<KernelUnitOfWorkManager>();
        var repository = new EventFreeSqlRepository(
            scope.ServiceProvider.GetRequiredService<FreeSqlUnitOfWorkManager>(), manager);

        await using var outer = manager.BeginScope();
        await outer.StartAsync();

        await using (var inner = manager.BeginScope())
        {
            await inner.StartAsync();
            await repository.InsertAsync(NewEntity("free-child"));
            await inner.CompleteAsync();
        }

        publisher.Events.Should().BeEmpty("a joined scope records participation and must not publish early");

        await outer.CompleteAsync();
        publisher.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task FreeSql_notification_failure_preserves_commit_and_retains_the_queue()
    {
        var publisher = new RecordingPublisher { FailFirstAttempt = true };
        using var provider = BuildFreeSqlProvider(publisher);
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<KernelUnitOfWorkManager>();
        var repository = new EventFreeSqlRepository(
            scope.ServiceProvider.GetRequiredService<FreeSqlUnitOfWorkManager>(), manager);

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        var entity = NewEntity("free-notify");
        await repository.InsertAsync(entity);

        var act = async () => await unitOfWork.CompleteAsync();
        await act.Should().ThrowAsync<UnitOfWorkPostCommitNotificationException>();
        unitOfWork.TransactionOutcome.Should().Be(UnitOfWorkTransactionOutcome.Committed);
        unitOfWork.NotificationOutcome.Should().Be(UnitOfWorkNotificationOutcome.Failed);
        ((IHasDomainEvents)entity).DomainEvents.Should().ContainSingle(
            "the failed entity keeps its event queue for a controlled retry");

        await ((IUnitOfWorkCommittedNotifier)unitOfWork.UnitOfWork).PublishCommittedNotificationsAsync();

        publisher.Events.Should().ContainSingle("a retry publishes the retained event");
        ((IHasDomainEvents)entity).DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SqlSugar_insert_publishes_only_after_commit()
    {
        var publisher = new RecordingPublisher();
        using var client = CreateSqlSugarClient();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(publisher);
        services.AddUnitOfWork();
        services.AddSqlSugarWithUow(() => client);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<KernelUnitOfWorkManager>();
        var repository = new EventSqlSugarRepository(
            scope.ServiceProvider.GetRequiredService<ISqlSugarClient>(), manager);

        await using var unitOfWork = manager.BeginScope();
        await unitOfWork.StartAsync();
        var entity = NewEntity("sugar-commit");
        await repository.InsertAsync(entity);

        publisher.Events.Should().BeEmpty();

        await unitOfWork.CompleteAsync();

        publisher.Events.Should().ContainSingle()
            .Which.Should().BeOfType<TestDomainEvent>().Which.Source.Should().Be("sugar-commit");
    }

    [Fact]
    public void Sdk_notification_does_not_use_reflection_over_domain_events()
    {
        var root = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Persistence", "CrestCreates.Data.FreeSql", "UnitOfWork", "FreeSqlUnitOfWork.cs"),
            Path.Combine(root, "src", "Persistence", "CrestCreates.Data.SqlSugar", "UnitOfWork", "SqlSugarUnitOfWork.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("GetProperty(\"DomainEvents\")");
            text.Should().NotContain("GetMethod(\"ClearDomainEvents\")");
        }
    }

    private ServiceProvider BuildFreeSqlProvider(RecordingPublisher publisher)
    {
        var freeSql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=" + _databasePath)
            .Build();
        freeSql.CodeFirst.SyncStructure<EventSourceEntity>();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(publisher);
        services.AddUnitOfWork();
        services.AddFreeSqlWithUow(freeSql, repositoryAssembly: null!);
        return services.BuildServiceProvider();
    }

    private SqlSugarClient CreateSqlSugarClient()
    {
        var client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = "DataSource=" + _databasePath,
            IsAutoCloseConnection = false
        });
        client.CodeFirst.InitTables(typeof(EventSourceEntity));
        return client;
    }

    private static EventSourceEntity NewEntity(string name)
    {
        var entity = new EventSourceEntity { Id = Guid.NewGuid(), Name = name };
        entity.AddDomainEvent(new TestDomainEvent(name));
        return entity;
    }

    private static string FindRepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    public sealed class EventFreeSqlRepository : FreeSqlRepository<EventSourceEntity, Guid>
    {
        public EventFreeSqlRepository(
            FreeSqlUnitOfWorkManager unitOfWorkManager,
            KernelUnitOfWorkManager kernelUnitOfWorkManager)
            : base(unitOfWorkManager, unitOfWorkManager: kernelUnitOfWorkManager)
        {
        }

        public override IQueryable<EventSourceEntity> GetQueryableUnfiltered()
            => throw new NotSupportedException();

        public override Task DeleteAsync(
            Guid id,
            string expectedStamp,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    public sealed class EventSqlSugarRepository : SqlSugarRepository<EventSourceEntity, Guid>
    {
        public EventSqlSugarRepository(
            ISqlSugarClient sqlSugarClient,
            KernelUnitOfWorkManager kernelUnitOfWorkManager)
            : base(sqlSugarClient, unitOfWorkManager: kernelUnitOfWorkManager)
        {
        }

        public override IQueryable<EventSourceEntity> GetQueryableUnfiltered()
            => throw new NotSupportedException();

        public override Task DeleteAsync(
            Guid id,
            string expectedStamp,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    public sealed class EventSourceEntity : IEntity<Guid>, IHasDomainEvents
    {
        private readonly List<IDomainEvent> _domainEvents = new();

        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        IReadOnlyCollection<IDomainEvent> IHasDomainEvents.DomainEvents => _domainEvents;

        public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

        void IHasDomainEvents.ClearDomainEvents() => _domainEvents.Clear();
    }

    public sealed class TestDomainEvent : IDomainEvent
    {
        public TestDomainEvent(string source)
        {
            Source = source;
        }

        public string Source { get; }

        public DateTime OccurredOn { get; } = DateTime.UtcNow;
    }

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public int Attempts { get; private set; }

        public bool FailFirstAttempt { get; init; }

        public List<IDomainEvent> Events { get; } = new();

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => PublishAsync((IDomainEvent)domainEvent, cancellationToken);

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (FailFirstAttempt && Attempts == 1)
            {
                throw new InvalidOperationException("publish-failure");
            }

            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
