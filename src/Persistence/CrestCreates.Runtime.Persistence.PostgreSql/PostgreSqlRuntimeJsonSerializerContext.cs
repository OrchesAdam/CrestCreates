using System.Text.Json.Serialization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkflowInstance))]
[JsonSerializable(typeof(HumanTaskInstance))]
[JsonSerializable(typeof(WorkflowSuspensionReceipt))]
[JsonSerializable(typeof(RuntimeDescriptorPin))]
[JsonSerializable(typeof(DescriptorSnapshot))]
[JsonSerializable(typeof(AuditEnvelope))]
internal sealed partial class PostgreSqlRuntimeJsonSerializerContext : JsonSerializerContext
{
}
