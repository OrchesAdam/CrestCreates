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
    public void RecoveryPolicy_Should_HaveNoProviderDependencies()
    {
        // Issue #73: recovery policy is a pure decision component. It must not
        // reach into any provider (gate, budget, auditor, store, dispatcher) or
        // hold injected state — otherwise it becomes an orchestration component.
        var policyType = AgentToolsAssembly
            .GetType("CrestCreates.Agent.Tools.AgentToolPreDispatchRecoveryPolicy");
        policyType.Should().NotBeNull();

        var forbiddenTypes = new[]
        {
            "IAgentToolInvocationGate",
            "IAgentToolBudgetGate",
            "IAgentToolGovernanceAuditor",
            "IAgentToolPreDispatchReconciliationStore",
            "ICapabilityDispatcher"
        };

        var fields = policyType!.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var ctorParams = policyType.GetConstructors()
            .SelectMany(c => c.GetParameters());

        foreach (var forbidden in forbiddenTypes)
        {
            fields.Should().NotContain(f => f.FieldType.Name == forbidden,
                $"{policyType.Name} must not hold a {forbidden} field.");
            ctorParams.Should().NotContain(p => p.ParameterType.Name == forbidden,
                $"{policyType.Name} must not receive a {forbidden} constructor parameter.");
        }
    }

    [Fact]
    public void SettlementExecutor_Should_HaveNoDispatcherDependency()
    {
        // Issue #73: the settlement executor is claim-first and settles budget /
        // governance. It must never depend on the capability dispatcher, which
        // belongs solely to the live invoker.
        var executorType = AgentToolsAssembly
            .GetType("CrestCreates.Agent.Tools.AgentToolPreDispatchSettlementExecutor");
        executorType.Should().NotBeNull();

        var forbidden = "ICapabilityDispatcher";

        var fields = executorType!.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var ctorParams = executorType.GetConstructors()
            .SelectMany(c => c.GetParameters());

        fields.Should().NotContain(f => f.FieldType.Name == forbidden,
            $"{executorType.Name} must not hold an {forbidden} field.");
        ctorParams.Should().NotContain(p => p.ParameterType.Name == forbidden,
            $"{executorType.Name} must not receive an {forbidden} constructor parameter.");
    }

    [Fact]
    public void ConsolidationTypes_Should_AllBeInternal()
    {
        // Issue #73: the consolidation types are implementation details of the
        // Agent.Tools assembly and must never become public API.
        var consolidationTypes = new[]
        {
            "CrestCreates.Agent.Tools.AgentToolPreDispatchRecoveryPolicy",
            "CrestCreates.Agent.Tools.AgentToolPreDispatchSettlementExecutor",
            "CrestCreates.Agent.Tools.AgentToolPreDispatchResultWriter",
            "CrestCreates.Agent.Tools.AgentToolPreDispatchFinalizer",
            "CrestCreates.Agent.Tools.AgentToolPreDispatchCoordinator"
        };

        foreach (var typeName in consolidationTypes)
        {
            var type = AgentToolsAssembly.GetType(typeName);
            type.Should().NotBeNull($"{typeName} should exist.");
            type!.IsPublic.Should().BeFalse(
                $"{typeName} must be internal; it is a complexity-consolidation implementation detail.");
        }
    }

    [Theory]
    [InlineData(
        "AgentToolInvocationPreDispatchState",
        "Unknown,Pending,Ready,Accepted,DispatchStarted,Abandoned,ReleasePending,Released,CompletionPending,Completed,Indeterminate,ReconciliationPending")]
    [InlineData(
        "AgentToolPreDispatchReconciliationStatus",
        "Unknown,Released,AlreadyReleased,StillPending,Conflict,PostDispatchUnknown,Missing")]
    [InlineData(
        "AgentToolBudgetReservationState",
        "Unknown,Reserved,Released,Committed,Indeterminate")]
    [InlineData(
        "AgentToolPreDispatchPersistenceCapability",
        "FullSemantic,FullDurable")]
    public void PersistedEnums_Should_HaveExactFrozenMemberSets(string enumName, string expectedMembersCsv)
    {
        // Issue #73: persisted enums must have an exact, frozen member set.
        // Adding a member is a durable-schema change and must be called out,
        // not silently introduced by complexity consolidation.
        var enumType = AbstractionsAssembly
            .GetTypes()
            .Single(t => t.Name == enumName);

        var expected = expectedMembersCsv.Split(',');
        var actual = Enum.GetNames(enumType);

        actual.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering(),
            $"persisted enum {enumName} must have exactly the frozen member set.");
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
