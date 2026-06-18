using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowRelationshipExtractorTests
{
    private readonly WorkflowRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_VariableSchema_And_StepTargets()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "order-workflow",
            Name = "Order Workflow",
            Version = 1,
            VariableSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-vars", Version = 1 },
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "validate-order", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(2);
        relationships.Should().AllSatisfy(r => r.From.Version.Should().Be(descriptor.Version));
        relationships.Should().ContainSingle(r => r.Role == "VariableSchema" && r.Kind == RelationshipKind.Uses);
        relationships.Should().ContainSingle(r => r.Role == "CapabilityStep" && r.Kind == RelationshipKind.Triggers);
    }

    [Fact]
    public void Extract_Nullable_VariableSchema_Omitted()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "order-workflow",
            Name = "Order Workflow",
            Version = 1,
            VariableSchema = null,
            Steps = Array.Empty<WorkflowStep>()
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().BeEmpty();
    }

    [Fact]
    public void Extract_CapabilityStep_Is_Strong_Triggers()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "w1",
            Name = "W1",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "c1", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var capRel = relationships.Should().ContainSingle(r => r.Role == "CapabilityStep").Subject;
        capRel.Kind.Should().Be(RelationshipKind.Triggers);
        capRel.Strength.Should().Be(RelationshipStrength.Strong);
        capRel.IsRuntimeBinding.Should().BeTrue();
        capRel.To.Namespace.Should().Be("capability");
    }

    [Fact]
    public void Extract_HumanTaskStep_Is_Strong_Triggers()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "w1",
            Name = "W1",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor> { Id = "ht1", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var htRel = relationships.Should().ContainSingle(r => r.Role == "HumanTaskStep").Subject;
        htRel.Kind.Should().Be(RelationshipKind.Triggers);
        htRel.Strength.Should().Be(RelationshipStrength.Strong);
        htRel.IsRuntimeBinding.Should().BeTrue();
        htRel.To.Namespace.Should().Be("humantask");
    }

    [Fact]
    public void Extract_SubWorkflowTarget_Is_Weak_NotRuntimeBinding()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "w1",
            Name = "W1",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new SubWorkflowTarget
                    {
                        SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor> { Id = "w2", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var subRel = relationships.Should().ContainSingle(r => r.Role == "SubWorkflowStep").Subject;
        subRel.Kind.Should().Be(RelationshipKind.References);
        subRel.Strength.Should().Be(RelationshipStrength.Weak);
        subRel.IsRuntimeBinding.Should().BeFalse();
    }
}
