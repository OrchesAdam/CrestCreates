using System.Linq;
using CrestCreates.CodeGenerator.OutboxConsumerActivationGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.OutboxConsumerActivationGenerator;

/// <summary>
/// Regression and positive tests for OutboxConsumerActivationSourceGenerator.
/// Covers PR #119 review findings R1-R4 and Spec §5 diagnostic matrix.
/// </summary>
public sealed class OutboxConsumerActivationGeneratorTests
{
    /// <summary>
    /// Minimal inline declarations of the framework contracts so tests
    /// do not require a project reference to Delivery.Abstractions.
    /// </summary>
    private const string FrameworkContracts = @"
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Runtime.Delivery.Abstractions.Activation
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GenerateOutboxConsumerActivationAttribute : Attribute { }

    public interface IOutboxConsumerActivation<TSelf>
        where TSelf : class, IOutboxConsumerActivation<TSelf>
    {
        static abstract TSelf CreateOutboxConsumer(IServiceProvider services);
    }
}

namespace CrestCreates.Runtime.Delivery.Abstractions.Handlers
{
    public readonly struct OutboxRequiredConsumerResult
    {
        public bool IsAck { get; init; }
        public static OutboxRequiredConsumerResult Ack() => new() { IsAck = true };
    }

    public readonly struct OutboxDeliveryContext { }

    public interface IOutboxRequiredConsumer<in TPayload>
    {
        string ConsumerId { get; }
        ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(TPayload payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default);
    }
}
";

    // ──────────────────────────────────────────────────────────────
    // R1 fix — hintName uses fully-qualified type identity + dedup
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void R1_TwoNamespacesSameTypeName_Should_ProduceTwoDistinctOutputsAndCompile()
    {
        var source = FrameworkContracts + @"
namespace NamespaceA
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class Consumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""consumer-a"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}

namespace NamespaceB
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class Consumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""consumer-b"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.GeneratedSources.Should().HaveCount(2, "two distinct consumers in different namespaces must produce two outputs");
        result.CompilationSuccess.Should().BeTrue("generated code must compile without CS8785 or other errors");
        result.Diagnostics.Where(d => d.Id == "CS8785").Should().BeEmpty("no generator failure diagnostics");
    }

    [Fact]
    public void R1_TwoAttributedPartialsOfSameType_Should_ProduceOneOutputAndCompile()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class Consumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""consumer-1"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }

    [System.Obsolete(""second partial with attribute"")]
    public sealed partial class Consumer
    {
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.GeneratedSources.Should().HaveCount(1, "same type from two partials must deduplicate to one output");
        result.CompilationSuccess.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────
    // R2 fix — generator adds activation interface to base list
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void R2_MarkerOnlyConsumer_Should_GenerateActivationInterfaceAndCompile()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class Consumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""consumer-1"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.CompilationSuccess.Should().BeTrue("marker-only consumer must compile without manually declaring IOutboxConsumerActivation<T>");
        result.Diagnostics.Where(d => d.Id == "CCOCA007").Should().BeEmpty("CCOCA007 must not fire for valid marker-only consumer");
        result.GeneratedSources.Should().HaveCount(1);
        result.GeneratedSources[0].SourceText.Should().Contain("IOutboxConsumerActivation",
            "generated partial must implement activation interface");
    }

    [Fact]
    public void R2_GeneratedOutput_Should_ContainExplicitInterfaceImplementation()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class MyConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""my-consumer"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.CompilationSuccess.Should().BeTrue();
        var generated = result.GeneratedSources[0].SourceText;
        generated.Should().Contain("global::CrestCreates.Runtime.Delivery.Abstractions.Activation.IOutboxConsumerActivation<");
        generated.Should().Contain("CreateOutboxConsumer");
        generated.Should().Contain("new global::TestNs.MyConsumer(");
    }

    // ──────────────────────────────────────────────────────────────
    // R3 fix — attribute matching uses exact metadata name
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void R3_ExternalSameNamedAttribute_Should_ProduceZeroOutputAndZeroDiagnostics()
    {
        var source = @"
using System;

namespace Other.Library
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class GenerateOutboxConsumerActivationAttribute : Attribute { }
}

namespace Other.Library.Consumers
{
    [Other.Library.GenerateOutboxConsumerActivation]
    public sealed partial class Ordinary
    {
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.GeneratedSources.Should().BeEmpty("external attribute must not trigger generator");
        result.Diagnostics.Where(d => d.Id.StartsWith("CCOCA")).Should().BeEmpty(
            "no CrestCreates diagnostics for unrelated types");
    }

    // ──────────────────────────────────────────────────────────────
    // R4 fix — global namespace handling
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void R4_GlobalNamespaceConsumer_Should_CompileWithoutNamespaceSyntaxError()
    {
        var source = FrameworkContracts + @"
public class SomeEvent { }

[CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
public sealed partial class GlobalConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
{
    public string ConsumerId => ""global-consumer"";
    public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
        => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.CompilationSuccess.Should().BeTrue("global namespace consumer must compile without CS1001/CS1514");
        result.Diagnostics.Where(d => d.Id == "CS1001" || d.Id == "CS1514").Should().BeEmpty();
        result.GeneratedSources.Should().HaveCount(1);
        result.GeneratedSources[0].SourceText.Should().NotContain("namespace <global namespace>",
            "must not output placeholder text for global namespace");
    }

    // ──────────────────────────────────────────────────────────────
    // Positive — various valid consumer shapes
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Positive_ZeroParameterConstructor_Should_GenerateParameterlessNew()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class NoDepsConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""no-deps"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.CompilationSuccess.Should().BeTrue();
        result.GeneratedSources.Should().HaveCount(1);
        result.GeneratedSources[0].SourceText.Should().Contain("new global::TestNs.NoDepsConsumer(");
    }

    [Fact]
    public void Positive_MultipleDependencies_Should_GenerateGetRequiredServiceForEach()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }
    public interface IDependencyA { }
    public interface IDependencyB { }
    public interface IDependencyC { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class MultiDepConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public MultiDepConsumer(IDependencyA a, IDependencyB b, IDependencyC c) { }
        public string ConsumerId => ""multi-dep"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.CompilationSuccess.Should().BeTrue();
        var generated = result.GeneratedSources[0].SourceText;
        generated.Should().Contain("GetRequiredService<global::TestNs.IDependencyA>(services)");
        generated.Should().Contain("GetRequiredService<global::TestNs.IDependencyB>(services)");
        generated.Should().Contain("GetRequiredService<global::TestNs.IDependencyC>(services)");
    }

    [Fact]
    public void Positive_InternalConsumer_Should_GenerateInternalPartial()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    internal sealed partial class InternalConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""internal"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.CompilationSuccess.Should().BeTrue();
        result.GeneratedSources.Should().HaveCount(1);
        result.GeneratedSources[0].SourceText.Should().Contain("internal partial class InternalConsumer");
    }

    // ──────────────────────────────────────────────────────────────
    // Diagnostics — CCOCA001-007
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void CCOCA001_NonPartialClass_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed class NonPartialConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""x"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void CCOCA001_RecordType_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed record RecordConsumer() : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""x"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void CCOCA001_AbstractClass_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public abstract partial class AbstractConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""x"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void CCOCA002_DoesNotImplementRequiredConsumer_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class NoInterfaceConsumer
    {
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA002");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void CCOCA003_MultiplePublicConstructors_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }
    public interface IDependency { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class MultiCtorConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public MultiCtorConsumer() { }
        public MultiCtorConsumer(IDependency dep) { }
        public string ConsumerId => ""x"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA003");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void CCOCA004_IServiceProviderParameter_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class ServiceProviderConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public ServiceProviderConsumer(System.IServiceProvider sp) { }
        public string ConsumerId => ""x"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA004");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void CCOCA004_IServiceScopeFactoryParameter_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class ScopeFactoryConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public ScopeFactoryConsumer(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory factory) { }
        public string ConsumerId => ""x"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA004");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void CCOCA006_HandWrittenCreateOutboxConsumer_Should_ReportError()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    [CrestCreates.Runtime.Delivery.Abstractions.Activation.GenerateOutboxConsumerActivation]
    public sealed partial class HandWrittenConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""x"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());

        public static HandWrittenConsumer CreateOutboxConsumer(System.IServiceProvider services) => new();
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA006");
        result.GeneratedSources.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // No marker — zero output
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NoMarker_Should_ProduceZeroOutput()
    {
        var source = FrameworkContracts + @"
namespace TestNs
{
    public class SomeEvent { }

    public sealed partial class UnmarkedConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<SomeEvent>
    {
        public string ConsumerId => ""unmarked"";
        public System.Threading.Tasks.ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(SomeEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, System.Threading.CancellationToken ct = default)
            => new(CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Ack());
    }
}
";

        var result = SourceGeneratorTestHelper.RunGenerator<OutboxConsumerActivationSourceGenerator>(source);

        result.GeneratedSources.Should().BeEmpty();
        result.Diagnostics.Where(d => d.Id.StartsWith("CCOCA")).Should().BeEmpty();
    }
}
