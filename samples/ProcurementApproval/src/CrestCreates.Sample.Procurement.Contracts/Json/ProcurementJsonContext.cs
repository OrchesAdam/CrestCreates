using System.Text.Json.Serialization;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Contracts.Json;

[JsonSerializable(typeof(SubmitProcurementRequestInput))]
[JsonSerializable(typeof(SubmitProcurementRequestResult))]
[JsonSerializable(typeof(ApproveProcurementRequestInput))]
[JsonSerializable(typeof(RejectProcurementRequestInput))]
[JsonSerializable(typeof(ProcurementRequestResult))]
[JsonSerializable(typeof(GetProcurementRequestInput))]
public sealed partial class ProcurementJsonContext : JsonSerializerContext;
