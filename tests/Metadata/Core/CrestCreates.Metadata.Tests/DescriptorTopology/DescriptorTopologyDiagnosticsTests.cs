using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Form.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologyDiagnosticsTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();
    private readonly IDescriptorStableHashBuilder _hashBuilder;

    public DescriptorTopologyDiagnosticsTests()
    {
        _hashBuilder = new DescriptorStableHashBuilder(_hashComputer);
    }

    private static IDescriptor CreateConcreteDesc(string ns, string id, string name, DescriptorKind kind,
        DescriptorState state = DescriptorState.Active, int? version = null)
    {
        return kind switch
        {
            DescriptorKind.Schema => new SchemaDescriptor
            {
                Id = id, Name = name,
                Version = version ?? 0,
                State = state, SupersededById = null,
                ChangeKind = SchemaChangeKind.Additive,
                Fields = [], References = [], ValidationRules = []
            },
            DescriptorKind.Capability => new CapabilityDescriptor
            {
                Id = id, Name = name,
                Version = version ?? 0,
                State = state, SupersededById = null,
                CapabilityKind = CapabilityKind.Command
            },
            DescriptorKind.Form => new FormDescriptor
            {
                Id = id, Name = name,
                Version = version ?? 0,
                State = state, SupersededById = null,
                Schema = default,
                Fields = [], LayoutColumns = null
            },
            DescriptorKind.Workflow => new WorkflowDescriptor
            {
                Id = id, Name = name,
                Version = version ?? 0,
                State = state, SupersededById = null,
                VariableSchema = null,
                Steps = [],
                DefaultVariableScope = WorkflowVariableScope.Workflow
            },
            _ => throw new ArgumentException($"Unsupported descriptor kind: {kind}", nameof(kind))
        };
    }

    [Fact]
    public void Missing_Strong_Target_Error()
    {
        var formDesc = CreateConcreteDesc("form", "UserForm", "UserForm", DescriptorKind.Form);
        var missingRef = new DescriptorRef("schema", "MissingSchema", null);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("form", "UserForm", null), missingRef,
                RelationshipKind.Uses, "Schema", "Schema",
                RelationshipStrength.Strong, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { formDesc });

        var diag = snapshot.Diagnostics.All.Should().ContainSingle(d =>
            d.Code == "MISSING_TARGET" && d.Severity == DiagnosticSeverity.Error).Subject;
        diag.Message.Should().Contain("MissingSchema");
    }

    [Fact]
    public void Missing_Weak_Target_Warning()
    {
        var capDesc = CreateConcreteDesc("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);
        var missingRef = new DescriptorRef("event", "UserCreated", null);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(capDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "CreateUser", null), missingRef,
                RelationshipKind.Produces, null, "Produces",
                RelationshipStrength.Weak, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { capDesc });

        var diag = snapshot.Diagnostics.All.Should().ContainSingle(d =>
            d.Code == "MISSING_TARGET" && d.Severity == DiagnosticSeverity.Warning).Subject;
    }

    [Fact]
    public void Strong_Cycle_Error()
    {
        var a = CreateConcreteDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = CreateConcreteDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "B", null), new DescriptorRef("capability", "A", null),
                RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "STRONG_CYCLE" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Weak_Cycle_No_Error()
    {
        var a = CreateConcreteDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = CreateConcreteDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.References, null, "SupersededBy", RelationshipStrength.Weak, false)
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "B", null), new DescriptorRef("capability", "A", null),
                RelationshipKind.References, null, "SupersededBy", RelationshipStrength.Weak, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "STRONG_CYCLE");
    }

    [Fact]
    public void Orphan_Warning()
    {
        var orphan = CreateConcreteDesc("form", "OrphanForm", "OrphanForm", DescriptorKind.Form, DescriptorState.Active);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(orphan)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { orphan });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "ORPHAN" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Orphan_Draft_Excluded()
    {
        var draft = CreateConcreteDesc("form", "DraftForm", "DraftForm", DescriptorKind.Form, DescriptorState.Draft);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(draft)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { draft });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "ORPHAN");
    }

    [Fact]
    public void Exact_Duplicate_Warning()
    {
        var a = CreateConcreteDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = CreateConcreteDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "Input", "InputSchema", RelationshipStrength.Strong, false),
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "Input", "InputSchema", RelationshipStrength.Strong, false),
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "EXACT_DUPLICATE" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Different_Role_Not_Duplicate()
    {
        var a = CreateConcreteDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = CreateConcreteDesc("capability", "B", "B", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(a)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "InputSchema", "InputSchema", RelationshipStrength.Strong, false),
            new DescriptorRelationship(
                new DescriptorRef("capability", "A", null), new DescriptorRef("capability", "B", null),
                RelationshipKind.Uses, "OutputSchema", "OutputSchema", RelationshipStrength.Strong, false),
        });
        mockProvider.Setup(p => p.GetRelationships(b)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "EXACT_DUPLICATE");
    }

    [Fact]
    public void Unsupported_Reference_Warning()
    {
        var wfDesc = CreateConcreteDesc("workflow", "MyWf", "MyWf", DescriptorKind.Workflow);
        var swRef = new DescriptorRef("workflow", "SubWf", null);

        var subWfDesc = CreateConcreteDesc("workflow", "SubWf", "SubWf", DescriptorKind.Workflow);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(wfDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("workflow", "MyWf", null), swRef,
                RelationshipKind.References, RelationshipRoles.SubWorkflowStep, "Steps",
                RelationshipStrength.Weak, false)
        });
        mockProvider.Setup(p => p.GetRelationships(subWfDesc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { wfDesc, subWfDesc });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "UNSUPPORTED_REFERENCE" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Unsupported_Not_Triggered_By_Weak_Alone()
    {
        var capDesc = CreateConcreteDesc("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);

        var oldCapDesc = CreateConcreteDesc("capability", "OldCap", "OldCap", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(capDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "CreateUser", null), new DescriptorRef("capability", "OldCap", null),
                RelationshipKind.DependsOn, RelationshipRoles.SupersededBy, "SupersededById",
                RelationshipStrength.Weak, false)
        });
        mockProvider.Setup(p => p.GetRelationships(oldCapDesc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { capDesc, oldCapDesc });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "UNSUPPORTED_REFERENCE");
    }
}
