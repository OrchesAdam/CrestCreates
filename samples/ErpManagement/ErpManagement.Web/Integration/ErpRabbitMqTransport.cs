using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.RabbitMQ;
using CrestCreates.EventBus.RabbitMQ.Composition;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.Generated.ErpManagement_Application_Contracts;
using CrestCreates.Generated.ErpManagement_Web;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Integration;
using ErpManagement.Application.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ErpManagement.Web.Integration;

internal static class ErpRabbitMqRegistration
{
    public static IServiceCollection AddErpRabbitMqTransport(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbit = configuration.GetSection("RabbitMQ");
        var options = services.AddOptions<RabbitMqOptions>();
        foreach (var setting in new[] { "HostName", "UserName", "Password", "VirtualHost" })
            options.Validate(_ => !string.IsNullOrWhiteSpace(rabbit[setting]), $"ERP requires RabbitMQ:{setting}.");
        options.ValidateOnStart();

        services.AddEventDescriptors();
        services.AddRabbitMqSubscriptions();
        services.AddRabbitMqEventBus(rabbit, builder => builder
            .RequirePublishableEvent<StockPostedEvent>()
            .RequirePublishableEvent<MesWorkOrderReleased>()
            .RequirePublishableEvent<MesWorkOrderCompleted>()
            .RequirePublishableEvent<CrmCustomerChanged>()
            .RequireSubscription<ErpRabbitMqSubscriptions>());

        services.AddSingleton<ErpRabbitMqTopology>();
        services.AddScoped<IErpIntegrationTransport>(sp => new ErpRabbitMqTransport(
            () => sp.GetRequiredService<RabbitMqEventBus>(), sp.GetRequiredService<ErpRabbitMqTopology>(),
            sp.GetRequiredService<ILogger<ErpRabbitMqTransport>>()));
        services.AddScoped<IErpIntegrationInbox, ErpPostgreSqlIntegrationInbox>();
        services.AddScoped<ErpStockPostedInboxConsumer>();
        services.AddScoped<ErpIntegrationOutbox>();
        services.AddOutboxDeliveryHandler<ErpStockPostedOutboxHandler>(ErpIntegrationWire.StockPostedId);
        AddDeliveryHandler<MesWorkOrderReleased>(services, ErpIntegrationWire.MesWorkOrderReleasedId, ErpIntegrationWire.MesWorkOrderReleased);
        AddDeliveryHandler<MesWorkOrderCompleted>(services, ErpIntegrationWire.MesWorkOrderCompletedId, ErpIntegrationWire.MesWorkOrderCompleted);
        AddDeliveryHandler<CrmCustomerChanged>(services, ErpIntegrationWire.CrmCustomerChangedId, ErpIntegrationWire.CrmCustomerChanged);

        services.AddHealthChecks().AddCheck<ErpRabbitMqHealthCheck>("erp-rabbitmq", timeout: TimeSpan.FromSeconds(3));
        return services;
    }

    private static void AddDeliveryHandler<TEvent>(IServiceCollection services, string contractId, string eventName)
        where TEvent : class, IErpIntegrationEvent
    {
        services.AddScoped(sp => new ErpIntegrationOutboxHandler<TEvent>(
            sp.GetRequiredService<IErpIntegrationTransport>(), contractId, eventName));
        services.AddSingleton(new OutboxDeliveryHandlerRegistration(contractId,
            sp => sp.GetRequiredService<ErpIntegrationOutboxHandler<TEvent>>()));
    }
}

internal sealed class ErpRabbitMqTransport(Func<RabbitMqEventBus> getBus, ErpRabbitMqTopology topology,
    ILogger<ErpRabbitMqTransport> logger) : IErpIntegrationTransport
{
    public async Task PublishAsync(IErpIntegrationEvent payload, CancellationToken cancellationToken)
    {
        // An external queue can accept a mandatory publish before ERP's own inbox has been bound.
        await topology.EnsureInitiallySubscribedAsync(cancellationToken).ConfigureAwait(false);
        await getBus().PublishAsync((IDomainEvent)payload, cancellationToken).ConfigureAwait(false);
        logger.LogDebug("RabbitMQ confirmed ERP message {MessageId}", payload.MessageId);
    }
}

public sealed class ErpRabbitMqSubscriptions(ErpStockPostedInboxConsumer inbox)
{
    public const string Queue = "erp.integration.inbox.v1";

    [RabbitMqSubscribe(ErpIntegrationWire.StockPosted, Version = ErpIntegrationWire.Version, Queue = Queue)]
    public Task StockPostedAsync(StockPostedEvent payload, CancellationToken cancellationToken)
        => inbox.ConsumeAsync(payload, cancellationToken);

    [RabbitMqSubscribe(ErpIntegrationWire.MesWorkOrderReleased, Version = ErpIntegrationWire.Version, Queue = Queue)]
    public Task WorkOrderReleasedAsync(MesWorkOrderReleased payload, CancellationToken cancellationToken)
        => inbox.ConsumeAsync(payload, cancellationToken);

    [RabbitMqSubscribe(ErpIntegrationWire.MesWorkOrderCompleted, Version = ErpIntegrationWire.Version, Queue = Queue)]
    public Task WorkOrderCompletedAsync(MesWorkOrderCompleted payload, CancellationToken cancellationToken)
        => inbox.ConsumeAsync(payload, cancellationToken);

    [RabbitMqSubscribe(ErpIntegrationWire.CrmCustomerChanged, Version = ErpIntegrationWire.Version, Queue = Queue)]
    public Task CustomerChangedAsync(CrmCustomerChanged payload, CancellationToken cancellationToken)
        => inbox.ConsumeAsync(payload, cancellationToken);
}

internal sealed class ErpRabbitMqTopology(RabbitMqCompositionReadiness readiness, RabbitMqConnectionPool connections)
{
    private volatile bool _initialSubscriptionSeen;

    public async Task EnsureInitiallySubscribedAsync(CancellationToken cancellationToken)
    {
        if (!_initialSubscriptionSeen && !await HasActiveSubscriptionAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The ERP RabbitMQ inbox has no active subscription.");
    }

    public async Task<bool> HasActiveSubscriptionAsync(CancellationToken cancellationToken)
    {
        await readiness.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!connections.IsConnectionOpen || connections.IsRecoveryInProgress)
            return false;

        await using var channel = await connections.CreateDedicatedChannelAsync(cancellationToken).ConfigureAwait(false);
        var queue = await channel.QueueDeclarePassiveAsync(ErpRabbitMqSubscriptions.Queue, cancellationToken).ConfigureAwait(false);
        if (queue.ConsumerCount == 0)
            return false;

        _initialSubscriptionSeen = true;
        return true;
    }
}

internal sealed class ErpRabbitMqHealthCheck(ErpRabbitMqTopology topology) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await topology.HasActiveSubscriptionAsync(cancellationToken).ConfigureAwait(false)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The ERP RabbitMQ inbox has no active subscription.");
}
