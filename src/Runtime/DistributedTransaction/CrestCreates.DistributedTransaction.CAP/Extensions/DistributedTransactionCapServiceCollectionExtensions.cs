using System;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.DistributedTransaction.CAP.BackgroundServices;
using CrestCreates.DistributedTransaction.CAP.Implementations;
using CrestCreates.DistributedTransaction.CAP.Options;
using CrestCreates.EventBus.Abstract;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DistributedTransaction.CAP.Extensions;

/// <summary>
/// CAP 集成唯一正式装配入口（#125）：注册持久化日志/补偿、原子发布器、CAP 本体与两层配置校验。
/// </summary>
/// <remarks>
/// 两层校验：
/// 1) 注册期：对 Crest 输入 options 做早期诊断（快速定位输入错误；**不构成最终保证**）；
/// 2) 启动期（权威）：对 CAP 实际消费的最终 <c>CapOptions</c>（所有 Configure/PostConfigure 之后）
///    通过 <c>IValidateOptions&lt;CapOptions&gt;</c> + ValidateOnStart 校验，并运行装配自检
///    （executor registry/声明必需集合/持久化仓库）。
/// 扫描投递前提：事务消息的首次投递依赖 CAP 存储扫描恢复，因此有效
/// <c>FailedRetryCount</c> 必须 &gt; 0、<c>FailedRetryInterval</c> 必须 &gt; 0、
/// <c>FallbackWindowLookbackSeconds</c> 必须 ≥ 30——「禁用重试」在本模式下不可用。
/// </remarks>
public static class DistributedTransactionCapServiceCollectionExtensions
{
    /// <summary>扫描恢复支持下限（与 CAP 的 <c>MinSuggestedValueForFallbackWindowLookbackSeconds</c> 对齐）。</summary>
    public const int MinFallbackWindowLookbackSeconds = 30;

    public static IServiceCollection AddCrestCapDistributedTransaction(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DistributedTransactionCapOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new DistributedTransactionCapOptions();
        configuration.GetSection(DistributedTransactionCapOptions.SectionName).Bind(options);
        configure?.Invoke(options);

        // 第 1 层（早期诊断）：Crest 输入合法性；最终保证由第 2 层（最终 CapOptions）提供。
        ValidateInputOptions(options);

        services.Configure<DistributedTransactionCapOptions>(
            configuration.GetSection(DistributedTransactionCapOptions.SectionName));

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddScoped<ITransactionLogger, PersistentTransactionLogger>();
        services.AddScoped<ITransactionCompensator, PersistentTransactionCompensator>();
        services.AddSingleton<ICapTopicNameProvider, DefaultCapTopicNameProvider>();
        services.AddScoped<IDistributedEventPublisher>(sp => new CapDistributedEventPublisher(
            sp.GetRequiredService<DotNetCore.CAP.ICapPublisher>(),
            sp.GetService<UnitOfWorkChainNode>(),
            sp.GetService<ICapManagedTransactionProvider>(),
            sp.GetService<ICurrentTenant>()));
        services.AddScoped<IEventBus, CapEventBus>();

        // Background retry service (optional)
        if (options.EnableCompensationBackgroundWorker)
        {
            services.AddHostedService<CompensationRetryBackgroundService>();
        }

        services.AddCap(capOptions =>
        {
            capOptions.DefaultGroupName = options.DefaultGroup;
            capOptions.FailedRetryCount = options.FailedRetryCount;
            capOptions.FailedRetryInterval = options.FailedRetryIntervalSeconds;
            capOptions.FallbackWindowLookbackSeconds = options.FallbackWindowLookbackSeconds;
            capOptions.Version = "v1";

            ConfigureStorage(capOptions, options);
            ConfigureTransport(capOptions, options);

            if (options.UseDashboard)
            {
                capOptions.UseDashboard();
            }
        });

        // 第 2 层（权威）：对最终 CapOptions（所有 Configure/PostConfigure 之后）做启动期校验——
        // 后续任何 Configure/PostConfigure<CapOptions> 覆盖都在宿主启动时被同一检查捕获。
        services.AddOptions<DotNetCore.CAP.CapOptions>()
            .Validate(
                options => options.FailedRetryCount > 0,
                "CapOptions.FailedRetryCount must be greater than 0: transactional messages rely on CAP's storage scan " +
                "for their first delivery and 'Retries < FailedRetryCount' must hold; disabling retries is not supported.")
            .Validate(
                options => options.FailedRetryInterval > 0,
                "CapOptions.FailedRetryInterval must be greater than 0 (zero-interval polling is not supported).")
            .Validate(
                options => options.FallbackWindowLookbackSeconds >= MinFallbackWindowLookbackSeconds,
                $"CapOptions.FallbackWindowLookbackSeconds must be at least {MinFallbackWindowLookbackSeconds} seconds " +
                "(CAP's suggested minimum for fallback scan safety).")
            .ValidateOnStart();

        // 装配自检（启动期）：registry/声明必需集合/持久化仓库。
        services.AddHostedService<CapPublishReadinessValidator>();

        return services;
    }

    private static void ValidateInputOptions(DistributedTransactionCapOptions options)
    {
        if (options.FailedRetryCount <= 0)
        {
            throw new InvalidOperationException(
                "DistributedTransactionCapOptions.FailedRetryCount must be greater than 0. Transactional messages " +
                "rely on CAP's storage scan for their first delivery; with 0 (or negative) the scan never selects new " +
                "rows and the 'disable retries' configuration is not supported by this integration.");
        }

        if (options.FailedRetryIntervalSeconds <= 0)
        {
            throw new InvalidOperationException(
                "DistributedTransactionCapOptions.FailedRetryIntervalSeconds must be greater than 0 (zero-interval " +
                "polling is not supported).");
        }

        if (options.FallbackWindowLookbackSeconds < MinFallbackWindowLookbackSeconds)
        {
            throw new InvalidOperationException(
                $"DistributedTransactionCapOptions.FallbackWindowLookbackSeconds must be at least " +
                $"{MinFallbackWindowLookbackSeconds} seconds (supported range starts at CAP's suggested minimum).");
        }
    }

    private static void ConfigureStorage(
        DotNetCore.CAP.CapOptions capOptions,
        DistributedTransactionCapOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.StorageConnectionString);

        switch (options.StorageProvider)
        {
            case CapStorageProvider.SqlServer:
                capOptions.UseSqlServer(options.StorageConnectionString);
                break;
            case CapStorageProvider.PostgreSql:
                capOptions.UsePostgreSql(options.StorageConnectionString);
                break;
            default:
                throw new NotSupportedException($"不支持的 CAP 存储提供程序: {options.StorageProvider}");
        }
    }

    private static void ConfigureTransport(
        DotNetCore.CAP.CapOptions capOptions,
        DistributedTransactionCapOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TransportConnectionString);

        switch (options.TransportProvider)
        {
            case CapTransportProvider.RabbitMQ:
                ConfigureRabbitMq(capOptions, options.TransportConnectionString);
                break;
            case CapTransportProvider.Kafka:
                // Kafka 的字符串重载即 bootstrap servers（"host:port[,host:port]"）。
                capOptions.UseKafka(options.TransportConnectionString);
                break;
            default:
                throw new NotSupportedException($"不支持的 CAP 消息传输提供程序: {options.TransportProvider}");
        }
    }

    /// <summary>
    /// RabbitMQ 传输配置：支持两种形式——
    /// 1) <c>amqp(s)://user:pass@host:port[/vhost]</c> URI：解析出 HostName/Port/凭据/VirtualHost（推荐）；
    /// 2) 纯主机名：沿用 CAP <c>UseRabbitMQ(string)</c> 的原生语义（该参数是 HostName，不是连接串）。
    /// </summary>
    private static void ConfigureRabbitMq(DotNetCore.CAP.CapOptions capOptions, string connectionString)
    {
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, "amqp", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, "amqps", StringComparison.OrdinalIgnoreCase)))
        {
            capOptions.UseRabbitMQ(rabbitOptions =>
            {
                rabbitOptions.HostName = uri.Host;
                if (uri.Port > 0)
                {
                    rabbitOptions.Port = uri.Port;
                }

                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var parts = uri.UserInfo.Split(':', 2);
                    rabbitOptions.UserName = Uri.UnescapeDataString(parts[0]);
                    if (parts.Length > 1)
                    {
                        rabbitOptions.Password = Uri.UnescapeDataString(parts[1]);
                    }
                }

                var virtualHost = uri.AbsolutePath.Trim('/');
                if (!string.IsNullOrEmpty(virtualHost))
                {
                    rabbitOptions.VirtualHost = virtualHost;
                }
            });
            return;
        }

        capOptions.UseRabbitMQ(connectionString);
    }
}
