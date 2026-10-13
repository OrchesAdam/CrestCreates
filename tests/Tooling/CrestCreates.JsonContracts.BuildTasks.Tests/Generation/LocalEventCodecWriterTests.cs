using CrestCreates.JsonContracts.BuildTasks.Generation;
using CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Generation;

public class LocalEventCodecWriterTests : JsonContractCompilationTestBase
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
    public void Write_EmitsCodecContextAndContribution()
    {
        var result = JsonContractTestHelper.BuildModel("TestAssembly",
        [
            ("Stub.cs", LocalEventStub),
            ("Events.cs", """
                using CrestCreates.EventBus.Abstractions;

                namespace MyApp;

                [LocalEvent(Replayable = true)]
                public sealed class ReplayableSalesEvent
                {
                    public string OrderId { get; set; } = string.Empty;
                }
                """),
        ], GetDefaultReferences());

        var discovery = new LocalEventJsonSurfaceDiscovery();
        result.Model!.LocalEventRoots.AddRange(discovery.Discover(result.Compilation));

        var output = JsonContractSourceWriter.WriteContextSource(result.Model);

        output.Should().Contain("namespace CrestCreates.Generated");
        output.Should().Contain("[global::System.Text.Json.Serialization.JsonSerializable(typeof(global::MyApp.ReplayableSalesEvent))]");
        output.Should().Contain("internal sealed partial class LocalEventJsonSerializerContext : global::System.Text.Json.Serialization.JsonSerializerContext");
        output.Should().Contain("internal static class LocalEventJsonCodecContributions");
        output.Should().Contain("CrestCreates.EventBus.Abstractions.LocalEventJsonCodecRegistry.Register(");
        output.Should().Contain("static () => LocalEventJsonSerializerContext.Default.GetTypeInfo(typeof(global::MyApp.ReplayableSalesEvent)));");
    }

    [Fact]
    public void Write_WithoutLocalEvents_DoesNotEmitCodecBlock()
    {
        var result = JsonContractTestHelper.BuildModel("TestAssembly",
        [
            ("Plain.cs", "namespace MyApp; public sealed class Plain { }"),
        ], GetDefaultReferences());

        var output = JsonContractSourceWriter.WriteContextSource(result.Model!);

        output.Should().NotContain("LocalEventJsonSerializerContext");
        output.Should().NotContain("LocalEventJsonCodecContributions");
    }
}
