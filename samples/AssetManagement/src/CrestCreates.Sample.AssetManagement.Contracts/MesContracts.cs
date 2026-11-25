using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Sample.AssetManagement.Contracts.Mes;

public sealed record RegisterProductionLockRequest([property: JsonRequired] bool IsCritical);

public enum TrialOutcome
{
    Passed = 1,
    Failed = 2
}

public sealed record RecordTrialRequest(
    [property: JsonRequired] TrialOutcome Outcome,
    [property: JsonRequired] DateTimeOffset ObservedAt);

public sealed record TrialObservation(
    string TenantId,
    Guid EquipmentId,
    string WorkOrderId,
    Guid ObservationId,
    Guid SourceEventId,
    TrialOutcome Outcome,
    DateTimeOffset ObservedAt);

public sealed record ReleaseProductionLockRequest(
    [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] Guid ObservationId,
    [property: JsonRequired] Guid SourceEventId);

public sealed record ProductionReleaseReceipt(
    Guid OperationId,
    TrialObservation Evidence,
    DateTimeOffset ReleasedAt);

public sealed record ProductionLockState(
    string TenantId,
    Guid EquipmentId,
    string WorkOrderId,
    DateTimeOffset RegisteredAt,
    ProductionReleaseReceipt? Release)
{
    public bool IsLocked => Release is null;
}

public sealed record MesError(string Code);

public static class MesTrialEventFeed
{
    public const string Path = "/api/mes/trial-events";
    public const int DefaultBatchSize = 32;
    public const int MaximumBatchSize = 100;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RegisterProductionLockRequest))]
[JsonSerializable(typeof(RecordTrialRequest))]
[JsonSerializable(typeof(TrialObservation))]
[JsonSerializable(typeof(TrialObservation[]))]
[JsonSerializable(typeof(ReleaseProductionLockRequest))]
[JsonSerializable(typeof(ProductionReleaseReceipt))]
[JsonSerializable(typeof(ProductionLockState))]
[JsonSerializable(typeof(MesError))]
public sealed partial class MesJsonContext : JsonSerializerContext;
