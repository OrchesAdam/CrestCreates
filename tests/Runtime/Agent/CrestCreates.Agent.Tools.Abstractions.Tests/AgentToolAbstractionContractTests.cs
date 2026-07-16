using System.Reflection;
using System.Text.Json;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Abstractions.Tests;

public sealed class AgentToolAbstractionContractTests
{
    public static TheoryData<Type> SafeZeroEnums => new()
    {
        typeof(AgentToolCallOrigin),
        typeof(AgentToolInvocationOutcomeKind),
        typeof(AgentToolInvocationAcquireStatus),
        typeof(AgentToolApprovalDecision),
        typeof(AgentToolApprovalEvidenceClaimState),
        typeof(AgentToolBudgetReserveStatus),
        typeof(AgentToolBudgetReservationState),
        typeof(AgentToolGovernanceAttemptFinalState),
        typeof(AgentToolInvocationTerminalState)
    };

    [Theory]
    [MemberData(nameof(SafeZeroEnums))]
    public void DecisionAndStateEnums_DefaultToUnknown(Type enumType)
    {
        Enum.GetName(enumType, 0).Should().Be("Unknown");
    }

    [Fact]
    public void AuthoringDefaults_AreExplicitAndFailClosed()
    {
        var attribute = new AgentToolSpecAttribute("orders.create");

        attribute.DescriptorVersion.Should().Be(1);
        attribute.CapabilityVersion.Should().Be(0);
        attribute.SelectionPolicy.Should().Be(AgentToolSelectionPolicy.ExplicitOnly);
        attribute.SideEffectKind.Should().Be(AgentToolSideEffectKind.Unknown);
        attribute.RiskFloor.Should().Be(AgentToolRiskFloor.Inherit);
        attribute.ApprovalMode.Should().Be(AgentToolApprovalMode.PolicyDriven);
        attribute.CostUnits.Should().Be(1);
        attribute.MaxCallsPerExecution.Should().Be(0);
        attribute.AuditMode.Should().Be(AgentToolAuditMode.Required);
        attribute.AllowedAgentRoles.Should().BeEmpty();
    }

    [Fact]
    public void InvocationRequest_DoesNotAcceptTrustedExecutionIdentity()
    {
        var propertyNames = typeof(AgentToolInvocationRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().BeEquivalentTo(
            nameof(AgentToolInvocationRequest.ToolName),
            nameof(AgentToolInvocationRequest.Arguments),
            nameof(AgentToolInvocationRequest.ApprovalEvidence));
        var trustedPropertyNames = new[]
        {
            "TenantId",
            "UserId",
            "AgentId",
            "AgentRoles",
            "ExecutionId",
            "InvocationId",
            "CallOrigin"
        };
        propertyNames.Intersect(trustedPropertyNames, StringComparer.Ordinal).Should().BeEmpty();
    }

    [Fact]
    public void DiscoveryContract_DoesNotExposeRolePolicyOrRuntimeObjects()
    {
        var propertyNames = typeof(AgentToolDiscoveryContract)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain("AllowedAgentRoles");
        propertyNames.Should().NotContain("Handler");
        propertyNames.Should().NotContain("RuntimeEntry");
    }

    [Fact]
    public void InvocationLease_ExposesAttemptExpiryAndFencingIdentity()
    {
        var acquiredAt = DateTimeOffset.UnixEpoch;
        var lease = new AgentToolInvocationLease
        {
            AttemptId = "attempt-1",
            LeaseId = "lease-1",
            FencingToken = 7,
            AcquiredAt = acquiredAt,
            ExpiresAt = acquiredAt.AddMinutes(1)
        };

        lease.AttemptId.Should().Be("attempt-1");
        lease.LeaseId.Should().Be("lease-1");
        lease.FencingToken.Should().Be(7);
        lease.ExpiresAt.Should().BeAfter(lease.AcquiredAt);
    }

    [Fact]
    public void BudgetCommittedAndInvocationIndeterminate_AreRepresentableIndependently()
    {
        var budget = new AgentToolBudgetReservation
        {
            ReservationId = "reservation-1",
            AttemptId = "attempt-1",
            InvocationFingerprint = "fingerprint",
            Category = "writes",
            CostUnits = 2,
            State = AgentToolBudgetReservationState.Committed
        };
        var record = new AgentToolGovernanceFinalizationRecord
        {
            AuditId = "audit-1",
            Context = CreateAuditContext(),
            Lease = new AgentToolInvocationLease
            {
                AttemptId = "attempt-1",
                LeaseId = "lease-1",
                FencingToken = 1,
                AcquiredAt = DateTimeOffset.UnixEpoch,
                ExpiresAt = DateTimeOffset.UnixEpoch.AddMinutes(1)
            },
            DispatchStarted = true,
            BudgetReservation = budget,
            AttemptState = AgentToolGovernanceAttemptFinalState.Indeterminate,
            InvocationState = AgentToolInvocationTerminalState.Indeterminate,
            Outcome = new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.InvocationIndeterminate,
                Code = "AGENT_TOOL_POST_DISPATCH_FINALIZATION_FAILED",
                Message = "The invocation result could not be determined."
            },
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(new AgentToolInvocationOutcome
            {
                Kind = AgentToolInvocationOutcomeKind.InvocationIndeterminate,
                Code = "AGENT_TOOL_POST_DISPATCH_FINALIZATION_FAILED",
                Message = "The invocation result could not be determined."
            }),
            ReasonCode = "PostDispatchAuditFailure"
        };

        record.BudgetReservation.State.Should().Be(AgentToolBudgetReservationState.Committed);
        record.InvocationState.Should().Be(AgentToolInvocationTerminalState.Indeterminate);
    }

    [Fact]
    public void GeneratedRegistrationSurfaces_AreKeyedByDescriptorIdentityAndClrType()
    {
        var descriptorId = $"test.{Guid.NewGuid():N}";
        var binding = new AgentToolBindingContract
        {
            ToolDescriptorId = descriptorId,
            ToolDescriptorVersion = 2,
            InputType = typeof(TestInput),
            OutputType = typeof(TestOutput),
            BindInputAsync = static (_, _, _) => ValueTask.FromResult<object?>(new TestInput()),
            SerializeOutputAsync = static (_, _, _) => ValueTask.FromResult<JsonElement?>(null)
        };

        AgentToolBindingRegistry.Register(binding);
        AgentToolJsonContractRegistry.RegisterInputType(typeof(TestInput));
        AgentToolJsonContractRegistry.RegisterOutputType(typeof(TestOutput));

        AgentToolBindingRegistry.Find(descriptorId, 2).Should().BeSameAs(binding);
        AgentToolBindingRegistry.Find(descriptorId, 1).Should().BeNull();
        AgentToolJsonContractRegistry.GetInputTypes().Should().Contain(typeof(TestInput));
        AgentToolJsonContractRegistry.GetOutputTypes().Should().Contain(typeof(TestOutput));
    }

    [Fact]
    public void Abstractions_DoNotReferenceProviderOrExecutionInfrastructureAssemblies()
    {
        var references = typeof(AgentToolSpecAttribute).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        references.Should().NotContain(name => name!.StartsWith("CrestCreates.Mcp", StringComparison.Ordinal));
        references.Should().NotContain(name => name!.Contains("AspNetCore", StringComparison.Ordinal));
        references.Should().NotContain(name => name!.Contains("ControlPlane", StringComparison.Ordinal));
    }

    private static AgentToolGovernanceContext CreateGovernanceContext()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "sales-agent" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest
        };
        var budget = new AgentToolBudgetRequirement
        {
            Category = "writes",
            CostUnits = 2,
            MaxCallsPerExecution = 1
        };

        return new AgentToolGovernanceContext
        {
            LogicalInvocationKey = new AgentToolLogicalInvocationKey(
                "tenant-1",
                "user-1",
                execution.AgentId,
                execution.ExecutionId,
                execution.InvocationId),
            AttemptId = "attempt-1",
            InvocationFingerprint = "fingerprint",
            ArgumentsHash = "arguments-hash",
            ExecutionContext = execution,
            ToolContract = new AgentToolContractIdentity("tool-1", 1, "tool-hash"),
            CapabilityContract = new AgentToolContractIdentity("capability-1", 1, "capability-hash"),
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                AgentToolSideEffectKind.ExternalWrite,
                CapabilityRiskLevel.High,
                AgentToolApprovalMode.Required,
                budget,
                AgentToolAuditMode.Required)
        };
    }

    private static AgentToolGovernanceAuditContext CreateAuditContext()
    {
        var context = CreateGovernanceContext();
        return new AgentToolGovernanceAuditContext
        {
            LogicalInvocationKey = context.LogicalInvocationKey,
            AttemptId = context.AttemptId,
            InvocationFingerprint = context.InvocationFingerprint,
            ArgumentsHash = context.ArgumentsHash,
            CallOrigin = context.ExecutionContext.CallOrigin,
            AgentRolesHash = "roles-hash",
            ToolContract = context.ToolContract,
            CapabilityContract = context.CapabilityContract,
            InputSchemaContract = context.InputSchemaContract,
            OutputSchemaContract = context.OutputSchemaContract,
            Governance = context.Governance
        };
    }

    private sealed class TestInput;

    private sealed class TestOutput;
}
