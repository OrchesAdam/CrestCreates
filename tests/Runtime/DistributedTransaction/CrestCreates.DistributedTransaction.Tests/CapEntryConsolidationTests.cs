using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.CAP.BackgroundServices;
using CrestCreates.DistributedTransaction.CAP.Extensions;
using CrestCreates.DistributedTransaction.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.DistributedTransaction.Tests;

/// <summary>
/// #125 S1 验收：唯一入口的两层配置校验、装配自检与模块收口守卫。
/// </summary>
public class CapEntryConsolidationTests
{
    private const string ValidStorage = "Host=localhost;Port=5432;Database=capdb;Username=u;Password=p";
    private const string ValidTransport = "amqp://guest:guest@localhost:5672";

    private static IConfiguration ValidConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedTransaction:CAP:StorageConnectionString"] = ValidStorage,
                ["DistributedTransaction:CAP:TransportConnectionString"] = ValidTransport,
            })
            .Build();

    // ── 第 1 层：Crest 输入早期诊断（注册期） ──

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Registration_rejects_non_positive_failed_retry_count(int count)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCrestCapDistributedTransaction(ValidConfiguration(), options => options.FailedRetryCount = count));

        Assert.Contains("FailedRetryCount", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Registration_rejects_non_positive_retry_interval(int seconds)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCrestCapDistributedTransaction(ValidConfiguration(), options => options.FailedRetryIntervalSeconds = seconds));

        Assert.Contains("FailedRetryIntervalSeconds", exception.Message);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Registration_rejects_lookback_below_supported_range(int seconds)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCrestCapDistributedTransaction(ValidConfiguration(), options => options.FallbackWindowLookbackSeconds = seconds));

        Assert.Contains("FallbackWindowLookbackSeconds", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void Registration_accepts_minimum_allowed_values(int failedRetryCount)
    {
        var services = new ServiceCollection();
        services.AddCrestCapDistributedTransaction(ValidConfiguration(), options =>
        {
            options.FailedRetryCount = failedRetryCount;
            options.FallbackWindowLookbackSeconds = 30;
        });

        Assert.Contains(services, descriptor => descriptor.ServiceType.Name == "CapMarkerService");
    }

    [Fact]
    public void Registration_rejects_empty_storage_connection_string()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedTransaction:CAP:TransportConnectionString"] = ValidTransport,
            })
            .Build();

        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddCrestCapDistributedTransaction(configuration));
    }

    [Fact]
    public void Registration_rejects_empty_transport_connection_string()
    {
        // localhost 默认已移除：未配置传输连接串必须显式失败。
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedTransaction:CAP:StorageConnectionString"] = ValidStorage,
            })
            .Build();

        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddCrestCapDistributedTransaction(configuration));
    }

    // ── 第 2 层：最终 CapOptions 启动校验（含 Configure/PostConfigure 覆盖） ──

    [Fact]
    public async Task Final_options_validation_catches_postconfiguration_retry_count_override()
    {
        // Crest 输入合法；后注册 PostConfigure 把最终值改为 0 —— 必须启动失败。
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => RunReadinessValidatorAsync(services => services.PostConfigure<DotNetCore.CAP.CapOptions>(o => o.FailedRetryCount = 0)));

        Assert.Contains("FailedRetryCount", exception.Message);
    }

    [Fact]
    public async Task Final_options_validation_catches_postconfiguration_interval_override()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => RunReadinessValidatorAsync(services => services.PostConfigure<DotNetCore.CAP.CapOptions>(o => o.FailedRetryInterval = 0)));

        Assert.Contains("FailedRetryInterval", exception.Message);
    }

    [Fact]
    public async Task Final_options_validation_catches_postconfiguration_lookback_override()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => RunReadinessValidatorAsync(services => services.PostConfigure<DotNetCore.CAP.CapOptions>(o => o.FallbackWindowLookbackSeconds = 20)));

        Assert.Contains("FallbackWindowLookbackSeconds", exception.Message);
    }

    [Fact]
    public async Task Final_options_validation_accepts_valid_postconfiguration()
    {
        await RunReadinessValidatorAsync(services => services.PostConfigure<DotNetCore.CAP.CapOptions>(o =>
        {
            o.FailedRetryCount = 2;
            o.FailedRetryInterval = 5;
            o.FallbackWindowLookbackSeconds = 60;
        }));
    }

    // ── 装配自检 ──

    [Fact]
    public async Task Readiness_validator_fails_when_executor_registry_is_missing()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunReadinessValidatorAsync(services =>
            {
                RemoveService(services, typeof(CrestCreates.DistributedTransaction.Abstractions.ICompensationExecutorRegistry));
            }));

        Assert.Contains("ICompensationExecutorRegistry", exception.ToString());
    }

    [Fact]
    public async Task Readiness_validator_fails_when_declared_required_executor_is_missing()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunReadinessValidatorAsync(services =>
            {
                services.Configure<CrestCreates.DistributedTransaction.CAP.Options.DistributedTransactionCapOptions>(
                    o => o.RequiredCompensationExecutors = new[] { "not-registered-executor" });
            }));

        Assert.Contains("not-registered-executor", exception.Message);
    }

    [Fact]
    public async Task Readiness_validator_fails_when_persistence_repositories_are_missing()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunReadinessValidatorAsync(services =>
            {
                RemoveService(services, typeof(IRepository<TransactionLog, Guid>));
                RemoveService(services, typeof(IRepository<TransactionCompensation, Guid>));
            }));

        Assert.Contains("AddCrestCapEfCoreIntegration", exception.Message);
    }

    // ── 模块收口守卫 ──

    [Fact]
    public void Cap_assembly_contains_no_auto_discovered_module_and_no_in_memory_fakes()
    {
        var archivedTypeNames = new[]
        {
            "DistributedTransactionCapModule",
            "TransactionLogger",
            "DefaultTransactionCompensator",
            "DistributedTransaction",
            "DistributedTransactionManager"
        };

        var types = typeof(DistributedTransactionCapServiceCollectionExtensions).Assembly.GetTypes();
        foreach (var archived in archivedTypeNames)
        {
            Assert.DoesNotContain(types, type => type.Name == archived);
        }

        Assert.DoesNotContain(
            types,
            type => type.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute.GetType().Name == "CrestModuleAttribute"));
    }

    [Fact]
    public void DistributedTransaction_assembly_contains_no_participant_engine_contracts()
    {
        var archivedTypeNames = new[]
        {
            "IDistributedTransaction",
            "IDistributedTransactionManager",
            "ITransactionParticipant",
            "ITransactionParticipantRegistry",
            "UnitOfWorkIntegrationExtensions"
        };

        var types = typeof(CrestCreates.DistributedTransaction.Abstractions.IDistributedEventPublisher).Assembly.GetTypes();
        foreach (var archived in archivedTypeNames)
        {
            Assert.DoesNotContain(types, type => type.Name == archived);
        }
    }

    // ── 测试基架 ──

    private static async Task RunReadinessValidatorAsync(Action<IServiceCollection>? mutate = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestCapDistributedTransaction(ValidConfiguration());
        services.AddCompensationExecutors(); // 真实生成产物（[CompensationExecutor] → 生成 registry + 扩展）
        services.AddScoped<IRepository<TransactionLog, Guid>>(_ => new TestCapRepository<TransactionLog, Guid>());
        services.AddScoped<IRepository<TransactionCompensation, Guid>>(_ => new TestCapRepository<TransactionCompensation, Guid>());
        services.Configure<CrestCreates.DistributedTransaction.CAP.Options.DistributedTransactionCapOptions>(
            o => o.EnableCompensationBackgroundWorker = false);

        mutate?.Invoke(services);

        await using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IHostedService>().OfType<CapPublishReadinessValidator>().Single();
        await validator.StartAsync(CancellationToken.None);
    }

    private static void RemoveService(IServiceCollection services, Type serviceType)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (services[index].ServiceType == serviceType)
            {
                services.RemoveAt(index);
            }
        }
    }
}
