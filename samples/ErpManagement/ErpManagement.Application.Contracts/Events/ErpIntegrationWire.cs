using System.Text.Json.Serialization.Metadata;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Generated;
using ErpManagement.Domain.Shared.Constants;

namespace ErpManagement.Application.Contracts.Events;

public static class ErpIntegrationWire
{
    public const int Version = 1;

    public const string StockPosted = "erp.stock-posted";
    public const string StockPostedId = ErpContractIds.StockPostedContractId;
    public const string MesWorkOrderReleased = "erp.mes.work-order-released";
    public const string MesWorkOrderReleasedId = "crest.erp.mes.work-order-released/v1";
    public const string MesWorkOrderCompleted = "erp.mes.work-order-completed";
    public const string MesWorkOrderCompletedId = "crest.erp.mes.work-order-completed/v1";
    public const string CrmCustomerChanged = "erp.crm.customer-changed";
    public const string CrmCustomerChangedId = "crest.erp.crm.customer-changed/v1";

    public static JsonTypeInfo<TEvent> GetJsonTypeInfo<TEvent>() where TEvent : IErpIntegrationEvent
        => (JsonTypeInfo<TEvent>?)EventJsonSerializerContext.Default.GetTypeInfo(typeof(TEvent))
           ?? throw new InvalidOperationException($"No generated ERP wire codec for '{typeof(TEvent).Name}'.");
}

public interface IErpIntegrationEvent : IDomainEvent
{
    string MessageId { get; }
    string? TenantKey { get; }
}
