using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

#pragma warning disable CC1001 // Sample descriptors are registered imperatively below and verified through ProcurementDescriptorLookup tests.

namespace CrestCreates.Sample.Procurement.Host;

public static class ProcurementDescriptorCatalog
{
    private const string HumanTaskDescriptorId = "ht_procurement_approval";

    public static IReadOnlyList<SchemaDescriptor> Schemas { get; } =
    [
        Schema(ProcurementContractIds.SubmitInputSchema,
            RequiredString("title", 1, 200),
            OptionalNonNullString("description", 1000),
            new SchemaFieldDescriptor { Name = "amount", FieldType = "decimal", IsRequired = true, MinValue = 0.01 },
            RequiredString("currency", 3, 3),
            RequiredString("category", 1, 100)),
        Schema(ProcurementContractIds.SubmitOutputSchema,
            RequiredGuid("requestId"),
            RequiredString("status", 1, 40),
            new SchemaFieldDescriptor { Name = "amount", FieldType = "decimal", IsRequired = true },
            RequiredString("currency", 3, 3),
            new SchemaFieldDescriptor { Name = "requiresApproval", FieldType = "bool", IsRequired = true }),
        Schema(ProcurementContractIds.GetInputSchema, RequiredGuid("requestId")),
        Schema(ProcurementContractIds.ApproveInputSchema,
            RequiredGuid("requestId"),
            RequiredString("comment", 1, 1000)),
        Schema(ProcurementContractIds.RejectInputSchema,
            RequiredGuid("requestId"),
            RequiredString("reason", 1, 1000)),
        Schema(ProcurementContractIds.RequestOutputSchema,
            RequiredGuid("id"),
            RequiredGuid("requestId"),
            RequiredString("title", 1, 200),
            OptionalString("description", 1000, required: true),
            new SchemaFieldDescriptor { Name = "amount", FieldType = "decimal", IsRequired = true },
            RequiredString("currency", 3, 3),
            RequiredString("requesterId", 1, 200),
            RequiredString("category", 1, 100),
            RequiredString("status", 1, 40),
            OptionalString("approverId", 200),
            OptionalString("workflowInstanceId", 200),
            OptionalDateTime("approvedAt"),
            OptionalDateTime("rejectedAt"))
    ];

    public static IReadOnlyList<CapabilityDescriptor> NativeCapabilities { get; } =
    [
        Capability(
            ProcurementContractIds.SubmitCapability,
            "submit-request",
            CapabilityKind.Command,
            CapabilityRiskLevel.Medium,
            ProcurementContractIds.SubmitInputSchema,
            ProcurementContractIds.SubmitOutputSchema),
        Capability(
            ProcurementContractIds.GetCapability,
            "get-request",
            CapabilityKind.Query,
            CapabilityRiskLevel.Low,
            ProcurementContractIds.GetInputSchema,
            ProcurementContractIds.RequestOutputSchema),
        Capability(
            ProcurementContractIds.ApproveCapability,
            "approve-request",
            CapabilityKind.Command,
            CapabilityRiskLevel.High,
            ProcurementContractIds.ApproveInputSchema,
            ProcurementContractIds.RequestOutputSchema,
            ["procurement.approve"]),
        Capability(
            ProcurementContractIds.RejectCapability,
            "reject-request",
            CapabilityKind.Command,
            CapabilityRiskLevel.High,
            ProcurementContractIds.RejectInputSchema,
            ProcurementContractIds.RequestOutputSchema,
            ["procurement.approve"]),
        Capability(
            ProcurementContractIds.ApplyApprovalDecisionCapability,
            "apply-approval-decision",
            CapabilityKind.Command,
            CapabilityRiskLevel.High,
            ProcurementContractIds.ApproveInputSchema,
            ProcurementContractIds.RequestOutputSchema,
            ["procurement.approve"]),
        Capability(
            ProcurementContractIds.ApplyRejectionDecisionCapability,
            "apply-rejection-decision",
            CapabilityKind.Command,
            CapabilityRiskLevel.High,
            ProcurementContractIds.RejectInputSchema,
            ProcurementContractIds.RequestOutputSchema,
            ["procurement.approve"])
    ];

    public static FormDescriptor ApprovalForm { get; } = new()
    {
        Id = ProcurementContractIds.ApprovalForm,
        Name = "Procurement approval form",
        Version = 1,
        State = DescriptorState.Active,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>(
            ProcurementContractIds.ApproveInputSchema,
            1),
        Fields =
        [
            new FormFieldDescriptor
            {
                SchemaFieldName = "requestId",
                Label = "Request",
                ControlType = "hidden",
                IsReadOnly = true,
                Order = 0
            },
            new FormFieldDescriptor
            {
                SchemaFieldName = "comment",
                Label = "Decision comment",
                ControlType = "textarea",
                Order = 1
            }
        ]
    };

    public static HumanTaskDescriptor ApprovalHumanTask { get; } = new()
    {
        Id = HumanTaskDescriptorId,
        Name = "Procurement approval",
        Version = 1,
        State = DescriptorState.Active,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>(ProcurementContractIds.ApprovalForm, 1),
        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
        Outcomes =
        [
            new CompletionOutcome { Condition = CompletionCondition.Approve },
            new CompletionOutcome { Condition = CompletionCondition.Reject }
        ]
    };

    public static WorkflowDescriptor ApprovalWorkflow { get; } = new()
    {
        Id = ProcurementContractIds.ApprovalWorkflow,
        Name = "Procurement approval workflow",
        Version = 1,
        State = DescriptorState.Active,
        Steps =
        [
            new WorkflowStep
            {
                Id = "manager-approval",
                Name = "Manager approval",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(
                        HumanTaskDescriptorId,
                        1)
                }
            }
        ]
    };

    private static SchemaDescriptor Schema(string id, params SchemaFieldDescriptor[] fields) => new()
    {
        Id = id,
        Name = id,
        Version = 1,
        State = DescriptorState.Active,
        Fields = fields
    };

    private static SchemaFieldDescriptor RequiredString(string name, int min, int max) => new()
    {
        Name = name,
        FieldType = "string",
        IsRequired = true,
        MinLength = min,
        MaxLength = max
    };

    private static SchemaFieldDescriptor OptionalString(
        string name,
        int max,
        bool required = false) => new()
    {
        Name = name,
        FieldType = "string",
        IsRequired = required,
        IsNullable = !required,
        MaxLength = max
    };

    private static SchemaFieldDescriptor OptionalNonNullString(string name, int max) => new()
    {
        Name = name,
        FieldType = "string",
        IsRequired = false,
        IsNullable = false,
        MaxLength = max
    };

    private static SchemaFieldDescriptor RequiredGuid(string name) => new()
    {
        Name = name,
        FieldType = "guid",
        IsRequired = true
    };

    private static SchemaFieldDescriptor OptionalDateTime(string name) => new()
    {
        Name = name,
        FieldType = "datetime",
        IsNullable = true
    };

    private static CapabilityDescriptor Capability(
        string id,
        string name,
        CapabilityKind kind,
        CapabilityRiskLevel risk,
        string inputSchema,
        string outputSchema,
        IReadOnlyList<string>? permissions = null) => new()
    {
        Namespace = "capability",
        Id = id,
        Name = name,
        CapabilityKind = kind,
        RiskLevel = risk,
        State = DescriptorState.Active,
        Version = 1,
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>(inputSchema, 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>(outputSchema, 1),
        Permissions = permissions ?? Array.Empty<string>()
    };

}

public sealed class ProcurementDescriptorProvider<T>(IReadOnlyList<T> descriptors)
    : IDescriptorProvider<T> where T : IDescriptor
{
    public IReadOnlyList<T> GetDescriptors() => descriptors;
}

public sealed class ProcurementDescriptorLookup(IEnumerable<IDescriptor> descriptors)
    : IDescriptorLookup
{
    private readonly HashSet<(string Namespace, string Id, int Version)> _descriptors = descriptors
        .Select(descriptor => (descriptor.Namespace, descriptor.Id,
            descriptor is IVersionedDescriptor versioned ? versioned.Version : 0))
        .ToHashSet();

    public bool Exists(DescriptorRef descriptorRef)
        => descriptorRef.Version is int version
            ? _descriptors.Contains((descriptorRef.Namespace, descriptorRef.Id, version))
            : _descriptors.Any(item => string.Equals(item.Namespace, descriptorRef.Namespace, StringComparison.Ordinal)
                && string.Equals(item.Id, descriptorRef.Id, StringComparison.Ordinal));
}

#pragma warning restore CC1001
