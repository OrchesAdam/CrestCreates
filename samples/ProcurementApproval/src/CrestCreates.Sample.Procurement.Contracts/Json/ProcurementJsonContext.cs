using System.Text.Json.Serialization;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Contracts.Json;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SubmitProcurementRequestInput))]
[JsonSerializable(typeof(SubmitProcurementRequestResult))]
[JsonSerializable(typeof(ApproveProcurementRequestInput))]
[JsonSerializable(typeof(RejectProcurementRequestInput))]
[JsonSerializable(typeof(ProcurementRequestResult))]
[JsonSerializable(typeof(GetProcurementRequestInput))]
[JsonSerializable(typeof(ProcurementHumanTaskDecisionFact))]
public sealed partial class ProcurementJsonContext : JsonSerializerContext;
