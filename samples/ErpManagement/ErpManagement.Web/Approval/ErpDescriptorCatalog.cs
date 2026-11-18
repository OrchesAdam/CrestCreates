using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using ErpManagement.Domain.Shared.Constants;

#pragma warning disable CC1001 // Descriptors are registered imperatively below and verified through ErpDescriptorLookup.

namespace ErpManagement.Web.Approval;

public static class ErpDescriptorCatalog
{
    public static IReadOnlyList<SchemaDescriptor> Schemas { get; } =
    [
        Schema(ErpContractIds.RequisitionDecisionSchema,
            RequiredGuid("requisitionId"),
            RequiredString("comment", 1, 1000)),
        Schema(ErpContractIds.SalesDecisionSchema,
            RequiredGuid("salesOrderId"),
            RequiredString("comment", 1, 1000))
    ];

    public static FormDescriptor ApprovalForm { get; } = new()
    {
        Id = ErpContractIds.RequisitionApprovalForm,
        Name = "Requisition approval form",
        Version = 1,
        State = DescriptorState.Active,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>(ErpContractIds.RequisitionDecisionSchema, 1),
        Fields =
        [
            new FormFieldDescriptor { SchemaFieldName = "requisitionId", Label = "Requisition", ControlType = "hidden", IsReadOnly = true, Order = 0 },
            new FormFieldDescriptor { SchemaFieldName = "comment", Label = "Decision comment", ControlType = "textarea", Order = 1 }
        ]
    };

    public static HumanTaskDescriptor ApprovalHumanTask { get; } = new()
    {
        Id = ErpContractIds.RequisitionApprovalHumanTask,
        Name = "Requisition approval",
        Version = 1,
        State = DescriptorState.Active,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>(ErpContractIds.RequisitionApprovalForm, 1),
        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
        Outcomes =
        [
            new CompletionOutcome { Condition = CompletionCondition.Approve },
            new CompletionOutcome { Condition = CompletionCondition.Reject }
        ]
    };

    public static WorkflowDescriptor ApprovalWorkflow { get; } = new()
    {
        Id = ErpContractIds.RequisitionApprovalWorkflow,
        Name = "Requisition approval workflow",
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
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(ErpContractIds.RequisitionApprovalHumanTask, 1)
                }
            }
        ]
    };

    public static FormDescriptor SalesApprovalForm { get; } = new()
    {
        Id = ErpContractIds.SalesApprovalForm,
        Name = "Sales order approval form",
        Version = 1,
        State = DescriptorState.Active,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>(ErpContractIds.SalesDecisionSchema, 1),
        Fields =
        [
            new FormFieldDescriptor { SchemaFieldName = "salesOrderId", Label = "Sales order", ControlType = "hidden", IsReadOnly = true, Order = 0 },
            new FormFieldDescriptor { SchemaFieldName = "comment", Label = "Decision comment", ControlType = "textarea", Order = 1 }
        ]
    };

    public static HumanTaskDescriptor SalesApprovalHumanTask { get; } = new()
    {
        Id = ErpContractIds.SalesApprovalHumanTask,
        Name = "Sales order approval",
        Version = 1,
        State = DescriptorState.Active,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>(ErpContractIds.SalesApprovalForm, 1),
        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
        Outcomes =
        [
            new CompletionOutcome { Condition = CompletionCondition.Approve },
            new CompletionOutcome { Condition = CompletionCondition.Reject }
        ]
    };

    public static WorkflowDescriptor SalesApprovalWorkflow { get; } = new()
    {
        Id = ErpContractIds.SalesApprovalWorkflow,
        Name = "Sales order approval workflow",
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
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(ErpContractIds.SalesApprovalHumanTask, 1)
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

    private static SchemaFieldDescriptor RequiredGuid(string name) => new()
    {
        Name = name,
        FieldType = "guid",
        IsRequired = true
    };
}

public sealed class ErpDescriptorProvider<T>(IReadOnlyList<T> descriptors) : IDescriptorProvider<T> where T : IDescriptor
{
    public IReadOnlyList<T> GetDescriptors() => descriptors;
}

public sealed class ErpDescriptorLookup(IEnumerable<IDescriptor> descriptors) : IDescriptorLookup
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