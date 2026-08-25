using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Snapshot.Abstractions;
using System.Text.Json.Serialization;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskInstance : ISnapshotable<HumanTaskInstance>
{
    public RuntimeInstanceKey Key { get; init; } = new(null, Guid.NewGuid().ToString("N"));
    public RuntimeDescriptorPin HumanTaskPin { get; init; } = CreateUnresolvedPin();

    public HumanTaskInstanceStatus Status { get; set; }

    public string? AssigneeUserId { get; set; }
    public string? AssigneeRoleId { get; set; }

    public RuntimeInstanceKey? WorkflowKey { get; init; }
    public string? WorkflowStepId { get; init; }

    public RuntimeStateValue? Input { get; init; }
    public RuntimeStateValue? Output { get; set; }

    public string? Outcome { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public string? CompletionDispatchError { get; set; }
    public DateTimeOffset? CompletionDispatchFailedAt { get; set; }
    public int CompletionDispatchAttemptCount { get; set; }
    public string? CompletionEventId { get; set; }

    [JsonIgnore]
    public IReadOnlyList<string> RequiredCompletionConsumerIds { get; internal set; } = Array.Empty<string>();

    public string? CancellationReason { get; set; }

    public IReadOnlyList<string> CandidateUserIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateRoleIds { get; set; } = Array.Empty<string>();
    public string? OrganizationUnitId { get; set; }
    public string? PositionId { get; set; }
    public string? AssigneeResolutionReason { get; set; }

    public long Revision { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonIgnore]
    public string Id => Key.InstanceId;
    [JsonIgnore]
    public string? TenantId => Key.TenantId;
    [JsonIgnore]
    public string HumanTaskId => HumanTaskPin.Ref.Id;
    [JsonIgnore]
    public int HumanTaskVersion => HumanTaskPin.Ref.Version ?? 0;
    [JsonIgnore]
    public string? WorkflowInstanceId => WorkflowKey?.InstanceId;

    public HumanTaskInstance Snapshot()
    {
        return new HumanTaskInstance
        {
            Key = Key,
            HumanTaskPin = HumanTaskPin,
            Status = Status,
            AssigneeUserId = AssigneeUserId,
            AssigneeRoleId = AssigneeRoleId,
            WorkflowKey = WorkflowKey,
            WorkflowStepId = WorkflowStepId,
            Input = Input,
            Output = Output,
            Outcome = Outcome,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt,
            CancelledAt = CancelledAt,
            CompletionDispatchError = CompletionDispatchError,
            CompletionDispatchFailedAt = CompletionDispatchFailedAt,
            CompletionDispatchAttemptCount = CompletionDispatchAttemptCount,
            CompletionEventId = CompletionEventId,
            RequiredCompletionConsumerIds = RequiredCompletionConsumerIds.ToArray(),
            CancellationReason = CancellationReason,
            Revision = Revision,
            UpdatedAt = UpdatedAt,
            CandidateUserIds = CandidateUserIds.ToArray(),
            CandidateRoleIds = CandidateRoleIds.ToArray(),
            OrganizationUnitId = OrganizationUnitId,
            PositionId = PositionId,
            AssigneeResolutionReason = AssigneeResolutionReason
        };
    }

    private static RuntimeDescriptorPin CreateUnresolvedPin() => new()
    {
        Ref = new DescriptorRef("humantask", "unresolved", 1),
        ContractHash = PlaceholderHash("Contract"),
        DefinitionHash = PlaceholderHash("Definition")
    };

    private static CanonicalHash PlaceholderHash(string purpose) => new()
    {
        Value = "unresolved",
        Algorithm = "unresolved",
        AlgorithmVersion = "unresolved",
        ArtifactKind = "Descriptor",
        DescriptorKind = "HumanTask",
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "unresolved",
        CanonicalShapeVersion = "unresolved"
    };
}
