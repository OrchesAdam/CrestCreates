using System.Reflection;
using CrestCreates.Core.Abstractions.Identity;
using FluentAssertions;
using Xunit;

// semantic-string-guard: allow

namespace CrestCreates.Core.Abstractions.Tests.Identity;

public class SemanticValueObjectTests
{
    [Fact]
    public void ErrorCode_Rejects_Null()
    {
        var act = () => new ErrorCode(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    [Fact]
    public void ErrorCode_Rejects_Empty()
    {
        var act = () => new ErrorCode("");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    [Fact]
    public void ErrorCode_Rejects_Whitespace()
    {
        var act = () => new ErrorCode(" ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    [Fact]
    public void ErrorCode_Default_Is_Empty()
    {
        var code = default(ErrorCode);

        code.IsEmpty.Should().BeTrue();
        code.Value.Should().BeNull();
    }

    [Fact]
    public void ErrorCode_Default_ToString_Returns_Empty_String()
    {
        var code = default(ErrorCode);

        code.ToString().Should().BeEmpty();
    }

    [Fact]
    public void ErrorCode_Default_Implicit_Conversion_Returns_Empty_String()
    {
        var code = default(ErrorCode);

        string value = code;

        value.Should().BeEmpty();
    }

    [Fact]
    public void ErrorCode_Default_RequireValue_Throws()
    {
        var code = default(ErrorCode);

        var act = () => code.RequireValue();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Error code is empty.");
    }

    [Fact]
    public void ErrorCode_Explicit_Construction_Stores_Value()
    {
        var code = new ErrorCode("Crest.FeatureManagement.InvalidValue");

        code.Value.Should().Be("Crest.FeatureManagement.InvalidValue");
        code.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ErrorCode_RequireValue_Returns_Value()
    {
        var code = new ErrorCode("Crest.FeatureManagement.InvalidValue");

        code.RequireValue().Should().Be("Crest.FeatureManagement.InvalidValue");
    }

    [Fact]
    public void ErrorCode_Implicit_Conversion_To_String_Returns_Value()
    {
        var code = new ErrorCode("Crest.FeatureManagement.InvalidValue");

        string value = code;

        value.Should().Be("Crest.FeatureManagement.InvalidValue");
    }

    [Fact]
    public void ErrorCode_ToString_Returns_Value()
    {
        var code = new ErrorCode("Crest.FeatureManagement.InvalidValue");

        code.ToString().Should().Be("Crest.FeatureManagement.InvalidValue");
    }

    [Fact]
    public void ErrorCode_Equality_By_Value()
    {
        var a = new ErrorCode("TEST");
        var b = new ErrorCode("TEST");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void ErrorCode_Inequality_By_Value()
    {
        var a = new ErrorCode("A");
        var b = new ErrorCode("B");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void All_Semantic_Value_Objects_Construct_And_Require()
    {
        new DiagnosticCode("CCHASH001").RequireValue().Should().Be("CCHASH001");
        new EventName("activation.rejected").RequireValue().Should().Be("activation.rejected");
        new PermissionName("agent.draft.create").RequireValue().Should().Be("agent.draft.create");
        new PolicyName("Permission:agent.draft.create").RequireValue().Should().Be("Permission:agent.draft.create");
        new CapabilityId("capability.test").RequireValue().Should().Be("capability.test");
        new WorkflowId("workflow.test").RequireValue().Should().Be("workflow.test");
        new HumanTaskId("descriptor-activation-review").RequireValue().Should().Be("descriptor-activation-review");
        new DescriptorId("schema.T1").RequireValue().Should().Be("schema.T1");
        new VersionKey("event.test:v1").RequireValue().Should().Be("event.test:v1");
        new MessageTemplateId("report.activation.eligible").RequireValue().Should().Be("report.activation.eligible");
    }

    [Theory]
    [InlineData(typeof(ErrorCode), "Error code")]
    [InlineData(typeof(DiagnosticCode), "Diagnostic code")]
    [InlineData(typeof(EventName), "Event name")]
    [InlineData(typeof(PermissionName), "Permission name")]
    [InlineData(typeof(PolicyName), "Policy name")]
    [InlineData(typeof(CapabilityId), "Capability id")]
    [InlineData(typeof(WorkflowId), "Workflow id")]
    [InlineData(typeof(HumanTaskId), "Human task id")]
    [InlineData(typeof(DescriptorId), "Descriptor id")]
    [InlineData(typeof(VersionKey), "Version key")]
    [InlineData(typeof(MessageTemplateId), "Message template id")]
    public void All_Value_Objects_Default_IsEmpty_RequireValue_Throws(Type type, string displayName)
    {
        var defaultValue = Activator.CreateInstance(type)!;
        var isEmptyProp = type.GetProperty("IsEmpty")!;
        var requireValueMethod = type.GetMethod("RequireValue")!;

        isEmptyProp.GetValue(defaultValue).Should().Be(true);

        var act = () => requireValueMethod.Invoke(defaultValue, null);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage($"{displayName} is empty.");
    }
}
