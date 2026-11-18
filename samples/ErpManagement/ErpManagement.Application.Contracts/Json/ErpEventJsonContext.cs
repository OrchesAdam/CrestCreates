using System.Text.Json.Serialization;
using ErpManagement.Application.Contracts.Events;

namespace ErpManagement.Application.Contracts.Json;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ErpRequisitionDecisionFact))]
[JsonSerializable(typeof(ErpSalesDecisionFact))]
[JsonSerializable(typeof(StockPostedEvent))]
[JsonSerializable(typeof(StockChanged))]
public partial class ErpEventJsonContext : JsonSerializerContext
{
}