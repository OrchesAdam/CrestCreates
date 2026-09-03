using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host;

#pragma warning disable CC1001
public static class AssetDescriptorCatalog
{
    private const string RegisterInput = "asset-management.schema.register-input";
    private const string QueryInput = "asset-management.schema.query-input";
    private const string UpdateInput = "asset-management.schema.update-input";
    private const string AssignInput = "asset-management.schema.assign-input";
    private const string ReturnInput = "asset-management.schema.return-input";
    private const string TransferInput = "asset-management.schema.transfer-input";
    private const string MaintenanceInput = "asset-management.schema.maintenance-input";
    private const string Asset = "asset-management.schema.asset";
    private const string List = "asset-management.schema.asset-list";
    private const string Operation = "asset-management.schema.operation";
    private const string Decision = "asset-management.schema.maintenance-decision";

    public static IReadOnlyList<SchemaDescriptor> Schemas { get; } =
    [
        Schema(RegisterInput, String("assetTag", 1, 100), String("name", 1, 200), String("description", 1, 2000), String("category", 1, 100), OptionalGuid("organizationId"), String("location", 1, 500, nullable: true)),
        Schema(QueryInput, OptionalGuid("assetId"), String("search", 1, 200, nullable: true), String("status", 1, 40, nullable: true), OptionalGuid("organizationId")),
        Schema(UpdateInput, RequiredGuid("assetId"), String("name", 1, 200), String("description", 1, 2000), String("category", 1, 100), String("location", 1, 500, nullable: true)),
        Schema(AssignInput, RequiredGuid("assetId"), String("userId", 1, 200), RequiredGuid("organizationId")),
        Schema(ReturnInput, RequiredGuid("assetId")),
        Schema(TransferInput, RequiredGuid("assetId"), RequiredGuid("organizationId"), String("location", 1, 500, nullable: true)),
        Schema(MaintenanceInput, RequiredGuid("assetId"), String("reason", 1, 1000)),
        Schema(Asset, new SchemaFieldDescriptor { Name = "id", FieldType = "guid", IsRequired = true }, String("tenantId", 1, 100), new SchemaFieldDescriptor { Name = "organizationId", FieldType = "guid", IsNullable = true }, String("assetTag", 1, 100), String("name", 1, 200), String("description", 1, 2000), String("category", 1, 100), String("location", 1, 500, nullable: true), String("status", 1, 40), String("assignedUserId", 1, 200, nullable: true), new SchemaFieldDescriptor { Name = "activeAssignmentId", FieldType = "guid", IsNullable = true }, String("maintenanceWorkflowInstanceId", 1, 200, nullable: true), String("concurrencyStamp", 1, 200)),
        Schema(List, new SchemaFieldDescriptor { Name = "items", FieldType = "array", IsRequired = true, IsCollection = true, CollectionElementType = "AssetResult" }),
        Schema(Operation, new SchemaFieldDescriptor { Name = "assetId", FieldType = "guid", IsRequired = true }, String("status", 1, 40), String("workflowInstanceId", 1, 200, nullable: true), String("humanTaskId", 1, 200, nullable: true)),
        Schema(Decision, RequiredGuid("assetId"), new SchemaFieldDescriptor { Name = "approved", FieldType = "bool", IsRequired = true }, String("note", 1, 1000))
    ];

    public static IReadOnlyList<CapabilityDescriptor> Capabilities { get; } =
    [
        Capability(AssetContractIds.RegisterCapability, "register", CapabilityKind.Command, CapabilityRiskLevel.Medium, RegisterInput, Asset, AssetPermissions.Assets.Register),
        Capability(AssetContractIds.GetCapability, "get", CapabilityKind.Query, CapabilityRiskLevel.Low, QueryInput, Asset, AssetPermissions.Assets.Read),
        Capability(AssetContractIds.QueryCapability, "query", CapabilityKind.Query, CapabilityRiskLevel.Low, QueryInput, List, AssetPermissions.Assets.Search),
        Capability(AssetContractIds.UpdateCapability, "update", CapabilityKind.Command, CapabilityRiskLevel.Medium, UpdateInput, Asset, AssetPermissions.Assets.Update),
        Capability(AssetContractIds.AssignCapability, "assign", CapabilityKind.Command, CapabilityRiskLevel.High, AssignInput, Asset, AssetPermissions.Assets.Assign),
        Capability(AssetContractIds.ReturnCapability, "return", CapabilityKind.Command, CapabilityRiskLevel.Medium, ReturnInput, Asset, AssetPermissions.Assets.Return),
        Capability(AssetContractIds.TransferCapability, "transfer", CapabilityKind.Command, CapabilityRiskLevel.High, TransferInput, Asset, AssetPermissions.Assets.Transfer),
        Capability(AssetContractIds.RequestMaintenanceCapability, "request-maintenance", CapabilityKind.Command, CapabilityRiskLevel.High, MaintenanceInput, Operation, AssetPermissions.Assets.RequestMaintenance),
        Capability(AssetContractIds.ApplyMaintenanceCapability, "apply-maintenance", CapabilityKind.Command, CapabilityRiskLevel.High, Decision, Asset, AssetPermissions.Assets.CompleteMaintenance)
    ];

    public static FormDescriptor MaintenanceForm { get; } = new()
    {
        Id = AssetContractIds.MaintenanceForm, Name = "Asset maintenance review", Version = 1, State = DescriptorState.Active,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>(Decision, 1),
        Fields = [
            new FormFieldDescriptor { SchemaFieldName = "assetId", Label = "Asset", ControlType = "hidden", IsReadOnly = true, Order = 0 },
            new FormFieldDescriptor { SchemaFieldName = "approved", Label = "Approve maintenance", ControlType = "checkbox", Order = 1 },
            new FormFieldDescriptor { SchemaFieldName = "note", Label = "Review note", ControlType = "textarea", Order = 2 }
        ]
    };

    public static HumanTaskDescriptor MaintenanceHumanTask { get; } = new()
    {
        Id = AssetContractIds.MaintenanceHumanTask, Name = "Asset maintenance review", Version = 1, State = DescriptorState.Active,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>(AssetContractIds.MaintenanceForm, 1),
        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
        Outcomes = [new CompletionOutcome { Condition = CompletionCondition.Approve }, new CompletionOutcome { Condition = CompletionCondition.Reject }]
    };

    public static WorkflowDescriptor MaintenanceWorkflow { get; } = new()
    {
        Id = AssetContractIds.MaintenanceWorkflow, Name = "Asset maintenance review", Version = 1, State = DescriptorState.Active,
        Steps = [new WorkflowStep { Id = "maintenance-review", Name = "Manager maintenance review", Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(AssetContractIds.MaintenanceHumanTask, 1) } }]
    };

    private static SchemaDescriptor Schema(string id, params SchemaFieldDescriptor[] fields) => new() { Id = id, Name = id, Version = 1, State = DescriptorState.Active, Fields = fields };
    private static SchemaFieldDescriptor RequiredGuid(string name) => new() { Name = name, FieldType = "guid", IsRequired = true };
    private static SchemaFieldDescriptor OptionalGuid(string name) => new() { Name = name, FieldType = "guid", IsNullable = true };
    private static SchemaFieldDescriptor String(string name, int min, int max, bool nullable = false) => new() { Name = name, FieldType = "string", IsRequired = !nullable, IsNullable = nullable, MinLength = nullable ? null : min, MaxLength = max };
    private static CapabilityDescriptor Capability(string id, string name, CapabilityKind kind, CapabilityRiskLevel risk, string input, string output, string permission) => new() { Namespace = "capability", Id = id, Name = name, CapabilityKind = kind, RiskLevel = risk, State = DescriptorState.Active, Version = 1, InputSchema = new VersionedDescriptorRef<SchemaDescriptor>(input, 1), OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>(output, 1), Permissions = [permission] };
}

public sealed class AssetDescriptorProvider<T>(IReadOnlyList<T> descriptors) : IDescriptorProvider<T> where T : IDescriptor
{
    public IReadOnlyList<T> GetDescriptors() => descriptors;
}

public sealed class AssetDescriptorLookup(IEnumerable<IDescriptor> descriptors) : IDescriptorLookup
{
    private readonly HashSet<(string Namespace, string Id, int Version)> _descriptors = descriptors.Select(d => (d.Namespace, d.Id, d is IVersionedDescriptor versioned ? versioned.Version : 0)).ToHashSet();
    public bool Exists(DescriptorRef descriptorRef) => descriptorRef.Version is int version
        ? _descriptors.Contains((descriptorRef.Namespace, descriptorRef.Id, version))
        : _descriptors.Any(d => d.Namespace == descriptorRef.Namespace && d.Id == descriptorRef.Id);
}
#pragma warning restore CC1001
