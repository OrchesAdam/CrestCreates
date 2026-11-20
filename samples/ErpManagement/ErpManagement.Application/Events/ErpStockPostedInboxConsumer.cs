using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Delivery.Abstractions.Contracts;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Integration;
using ErpManagement.Domain.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ErpManagement.Application.Events;

public interface IErpIntegrationInbox
{
    Task<ErpInboxWriteResult> RecordAsync(string consumerId, string messageId, string contractId, Guid tenantId,
        string payloadHash, CancellationToken cancellationToken);
}

public enum ErpInboxWriteResult
{
    Inserted,
    Duplicate,
    Conflict
}

/// <summary>Commits ERP's integration receipts before the broker subscription returns.</summary>
public sealed class ErpStockPostedInboxConsumer(ICurrentTenant currentTenant, ITenantProvider tenants,
    IErpIntegrationInbox inbox, ILogger<ErpStockPostedInboxConsumer> logger)
{
    public const string ConsumerIdValue = ErpContractIds.StockPostedConsumerId;
    public const string IntegrationConsumerId = "crest.erp.mes-crm-inbox/v1";

    public Task ConsumeAsync(StockPostedEvent payload, CancellationToken cancellationToken = default)
        => RecordAsync(payload, ConsumerIdValue, ErpIntegrationWire.StockPostedId, cancellationToken);

    public Task ConsumeAsync(MesWorkOrderReleased payload, CancellationToken cancellationToken = default)
        => RecordAsync(payload, IntegrationConsumerId, ErpIntegrationWire.MesWorkOrderReleasedId, cancellationToken);

    public Task ConsumeAsync(MesWorkOrderCompleted payload, CancellationToken cancellationToken = default)
        => RecordAsync(payload, IntegrationConsumerId, ErpIntegrationWire.MesWorkOrderCompletedId, cancellationToken);

    public Task ConsumeAsync(CrmCustomerChanged payload, CancellationToken cancellationToken = default)
        => RecordAsync(payload, IntegrationConsumerId, ErpIntegrationWire.CrmCustomerChangedId, cancellationToken);

    private async Task RecordAsync<TEvent>(TEvent payload, string consumerId, string contractId,
        CancellationToken cancellationToken) where TEvent : IErpIntegrationEvent
    {
        if (string.IsNullOrWhiteSpace(payload.MessageId) || payload.MessageId.Length > OutboxContractLimits.MaxSemanticIdentifierLength)
            throw new InvalidOperationException("ERP integration payload has an invalid message ID.");

        var tenantId = Guid.Empty;
        if (payload.TenantKey is { } key && (!Guid.TryParseExact(key, "D", out tenantId) || tenantId == Guid.Empty))
            throw new InvalidOperationException("ERP integration payload has an invalid tenant ID.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, ErpIntegrationWire.GetJsonTypeInfo<TEvent>());
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        ITenantInfo? tenant = null;
        if (tenantId != Guid.Empty)
        {
            var id = tenantId.ToString();
            tenant = await tenants.GetTenantAsync(id, cancellationToken).ConfigureAwait(false);
            if (tenant?.Id != id)
                throw new InvalidOperationException($"ERP integration tenant '{id}' could not be resolved by ID.");
        }

        using var tenantScope = currentTenant.Change(tenant);
        var result = await inbox.RecordAsync(consumerId, payload.MessageId, contractId, tenantId,
            payloadHash, cancellationToken).ConfigureAwait(false);

        if (result == ErpInboxWriteResult.Conflict)
            throw new InvalidOperationException($"ERP inbox conflict for contract '{contractId}', message '{payload.MessageId}'.");

        logger.LogDebug("ERP inbox {Result} for contract {ContractId}, message {MessageId}", result, contractId, payload.MessageId);
    }
}
