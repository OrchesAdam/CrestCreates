using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Extensions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Extensions;
using CrestCreates.DistributedTransaction.Models;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Domain.Entities;
using CrestCreates.Integration.Cap.EfCore;
using CrestCreates.Integration.Cap.EfCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.TestServices;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CrestCreates.DistributedTransaction.CAP.IntegrationTests;

/// <summary>
/// 供 Source Generator 识别的补偿执行器（真实生成产物在本程序集内消费）。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CompensationExecutorAttribute : Attribute
{
}

[CompensationExecutor]
public sealed class PgTestCompensationExecutor : ICompensationExecutor
{
    public const string ExecutorName = "pg-test";

    /// <summary>测试开关：置位后本执行器抛出（同名测试内串行控制）。</summary>
    public static volatile bool Fail;

    public string Name => ExecutorName;

    public List<string?> Invocations { get; } = new();

    public Task ExecuteAsync(string? compensationData)
    {
        Invocations.Add(compensationData);
        if (Fail)
        {
            throw new InvalidOperationException("pg-executor-failure");
        }

        return Task.CompletedTask;
    }
}

/// <summary>业务探针实体（验证消息行与业务行的同事务原子性）。</summary>
public sealed class CapProbeEntity : AggregateRoot<Guid>
{
    public CapProbeEntity(Guid id, string note)
    {
        Id = id;
        Note = note;
    }

    public string Note { get; set; } = string.Empty;
}

/// <summary>集成测试 DbContext：显式应用 CAP 持久化实体映射（无扫描 fallback）。</summary>
public sealed class CapIntegrationDbContext : DbContext
{
    public CapIntegrationDbContext(DbContextOptions<CapIntegrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<CapProbeEntity> Probes => Set<CapProbeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyCrestCapPersistence();
        modelBuilder.Entity<CapProbeEntity>(builder =>
        {
            builder.ToTable("CapProbeEntities");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Note).HasMaxLength(512);
        });
    }
}

/// <summary>
/// PG + RabbitMQ 集成夹具：长驻测试服务（infra/docker-compose.test-services.yml）、
/// 每轮一次性隔离数据库、真实 Host（CAP 装配 + UoW + EF 集成）与 CAP 存储检查器。
/// 故障注入停启的是专属实例（CREST_CAP_TEST_PG_CONTAINER / CREST_CAP_TEST_RABBIT_CONTAINER）。
/// </summary>
public sealed class CapIntegrationFixture : IAsyncLifetime
{
    private readonly string _serverConnectionString =
        ExternalTestServices.RequiredConnectionString("CREST_CAP_TEST_PG_CONNECTION", "PostgreSQL 16");

    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);
    private TestServiceControl.ServiceLease? _pgService;
    private TestServiceControl.ServiceLease? _rabbitService;
    private Uri _rabbitEndpoint = null!;
    private readonly List<string> _createdDatabases = new();

    public string PostgresConnectionString { get; private set; } = null!;

    private string RabbitConnectionString { get; set; } = null!;

    public IHost Host { get; private set; } = null!;

    public CapStoreInspector Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        RabbitConnectionString = ExternalTestServices.RequiredConnectionString("CREST_CAP_TEST_AMQP_CONNECTION", "RabbitMQ 4");
        if (!Uri.TryCreate(RabbitConnectionString, UriKind.Absolute, out var rabbitEndpoint) || rabbitEndpoint.Scheme != "amqp")
            throw new InvalidOperationException("CREST_CAP_TEST_AMQP_CONNECTION must be an amqp URI for the dedicated broker.");
        _rabbitEndpoint = rabbitEndpoint;

        try
        {
            _pgService = await TestServiceControl.AcquirePostgresAsync(DedicatedTestService.CapPostgres, _serverConnectionString);
            _rabbitService = await TestServiceControl.AcquireAsync(DedicatedTestService.CapRabbitMq, _rabbitEndpoint.Host, _rabbitEndpoint.Port);
            await TestServiceControl.WaitPostgresAsync(_serverConnectionString, ReadinessTimeout);
            await WaitForBrokerAsync();
            PostgresConnectionString = TrackDatabase(
                await ExternalTestServices.CreateIsolatedDatabaseAsync(_serverConnectionString, "capit"));
            Store = new CapStoreInspector(PostgresConnectionString);
            Host = await CreateHostAsync(PostgresConnectionString, RabbitConnectionString);
        }
        catch (Exception failure)
        {
            await ExternalTestServices.CleanupAsync([DisposeAsync], failure);
            throw;
        }
    }

    public Task DisposeAsync()
    {
        var cleanup = new List<Func<Task>>();
        var pgService = _pgService;
        var rabbitService = _rabbitService;
        _pgService = null;
        _rabbitService = null;
        if (Host is { } host)
        {
            Host = null!;
            cleanup.Add(() => StopHostAsync(host));
        }
        // Restore dedicated services only after the host is disposed, then drop databases before releasing locks.
        if (pgService is not null)
            cleanup.Add(() => pgService.RecoverAsync(() => TestServiceControl.WaitPostgresAsync(_serverConnectionString, ReadinessTimeout)));
        if (rabbitService is not null)
            cleanup.Add(() => rabbitService.RecoverAsync(WaitForBrokerAsync));
        cleanup.AddRange(_createdDatabases.Select<string, Func<Task>>(database =>
            () => ExternalTestServices.DropDatabaseAsync(_serverConnectionString, database)));
        _createdDatabases.Clear();
        if (rabbitService is not null)
            cleanup.Add(() => rabbitService.DisposeAsync().AsTask());
        if (pgService is not null)
            cleanup.Add(() => pgService.DisposeAsync().AsTask());
        return ExternalTestServices.CleanupAsync(cleanup);
    }

    /// <summary>在同一 PG 服务器上创建独立数据库（隔离扫描恢复类用例），返回连接串。</summary>
    public async Task<string> CreateIsolatedDatabaseAsync(string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await command.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(databaseName);
        return new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = databaseName }.ConnectionString;
    }

    private string TrackDatabase(string connectionString)
    {
        _createdDatabases.Add(ExternalTestServices.DatabaseName(connectionString));
        return connectionString;
    }

    public string RabbitMqConnectionString => RabbitConnectionString;

    /// <summary>暂停/恢复主 PG 实例（Unknown 提交故障注入用例；仅限串行用例，需在 finally 中恢复）。</summary>
    public Task PauseDatabaseAsync() => _pgService!.StopAsync();

    public Task ResumeDatabaseAsync() => _pgService!.RecoverAsync(
        () => TestServiceControl.WaitPostgresAsync(PostgresConnectionString, ReadinessTimeout));

    /// <summary>暂停/恢复 RabbitMQ 实例（broker 离线恢复用例；仅限串行用例，需在 finally 中恢复）。</summary>
    public Task PauseBrokerAsync() => _rabbitService!.StopAsync();

    public Task ResumeBrokerAsync() => _rabbitService!.RecoverAsync(WaitForBrokerAsync);

    private Task WaitForBrokerAsync()
    {
        var factory = new ConnectionFactory
        {
            Uri = _rabbitEndpoint,
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
            HandshakeContinuationTimeout = TimeSpan.FromSeconds(3)
        };
        return TestServiceControl.WaitUntilAsync(async cancellation =>
        {
            try
            {
                await using var probe = await factory.CreateConnectionAsync(cancellation);
                return probe.IsOpen;
            }
            catch (BrokerUnreachableException)
            {
                return false;
            }
        }, ReadinessTimeout, "CAP broker AMQP connection");
    }

    /// <summary>预热指定数据库上的 CAP 存储表（真实双库负例准备；用一次性宿主完成初始化）。</summary>
    public async Task InitializeCapStorageAsync(string connectionString)
    {
        var host = await CreateHostAsync(connectionString, RabbitMqConnectionString);
        await StopHostAsync(host);
    }

    /// <summary>构建并启动一个真实 Host（UoW + EF 集成 + CAP 单入口）；调用方负责释放。</summary>
    public async Task<IHost> CreateHostAsync(
        string postgresConnectionString,
        string rabbitConnectionString,
        bool ensureUowSchema = true,
        string? capStorageConnectionString = null,
        int compensationRetryIntervalSeconds = 30,
        int failedRetryCount = 5,
        Action<IServiceCollection>? mutateServices = null,
        string? capTableWaitConnectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedTransaction:CAP:StorageProvider"] = "PostgreSql",
                ["DistributedTransaction:CAP:TransportProvider"] = "RabbitMQ",
                ["DistributedTransaction:CAP:StorageConnectionString"] = capStorageConnectionString ?? postgresConnectionString,
                ["DistributedTransaction:CAP:TransportConnectionString"] = rabbitConnectionString,
                ["DistributedTransaction:CAP:FailedRetryCount"] = failedRetryCount.ToString(),
                ["DistributedTransaction:CAP:FailedRetryIntervalSeconds"] = "2",
                ["DistributedTransaction:CAP:FallbackWindowLookbackSeconds"] = "30",
                ["DistributedTransaction:CAP:CompensationRetryIntervalSeconds"] = compensationRetryIntervalSeconds.ToString(),
            })
            .Build();

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddDbContext<CapIntegrationDbContext>(options => options.UseNpgsql(postgresConnectionString));
                services.AddScoped<IDataBaseContext>(sp => new EfCoreDbContextAdapter(
                    sp.GetRequiredService<CapIntegrationDbContext>(),
                    sp.GetRequiredService<UnitOfWorkChainNode>()));
                services.AddSingleton<IDomainEventPublisher>(new NoOpDomainEventPublisher());
                services.AddSingleton<MutableCurrentTenant>();
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(sp =>
                    sp.GetRequiredService<MutableCurrentTenant>());
                services.AddUnitOfWork();
                services.AddEfCoreUnitOfWork();
                services.AddCrestCapEfCoreIntegration();
                services.AddCompensationExecutors(); // 真实生成产物
                services.AddCrestCapDistributedTransaction(configuration);
                mutateServices?.Invoke(services);
            })
            .Build();

        try
        {
            using var deadline = new CancellationTokenSource(ReadinessTimeout);
            if (ensureUowSchema)
            {
                using var scope = host.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>().Database.EnsureCreatedAsync(deadline.Token);
            }

            await host.StartAsync(deadline.Token).WaitAsync(deadline.Token);
            await TestServiceControl.WaitTcpAsync(_rabbitEndpoint.Host, _rabbitEndpoint.Port, ReadinessTimeout);

            // CAP 在后台异步初始化存储。覆盖型宿主按最终存储目标等待。
            var storageConnectionString = capTableWaitConnectionString ?? capStorageConnectionString ?? postgresConnectionString;
            await WaitForCapTablesAsync(storageConnectionString);
            return host;
        }
        catch (Exception failure)
        {
            await ExternalTestServices.CleanupAsync([() => StopHostAsync(host)], failure);
            throw;
        }
    }

    private static async Task StopHostAsync(IHost host)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await host.StopAsync(deadline.Token).WaitAsync(deadline.Token);
        }
        finally
        {
            host.Dispose();
        }
    }

    private static Task WaitForCapTablesAsync(string storageConnectionString) => TestServiceControl.WaitUntilAsync(async cancellation =>
    {
        try
        {
            await using var connection = new NpgsqlConnection(storageConnectionString);
            await connection.OpenAsync(cancellation);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('cap.\"published\"') IS NOT NULL";
            return await command.ExecuteScalarAsync(cancellation) is true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }, ReadinessTimeout, "CAP storage tables");

    private sealed class NoOpDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;
    }
}

/// <summary>CAP 存储检查器：直接观察 cap."published" 行与状态（含投递 = Succeeded 证明 broker ack）。</summary>
public sealed class CapStoreInspector
{
    private readonly string _connectionString;

    public CapStoreInspector(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> CountPublishedAsync(string topic)
    {
        var result = await ExecuteScalarAsync(
            "SELECT count(*) FROM cap.\"published\" WHERE \"Name\" = @name", topic);
        return Convert.ToInt32(result);
    }

    public async Task<string?> GetStatusAsync(string topic)
    {
        var result = await ExecuteScalarAsync(
            "SELECT \"StatusName\" FROM cap.\"published\" WHERE \"Name\" = @name ORDER BY \"Added\" DESC LIMIT 1", topic);
        return result as string;
    }

    private async Task<object?> ExecuteScalarAsync(string sql, string topic)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("name", topic);
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    public async Task<bool> WaitForStatusAsync(string topic, string expectedStatus, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await GetStatusAsync(topic);
            if (status == expectedStatus)
            {
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }

    public async Task<bool> WaitForCountAsync(string topic, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var count = await CountPublishedAsync(topic);
            if (count == expectedCount)
            {
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }
}


/// <summary>
/// 可控 EF 工作单元（清理失败故障注入）：真实 BEGIN/COMMIT 到 PG；提交确认后抛
/// <see cref="UnitOfWorkCommitCleanupFailureException"/> 模拟句柄清理失败（内核应固定 Committed + Failed）。
/// </summary>
public sealed class ControllableEfUnitOfWork : IUnitOfWork, IUnitOfWorkManagedResource, IUnitOfWorkAbandonable
{
    private readonly CapIntegrationDbContext _db;
    private object? _handle;

    public ControllableEfUnitOfWork(CapIntegrationDbContext db)
    {
        _db = db;
    }

    public bool FailCleanupAfterCommit { get; set; }

    /// <summary>提交派发阶段故障注入：提交调用被触达但派发失败（不产生已提交事实）。</summary>
    public bool FailCommitDispatch { get; set; }

    /// <summary>提交已落到数据库但确认丢失（提交调用成功、随后抛非清理型异常）。</summary>
    public bool CommitThenFailConfirmation { get; set; }

    /// <summary>提交派发调用计数（证明故障发生在提交阶段而非 flush 阶段）。</summary>
    public int CommitDispatchAttempts { get; private set; }

    public string? ManagedResourceKind => typeof(CapIntegrationDbContext).FullName;

    public string? ManagedResourceConnectionString => _db.Database.GetConnectionString();

    public object? ManagedResourceContext => _db;

    public object? ManagedTransactionHandle => _handle;

    public async Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default)
    {
        await _db.Database.BeginTransactionAsync(cancellationToken);
        _handle = _db.Database.CurrentTransaction;
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        CommitDispatchAttempts++;

        if (FailCommitDispatch)
        {
            // 提交派发期失败：未产生已提交事实（内核应记 Unknown 并尝试终结）。
            throw new InvalidOperationException("injected-commit-dispatch-failure");
        }

        var transaction = _db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("No transaction has been started");
        await transaction.CommitAsync(cancellationToken);

        if (CommitThenFailConfirmation)
        {
            // 数据库已提交但确认响应丢失：内核应保持 Unknown（不被回滚尝试改写），
            // 消息行/业务行按数据库事实存在并可经扫描恢复投递。
            throw new InvalidOperationException("injected-confirmation-lost");
        }

        if (FailCleanupAfterCommit)
        {
            throw new UnitOfWorkCommitCleanupFailureException(
                "Injected: the database commit was confirmed but releasing the transaction handle failed.",
                new InvalidOperationException("injected-cleanup-failure"));
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is { } transaction)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);

    public void AbandonPendingWork()
    {
        try
        {
            _db.Database.CurrentTransaction?.Rollback();
        }
        catch (Exception)
        {
            // Best-effort termination for the injected-fault harness.
        }

        try
        {
            _db.Database.CurrentTransaction?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort release.
        }

        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
    }
}


/// <summary>可变租户（ResourceMismatch 拒绝用例；默认 null=host）。</summary>
public sealed class MutableCurrentTenant : CrestCreates.MultiTenancy.Abstract.ICurrentTenant
{
    public string? Id { get; set; }

    public CrestCreates.MultiTenancy.Abstract.ITenantInfo? Tenant => null;

    public Task<IDisposable> ChangeAsync(string tenantId) => throw new NotSupportedException();

    public IDisposable Change(CrestCreates.MultiTenancy.Abstract.ITenantInfo tenant) => throw new NotSupportedException();

    public void SetTenantId(string tenantId) => Id = tenantId;
}
