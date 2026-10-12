using System;
using System.Collections.Generic;
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
using CrestCreates.Domain.Entities;
using CrestCreates.Integration.Cap.EfCore;
using CrestCreates.Integration.Cap.EfCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

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

    public string Name => ExecutorName;

    public List<string?> Invocations { get; } = new();

    public Task ExecuteAsync(string? compensationData)
    {
        Invocations.Add(compensationData);
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
/// PG + RabbitMQ 集成夹具：容器、真实 Host（CAP 装配 + UoW + EF 集成）与 CAP 存储检查器。
/// </summary>
public sealed class CapIntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("cap_it")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.0-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public string PostgresConnectionString { get; private set; } = null!;

    private string RabbitConnectionString { get; set; } = null!;

    public IHost Host { get; private set; } = null!;

    public CapStoreInspector Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _rabbit.StartAsync();
        PostgresConnectionString = _postgres.GetConnectionString();
        RabbitConnectionString = _rabbit.GetConnectionString();
        Store = new CapStoreInspector(PostgresConnectionString);

        Host = await CreateHostAsync(PostgresConnectionString, RabbitConnectionString);
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    /// <summary>在同一 PG 服务器上创建独立数据库（隔离扫描恢复类用例），返回连接串。</summary>
    public async Task<string> CreateIsolatedDatabaseAsync(string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = databaseName }.ConnectionString;
    }

    public string RabbitMqConnectionString => RabbitConnectionString;

    /// <summary>构建并启动一个真实 Host（UoW + EF 集成 + CAP 单入口）；调用方负责释放。</summary>
    public async Task<IHost> CreateHostAsync(
        string postgresConnectionString,
        string rabbitConnectionString,
        bool ensureUowSchema = true,
        string? capStorageConnectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedTransaction:CAP:StorageProvider"] = "PostgreSql",
                ["DistributedTransaction:CAP:TransportProvider"] = "RabbitMQ",
                ["DistributedTransaction:CAP:StorageConnectionString"] = capStorageConnectionString ?? postgresConnectionString,
                ["DistributedTransaction:CAP:TransportConnectionString"] = rabbitConnectionString,
                ["DistributedTransaction:CAP:FailedRetryCount"] = "5",
                ["DistributedTransaction:CAP:FailedRetryIntervalSeconds"] = "2",
                ["DistributedTransaction:CAP:FallbackWindowLookbackSeconds"] = "30",
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
                services.AddUnitOfWork();
                services.AddEfCoreUnitOfWork();
                services.AddCrestCapEfCoreIntegration();
                services.AddCompensationExecutors(); // 真实生成产物
                services.AddCrestCapDistributedTransaction(configuration);
            })
            .Build();

        if (ensureUowSchema)
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CapIntegrationDbContext>().Database.EnsureCreatedAsync();
        }

        await host.StartAsync();

        // CAP 的存储初始化在 BackgroundService 内异步进行：等待 cap."published" 可见，避免启动竞态。
        var storageConnectionString = capStorageConnectionString ?? postgresConnectionString;
        await WaitForCapTablesAsync(storageConnectionString, TimeSpan.FromSeconds(60));
        return host;
    }

    private static async Task WaitForCapTablesAsync(string storageConnectionString, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var connection = new NpgsqlConnection(storageConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT to_regclass('cap.\"published\"') IS NOT NULL";
                if (await command.ExecuteScalarAsync() is true)
                {
                    return;
                }
            }
            catch (NpgsqlException)
            {
                // 容器/网络尚未就绪：继续等待。
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("CAP storage tables were not initialized in time.");
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
