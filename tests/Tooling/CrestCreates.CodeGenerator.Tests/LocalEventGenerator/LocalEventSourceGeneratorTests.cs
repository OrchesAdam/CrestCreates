using System.Collections.Generic;
using System.Linq;
using CrestCreates.CodeGenerator.LocalEventGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.LocalEventGenerator;

public class LocalEventSourceGeneratorTests
{
    private static readonly string[] EventingReferences = ["CrestCreates.EventBus.Abstractions", "System.Text.Json"];

    private const string HandlerSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using CrestCreates.EventBus.Abstractions;

        namespace MyApp;

        public sealed class OrderPlacedEvent : ILocalEvent
        {
            public string OrderId { get; set; } = string.Empty;
        }

        public sealed class OrderPlacedHandler : ILocalEventHandler<OrderPlacedEvent>
        {
            public Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
        """;

    [Fact]
    public void Handler_Produces_Typed_Binding_And_Explicit_Activation()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            HandlerSource, additionalReferences: EventingReferences);

        result.CompilationSuccess.Should().BeTrue(Describe(result));
        result.GetErrors().Should().BeEmpty();

        var generated = result.GetSourceByFileName("LocalEventRegistration.g.cs")!;
        generated.SourceText.Should().Contain("TestCompilation_LocalEventContributor");
        generated.SourceText.Should().Contain("global::MyApp.OrderPlacedEvent");
        generated.SourceText.Should().Contain("global::MyApp.OrderPlacedHandler");
        generated.SourceText.Should().Contain("LocalEventInvoker<global::MyApp.OrderPlacedEvent>");
        generated.SourceText.Should().Contain("LocalEventDispatch.InvokeHandlersAsync");
        generated.SourceText.Should().Contain("_expectedHandlers0");
        generated.SourceText.Should().Contain("namespace CrestCreates.Generated.TestCompilation");
        generated.SourceText.Should().Contain("public static class LocalEventRegistrations");
        generated.SourceText.Should().Contain("IServiceCollection services");
        generated.SourceText.Should().Contain("TryAddEnumerable");
        generated.SourceText.Should().Contain("ILocalEventRegistrationContributor");
    }

    [Fact]
    public void Handler_Implementing_Two_Events_Generates_Two_Bindings()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.EventBus.Abstractions;

            namespace MyApp;

            public sealed class FirstEvent : ILocalEvent { }
            public sealed class SecondEvent : ILocalEvent { }

            public sealed class MultiHandler : ILocalEventHandler<FirstEvent>, ILocalEventHandler<SecondEvent>
            {
                public Task HandleAsync(FirstEvent @event, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;

                public Task HandleAsync(SecondEvent @event, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            source, additionalReferences: EventingReferences);

        result.CompilationSuccess.Should().BeTrue(Describe(result));
        var generated = result.GetSourceByFileName("LocalEventRegistration.g.cs")!;
        generated.SourceText.Split("builder.Add(").Length.Should().Be(3);
        generated.SourceText.Should().Contain("global::MyApp.FirstEvent");
        generated.SourceText.Should().Contain("global::MyApp.SecondEvent");
    }

    [Fact]
    public void DeclarationOnly_Event_Produces_Empty_Expected_Handler_Binding()
    {
        var source = """
            using CrestCreates.EventBus.Abstractions;

            namespace MyApp;

            [LocalEvent]
            public sealed class AuditExpectedEvent : ILocalEvent { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            source, additionalReferences: EventingReferences);

        result.CompilationSuccess.Should().BeTrue(Describe(result));
        var generated = result.GetSourceByFileName("LocalEventRegistration.g.cs")!;
        generated.SourceText.Should().Contain("global::MyApp.AuditExpectedEvent");
        generated.SourceText.Should().Contain("_expectedHandlers0");
        generated.SourceText.Should().Contain("global::System.Type[] {  }");
    }

    [Fact]
    public void Replayable_Without_Toolchain_Reports_CCCLE004_Error()
    {
        var source = """
            using CrestCreates.EventBus.Abstractions;

            namespace MyApp;

            [LocalEvent(Replayable = true)]
            public sealed class ReplayableEvent : ILocalEvent
            {
                public string Data { get; set; } = string.Empty;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            source, additionalReferences: EventingReferences);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "CCCLE004" && diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Replayable_With_Toolchain_Emits_Codec_Contribution_Call()
    {
        var source = """
            using CrestCreates.EventBus.Abstractions;

            namespace MyApp;

            [LocalEvent(Replayable = true, Version = 4)]
            public sealed class ReplayableEvent : ILocalEvent
            {
                public string Data { get; set; } = string.Empty;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            source,
            additionalReferences: EventingReferences,
            globalAnalyzerOptions: new Dictionary<string, string>
            {
                ["build_property.CrestCreatesLocalEventJsonContracts"] = "true",
            });

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CCCLE004");
        var generated = result.GetSourceByFileName("LocalEventRegistration.g.cs")!;
        generated.SourceText.Should().Contain("LocalEventJsonCodecContributions.Register();");
        generated.SourceText.Should().Contain("LocalEventJsonCodecRegistry.Resolve(typeof(global::MyApp.ReplayableEvent))");
        generated.SourceText.Should().Contain("\n                4,\n                true,");
    }

    [Fact]
    public void OpenGeneric_Handler_Reports_CCCLE001()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.EventBus.Abstractions;

            namespace MyApp;

            public sealed class SomeEvent : ILocalEvent { }

            public sealed class OpenGenericHandler<TEvent> : ILocalEventHandler<SomeEvent> where TEvent : ILocalEvent
            {
                public Task HandleAsync(SomeEvent @event, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            source, additionalReferences: EventingReferences);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CCCLE001");
    }

    [Fact]
    public void Inaccessible_Nested_Handler_Reports_CCCLE003()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using CrestCreates.EventBus.Abstractions;

            namespace MyApp;

            public sealed class SomeEvent : ILocalEvent { }

            public sealed class SomeEventHandler
            {
                private sealed class InnerHandler : ILocalEventHandler<SomeEvent>
                {
                    public Task HandleAsync(SomeEvent @event, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            source, additionalReferences: EventingReferences);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CCCLE003");
    }

    [Fact]
    public void Abstract_Replayable_Event_Reports_CCCLE005()
    {
        var source = """
            using CrestCreates.EventBus.Abstractions;

            namespace MyApp;

            [LocalEvent(Replayable = true)]
            public abstract class AbstractReplayableEvent : ILocalEvent { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            source,
            additionalReferences: EventingReferences,
            globalAnalyzerOptions: new Dictionary<string, string>
            {
                ["build_property.CrestCreatesLocalEventJsonContracts"] = "true",
            });

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CCCLE005");
    }

    [Fact]
    public void NoLocalEventCode_Produces_No_Source()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            "namespace MyApp; public sealed class Plain { }",
            additionalReferences: EventingReferences);

        result.GetSourceByFileName("LocalEventRegistration.g.cs").Should().BeNull();
    }

    [Fact]
    public void Generated_Output_Is_Deterministic()
    {
        var first = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            HandlerSource, additionalReferences: EventingReferences);
        var second = SourceGeneratorTestHelper.RunGenerator<LocalEventSourceGenerator>(
            HandlerSource, additionalReferences: EventingReferences);

        var firstContent = first.GetSourceByFileName("LocalEventRegistration.g.cs")!.SourceText;
        var secondContent = second.GetSourceByFileName("LocalEventRegistration.g.cs")!.SourceText;

        firstContent.Should().Be(secondContent);
    }

    private static string Describe(SourceGeneratorResult result)
        => string.Join("\n", result.GetErrors().Select(error => error.ToString()));
}
