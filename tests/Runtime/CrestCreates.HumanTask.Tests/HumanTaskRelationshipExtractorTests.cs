using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRelationshipExtractorTests
{
    private readonly HumanTaskRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_All_Four_Ref_Types()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "review-input", Version = 1 },
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "review-output", Version = 1 },
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "approve-order", Version = 1 }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(4);
        relationships.Should().AllSatisfy(r => r.From.Version.Should().Be(descriptor.Version));
    }

    [Fact]
    public void Extract_Interaction_Is_Uses_Kind()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            Outcomes = Array.Empty<CompletionOutcome>()
        };

        var relationships = _extractor.Extract(descriptor);

        var interaction = relationships.Should().ContainSingle(r => r.Role == "Interaction").Subject;
        interaction.Kind.Should().Be(RelationshipKind.Uses);
        interaction.To.Namespace.Should().Be("form");
        interaction.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Extract_Outcome_Capability_Is_Triggers_Kind()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor> { Id = "approve-order", Version = 1 }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var outcome = relationships.Should().ContainSingle(r => r.Role == "Outcome").Subject;
        outcome.Kind.Should().Be(RelationshipKind.Triggers);
        outcome.IsRuntimeBinding.Should().BeTrue();
        outcome.To.Namespace.Should().Be("capability");
    }

    [Fact]
    public void Extract_Nullable_Schemas_Omitted()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            InputSchema = null,
            OutputSchema = null,
            Outcomes = Array.Empty<CompletionOutcome>()
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(1);
        relationships.Should().NotContain(r => r.Role == "InputSchema" || r.Role == "OutputSchema");
    }
}
