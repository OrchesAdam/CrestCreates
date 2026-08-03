using System.Reflection;
using CrestCreates.Agent.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Architecture;

public class AgentToolPreDispatchContractArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(AgentToolPreDispatchIdentity).Assembly;

    [Fact]
    public void AgentToolPreDispatchIdentity_CannotBeConstructedWithoutLogicalKeyAndAttemptId()
    {
        var constructor = typeof(AgentToolPreDispatchIdentity).GetConstructors()[0];
        var parameters = constructor.GetParameters();

        parameters.Should().HaveCount(2);
        parameters[0].Name.Should().Be("LogicalInvocationKey");
        parameters[1].Name.Should().Be("AttemptId");
    }

    [Fact]
    public void Auditor_HasAuthoritativeLookupAndTypedWriteResult()
    {
        var auditorType = typeof(IAgentToolGovernanceAuditor);

        var recordMethod = auditorType.GetMethod(
            "RecordPreDispatchAsync",
            BindingFlags.Instance | BindingFlags.Public);

        recordMethod.Should().NotBeNull();
        recordMethod!.ReturnType.Should().Be(
            typeof(ValueTask<AgentToolGovernancePreDispatchWriteResult>));

        var lookupMethod = auditorType.GetMethod(
            "GetPreDispatchStateAsync",
            BindingFlags.Instance | BindingFlags.Public);

        lookupMethod.Should().NotBeNull();
        lookupMethod!.ReturnType.Should().Be(
            typeof(ValueTask<AgentToolGovernancePreDispatchReadResult>));
    }

    [Fact]
    public void Gate_Has_Prepare_BindReservation_BindAccepted_Get_Operations()
    {
        var gateType = typeof(IAgentToolInvocationGate);

        gateType.GetMethod("PreparePreDispatchIntentAsync").Should().NotBeNull();
        gateType.GetMethod("BindPreDispatchReservationAsync").Should().NotBeNull();
        gateType.GetMethod("BindAcceptedPreDispatchAsync").Should().NotBeNull();
        gateType.GetMethod("GetPreDispatchStateAsync").Should().NotBeNull();
        gateType.GetMethod("PublishBudgetDenialAsync").Should().NotBeNull();
    }

    [Fact]
    public void Dispatch_Requires_Receipt_And_ReservationId()
    {
        var gateType = typeof(IAgentToolInvocationGate);

        var dispatchMethod = gateType.GetMethod(
            "TryMarkDispatchStartedAsync",
            BindingFlags.Instance | BindingFlags.Public);

        dispatchMethod.Should().NotBeNull();

        var parameters = dispatchMethod!.GetParameters();
        parameters.Should().HaveCount(4);
        parameters[1].ParameterType.Should().Be(typeof(AgentToolGovernancePreDispatchReceipt));
        parameters[2].ParameterType.Should().Be(typeof(string));
    }

    [Fact]
    public void Budget_Has_AttemptIdentityRead()
    {
        var budgetType = typeof(IAgentToolBudgetGate);

        var readMethod = budgetType.GetMethod(
            "GetReservationStateAsync",
            BindingFlags.Instance | BindingFlags.Public);

        readMethod.Should().NotBeNull();
        readMethod!.ReturnType.Should().Be(
            typeof(ValueTask<AgentToolBudgetReservationReadResult>));
    }

    [Fact]
    public void Reconciler_HasNo_DispatcherDependency()
    {
        var reconcilerType = typeof(IAgentToolPreDispatchReconciler);

        var reconcileMethod = reconcilerType.GetMethod(
            "ReconcileAsync",
            BindingFlags.Instance | BindingFlags.Public);

        reconcileMethod.Should().NotBeNull();

        var parameters = reconcileMethod!.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(AgentToolPreDispatchIdentity));
        parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
    }

    [Fact]
    public void AgentToolDurableContracts_Should_Not_Expose_NpgsqlTypes()
    {
        var publicTypes = AbstractionsAssembly.GetExportedTypes();

        foreach (var type in publicTypes)
        {
            type.Namespace.Should().NotStartWith("Npgsql",
                $"public type {type.FullName} must not expose Npgsql namespace.");

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                field.FieldType.Namespace.Should().NotStartWith("Npgsql",
                    $"field {type.FullName}.{field.Name} must not expose Npgsql type.");
            }

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                property.PropertyType.Namespace.Should().NotStartWith("Npgsql",
                    $"property {type.FullName}.{property.Name} must not expose Npgsql type.");
            }
        }
    }

    [Fact]
    public void Manifest_Should_Contain_All_Seventy_CaseIds()
    {
        var expectedIds = new HashSet<string>
        {
            "H01","H02","H03","H04","H05","H06","H07","H08","H09","H10",
            "B01","B02","B03","B04","B05","B06","B07","B08","B09","B10",
            "B11","B12","B13","B14","B15","B16","B17","B18",
            "F01","F02","F03","F04","F05","F06","F07","F08","F09","F10",
            "F11","F12","F13","F14","F15","F16","F17","F18","F19","F20",
            "F21","F22","F23","F24","F25","F26","F27","F28","F29","F30",
            "C01","C02","C03","C04","C05","C06","C07","C08","C09","C10","C11","C12",
        };

        var actualIds = AgentToolPreDispatchCaseManifest.AllCases
            .Select(c => c.Id)
            .ToHashSet();

        actualIds.Should().BeEquivalentTo(expectedIds,
            "manifest must contain every Case ID from the Plan ledger.");
    }
}
