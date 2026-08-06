using System.Reflection;
using CrestCreates.Agent.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Architecture;

/// <summary>
/// Issue #73 architecture gates: production Abstractions must not leak
/// test-only models, and persisted enum values must remain frozen.
/// </summary>
public class AgentToolPreDispatchComplexityArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(AgentToolPreDispatchIdentity).Assembly;

    private static readonly Assembly AgentToolsAssembly =
        typeof(AgentToolInvoker).Assembly;

    private static readonly HashSet<string> TestOnlyTypeNames = new()
    {
        "AgentToolPreDispatchCrashWindow",
        "StoredAgentToolPreDispatchSnapshot"
    };

    [Fact]
    public void Invoker_Should_NotDependOnReconciliationStore()
    {
        // Slice 8.4: the live pre-dispatch mainline moved out of the invoker.
        // The invoker must no longer reach into the durable reconciliation store.
        var storeInterface = AbstractionsAssembly
            .GetTypes()
            .SingleOrDefault(t => t.Name == "IAgentToolPreDispatchReconciliationStore");

        storeInterface.Should().NotBeNull("the store contract should still exist in Abstractions.");

        var invoker = AgentToolsAssembly.GetType("CrestCreates.Agent.Tools.AgentToolInvoker");
        invoker.Should().NotBeNull();

        var storeDependency = invoker!.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(f => f.FieldType == storeInterface)
            || invoker.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == storeInterface);

        storeDependency.Should().BeFalse(
            "AgentToolInvoker must not hold or receive the reconciliation store; the pre-dispatch mainline lives in the coordinator.");
    }

    [Fact]
    public void ProductionAbstractions_Should_NotExposeCrashWindow()
    {
        var publicType = AbstractionsAssembly
            .GetTypes()
            .SingleOrDefault(t => t.Name == "AgentToolPreDispatchCrashWindow");

        publicType.Should().BeNull(
            "AgentToolPreDispatchCrashWindow is a test-only model and must not be part of production Abstractions.");
    }

    [Fact]
    public void ProductionAbstractions_Should_NotExposeStoredSnapshot()
    {
        var publicType = AbstractionsAssembly
            .GetTypes()
            .SingleOrDefault(t => t.Name == "StoredAgentToolPreDispatchSnapshot");

        publicType.Should().BeNull(
            "StoredAgentToolPreDispatchSnapshot is a test-only model and must not be part of production Abstractions.");
    }

    [Fact]
    public void ProductionAbstractions_Should_NotExposeAnyTestOnlyModel()
    {
        var exported = AbstractionsAssembly.GetExportedTypes().Select(t => t.Name).ToHashSet();

        exported.Should().NotContain(TestOnlyTypeNames,
            "no test-only model may leak into the production Abstractions surface.");
    }

    [Theory]
    [InlineData("AgentToolInvocationPreDispatchState", "Unknown", 0)]
    [InlineData("AgentToolInvocationPreDispatchState", "Pending", 1)]
    [InlineData("AgentToolInvocationPreDispatchState", "Ready", 2)]
    [InlineData("AgentToolInvocationPreDispatchState", "Accepted", 3)]
    [InlineData("AgentToolInvocationPreDispatchState", "DispatchStarted", 4)]
    [InlineData("AgentToolInvocationPreDispatchState", "Abandoned", 5)]
    [InlineData("AgentToolInvocationPreDispatchState", "ReleasePending", 6)]
    [InlineData("AgentToolInvocationPreDispatchState", "Released", 7)]
    [InlineData("AgentToolInvocationPreDispatchState", "CompletionPending", 8)]
    [InlineData("AgentToolInvocationPreDispatchState", "Completed", 9)]
    [InlineData("AgentToolInvocationPreDispatchState", "Indeterminate", 10)]
    [InlineData("AgentToolInvocationPreDispatchState", "ReconciliationPending", 11)]
    [InlineData("AgentToolPreDispatchReconciliationStatus", "Unknown", 0)]
    [InlineData("AgentToolPreDispatchReconciliationStatus", "Released", 1)]
    [InlineData("AgentToolPreDispatchReconciliationStatus", "AlreadyReleased", 2)]
    [InlineData("AgentToolPreDispatchReconciliationStatus", "StillPending", 3)]
    [InlineData("AgentToolPreDispatchReconciliationStatus", "Conflict", 4)]
    [InlineData("AgentToolPreDispatchReconciliationStatus", "PostDispatchUnknown", 5)]
    [InlineData("AgentToolPreDispatchReconciliationStatus", "Missing", 6)]
    [InlineData("AgentToolPreDispatchPersistenceCapability", "FullSemantic", 0)]
    [InlineData("AgentToolPreDispatchPersistenceCapability", "FullDurable", 1)]
    public void PersistedEnumValues_Should_RemainFrozen(string enumName, string memberName, int expectedValue)
    {
        var enumType = AbstractionsAssembly
            .GetTypes()
            .Single(t => t.Name == enumName);

        var member = Enum.GetName(enumType, Convert.ChangeType(expectedValue, Enum.GetUnderlyingType(enumType)));

        member.Should().Be(
            memberName,
            $"persisted enum {enumName}.{memberName} must keep its frozen value {expectedValue}.");

        Convert.ToInt32(Enum.Parse(enumType, memberName)).Should().Be(expectedValue);
    }
}
