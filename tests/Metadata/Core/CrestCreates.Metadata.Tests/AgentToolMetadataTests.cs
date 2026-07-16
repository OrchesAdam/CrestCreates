using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using CrestCreates.Metadata.DescriptorRelationship;
using CrestCreates.Metadata.DescriptorTopology;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public sealed class AgentToolMetadataTests
{
    private readonly DefaultCanonicalHashComputer _hashComputer = new();

    [Fact]
    public void Descriptor_kind_and_defaults_are_stable_and_fail_closed()
    {
        var descriptor = Create();

        ((int)DescriptorKind.AgentTool).Should().Be(9);
        DescriptorKindNames.ToCanonicalString(DescriptorKind.AgentTool)
            .Should().Be("AgentTool");
        descriptor.Namespace.Should().Be("agent-tool");
        descriptor.Kind.Should().Be(DescriptorKind.AgentTool);
        descriptor.SelectionPolicy.Should().Be(AgentToolSelectionPolicy.ExplicitOnly);
        descriptor.SideEffectKind.Should().Be(AgentToolSideEffectKind.Unknown);
        descriptor.ApprovalMode.Should().Be(AgentToolApprovalMode.PolicyDriven);
        descriptor.AuditMode.Should().Be(AgentToolAuditMode.Required);
        descriptor.Budget.CostUnits.Should().Be(1);

        ((int)AgentToolSelectionPolicy.Unknown).Should().Be(0);
        ((int)AgentToolSideEffectKind.Unknown).Should().Be(0);
        ((int)AgentToolApprovalMode.Unknown).Should().Be(0);
        ((int)AgentToolAuditMode.Unknown).Should().Be(0);
        ((int)AgentToolApprovalMode.None).Should().Be(3);
    }

    [Fact]
    public void Every_selection_binding_and_governance_field_changes_contract_hash()
    {
        var baselineHash = ContractHash(Create());
        var variants = new Dictionary<string, AgentCapabilityToolDescriptor>
        {
            [nameof(AgentCapabilityToolDescriptor.Id)] = Create(id: "agent-tool:orders.lookup-v2"),
            [nameof(AgentCapabilityToolDescriptor.Name)] = Create(name: "Lookup a customer order"),
            [nameof(AgentCapabilityToolDescriptor.Version)] = Create(version: 2),
            [nameof(AgentCapabilityToolDescriptor.State)] = Create(state: DescriptorState.Deprecated),
            [nameof(AgentCapabilityToolDescriptor.SupersededById)] = Create(supersededById: "agent-tool:orders.lookup-v2"),
            ["Capability.Id"] = Create(capability: new("orders.find", 1)),
            ["Capability.Version"] = Create(capability: new("orders.lookup", 2)),
            ["Capability.SelectionMode"] = Create(capability: new("orders.lookup", 0, VersionSelectionMode.Latest)),
            ["Capability.ExpectedContractHash"] = Create(capability: new("orders.lookup", 1, VersionSelectionMode.Exact, "expected-hash")),
            [nameof(AgentCapabilityToolDescriptor.ToolName)] = Create(toolName: "orders.find"),
            [nameof(AgentCapabilityToolDescriptor.Title)] = Create(title: "Find order"),
            [nameof(AgentCapabilityToolDescriptor.Description)] = Create(description: "Finds one order."),
            [nameof(AgentCapabilityToolDescriptor.SelectionPolicy)] = Create(selectionPolicy: AgentToolSelectionPolicy.AutomaticAllowed),
            [nameof(AgentCapabilityToolDescriptor.SideEffectKind)] = Create(sideEffectKind: AgentToolSideEffectKind.ReadOnly),
            [nameof(AgentCapabilityToolDescriptor.RiskFloor)] = Create(riskFloor: CapabilityRiskLevel.High),
            [nameof(AgentCapabilityToolDescriptor.ApprovalMode)] = Create(approvalMode: AgentToolApprovalMode.Required),
            ["Budget.Category"] = Create(budget: new() { Category = "priority-read" }),
            ["Budget.CostUnits"] = Create(budget: new() { Category = "read", CostUnits = 2 }),
            ["Budget.MaxCallsPerExecution"] = Create(budget: new() { Category = "read", MaxCallsPerExecution = 2 }),
            [nameof(AgentCapabilityToolDescriptor.AuditMode)] = Create(auditMode: AgentToolAuditMode.BestEffort),
            [nameof(AgentCapabilityToolDescriptor.AllowedAgentRoles)] = Create(roles: ["support-agent"])
        };

        foreach (var (field, variant) in variants)
        {
            ContractHash(variant).Should().NotBe(
                baselineHash,
                $"{field} changes model selection, binding, exposure, or governance");
        }
    }

    [Fact]
    public void Role_order_is_canonical_but_role_membership_is_contractual()
    {
        var first = Create(roles: ["ops-agent", "sales-agent"]);
        var reordered = Create(roles: ["sales-agent", "ops-agent"]);
        var changed = Create(roles: ["ops-agent", "support-agent"]);

        ContractHash(first).Should().Be(ContractHash(reordered));
        ContractHash(first).Should().NotBe(ContractHash(changed));
    }

    [Fact]
    public void Contract_and_definition_hashes_use_agent_tool_domain_separation()
    {
        var descriptor = Create();

        var contract = _hashComputer.ComputeContractHash(
            descriptor,
            CanonicalHashScope.InternalFull);
        var definition = _hashComputer.ComputeDefinitionHash(
            descriptor,
            CanonicalHashScope.InternalFull);

        contract.DescriptorKind.Should().Be(DescriptorKindNames.AgentTool);
        contract.CanonicalShapeVersion.Should().Be("agent-tool-contract-hash-v1");
        definition.DescriptorKind.Should().Be(DescriptorKindNames.AgentTool);
        definition.CanonicalShapeVersion.Should().Be("agent-tool-definition-hash-v1");
        contract.Value.Should().HaveLength(64);
        definition.Value.Should().HaveLength(64);
    }

    [Fact]
    public void Relationship_extractor_emits_one_strong_capability_reference()
    {
        var descriptor = Create();
        var relationship = new AgentToolRelationshipExtractor()
            .Extract(descriptor)
            .Should().ContainSingle().Subject;

        relationship.From.Should().Be(
            new DescriptorRef("agent-tool", descriptor.Id, descriptor.Version));
        relationship.To.Should().Be(
            new DescriptorRef("capability", descriptor.Capability.Id, descriptor.Capability.Version));
        relationship.Kind.Should().Be(RelationshipKind.References);
        relationship.Role.Should().Be(RelationshipRoles.Capability);
        relationship.SourcePath.Should().Be(nameof(AgentCapabilityToolDescriptor.Capability));
        relationship.Strength.Should().Be(RelationshipStrength.Strong);
        relationship.IsRuntimeBinding.Should().BeFalse();
    }

    [Fact]
    public void Relationship_kernel_registers_agent_tool_extractor()
    {
        var services = new ServiceCollection();
        services.AddRelationshipKernel();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IDescriptorRelationshipExtractor>()
            .Should().ContainSingle(extractor =>
                extractor.SupportedKind == DescriptorKind.AgentTool &&
                extractor.DescriptorType == typeof(AgentCapabilityToolDescriptor));
    }

    [Fact]
    public void Package_round_trip_preserves_agent_tool_ref_kind_hashes_and_relationship()
    {
        var descriptor = Create();
        var capability = new CapabilityDescriptor
        {
            Id = descriptor.Capability.Id,
            Name = "Lookup order",
            Version = descriptor.Capability.Version
        };
        var hashBuilder = new DescriptorStableHashBuilder(_hashComputer);
        var relationships = new DefaultDescriptorRelationshipProvider(
            [new AgentToolRelationshipExtractor()]);
        var topology = new DescriptorTopologyBuilder(relationships, hashBuilder)
            .Build([descriptor, capability]);
        var packageHashComputer = new DefaultDescriptorPackageCanonicalHashComputer(
            _hashComputer);
        var package = new DefaultDescriptorPackageBuilder(
            hashBuilder,
            packageHashComputer).Build(new DescriptorPackageBuildRequest
            {
                PackageId = "agent-tools.pkg",
                PackageVersion = "1.0.0",
                CreatedAt = DateTimeOffset.UnixEpoch,
                Descriptors = [descriptor, capability],
                TopologySnapshot = topology
            });
        var serializer = new DescriptorPackageSerializer();

        var roundTripped = serializer.Deserialize(serializer.Serialize(package));

        var entry = roundTripped!.SnapshotData.Descriptors
            .Should().ContainSingle(candidate => candidate.Kind == DescriptorKind.AgentTool)
            .Subject;
        entry.Ref.Should().Be(new DescriptorRef(
            descriptor.Namespace,
            descriptor.Id,
            descriptor.Version));
        entry.ContractHash.Should().Be(ContractHash(descriptor));
        entry.DefinitionHash.Should().Be(
            _hashComputer.ComputeDefinitionHash(
                descriptor,
                CanonicalHashScope.InternalFull).Value);

        var relationship = roundTripped.SnapshotData.Relationships
            .Should().ContainSingle().Subject;
        relationship.From.Should().Be(entry.Ref);
        relationship.To.Should().Be(new DescriptorRef(
            "capability",
            descriptor.Capability.Id,
            descriptor.Capability.Version));
        relationship.Kind.Should().Be(RelationshipKind.References);
        relationship.Role.Should().Be(RelationshipRoles.Capability);
        relationship.Strength.Should().Be(RelationshipStrength.Strong);
    }

    private string ContractHash(AgentCapabilityToolDescriptor descriptor)
        => _hashComputer.ComputeContractHash(
            descriptor,
            CanonicalHashScope.InternalFull).Value;

    private static AgentCapabilityToolDescriptor Create(
        string id = "agent-tool:orders.lookup",
        string name = "Lookup order",
        int version = 1,
        DescriptorState state = DescriptorState.Active,
        string? supersededById = null,
        CapabilityProjectionReference? capability = null,
        string toolName = "orders.lookup",
        string? title = "Lookup order",
        string description = "Looks up one order.",
        AgentToolSelectionPolicy selectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        AgentToolSideEffectKind sideEffectKind = AgentToolSideEffectKind.Unknown,
        CapabilityRiskLevel? riskFloor = null,
        AgentToolApprovalMode approvalMode = AgentToolApprovalMode.PolicyDriven,
        AgentToolBudgetRequirement? budget = null,
        AgentToolAuditMode auditMode = AgentToolAuditMode.Required,
        IReadOnlyList<string>? roles = null) => new()
        {
            Id = id,
            Name = name,
            Version = version,
            State = state,
            SupersededById = supersededById,
            Capability = capability ?? new CapabilityProjectionReference(
                "orders.lookup",
                1,
                VersionSelectionMode.Exact),
            ToolName = toolName,
            Title = title,
            Description = description,
            SelectionPolicy = selectionPolicy,
            SideEffectKind = sideEffectKind,
            RiskFloor = riskFloor,
            ApprovalMode = approvalMode,
            Budget = budget ?? new AgentToolBudgetRequirement { Category = "read" },
            AuditMode = auditMode,
            AllowedAgentRoles = roles ?? ["ops-agent", "sales-agent"]
        };
}
