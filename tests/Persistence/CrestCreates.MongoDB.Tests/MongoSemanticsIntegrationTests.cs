using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.UnitOfWork;
using MongoDB.Bson;
using MongoDB.Driver.Core.Events;
using System.Threading.Tasks;
using CrestCreates.DataFilter.Entities;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.MongoDB.Repositories;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace CrestCreates.MongoDB.Tests;

public sealed class MongoServerFixture : IAsyncLifetime
{
    private readonly IContainer _server = new ContainerBuilder()
        .WithImage("mongo:7").WithPortBinding(27017, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(27017)).Build();
    public string ConnectionString => $"mongodb://{_server.Hostname}:{_server.GetMappedPublicPort(27017)}";
    public Task InitializeAsync() => _server.StartAsync();
    public async Task DisposeAsync() => await _server.DisposeAsync();
}

[CollectionDefinition("Mongo server", DisableParallelization = true)]
public sealed class MongoServerCollection : ICollectionFixture<MongoServerFixture> { }

[Collection("Mongo server")]
public class MongoSemanticsIntegrationTests
{
    private readonly MongoServerFixture _server;
    public MongoSemanticsIntegrationTests(MongoServerFixture server) => _server = server;
    private IMongoDatabase Database() => new MongoClient(_server.ConnectionString).GetDatabase("itest_" + Guid.NewGuid().ToString("N"));
    private static ICurrentTenant Tenant(string? id)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(x => x.Id).Returns(id);
        return tenant.Object;
    }
    private static MongoRepositoryBase<T, string> Repo<T>(IMongoDatabase db, string? id) where T : class, IEntity<string> => new(db, Tenant(id));

    [Fact]
    public async Task Deleted_rows_are_hidden()
    {
        var db = Database();
        await db.GetCollection<MappedOptional>(nameof(MappedOptional)).InsertManyAsync(new[]
        {
            new MappedOptional { Id = "live", TenantId = "A" },
            new MappedOptional { Id = "deleted", TenantId = "A", IsDeleted = true }
        });
        (await Repo<MappedOptional>(db, "A").GetListAsync()).Select(x => x.Id).Should().Equal("live");
    }

    [Fact]
    public async Task Paging_starts_at_zero_and_is_stable()
    {
        var db = Database();
        await db.GetCollection<MappedOptional>(nameof(MappedOptional)).InsertManyAsync(new[]
        {
            new MappedOptional { Id = "c", TenantId = "A" },
            new MappedOptional { Id = "a", TenantId = "A" },
            new MappedOptional { Id = "b", TenantId = "A" }
        });
        var repo = Repo<MappedOptional>(db, "A");
        (await repo.GetPagedAsync(0, 2)).Items.Select(x => x.Id).Should().Equal("a", "b");
        (await repo.GetPagedAsync(1, 2)).Items.Select(x => x.Id).Should().Equal("c");
    }

    [Fact]
    public async Task Stale_stamp_has_typed_conflict()
    {
        var db = Database();
        var repo = Repo<MappedOptional>(db, "A");
        await repo.InsertAsync(new MappedOptional { Id = "one", TenantId = "A", ConcurrencyStamp = "old-opaque" });
        var first = (await repo.GetAsync("one"))!;
        var stale = (await repo.GetAsync("one"))!;
        first.Name = "winner";
        await repo.UpdateAsync(first);
        first.ConcurrencyStamp.Should().MatchRegex("^[0-9a-f]{32}$");
        stale.Name = "loser";
        await repo.Invoking(x => x.UpdateAsync(stale)).Should().ThrowAsync<CrestConcurrencyException>();
        (await repo.GetAsync("one"))!.Name.Should().Be("winner");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A")]
    [InlineData("B")]
    public async Task Every_read_overload_respects_optional_scope_and_live_rows(string? tenant)
    {
        var db = Database();
        var rows = new[]
        {
            new MappedOptional { Id = "a", TenantId = "A" },
            new MappedOptional { Id = "b", TenantId = "B" },
            new MappedOptional { Id = "s" },
            new MappedOptional { Id = "ad", TenantId = "A", IsDeleted = true },
            new MappedOptional { Id = "bd", TenantId = "B", IsDeleted = true },
            new MappedOptional { Id = "sd", IsDeleted = true }
        };
        await db.GetCollection<MappedOptional>(nameof(MappedOptional)).InsertManyAsync(rows);
        var expected = rows.Where(x => !x.IsDeleted && (x.TenantId == null || x.TenantId == tenant)).Select(x => x.Id).OrderBy(x => x).ToArray();
        await AssertReads(Repo<MappedOptional>(db, tenant), rows.Select(x => x.Id), expected);
        if (tenant == null) await AssertReads(new MongoRepositoryBase<MappedOptional, string>(db), rows.Select(x => x.Id), expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A")]
    [InlineData("B")]
    public async Task Every_read_overload_respects_required_scope_and_live_rows(string? tenant)
    {
        var db = Database();
        var rows = new[]
        {
            new MappedRequired { Id = "a", TenantId = "A" },
            new MappedRequired { Id = "b", TenantId = "B" },
            new MappedRequired { Id = "s", TenantId = "" },
            new MappedRequired { Id = "ad", TenantId = "A", IsDeleted = true },
            new MappedRequired { Id = "bd", TenantId = "B", IsDeleted = true }
        };
        await db.GetCollection<MappedRequired>(nameof(MappedRequired)).InsertManyAsync(rows);
        var expected = rows.Where(x => tenant != null && x.TenantId == tenant && !x.IsDeleted).Select(x => x.Id).ToArray();
        await AssertReads(Repo<MappedRequired>(db, tenant), rows.Select(x => x.Id), expected);
        if (tenant == null) await AssertReads(new MongoRepositoryBase<MappedRequired, string>(db), rows.Select(x => x.Id), expected);
    }

    private static async Task AssertReads<T>(MongoRepositoryBase<T, string> repo, IEnumerable<string> ids, string[] expected) where T : class, IEntity<string>
    {
        repo.GetQueryable().Select(x => x.Id).ToList().Should().BeEquivalentTo(expected);
        (await repo.GetListAsync()).Select(x => x.Id).Should().BeEquivalentTo(expected);
        (await repo.GetListAsync(x => true)).Select(x => x.Id).Should().BeEquivalentTo(expected);
        (await repo.GetListAsync(x => true, x => x.Id)).Select(x => x.Id).Should().BeEquivalentTo(expected);
        (await repo.GetCountAsync()).Should().Be(expected.Length);
        (await repo.GetCountAsync(x => true)).Should().Be(expected.Length);
        (await repo.AnyAsync()).Should().Be(expected.Length > 0);
        (await repo.AnyAsync(x => true)).Should().Be(expected.Length > 0);
        foreach (var id in ids)
        {
            var visible = expected.Contains(id);
            ((await repo.GetAsync(id)) != null).Should().Be(visible);
            ((await repo.GetAsync(x => x.Id == id)) != null).Should().Be(visible);
            (await repo.ExistsAsync(id)).Should().Be(visible);
            (await repo.AnyAsync(x => x.Id == id)).Should().Be(visible);
            (await repo.GetCountAsync(x => x.Id == id)).Should().Be(visible ? 1 : 0);
        }
        var pages = new[]
        {
            await repo.GetPagedAsync(0, 10), await repo.GetPagedAsync(0, 10, x => true),
            await repo.GetPagedAsync(0, 10, x => true, x => x.Id), await repo.GetPagedAsync(0, 10, x => x.Id)
        };
        foreach (var page in pages)
        {
            page.Items.Select(x => x.Id).Should().BeEquivalentTo(expected);
            page.TotalCount.Should().Be(expected.Length);
        }
    }

    private IMongoDatabase ObservedDatabase(IMongoDatabase db, Action<CommandStartedEvent> observe)
    {
        var settings = MongoClientSettings.FromConnectionString(_server.ConnectionString);
        settings.ClusterConfigurator = cluster => cluster.Subscribe<CommandStartedEvent>(command =>
        {
            if (command.DatabaseNamespace.DatabaseName == db.DatabaseNamespace.DatabaseName) observe(command);
        });
        return new MongoClient(settings).GetDatabase(db.DatabaseNamespace.DatabaseName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Managed_windows_reject_every_entry_with_zero_Mongo_commands(bool transactional, bool start)
    {
        var db = Database();
        var commands = 0;
        var repo = Repo<MappedOptional>(ObservedDatabase(db, _ => Interlocked.Increment(ref commands)), "A");
        var query = repo.GetQueryable().Where(x => x.Id == "one").Select(x => x.Id);
        var enumerator = query.GetEnumerator();
        var unit = new Mock<IUnitOfWork>();
        var factory = new Mock<IUnitOfWorkFactory>();
        factory.Setup(x => x.Create(OrmProvider.EfCore)).Returns(unit.Object);
        var manager = new UnitOfWorkManager(factory.Object);
        await using (var scope = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = transactional }))
        {
            if (start) await scope.StartAsync();
            Action acquire = () => repo.GetQueryable();
            acquire.Should().Throw<NotSupportedException>().WithMessage("*no managed unit-of-work binding*");
            Action deferred = () => query.ToList();
            deferred.Should().Throw<NotSupportedException>();
            Action scalar = () => query.Count();
            scalar.Should().Throw<NotSupportedException>();
            Action move = () => enumerator.MoveNext();
            move.Should().Throw<NotSupportedException>();
            var row = new MappedOptional { Id = "one", TenantId = "A" };
            var operations = new Func<Task>[]
            {
                async () => await repo.GetListAsync(), async () => await repo.GetListAsync(x => true),
                async () => await repo.GetListAsync(x => true, x => x.Id),
                async () => await repo.GetAsync("one"), async () => await repo.GetAsync(x => true),
                async () => await repo.GetCountAsync(), async () => await repo.GetCountAsync(x => true),
                async () => await repo.AnyAsync(), async () => await repo.AnyAsync(x => true), async () => await repo.ExistsAsync("one"),
                async () => await repo.GetPagedAsync(0, 1), async () => await repo.GetPagedAsync(0, 1, x => true),
                async () => await repo.GetPagedAsync(0, 1, x => true, x => x.Id), async () => await repo.GetPagedAsync(0, 1, x => x.Id),
                async () => await repo.InsertAsync(row), async () => await repo.InsertRangeAsync(new[] { row }),
                async () => await repo.UpdateAsync(row), async () => await repo.UpdateRangeAsync(new[] { row }),
                () => repo.DeleteAsync(row), () => repo.DeleteAsync("one"), () => repo.DeleteAsync("one", row.ConcurrencyStamp),
                () => repo.DeleteRangeAsync(new[] { row }), () => repo.DeleteRangeAsync(x => true)
            };
            foreach (var operation in operations) await operation.Should().ThrowAsync<NotSupportedException>().WithMessage("*no managed unit-of-work binding*");
            commands.Should().Be(0);
            if (start) await scope.CompleteAsync();
        }
        enumerator.Dispose();
        await repo.InsertAsync(new MappedOptional { Id = "outside", TenantId = "A" });
        commands.Should().BeGreaterThan(0);
        (await repo.ExistsAsync("outside")).Should().BeTrue();
    }

    [Fact]
    public void Deferred_query_rejects_changed_tenant_without_commands()
    {
        var db = Database();
        var commands = 0;
        string? tenantId = "A";
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(x => x.Id).Returns(() => tenantId);
        var repo = new MongoRepositoryBase<MappedOptional, string>(ObservedDatabase(db, _ => commands++), tenant.Object);
        var query = repo.GetQueryable();
        var untyped = query.Provider.CreateQuery(query.Expression);
        tenantId = "B";
        Action typed = () => query.Count();
        Action enumerate = () => untyped.GetEnumerator();
        typed.Should().Throw<InvalidOperationException>();
        enumerate.Should().Throw<InvalidOperationException>();
        commands.Should().Be(0);
    }

    [Theory]
    [InlineData("Concat")]
    [InlineData("Union")]
    public async Task Multi_source_query_rejects_cross_tenant_context_change_without_commands(string operation)
    {
        var db = Database();
        var collection = db.GetCollection<MappedOptional>(nameof(MappedOptional));
        await collection.InsertManyAsync(new[]
        {
            new MappedOptional { Id = "a", TenantId = "A" },
            new MappedOptional { Id = "b", TenantId = "B" }
        });
        var commands = 0;
        var observed = ObservedDatabase(db, _ => Interlocked.Increment(ref commands));
        string? firstTenant = "A";
        string? secondTenant = "B";
        var firstContext = new Mock<ICurrentTenant>();
        var secondContext = new Mock<ICurrentTenant>();
        firstContext.Setup(x => x.Id).Returns(() => firstTenant);
        secondContext.Setup(x => x.Id).Returns(() => secondTenant);
        var first = new MongoRepositoryBase<MappedOptional, string>(observed, firstContext.Object).GetQueryable();
        var second = new MongoRepositoryBase<MappedOptional, string>(observed, secondContext.Object).GetQueryable();
        var query = operation == "Concat" ? first.Concat(second) : first.Union(second);
        commands.Should().Be(0);

        secondTenant = "A";
        Action materialize = () => query.ToList();
        materialize.Should().Throw<InvalidOperationException>().WithMessage("*tenant context changed*");
        Action scalar = () => query.Count();
        scalar.Should().Throw<InvalidOperationException>().WithMessage("*tenant context changed*");
        commands.Should().Be(0);

        secondTenant = "B";
        firstTenant = "B";
        materialize.Should().Throw<InvalidOperationException>().WithMessage("*tenant context changed*");
        commands.Should().Be(0);
    }

    [Fact]
    public async Task Unsupported_standard_join_rejects_before_Mongo_commands()
    {
        var db = Database();
        await db.GetCollection<MappedOptional>(nameof(MappedOptional))
            .InsertOneAsync(new MappedOptional { Id = "a", TenantId = "A" });
        var commands = 0;
        var repo = Repo<MappedOptional>(ObservedDatabase(db, _ => Interlocked.Increment(ref commands)), "A");
        var source = repo.GetQueryable();
        var query = source.Join(source, x => x.Id, x => x.Id, (x, y) => x.Id);
        Action materialize = () => query.ToList();
        materialize.Should().Throw<NotSupportedException>();
        commands.Should().Be(0);
    }

    [Theory]
    [InlineData("Concat")]
    [InlineData("Union")]
    public async Task Same_tenant_multi_source_query_rejects_before_Mongo_commands(string operation)
    {
        var db = Database();
        await db.GetCollection<MappedOptional>(nameof(MappedOptional))
            .InsertOneAsync(new MappedOptional { Id = "a", TenantId = "A" });
        var commands = 0;
        var repo = Repo<MappedOptional>(ObservedDatabase(db, _ => Interlocked.Increment(ref commands)), "A");
        var first = repo.GetQueryable();
        var second = repo.GetQueryable();
        var query = operation == "Concat" ? first.Concat(second) : first.Union(second);
        var untyped = query.Provider.CreateQuery(query.Expression);
        Action materialize = () => query.ToList();
        materialize.Should().Throw<NotSupportedException>().WithMessage("*multiple guarded MongoDB query sources*");
        Action scalar = () => query.Count();
        scalar.Should().Throw<NotSupportedException>().WithMessage("*multiple guarded MongoDB query sources*");
        Action enumerateUntyped = () => untyped.GetEnumerator();
        enumerateUntyped.Should().Throw<NotSupportedException>().WithMessage("*multiple guarded MongoDB query sources*");
        commands.Should().Be(0);
    }

    [Fact]
    public async Task Single_source_projection_scalar_and_untyped_query_remain_executable()
    {
        var db = Database();
        await db.GetCollection<MappedOptional>(nameof(MappedOptional)).InsertManyAsync(new[]
        {
            new MappedOptional { Id = "a", TenantId = "A" },
            new MappedOptional { Id = "b", TenantId = "B" }
        });
        var repo = Repo<MappedOptional>(db, "A");
        var query = repo.GetQueryable().Where(x => x.Id == "a").Select(x => x.Id);
        query.ToList().Should().Equal("a");
        query.Count().Should().Be(1);
        var untyped = query.Provider.CreateQuery(query.Expression);
        var rows = new List<object>();
        foreach (var row in untyped) rows.Add(row);
        rows.Should().Equal("a");
        repo.GetQueryable().Count(x => x.Id == "a").Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_multi_source_query_rejects_with_zero_Mongo_commands(bool start)
    {
        var db = Database();
        var commands = 0;
        var repo = Repo<MappedOptional>(ObservedDatabase(db, _ => Interlocked.Increment(ref commands)), "A");
        var first = repo.GetQueryable();
        var second = repo.GetQueryable();
        var unit = new Mock<IUnitOfWork>();
        var factory = new Mock<IUnitOfWorkFactory>();
        factory.Setup(x => x.Create(OrmProvider.EfCore)).Returns(unit.Object);
        var manager = new UnitOfWorkManager(factory.Object);
        await using (var scope = manager.BeginScope(new UnitOfWorkOptions { IsTransactional = false }))
        {
            if (start) await scope.StartAsync();
            var concat = first.Concat(second);
            var union = first.Union(second);
            var join = first.Join(second, x => x.Id, x => x.Id, (x, y) => x.Id);
            Action deferredConcat = () => concat.ToList();
            Action deferredUnion = () => union.Count();
            Action deferredJoin = () => join.ToList();
            deferredConcat.Should().Throw<NotSupportedException>().WithMessage("*no managed unit-of-work binding*");
            deferredUnion.Should().Throw<NotSupportedException>().WithMessage("*no managed unit-of-work binding*");
            deferredJoin.Should().Throw<NotSupportedException>().WithMessage("*no managed unit-of-work binding*");
            commands.Should().Be(0);
            if (start) await scope.CompleteAsync();
        }
        commands.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(int.MaxValue, 2)]
    public async Task Invalid_pages_reject_all_overloads_before_count(int index, int size)
    {
        var db = Database();
        var commands = 0;
        var repo = Repo<MappedOptional>(ObservedDatabase(db, _ => commands++), "A");
        var operations = new Func<Task>[]
        {
            async () => await repo.GetPagedAsync(index, size), async () => await repo.GetPagedAsync(index, size, x => true),
            async () => await repo.GetPagedAsync(index, size, x => true, x => x.Id), async () => await repo.GetPagedAsync(index, size, x => x.Id)
        };
        foreach (var operation in operations)
        {
            if (index == int.MaxValue) await operation.Should().ThrowAsync<OverflowException>();
            else await operation.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }
        commands.Should().Be(0);
    }

    [Fact]
    public async Task Every_paging_overload_has_stable_ties_and_zero_based_pages()
    {
        var db = Database();
        await db.GetCollection<MappedOptional>(nameof(MappedOptional)).InsertManyAsync(new[] { "c", "a", "b" }
            .Select(id => new MappedOptional { Id = id, TenantId = "A" }));
        var repo = Repo<MappedOptional>(db, "A");
        for (var i = 0; i < 2; i++)
        {
            var expected = i == 0 ? new[] { "a", "b" } : new[] { "c" };
            var pages = new[]
            {
                await repo.GetPagedAsync(i, 2), await repo.GetPagedAsync(i, 2, x => true),
                await repo.GetPagedAsync(i, 2, x => true, x => x.Name), await repo.GetPagedAsync(i, 2, x => x.Name, false)
            };
            foreach (var page in pages) { page.Items.Select(x => x.Id).Should().Equal(expected); page.TotalCount.Should().Be(3); }
        }
        (await repo.GetPagedAsync(0, 2, x => x.Id, false)).Items.Select(x => x.Id).Should().Equal("c", "b");
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("id")]
    [InlineData("stamp")]
    public async Task Soft_delete_changes_only_delete_fields_and_stamp(string method)
    {
        var db = Database();
        var collection = db.GetCollection<MappedOptional>(nameof(MappedOptional));
        await collection.InsertOneAsync(new MappedOptional { Id = "one", TenantId = "A", Name = "original", ConcurrencyStamp = "opaque-old" });
        var repo = Repo<MappedOptional>(db, "A");
        var row = (await repo.GetAsync("one"))!;
        row.Name = "forged";
        if (method == "entity") await repo.DeleteAsync(row);
        else if (method == "id") await repo.DeleteAsync("one");
        else await repo.DeleteAsync("one", "opaque-old");
        (await repo.GetCountAsync()).Should().Be(0);
        (await repo.GetAsync("one")).Should().BeNull();
        var stored = await collection.Find(x => x.Id == "one").SingleAsync();
        stored.IsDeleted.Should().BeTrue(); stored.DeletionTime.Should().NotBeNull();
        stored.Name.Should().Be("original"); stored.TenantId.Should().Be("A");
        stored.ConcurrencyStamp.Should().MatchRegex("^[0-9a-f]{32}$");
        await repo.Invoking(x => x.DeleteAsync("one", "opaque-old")).Should().ThrowAsync<CrestConcurrencyException>();
        await repo.DeleteAsync("one");
        var repeated = await collection.Find(x => x.Id == "one").SingleAsync();
        repeated.DeletionTime.Should().Be(stored.DeletionTime);
        repeated.ConcurrencyStamp.Should().Be(stored.ConcurrencyStamp);
    }

    [Theory]
    [InlineData(false, "update", false)]
    [InlineData(false, "entity", false)]
    [InlineData(false, "stamp", false)]
    [InlineData(true, "update", false)]
    [InlineData(true, "entity", false)]
    [InlineData(true, "stamp", false)]
    [InlineData(false, "update", true)]
    [InlineData(false, "entity", true)]
    [InlineData(false, "stamp", true)]
    [InlineData(true, "update", true)]
    [InlineData(true, "entity", true)]
    [InlineData(true, "stamp", true)]
    public async Task Final_write_rechecks_tenant_and_stamp(bool physical, string method, bool changeStamp)
    {
        if (physical) await RaceWrite(new MappedPhysical { Id = "one", TenantId = "A", ConcurrencyStamp = "old" }, method, changeStamp);
        else await RaceWrite(new MappedOptional { Id = "one", TenantId = "A", ConcurrencyStamp = "old" }, method, changeStamp);
    }

    private async Task RaceWrite<T>(T original, string method, bool changeStamp) where T : class, IEntity<string>, IMultiTenant, IHasConcurrencyStamp
    {
        var db = Database();
        var collection = db.GetCollection<T>(typeof(T).Name);
        await collection.InsertOneAsync(original);
        var raced = false;
        var observed = ObservedDatabase(db, command =>
        {
            if (!raced && (command.CommandName == "update" || command.CommandName == "delete"))
            {
                raced = true;
                var update = changeStamp ? Builders<T>.Update.Set(x => x.ConcurrencyStamp, "new-winner") : Builders<T>.Update.Set(x => x.TenantId, "B");
                collection.UpdateOne(x => x.Id == "one", update);
            }
        });
        var repo = Repo<T>(observed, "A");
        var row = (await repo.GetAsync("one"))!;
        Func<Task> operation = method == "update" ? async () => await repo.UpdateAsync(row)
            : method == "stamp" ? () => repo.DeleteAsync("one", "old") : () => repo.DeleteAsync(row);
        await operation.Should().ThrowAsync<CrestConcurrencyException>();
        raced.Should().BeTrue();
        var stored = await collection.Find(x => x.Id == "one").SingleAsync();
        stored.TenantId.Should().Be(changeStamp ? "A" : "B");
        stored.ConcurrencyStamp.Should().Be(changeStamp ? "new-winner" : "old");
        if (stored is ISoftDelete soft) soft.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Stamped_batches_and_tenant_update_ranges_reject_with_zero_commands()
    {
        var db = Database();
        var commands = 0;
        var observed = ObservedDatabase(db, _ => commands++);
        var repo = Repo<MappedOptional>(observed, "A");
        await repo.Invoking(x => x.DeleteRangeAsync(new[] { new MappedOptional { TenantId = "A" } })).Should().ThrowAsync<NotSupportedException>();
        await repo.Invoking(x => x.DeleteRangeAsync(x => true)).Should().ThrowAsync<NotSupportedException>();
        await repo.Invoking(x => x.UpdateRangeAsync(new[] { new MappedOptional { TenantId = "A" } })).Should().ThrowAsync<NotSupportedException>();
        await Repo<MappedRequired>(observed, "A").Invoking(x => x.UpdateRangeAsync(new[] { new MappedRequired { TenantId = "A" } })).Should().ThrowAsync<NotSupportedException>();
        var physical = Repo<MappedPhysical>(observed, "A");
        await physical.Invoking(x => x.DeleteRangeAsync(new[] { new MappedPhysical { TenantId = "A" } })).Should().ThrowAsync<NotSupportedException>();
        await physical.Invoking(x => x.DeleteRangeAsync(x => true)).Should().ThrowAsync<NotSupportedException>();
        commands.Should().Be(0);
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("id")]
    [InlineData("entities")]
    [InlineData("predicate")]
    public async Task Unstamped_soft_delete_overloads_preserve_rows_and_business_fields(string method)
    {
        var db = Database();
        var collection = db.GetCollection<MappedRequired>(nameof(MappedRequired));
        var repo = Repo<MappedRequired>(db, "A");
        await repo.InsertAsync(new MappedRequired { Id = "one" });
        await repo.InsertRangeAsync(new[] { new MappedRequired { Id = "two" } });
        var row = (await repo.GetAsync("one"))!;
        row.TenantId.Should().Be("A");
        (await repo.GetAsync("two"))!.TenantId.Should().Be("A");
        row.Name = "forged";
        if (method == "entity") await repo.DeleteAsync(row);
        else if (method == "id") await repo.DeleteAsync("one");
        else if (method == "entities") await repo.DeleteRangeAsync(new[] { row });
        else await repo.DeleteRangeAsync(x => x.Id == "one");
        (await repo.GetCountAsync()).Should().Be(1);
        var stored = await collection.Find(x => x.Id == "one").SingleAsync();
        stored.IsDeleted.Should().BeTrue(); stored.Name.Should().Be("original"); stored.DeletionTime.Should().NotBeNull();
        await repo.DeleteAsync("one");
        (await collection.Find(x => x.Id == "one").SingleAsync()).DeletionTime.Should().Be(stored.DeletionTime);
        (await collection.CountDocumentsAsync(_ => true)).Should().Be(2);
    }

    [Fact]
    public async Task Stale_update_reports_framework_concurrency_exception()
    {
        var db = Database();
        await db.GetCollection<MappedPhysical>(nameof(MappedPhysical)).InsertOneAsync(new MappedPhysical { Id = "one", TenantId = "A", ConcurrencyStamp = "current" });
        var repo = Repo<MappedPhysical>(db, "A");
        await repo.Invoking(x => x.UpdateAsync(new MappedPhysical { Id = "one", TenantId = "A", ConcurrencyStamp = "stale" }))
            .Should().ThrowAsync<CrestConcurrencyException>();
        (await db.GetCollection<MappedPhysical>(nameof(MappedPhysical)).Find(x => x.Id == "one").SingleAsync()).ConcurrencyStamp.Should().Be("current");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A")]
    [InlineData("B")]
    public async Task Optional_ownership_rejects_forged_detached_and_shared_writes(string? tenant)
    {
        var db = Database();
        var collection = db.GetCollection<MappedOptional>(nameof(MappedOptional));
        await collection.InsertManyAsync(new[]
        {
            new MappedOptional { Id = "a", TenantId = "A" }, new MappedOptional { Id = "b", TenantId = "B" },
            new MappedOptional { Id = "shared" }, new MappedOptional { Id = "deleted", TenantId = tenant, IsDeleted = true }
        });
        var repo = Repo<MappedOptional>(db, tenant);
        foreach (var target in new[] { "a", "b", "shared", "deleted" })
        {
            var actual = target == "a" ? "A" : target == "b" ? "B" : target == "shared" ? null : tenant;
            if (actual == tenant && target != "deleted") continue;
            var forged = new MappedOptional { Id = target, TenantId = tenant, Name = "forged" };
            await repo.Invoking(x => x.UpdateAsync(forged)).Should().ThrowAsync<CrestConcurrencyException>();
            await repo.Invoking(x => x.DeleteAsync(forged)).Should().ThrowAsync<CrestConcurrencyException>();
        }
        var forbidden = tenant == "A" ? "B" : "A";
        var commands = 0;
        var observedRepo = Repo<MappedOptional>(ObservedDatabase(db, _ => commands++), tenant);
        await observedRepo.Invoking(x => x.InsertAsync(new MappedOptional { TenantId = forbidden })).Should().ThrowAsync<InvalidOperationException>();
        await observedRepo.Invoking(x => x.InsertRangeAsync(new[] { new MappedOptional { TenantId = tenant }, new MappedOptional { TenantId = forbidden } })).Should().ThrowAsync<InvalidOperationException>();
        await observedRepo.Invoking(x => x.UpdateAsync(new MappedOptional { TenantId = forbidden })).Should().ThrowAsync<InvalidOperationException>();
        await observedRepo.Invoking(x => x.DeleteAsync(new MappedOptional { TenantId = forbidden })).Should().ThrowAsync<InvalidOperationException>();
        if (tenant != null)
        {
            await observedRepo.Invoking(x => x.UpdateAsync(new MappedOptional { Id = "shared" })).Should().ThrowAsync<InvalidOperationException>();
            await observedRepo.Invoking(x => x.DeleteAsync(new MappedOptional { Id = "shared" })).Should().ThrowAsync<InvalidOperationException>();
        }
        commands.Should().Be(0);
        var stored = await collection.Find(_ => true).ToListAsync();
        stored.Should().HaveCount(4);
        stored.Should().OnlyContain(x => x.Name == "same" && x.ConcurrencyStamp == "legacy-token");
        stored.Count(x => x.IsDeleted).Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A")]
    [InlineData("B")]
    public async Task Required_detached_writes_preserve_persisted_ownership_and_host_rejects(string? tenant)
    {
        var db = Database();
        var collection = db.GetCollection<MappedRequired>(nameof(MappedRequired));
        await collection.InsertManyAsync(new[] { new MappedRequired { Id = "a", TenantId = "A" }, new MappedRequired { Id = "b", TenantId = "B" } });
        var repo = Repo<MappedRequired>(db, tenant);
        var other = tenant == "A" ? "b" : "a";
        var forged = new MappedRequired { Id = other, TenantId = tenant ?? "A", Name = "forged" };
        if (tenant == null)
        {
            await repo.Invoking(x => x.UpdateAsync(forged)).Should().ThrowAsync<InvalidOperationException>();
            await repo.Invoking(x => x.DeleteAsync(forged)).Should().ThrowAsync<InvalidOperationException>();
            await repo.Invoking(x => x.InsertAsync(forged)).Should().ThrowAsync<InvalidOperationException>();
        }
        else { await repo.UpdateAsync(forged); await repo.DeleteAsync(forged); }
        (await collection.Find(_ => true).ToListAsync()).Should().OnlyContain(x => x.Name == "original" && !x.IsDeleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A")]
    [InlineData("B")]
    public async Task Unstamped_batch_delete_uses_write_scope_and_live_rows(string? tenant)
    {
        var db = Database();
        var collection = db.GetCollection<MappedUnstamped>(nameof(MappedUnstamped));
        await collection.InsertManyAsync(new[]
        {
            new MappedUnstamped { Id = "a", TenantId = "A" }, new MappedUnstamped { Id = "b", TenantId = "B" },
            new MappedUnstamped { Id = "s" }, new MappedUnstamped { Id = "d", TenantId = tenant, IsDeleted = true, DeletionTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        });
        var repo = Repo<MappedUnstamped>(db, tenant);
        await repo.DeleteRangeAsync(x => true);
        var rows = await collection.Find(_ => true).ToListAsync();
        rows.Should().HaveCount(4);
        rows.Where(x => x.Id != "d").Should().OnlyContain(x => x.IsDeleted == (x.TenantId == tenant));
        rows.Single(x => x.Id == "d").DeletionTime.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Physical_stamp_delete_matches_final_version_and_missing_stamped_delete_conflicts()
    {
        var db = Database();
        var repo = Repo<MappedPhysical>(db, "A");
        await repo.InsertAsync(new MappedPhysical { Id = "one", TenantId = "A", ConcurrencyStamp = "old-opaque" });
        await repo.Invoking(x => x.DeleteAsync("one", "wrong")).Should().ThrowAsync<CrestConcurrencyException>();
        await repo.DeleteAsync("one", "old-opaque");
        (await db.GetCollection<MappedPhysical>(nameof(MappedPhysical)).CountDocumentsAsync(_ => true)).Should().Be(0);
        await repo.Invoking(x => x.DeleteAsync("missing", "old-opaque")).Should().ThrowAsync<CrestConcurrencyException>();
    }

    public class MappedUnstamped : IEntity<string>, IMultiTenant, ISoftDelete
    {
        public string Id { get; set; } = "";
        [BsonElement("owner")] public string? TenantId { get; set; }
        [BsonElement("gone")] public bool IsDeleted { get; set; }
        [BsonElement("deleted_at")] public DateTime? DeletionTime { get; set; }
        public Guid? DeleterId { get; set; }
    }

    public class MappedPhysical : IEntity<string>, IMultiTenant, IHasConcurrencyStamp
    {
        public string Id { get; set; } = "";
        [BsonElement("owner")] public string? TenantId { get; set; }
        [BsonElement("version")] public string ConcurrencyStamp { get; set; } = "old";
    }

    public class MappedRequired : IEntity<string>, IMustHaveTenant, ISoftDelete
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [BsonElement("owner")] public string TenantId { get; set; } = "";
        [BsonElement("gone")] public bool IsDeleted { get; set; }
        [BsonElement("deleted_at")] public DateTime? DeletionTime { get; set; }
        public Guid? DeleterId { get; set; }
        public string Name { get; set; } = "original";
    }

    public class MappedOptional : IEntity<string>, IMultiTenant, ISoftDelete, IHasConcurrencyStamp
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [BsonElement("owner")] public string? TenantId { get; set; }
        [BsonElement("gone")] public bool IsDeleted { get; set; }
        [BsonElement("deleted_at")] public DateTime? DeletionTime { get; set; }
        public Guid? DeleterId { get; set; }
        [BsonElement("version")] public string ConcurrencyStamp { get; set; } = "legacy-token";
        public string Name { get; set; } = "same";
    }
}
