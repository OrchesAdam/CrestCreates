using System.Collections.Generic;
using System.Linq;
using CrestCreates.CodeGenerator.EventDescriptorGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.EventDescriptorGenerator;

/// <summary>
/// EventDescriptorSourceGenerator: [CrestEvent] declarations produce a static IEventDescriptorProvider
/// plus an explicit AddEventDescriptors() activation; Integration scope additionally requires the JSON
/// contract toolchain (CCCEV001); invalid declarations and duplicate identities fail at compile time.
/// </summary>
public class EventDescriptorSourceGeneratorTests
{
    private static readonly string[] References =
        ["CrestCreates.Event.Abstractions", "CrestCreates.Metadata.Abstractions",
         "Microsoft.Extensions.DependencyInjection.Abstractions"];

    private const string ToolchainStub = """
        namespace CrestCreates.Generated
        {
            internal static class EventJsonCodecContributions
            {
                public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddCodecs(
                    this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    => services;
            }
        }
        """;

    private static Dictionary<string, string> ToolchainOn() =>
        new() { ["build_property.CrestCreatesEventJsonContracts"] = "true" };

    private const string IntegrationEventSource = """
        using CrestCreates.Event.Abstractions;

        namespace MyApp.Orders;

        [CrestEvent(Name = "orders.order-created", Version = 1, Scope = EventScope.Integration,
            Importance = EventImportance.Business, RequiresIdempotency = true)]
        public sealed class OrderCreatedEvent
        {
            public string OrderId { get; set; } = string.Empty;
        }
        """;

    [Fact]
    public void Integration_event_with_toolchain_emits_provider_codec_call_and_compiles()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            IntegrationEventSource,
            additionalSources: [ToolchainStub],
            additionalReferences: References,
            globalAnalyzerOptions: ToolchainOn());

        result.GetErrors().Should().BeEmpty(Describe(result));
        result.CompilationSuccess.Should().BeTrue(Describe(result));

        var generated = result.GetSourceByFileName("EventDescriptorRegistration.g.cs")!.SourceText;
        generated.Should().Contain("internal sealed class TestCompilation_EventDescriptorProvider");
        generated.Should().Contain("Name = \"orders.order-created\"");
        generated.Should().Contain("Version = 1");
        generated.Should().Contain("PayloadType = typeof(global::MyApp.Orders.OrderCreatedEvent)");
        generated.Should().Contain("Scope = global::CrestCreates.Event.Abstractions.EventScope.Integration");
        generated.Should().Contain("Importance = global::CrestCreates.Event.Abstractions.EventImportance.Business");
        generated.Should().Contain("RequiresIdempotency = true");
        generated.Should().Contain("GeneratedEventDescriptor.GenerateId(\"orders.order-created\")");
        generated.Should().Contain("public static class EventRegistrations");
        generated.Should().Contain("IEventDescriptorProvider");
        generated.Should().Contain("EventJsonCodecContributions.AddCodecs(services);");
    }

    [Fact]
    public void Integration_event_without_toolchain_reports_CCCEV001()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            IntegrationEventSource,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCEV001" && d.GetMessage().Contains("orders.order-created"));
        result.GetErrors().Should().NotBeEmpty();
    }

    [Fact]
    public void Explicit_id_is_not_recomputed()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            """
            using CrestCreates.Event.Abstractions;

            namespace MyApp;

            [CrestEvent(Name = "orders.stable", Id = "evt_custom_stable", Version = 2, Scope = EventScope.Domain)]
            public sealed class StableEvent { }
            """,
            additionalReferences: References);

        var generated = result.GetSourceByFileName("EventDescriptorRegistration.g.cs")!.SourceText;
        generated.Should().Contain("Id = \"evt_custom_stable\"");
        generated.Should().Contain("Version = 2");
        generated.Should().Contain("Scope = global::CrestCreates.Event.Abstractions.EventScope.Domain");
        generated.Should().NotContain("GenerateId");
        generated.Should().NotContain("EventJsonCodecContributions.AddCodecs(services);");
    }

    [Fact]
    public void Missing_scope_reports_CCCEV002()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            """
            using CrestCreates.Event.Abstractions;

            namespace MyApp;

            [CrestEvent(Name = "orders.no-scope")]
            public sealed class NoScopeEvent { }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCEV002" && d.GetMessage().Contains("Scope"));
    }

    [Fact]
    public void Empty_name_reports_CCCEV002()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            """
            using CrestCreates.Event.Abstractions;

            namespace MyApp;

            [CrestEvent(Scope = EventScope.Integration)]
            public sealed class NoNameEvent { }
            """,
            additionalReferences: References,
            globalAnalyzerOptions: ToolchainOn());

        result.Diagnostics.Should().Contain(d => d.Id == "CCCEV002" && d.GetMessage().Contains("Name"));
    }

    [Fact]
    public void Abstract_declaration_reports_CCCEV002()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            """
            using CrestCreates.Event.Abstractions;

            namespace MyApp;

            [CrestEvent(Name = "orders.abstract", Scope = EventScope.Integration)]
            public abstract class AbstractEvent { }
            """,
            additionalReferences: References,
            globalAnalyzerOptions: ToolchainOn());

        result.Diagnostics.Should().Contain(d => d.Id == "CCCEV002" && d.GetMessage().Contains("non-abstract"));
    }

    [Fact]
    public void Duplicate_name_version_reports_CCCEV003()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            """
            using CrestCreates.Event.Abstractions;

            namespace MyApp;

            [CrestEvent(Name = "orders.dup", Version = 1, Scope = EventScope.Domain)]
            public sealed class FirstDup { }

            [CrestEvent(Name = "orders.dup", Version = 1, Scope = EventScope.Domain)]
            public sealed class SecondDup { }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCEV003" && d.GetMessage().Contains("orders.dup"));
    }

    [Fact]
    public void Duplicate_explicit_id_reports_CCCEV003()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            """
            using CrestCreates.Event.Abstractions;

            namespace MyApp;

            [CrestEvent(Name = "orders.one", Id = "evt_same", Scope = EventScope.Domain)]
            public sealed class OneEvent { }

            [CrestEvent(Name = "orders.two", Id = "evt_same", Scope = EventScope.Domain)]
            public sealed class TwoEvent { }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().Contain(d => d.Id == "CCCEV003" && d.GetMessage().Contains("evt_same"));
    }

    [Fact]
    public void Non_integration_scope_without_toolchain_has_no_errors()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<EventDescriptorSourceGenerator>(
            """
            using CrestCreates.Event.Abstractions;

            namespace MyApp;

            [CrestEvent(Name = "domain.only", Scope = EventScope.Domain)]
            public sealed class DomainOnlyEvent { }
            """,
            additionalReferences: References);

        result.Diagnostics.Should().NotContain(d => d.Id == "CCCEV001");
        result.GetSourceByFileName("EventDescriptorRegistration.g.cs")!.SourceText
            .Should().Contain("Name = \"domain.only\"");
    }

    private static string Describe(SourceGeneratorResult result)
        => string.Join("\n", result.Diagnostics.Select(d => d.ToString()));
}
