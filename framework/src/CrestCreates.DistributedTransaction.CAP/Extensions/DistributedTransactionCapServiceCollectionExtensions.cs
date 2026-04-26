using System;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.DistributedTransaction.CAP.BackgroundServices;
using CrestCreates.DistributedTransaction.CAP.Implementations;
using CrestCreates.DistributedTransaction.CAP.Options;
using CrestCreates.EventBus.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DistributedTransaction.CAP.Extensions;

public static class DistributedTransactionCapServiceCollectionExtensions
{
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

        services.Configure<DistributedTransactionCapOptions>(
            configuration.GetSection(DistributedTransactionCapOptions.SectionName));

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddScoped<IDistributedTransactionManager, DistributedTransactionManager>();
        services.AddScoped<ITransactionLogger, PersistentTransactionLogger>();
        services.AddScoped<ITransactionCompensator, PersistentTransactionCompensator>();
        services.AddSingleton<ICapTopicNameProvider, DefaultCapTopicNameProvider>();
        services.AddScoped<IDistributedEventPublisher, CapDistributedEventPublisher>();
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
            capOptions.Version = "v1";

            ConfigureStorage(capOptions, options);
            ConfigureTransport(capOptions, options);

            if (options.UseDashboard)
            {
                capOptions.UseDashboard();
            }
        });

        return services;
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
                capOptions.UseRabbitMQ(options.TransportConnectionString);
                break;
            case CapTransportProvider.Kafka:
                capOptions.UseKafka(options.TransportConnectionString);
                break;
            default:
                throw new NotSupportedException($"不支持的 CAP 消息传输提供程序: {options.TransportProvider}");
        }
    }
}
