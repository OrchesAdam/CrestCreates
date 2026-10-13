using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Semantic;

public class LocalEventJsonSurfaceDiscoveryTests : JsonContractCompilationTestBase
{
    private const string LocalEventStub = """
        namespace CrestCreates.EventBus.Abstractions;

        [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
        public sealed class LocalEventAttribute : System.Attribute
        {
            public int Version { get; set; } = 1;
            public bool Replayable { get; set; }
        }
        """;

    [Fact]
    public void Discover_ReplayableEvents_ProducesSortedRoots()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class ZetaEvent { }

                [LocalEvent(Replayable = true, Version = 3)]
                public sealed class AlphaEvent { }

                [LocalEvent]
                public sealed class NotReplayableEvent { }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().HaveCount(2);
        roots[0].FullMetadataName.Should().Contain("AlphaEvent");
        roots[1].FullMetadataName.Should().Contain("ZetaEvent");
        roots.Should().OnlyContain(root => root.Provenance.Origins.Single().SourceKind == JsonContractRootSourceKind.LocalEvent);
    }

    [Fact]
    public void Discover_AbstractReplayable_Reports_CJC015()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public abstract class AbstractEvent { }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CJC015");
    }

    [Fact]
    public void Discover_WithoutEventingReference_ReturnsEmptyWithoutDiagnostics()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Plain.cs", "namespace MyApp; public sealed class Plain { }"),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Discover_PrivateNestedReplayable_Reports_CJC016()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                public sealed class Holder
                {
                    [LocalEvent(Replayable = true)]
                    private sealed class HiddenEvent { }
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CJC016");
    }

    [Fact]
    public void Discover_ObjectMember_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class OpenPayloadEvent
                {
                    public object Payload { get; set; } = new object();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Payload"));
    }

    [Fact]
    public void Discover_InterfaceMember_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                public interface IPayload { }

                [LocalEvent(Replayable = true)]
                public sealed class InterfacePayloadEvent
                {
                    public IPayload Payload { get; set; } = null!;
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("interface"));
    }

    [Fact]
    public void Discover_AbstractMemberWithoutDerivedTypes_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                public abstract class BasePayload { }

                [LocalEvent(Replayable = true)]
                public sealed class AbstractPayloadEvent
                {
                    public BasePayload Payload { get; set; } = null!;
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("abstract"));
    }

    [Fact]
    public void Discover_CollectionOfObjectElements_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class ListPayloadEvent
                {
                    public System.Collections.Generic.List<object> Payload { get; set; } = new();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Payload[]"));
    }

    [Fact]
    public void Discover_InheritedUnsealedMember_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                public class BasePayload
                {
                    public object Value { get; set; } = new object();
                }

                public sealed class DerivedPayload : BasePayload { }

                [LocalEvent(Replayable = true)]
                public sealed class InheritedPayloadEvent
                {
                    public DerivedPayload Payload { get; set; } = new();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Value"));
    }

    [Fact]
    public void Discover_JsonIgnoredUnsealedMember_IsAllowed()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class IgnoredPayloadEvent
                {
                    [JsonIgnore]
                    public object Ignored { get; set; } = new object();

                    public string Kept { get; set; } = string.Empty;
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("IgnoredPayloadEvent"));
    }

    [Fact]
    public void Discover_ClosedMembers_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                public enum Status { Active, Inactive }

                public sealed class ClosedPayload
                {
                    public string Name { get; set; } = string.Empty;
                    public int Count { get; set; }
                }

                [LocalEvent(Replayable = true)]
                public sealed class ClosedEvent
                {
                    public ClosedPayload Payload { get; set; } = new();
                    public System.Collections.Generic.List<ClosedPayload> Items { get; set; } = new();
                    public System.DateTime When { get; set; }
                    public Status State { get; set; }
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("ClosedEvent"));
    }

    [Fact]
    public void Discover_ClosedJsonDerivedTypeHierarchy_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
                [JsonDerivedType(typeof(CatPayload), "cat")]
                public abstract class AnimalPayload { }

                public sealed class CatPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                [LocalEvent(Replayable = true)]
                public sealed class PolymorphicEvent
                {
                    public AnimalPayload Payload { get; set; } = new CatPayload();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("PolymorphicEvent"));
    }

    [Fact]
    public void Discover_JsonDerivedTypeHierarchy_WithUnsealedDerivedMember_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
                [JsonDerivedType(typeof(CatPayload), "cat")]
                public abstract class AnimalPayload { }

                public sealed class CatPayload : AnimalPayload
                {
                    public object LooseValue { get; set; } = new object();
                }

                [LocalEvent(Replayable = true)]
                public sealed class PolymorphicEvent
                {
                    public AnimalPayload Payload { get; set; } = new CatPayload();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("LooseValue"));
    }

    [Fact]
    public void Discover_JsonIgnoreNever_OnObjectMember_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class ConditionalIgnoreEvent
                {
                    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
                    public object Payload { get; set; } = new object();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Payload"));
    }

    [Fact]
    public void Discover_JsonIgnoreWhenWritingNull_OnObjectMember_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class ConditionalIgnoreEvent
                {
                    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                    public object? Payload { get; set; }
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Payload"));
    }

    [Fact]
    public void Discover_JsonIgnoreWhenWritingDefault_OnObjectMember_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class ConditionalIgnoreEvent
                {
                    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
                    public object? Payload { get; set; }
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Payload"));
    }

    [Fact]
    public void Discover_PolymorphicWithValidDiscriminator_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
                [JsonDerivedType(typeof(CatPayload), "cat")]
                public abstract class AnimalPayload { }

                public sealed class CatPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                [LocalEvent(Replayable = true)]
                public sealed class PolymorphicEvent
                {
                    public AnimalPayload Payload { get; set; } = new CatPayload();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("PolymorphicEvent"));
    }

    [Fact]
    public void Discover_PolymorphicWithoutDiscriminator_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [JsonDerivedType(typeof(CatPayload))]
                public abstract class AnimalPayload { }

                public sealed class CatPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                [LocalEvent(Replayable = true)]
                public sealed class PolymorphicEvent
                {
                    public AnimalPayload Payload { get; set; } = new CatPayload();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("discriminator"));
    }

    [Fact]
    public void Discover_JsonNodeMember_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Nodes;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class JsonNodeEvent
                {
                    public JsonNode Data { get; set; } = new JsonObject();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("JsonNodeEvent"));
    }

    [Fact]
    public void Discover_ReadOnlyListOfString_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class CollectionEvent
                {
                    public IReadOnlyList<string> Tags { get; set; } = new List<string>();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("CollectionEvent"));
    }

    [Fact]
    public void Discover_EnumerableOfInt_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class EnumerableEvent
                {
                    public IEnumerable<int> Numbers { get; set; } = new List<int>();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("EnumerableEvent"));
    }

    [Fact]
    public void Discover_DictionaryOfStringToInt_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class DictionaryEvent
                {
                    public IDictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("DictionaryEvent"));
    }

    [Fact]
    public void Discover_ReadOnlyListOfObject_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class OpenCollectionEvent
                {
                    public IReadOnlyList<object> Items { get; set; } = new List<object>();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Items[]"));
    }

    [Fact]
    public void Discover_JsonIgnoreExplicitAlways_OnObjectMember_IsAllowed()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class ExplicitAlwaysIgnoreEvent
                {
                    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
                    public object Ignored { get; set; } = new object();

                    public string Kept { get; set; } = string.Empty;
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("ExplicitAlwaysIgnoreEvent"));
    }

    [Fact]
    public void Discover_PolymorphicWithOnlyPropertyName_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
                [JsonDerivedType(typeof(CatPayload))]
                public abstract class AnimalPayload { }

                public sealed class CatPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                [LocalEvent(Replayable = true)]
                public sealed class PolymorphicEvent
                {
                    public AnimalPayload Payload { get; set; } = new CatPayload();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("discriminator"));
    }

    [Fact]
    public void Discover_PolymorphicWithMixedTags_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [JsonDerivedType(typeof(CatPayload), "cat")]
                [JsonDerivedType(typeof(DogPayload))]
                public abstract class AnimalPayload { }

                public sealed class CatPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                public sealed class DogPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                [LocalEvent(Replayable = true)]
                public sealed class PolymorphicEvent
                {
                    public AnimalPayload Payload { get; set; } = new CatPayload();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("discriminator"));
    }

    [Fact]
    public void Discover_CustomCollectionInterface_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                public interface ICustomCollection : IEnumerable<int> { }

                [LocalEvent(Replayable = true)]
                public sealed class CollectionEvent
                {
                    public ICustomCollection Items { get; set; } = null!;
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("interface"));
    }

    [Fact]
    public void Discover_DerivedListOfObject_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                public sealed class ObjectBag : List<object> { }

                [LocalEvent(Replayable = true)]
                public sealed class CollectionEvent
                {
                    public ObjectBag Items { get; set; } = new();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Items[]"));
    }

    [Fact]
    public void Discover_DerivedListOfString_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                public sealed class StringBag : List<string> { }

                [LocalEvent(Replayable = true)]
                public sealed class CollectionEvent
                {
                    public StringBag Items { get; set; } = new();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("CollectionEvent"));
    }

    [Fact]
    public void Discover_HashSetOfInt_ProducesRoot()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections.Generic;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class CollectionEvent
                {
                    public HashSet<int> Items { get; set; } = new();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        discovery.Diagnostics.Should().BeEmpty();
        roots.Should().ContainSingle(root => root.FullMetadataName.Contains("CollectionEvent"));
    }

    [Fact]
    public void Discover_DuplicatePolymorphicTags_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Text.Json.Serialization;

                namespace MyApp;

                [JsonDerivedType(typeof(CatPayload), "same")]
                [JsonDerivedType(typeof(DogPayload), "same")]
                public abstract class AnimalPayload { }

                public sealed class CatPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                public sealed class DogPayload : AnimalPayload
                {
                    public string Name { get; set; } = string.Empty;
                }

                [LocalEvent(Replayable = true)]
                public sealed class PolymorphicEvent
                {
                    public AnimalPayload Payload { get; set; } = new CatPayload();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("discriminator"));
    }

    [Fact]
    public void Discover_ConcreteEnumerableOnlyClass_Reports_CJC017()
    {
        var compilation = CreateCompilation("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;
                using System.Collections;
                using System.Collections.Generic;

                namespace MyApp;

                public sealed class EnumerableOnly : IEnumerable<int>
                {
                    public IEnumerator<int> GetEnumerator()
                        => ((IEnumerable<int>)new[] { 1, 2 }).GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }

                [LocalEvent(Replayable = true)]
                public sealed class ExampleEvent
                {
                    public EnumerableOnly Items { get; set; } = new();
                }
                """),
        ]);

        var discovery = new LocalEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CJC017" && diagnostic.Message.Contains("Items"));
    }
}
