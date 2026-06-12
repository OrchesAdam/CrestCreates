using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologyDiagnosticsTests
{
    private static IDescriptor MockDesc(string ns, string id, string name, DescriptorKind kind,
        DescriptorState state = DescriptorState.Active, int? version = null)
    {
        var mock = new Mock<IDescriptor>();
        mock.Setup(d => d.Namespace).Returns(ns);
        mock.Setup(d => d.Id).Returns(id);
        mock.Setup(d => d.Name).Returns(name);
        mock.Setup(d => d.Kind).Returns(kind);
        mock.Setup(d => d.State).Returns(state);
        mock.Setup(d => d.ContractHash).Returns("hash");
        mock.Setup(d => d.SupersededById).Returns((string?)null);
        if (version.HasValue)
        {
            mock.As<IVersionedDescriptor>().Setup(v => v.Version).Returns(version.Value);
        }
        return mock.Object;
    }

    [Fact]
    public void Missing_Strong_Target_Error()
    {
        var formDesc = MockDesc("form", "UserForm", "UserForm", DescriptorKind.Form);
        var missingRef = new DescriptorRef("schema", "MissingSchema", null);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("form", "UserForm", null), missingRef,
                RelationshipKind.Uses, "Schema", "Schema",
                RelationshipStrength.Strong, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { formDesc });

        var diag = snapshot.Diagnostics.All.Should().ContainSingle(d =>
            d.Code == "MISSING_TARGET" && d.Severity == DiagnosticSeverity.Error).Subject;
        diag.Message.Should().Contain("MissingSchema");
    }

    [Fact]
    public void Missing_Weak_Target_Warning()
    {
        var capDesc = MockDesc("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);
        var missingRef = new DescriptorRef("event", "UserCreated", null);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(capDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "CreateUser", null), missingRef,
                RelationshipKind.Produces, null, "Produces",
                RelationshipStrength.Weak, false)
        });

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { capDesc });

        var diag = snapshot.Diagnostics.All.Should().ContainSingle(d =>
            d.Code == "MISSING_TARGET" && d.Severity == DiagnosticSeverity.Warning).Subject;
    }

    [Fact]
    public void Strong_Cycle_Error()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

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

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "STRONG_CYCLE" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Weak_Cycle_No_Error()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

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

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "STRONG_CYCLE");
    }

    [Fact]
    public void Orphan_Warning()
    {
        var orphan = MockDesc("form", "OrphanForm", "OrphanForm", DescriptorKind.Form, DescriptorState.Active);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(orphan)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { orphan });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "ORPHAN" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Orphan_Draft_Excluded()
    {
        var draft = MockDesc("form", "DraftForm", "DraftForm", DescriptorKind.Form, DescriptorState.Draft);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(draft)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { draft });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "ORPHAN");
    }

    [Fact]
    public void Exact_Duplicate_Warning()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

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

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "EXACT_DUPLICATE" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Different_Role_Not_Duplicate()
    {
        var a = MockDesc("capability", "A", "A", DescriptorKind.Capability);
        var b = MockDesc("capability", "B", "B", DescriptorKind.Capability);

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

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new[] { a, b });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "EXACT_DUPLICATE");
    }

    [Fact]
    public void Unsupported_Reference_Warning()
    {
        var wfDesc = MockDesc("workflow", "MyWf", "MyWf", DescriptorKind.Workflow);
        var swRef = new DescriptorRef("workflow", "SubWf", null);

        var subWfDesc = MockDesc("workflow", "SubWf", "SubWf", DescriptorKind.Workflow);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(wfDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("workflow", "MyWf", null), swRef,
                RelationshipKind.References, RelationshipRoles.SubWorkflowStep, "Steps",
                RelationshipStrength.Weak, false)
        });
        mockProvider.Setup(p => p.GetRelationships(subWfDesc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { wfDesc, subWfDesc });

        snapshot.Diagnostics.All.Should().Contain(d => d.Code == "UNSUPPORTED_REFERENCE" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Unsupported_Not_Triggered_By_Weak_Alone()
    {
        var capDesc = MockDesc("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);

        var oldCapDesc = MockDesc("capability", "OldCap", "OldCap", DescriptorKind.Capability);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(capDesc)).Returns(new[]
        {
            new DescriptorRelationship(
                new DescriptorRef("capability", "CreateUser", null), new DescriptorRef("capability", "OldCap", null),
                RelationshipKind.DependsOn, RelationshipRoles.SupersededBy, "SupersededById",
                RelationshipStrength.Weak, false)
        });
        mockProvider.Setup(p => p.GetRelationships(oldCapDesc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { capDesc, oldCapDesc });

        snapshot.Diagnostics.All.Should().NotContain(d => d.Code == "UNSUPPORTED_REFERENCE");
    }
}
