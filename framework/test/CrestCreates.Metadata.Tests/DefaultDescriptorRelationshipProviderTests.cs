using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DefaultDescriptorRelationshipProviderTests
{
    [Fact]
    public void GetRelationships_Dispatches_To_Correct_Concrete_Type()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();
        services.AddSingleton<IDescriptorRelationshipProvider, DefaultDescriptorRelationshipProvider>();
        var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDescriptorRelationshipProvider>();
        var schema = new Schema.Abstractions.SchemaDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            References = new[]
            {
                new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor> { Id = "ref1", Version = 1 }
            }
        };

        var relationships = provider.GetRelationships(schema);

        relationships.Should().HaveCount(1);
        relationships[0].To.Id.Should().Be("ref1");
    }

    [Fact]
    public void GetRelationships_Returns_Empty_For_Unknown_Concrete_Type()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();
        services.AddSingleton<IDescriptorRelationshipProvider, DefaultDescriptorRelationshipProvider>();
        var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDescriptorRelationshipProvider>();
        var unknownDescriptor = new UnknownDescriptor();

        var relationships = provider.GetRelationships(unknownDescriptor);

        relationships.Should().BeEmpty();
    }

    [Fact]
    public void GetRelationships_Dispatches_GeneratedEventDescriptor_To_EventExtractor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDescriptorRelationshipExtractor, EventRelationshipExtractor>();
        services.AddSingleton<IDescriptorRelationshipProvider, DefaultDescriptorRelationshipProvider>();
        var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDescriptorRelationshipProvider>();
        var genEvent = new GeneratedEventDescriptor
        {
            Id = "test-event",
            Name = "Test",
            Version = 1,
            PayloadType = typeof(string),
            PayloadSchemaRef = new VersionedDescriptorRef<SchemaDescriptor> { Id = "test-schema", Version = 1 }
        };

        var relationships = provider.GetRelationships(genEvent);

        relationships.Should().HaveCount(1);
        relationships[0].Kind.Should().Be(RelationshipKind.Uses);
    }

    [Fact]
    public void GetRelationships_EventDescriptor_Returns_Empty()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDescriptorRelationshipExtractor, EventRelationshipExtractor>();
        services.AddSingleton<IDescriptorRelationshipProvider, DefaultDescriptorRelationshipProvider>();
        var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDescriptorRelationshipProvider>();
        // EventDescriptor (not GeneratedEventDescriptor) — should not match
        var eventDesc = new EventDescriptor
        {
            Id = "test-event",
            Name = "Test",
            Version = 1
        };

        var relationships = provider.GetRelationships(eventDesc);

        relationships.Should().BeEmpty();
    }

    private sealed class UnknownDescriptor : IDescriptor
    {
        public string Namespace => "unknown";
        public string Id => "x";
        public string Name => "Unknown";
        public DescriptorKind Kind => (DescriptorKind)999;
        public DescriptorState State => DescriptorState.Active;
        public string ContractHash => "";
        public string DefinitionHash => "";
        public string? SupersededById => null;
    }
}
