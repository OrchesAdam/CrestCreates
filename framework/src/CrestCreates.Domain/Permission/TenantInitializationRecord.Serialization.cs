using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrestCreates.Domain.Permission;

[JsonSerializable(typeof(List<TenantInitializationRecord.StepResult>))]
[JsonSerializable(typeof(TenantInitializationRecord.StepResult))]
internal partial class TenantInitializationRecordJsonContext : JsonSerializerContext
{
}
