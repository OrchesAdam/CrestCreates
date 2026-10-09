using System.Linq;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.OutboxConsumerActivationGenerator;

using GeneratorType = global::CrestCreates.CodeGenerator.OutboxConsumerActivationGenerator.OutboxConsumerActivationGenerator;

public sealed class OutboxConsumerActivationDiagnosticTests
{
    private const string MarkerAttributeStub = @"
using System;
namespace CrestCreates.Runtime.Delivery.Abstractions.Activation
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GenerateOutboxConsumerActivationAttribute : Attribute { }
}
";

    private const string ConsumerInterfaceStub = @"
namespace CrestCreates.Runtime.Delivery.Abstractions.Handlers
{
    public interface IOutboxRequiredConsumer<in TPayload>
    {
        string ConsumerId { get; }
    }
}
";

    private const string ServiceProviderStub = @"
namespace System
{
    public interface IServiceProvider
    {
        object? GetService(System.Type serviceType);
    }
}
";

    private const string ActivationInterfaceStub = @"
namespace CrestCreates.Runtime.Delivery.Abstractions.Activation
{
    public interface IOutboxConsumerActivation<TSelf> where TSelf : class, IOutboxConsumerActivation<TSelf>
    {
        static abstract TSelf CreateOutboxConsumer(System.IServiceProvider services);
    }
}
";

    private static string ConsumerWithBody(string classModifiers, string classBody) => $@"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
{classModifiers} class TestConsumer : IOutboxRequiredConsumer<string>
{{
    public string ConsumerId => ""test"";
    {classBody}
}}
";

    #region CCOCA001 -- Non-partial or unsupported type shape

    [Fact]
    public void CCOCA001_NonPartialClass_Should_ReportError()
    {
        var source = ConsumerWithBody("public sealed", "");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CCOCA001_RecordType_Should_ReportError()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
public sealed record TestConsumer() : IOutboxRequiredConsumer<string>
{
    public string ConsumerId => ""test"";
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CCOCA001_AbstractClass_Should_ReportError()
    {
        var source = ConsumerWithBody("public abstract partial", "");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CCOCA001_OpenGenericType_Should_ReportError()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
public partial class TestConsumer<T> : IOutboxRequiredConsumer<string>
{
    public string ConsumerId => ""test"";
    public TestConsumer() { }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CCOCA001_NestedType_Should_ReportError()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

public class Outer
{
    [GenerateOutboxConsumerActivation]
    public sealed partial class TestConsumer : IOutboxRequiredConsumer<string>
    {
        public string ConsumerId => ""test"";
        public TestConsumer() { }
    }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA001" && d.Severity == DiagnosticSeverity.Error);
    }

    #endregion

    #region CCOCA002 -- Does not implement IOutboxRequiredConsumer

    [Fact]
    public void CCOCA002_MarkedTypeWithoutRequiredConsumerInterface_Should_ReportError()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;

[GenerateOutboxConsumerActivation]
public sealed partial class TestConsumer
{
    public TestConsumer() { }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA002" && d.Severity == DiagnosticSeverity.Error);
    }

    #endregion

    #region CCOCA003 -- Zero or multiple public constructors

    [Fact]
    public void CCOCA003_MultiplePublicConstructors_Should_ReportError()
    {
        var source = ConsumerWithBody("public sealed partial", @"
public TestConsumer() { }
public TestConsumer(string x) { }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CCOCA003_NoPublicConstructor_OnlyInternal_Should_ReportError()
    {
        var source = ConsumerWithBody("public sealed partial", @"
internal TestConsumer() { }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA003" && d.Severity == DiagnosticSeverity.Error);
    }

    #endregion

    #region CCOCA004 -- Unsupported parameter types

    [Fact]
    public void CCOCA004_ConstructorReceivingIServiceProvider_Should_ReportError()
    {
        var source = ConsumerWithBody("public sealed partial", @"
public TestConsumer(System.IServiceProvider sp) { }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ServiceProviderStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA004" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CCOCA004_RefParameter_Should_ReportError()
    {
        var source = ConsumerWithBody("public sealed partial", @"
public TestConsumer(ref string value) { }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA004" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CCOCA004_OutParameter_Should_ReportError()
    {
        var source = ConsumerWithBody("public sealed partial", @"
public TestConsumer(out string value) { value = """"; }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA004" && d.Severity == DiagnosticSeverity.Error);
    }

    #endregion

    #region CCOCA005 -- Required member not satisfiable

    [Fact]
    public void CCOCA005_RequiredMemberNotSatisfiedByConstructor_Should_ReportError()
    {
        var source = ConsumerWithBody("public sealed partial", @"
public required string Name { get; init; }
public TestConsumer() { }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA005" && d.Severity == DiagnosticSeverity.Error);
    }

    #endregion

    #region CCOCA006 -- Hand-written activation conflict

    [Fact]
    public void CCOCA006_HandWrittenActivationInterface_Should_ReportError()
    {
        var source = @"
using System;
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
public sealed partial class TestConsumer : IOutboxRequiredConsumer<string>, IOutboxConsumerActivation<TestConsumer>
{
    public string ConsumerId => ""test"";
    public TestConsumer() { }

    static TestConsumer IOutboxConsumerActivation<TestConsumer>.CreateOutboxConsumer(IServiceProvider services)
        => new TestConsumer();
}
";
        var activationInterfaceStub = @"
namespace CrestCreates.Runtime.Delivery.Abstractions.Activation
{
    public interface IOutboxConsumerActivation<TSelf> where TSelf : class, IOutboxConsumerActivation<TSelf>
    {
        static abstract TSelf CreateOutboxConsumer(System.IServiceProvider services);
    }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, activationInterfaceStub, ServiceProviderStub });

        result.Diagnostics.Should().Contain(d => d.Id == "CCOCA006" && d.Severity == DiagnosticSeverity.Error);
    }

    #endregion

    #region CCOCA007 -- Missing contract reference

    [Fact]
    public void CCOCA007_MissingActivationAbstractionsReference_Should_ReportError()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
public sealed partial class TestConsumer : IOutboxRequiredConsumer<string>
{
    public string ConsumerId => ""test"";
    public TestConsumer() { }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub });

        result.CompilationSuccess.Should().BeFalse(
            because: "generated code references IOutboxConsumerActivation which is not available without the full Abstractions reference");
    }

    #endregion

    #region Positive cases -- valid generation

    [Fact]
    public void SingleConstructor_ZeroDependencies_Should_GenerateParameterlessNew()
    {
        var source = ConsumerWithBody("public sealed partial", "public TestConsumer() { }");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().NotBeEmpty();
        result.ContainsSource("new global::TestConsumer()").Should().BeTrue();
    }

    [Fact]
    public void SingleConstructor_SingleDependency_Should_GenerateGetRequiredService()
    {
        var depStub = @"
namespace TestApp { public interface IDependencyA { } }
";
        var source = ConsumerWithBody("public sealed partial", @"
public TestConsumer(TestApp.IDependencyA dep) { }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub, depStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().NotBeEmpty();
        result.ContainsSource("GetRequiredService<global::TestApp.IDependencyA>").Should().BeTrue();
    }

    [Fact]
    public void SingleConstructor_MultipleDependencies_Should_GenerateInDeclarationOrder()
    {
        var depStubs = @"
namespace TestApp {
    public interface IDependencyA { }
    public interface IDependencyB { }
    public interface IDependencyC { }
}
";
        var source = ConsumerWithBody("public sealed partial", @"
public TestConsumer(TestApp.IDependencyA a, TestApp.IDependencyB b, TestApp.IDependencyC c) { }
");
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub, depStubs });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().NotBeEmpty();
        var generatedText = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText));
        var indexA = generatedText.IndexOf("GetRequiredService<global::TestApp.IDependencyA>");
        var indexB = generatedText.IndexOf("GetRequiredService<global::TestApp.IDependencyB>");
        var indexC = generatedText.IndexOf("GetRequiredService<global::TestApp.IDependencyC>");
        indexA.Should().BeGreaterThan(-1);
        indexB.Should().BeGreaterThan(-1);
        indexC.Should().BeGreaterThan(-1);
        indexA.Should().BeLessThan(indexB);
        indexB.Should().BeLessThan(indexC);
    }

    [Fact]
    public void PrimaryConstructor_Should_GenerateCorrectActivation()
    {
        var depStub = @"
namespace TestApp { public interface IService { } }
";
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
public sealed partial class TestConsumer(TestApp.IService svc) : IOutboxRequiredConsumer<string>
{
    public string ConsumerId => ""test"";
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub, depStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().NotBeEmpty();
        result.ContainsSource("GetRequiredService<global::TestApp.IService>").Should().BeTrue();
    }

    [Fact]
    public void InternalConsumer_Should_GenerateInternalPartialClass()
    {
        var depStub = @"
namespace TestApp { public interface ISvc { } }
";
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
internal sealed partial class TestConsumer : IOutboxRequiredConsumer<string>
{
    public string ConsumerId => ""test"";
    public TestConsumer(TestApp.ISvc svc) { }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub, depStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().NotBeEmpty();
        result.ContainsSource("internal sealed partial class TestConsumer").Should().BeTrue();
    }

    #endregion

    #region Marker-missing compile contract

    [Fact]
    public void MarkerMissing_Should_NotGenerateAnyOutput()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

public sealed partial class TestConsumer : IOutboxRequiredConsumer<string>
{
    public string ConsumerId => ""test"";
    public TestConsumer() { }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { ConsumerInterfaceStub });

        result.GeneratedSources.Should().BeEmpty();
    }

    #endregion

    #region Deterministic output

    [Fact]
    public void SameInput_Should_ProduceDeterministicOutput()
    {
        var depStub = @"
namespace TestApp { public interface IDep { } }
";
        var source = ConsumerWithBody("public sealed partial", @"
public TestConsumer(TestApp.IDep dep) { }
");

        var result1 = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub, depStub });
        var result2 = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub, depStub });

        result1.GeneratedSources.Should().HaveCount(result2.GeneratedSources.Count);
        for (var i = 0; i < result1.GeneratedSources.Count; i++)
        {
            result1.GeneratedSources[i].Text.ToString()
                .Should().Be(result2.GeneratedSources[i].Text.ToString());
        }
    }

    #endregion

    #region R1-R4 Regression Tests

    [Fact]
    public void R1_TwoNamespacesSameTypeName_Should_ProduceDistinctOutputs()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace NamespaceA
{
    [GenerateOutboxConsumerActivation]
    public sealed partial class SameNameConsumer : IOutboxRequiredConsumer<string>
    {
        public string ConsumerId => ""a"";
        public SameNameConsumer() { }
    }
}

namespace NamespaceB
{
    [GenerateOutboxConsumerActivation]
    public sealed partial class SameNameConsumer : IOutboxRequiredConsumer<string>
    {
        public string ConsumerId => ""b"";
        public SameNameConsumer() { }
    }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().HaveCount(2, because: "two distinct types should produce two separate outputs");
        var hintNames = result.GeneratedSources.Select(s => s.FileName).ToList();
        hintNames.Distinct().Should().HaveCount(2, because: "hint names must be unique to avoid AddSource ArgumentException");
    }

    [Fact]
    public void R1_TwoPartialDeclarations_Should_ProduceSingleOutput()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace TestApp
{
    [GenerateOutboxConsumerActivation]
    public sealed partial class SplitConsumer : IOutboxRequiredConsumer<string>
    {
        public string ConsumerId => ""split"";
    }

    public partial class SplitConsumer
    {
        public SplitConsumer() { }
    }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().HaveCount(1, because: "partial class split across declarations should produce exactly one output");
    }

    [Fact]
    public void R2_MarkerOnlyContract_Should_GenerateActivationInterface()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace TestApp
{
    [GenerateOutboxConsumerActivation]
    public sealed partial class MarkerOnlyConsumer : IOutboxRequiredConsumer<string>
    {
        public string ConsumerId => ""marker-only"";
        public MarkerOnlyConsumer() { }
    }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().NotBeEmpty();
        result.ContainsSource("IOutboxConsumerActivation").Should().BeTrue();
        result.ContainsSource("CreateOutboxConsumer").Should().BeTrue();
    }

    [Fact]
    public void R3_ExternalSameNameAttribute_Should_ProduceNoOutputAndNoDiagnostics()
    {
        var foreignMarkerStub = @"
using System;
namespace SomeOther.Library.Activation
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GenerateOutboxConsumerActivationAttribute : Attribute { }
}
";
        var source = @"
using SomeOther.Library.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace TestApp
{
    [GenerateOutboxConsumerActivation]
    public sealed partial class ForeignMarkedConsumer : IOutboxRequiredConsumer<string>
    {
        public string ConsumerId => ""foreign"";
        public ForeignMarkedConsumer() { }
    }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { foreignMarkerStub, ConsumerInterfaceStub });

        result.GeneratedSources.Should().BeEmpty(
            because: "a foreign attribute with the same short name must not trigger generation");
        result.GetErrors().Should().BeEmpty(
            because: "no CCOCA diagnostics should fire for a non-matching attribute");
    }

    [Fact]
    public void R4_GlobalNamespaceConsumer_Should_GenerateWithoutNamespaceDeclaration()
    {
        var source = @"
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

[GenerateOutboxConsumerActivation]
public sealed partial class GlobalConsumer : IOutboxRequiredConsumer<string>
{
    public string ConsumerId => ""global"";
    public GlobalConsumer() { }
}
";
        var result = SourceGeneratorTestHelper.RunGenerator<GeneratorType>(
            source,
            additionalSources: new[] { MarkerAttributeStub, ConsumerInterfaceStub, ActivationInterfaceStub });

        result.HasNoErrors().Should().BeTrue(because: string.Join("; ", result.GetErrors().Select(e => e.GetMessage())));
        result.GeneratedSources.Should().NotBeEmpty();
        var generatedText = result.GeneratedSources[0].SourceText;
        generatedText.Should().NotContain("namespace <global", because: "global namespace should not produce a namespace declaration with angle brackets");
        generatedText.Should().Contain("partial class GlobalConsumer");
    }

    #endregion
}
