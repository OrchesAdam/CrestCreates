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
}
