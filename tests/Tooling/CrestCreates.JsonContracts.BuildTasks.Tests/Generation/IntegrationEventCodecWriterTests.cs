using CrestCreates.JsonContracts.BuildTasks.Generation;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Generation;

/// <summary>
/// Integration event JSON contract pipeline: [CrestEvent(Scope = EventScope.Integration)] declarations
/// become source-generated codec roots + a static EventJsonCodecRegistry contribution, sharing the
/// same closure validation as replayable local events (never requiring [LocalEvent]).
/// </summary>
public class IntegrationEventCodecWriterTests : JsonContractCompilationTestBase
{
    private const string CrestEventStub = """
        namespace CrestCreates.Event.Abstractions;

        public enum EventScope { Local, Domain, Integration }

        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class CrestEventAttribute : System.Attribute
        {
            public string Name { get; init; } = string.Empty;
            public int Version { get; init; } = 1;
            public EventScope Scope { get; init; }
        }
        """;

    [Fact]
    public void Write_EmitsIntegrationContextAndContribution_WithoutLocalEvent()
    {
        var result = JsonContractTestHelper.BuildModel("TestAssembly",
        [
            ("Stub.cs", CrestEventStub),
            ("Events.cs", """
                using CrestCreates.Event.Abstractions;

                namespace MyApp;

                [CrestEvent(Name = "orders.order-created", Scope = EventScope.Integration)]
                public sealed class OrderCreatedEvent
                {
                    public string OrderId { get; set; } = string.Empty;
                    public System.Collections.Generic.List<string> Tags { get; set; } = new();
                }
                """),
        ], GetDefaultReferences());

        var discovery = new IntegrationEventJsonSurfaceDiscovery();
        result.Model!.IntegrationEventRoots.AddRange(discovery.Discover(result.Compilation));
        discovery.Diagnostics.Should().BeEmpty();

        var output = JsonContractSourceWriter.WriteContextSource(result.Model!);

        output.Should().Contain("[global::System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = global::System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]");
        output.Should().Contain("internal sealed partial class EventJsonSerializerContext");
        output.Should().Contain("internal static class EventJsonCodecContributions");
        output.Should().Contain("CrestCreates.Event.Abstractions.EventJsonCodecRegistry.Register(");
        output.Should().Contain("typeof(global::MyApp.OrderCreatedEvent)");
        output.Should().Contain("static () => EventJsonSerializerContext.Default.GetTypeInfo(typeof(global::MyApp.OrderCreatedEvent)));");
        output.Should().NotContain("LocalEventJsonSerializerContext");
        output.Should().NotContain("LocalEventJsonCodecContributions");
    }

    [Fact]
    public void Discovery_Ignores_NonIntegrationScopes()
    {
        var result = JsonContractTestHelper.BuildModel("TestAssembly",
        [
            ("Stub.cs", CrestEventStub),
            ("Events.cs", """
                using CrestCreates.Event.Abstractions;

                namespace MyApp;

                [CrestEvent(Name = "local.only", Scope = EventScope.Local)]
                public sealed class LocalOnlyEvent { }

                [CrestEvent(Name = "domain.only", Scope = EventScope.Domain)]
                public sealed class DomainOnlyEvent { }
                """),
        ], GetDefaultReferences());

        var discovery = new IntegrationEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(result.Compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Discovery_Reports_UnsealedMember_As_CJC020()
    {
        var result = JsonContractTestHelper.BuildModel("TestAssembly",
        [
            ("Stub.cs", CrestEventStub),
            ("Events.cs", """
                using CrestCreates.Event.Abstractions;

                namespace MyApp;

                [CrestEvent(Name = "orders.open", Scope = EventScope.Integration)]
                public sealed class OpenPayloadEvent
                {
                    public object Anything { get; set; } = new();
                }
                """),
        ], GetDefaultReferences());

        var discovery = new IntegrationEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(result.Compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().ContainSingle(d => d.Id == "CJC020");
    }

    [Fact]
    public void Discovery_Reports_AbstractPayload_As_CJC018()
    {
        var result = JsonContractTestHelper.BuildModel("TestAssembly",
        [
            ("Stub.cs", CrestEventStub),
            ("Events.cs", """
                using CrestCreates.Event.Abstractions;

                namespace MyApp;

                [CrestEvent(Name = "orders.abstract", Scope = EventScope.Integration)]
                public abstract class AbstractWireEvent { }
                """),
        ], GetDefaultReferences());

        var discovery = new IntegrationEventJsonSurfaceDiscovery();
        var roots = discovery.Discover(result.Compilation);

        roots.Should().BeEmpty();
        discovery.Diagnostics.Should().ContainSingle(d => d.Id == "CJC018");
    }

    [Fact]
    public void Context_source_with_only_integration_roots_still_emits()
    {
        var result = JsonContractTestHelper.BuildModel("TestAssembly",
        [
            ("Stub.cs", CrestEventStub),
            ("Events.cs", """
                using CrestCreates.Event.Abstractions;

                namespace MyApp;

                [CrestEvent(Name = "orders.simple", Scope = EventScope.Integration)]
                public sealed class SimpleEvent { }
                """),
        ], GetDefaultReferences());

        var discovery = new IntegrationEventJsonSurfaceDiscovery();
        result.Model!.IntegrationEventRoots.AddRange(discovery.Discover(result.Compilation));

        var output = JsonContractSourceWriter.WriteContextSource(result.Model!);
        output.Should().Contain("EventJsonSerializerContext");
        output.Should().Contain("typeof(global::MyApp.SimpleEvent)");
    }
}
