using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Mutation tests for <see cref="AgentToolGovernancePreDispatchComparer"/>.
/// Each row mutates exactly one dispatch-authorizing fact (INV-04) and
/// asserts that <see cref="AgentToolGovernancePreDispatchComparer.Equivalent"/>
/// returns false.  The baseline record is reused across all rows so that
/// only the mutated field differs.
/// </summary>
public static class AgentToolPreDispatchComparerTests
{
    [Fact]
    public static void Baseline_is_equal_to_itself()
    {
        var record = SampleRecord();
        AgentToolGovernancePreDispatchComparer.Equivalent(record, record)
            .Should().BeTrue();
    }

    // ── LogicalInvocationKey mutations ──────────────────────────────

    [Fact]
    public static void Mutate_TenantId()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            LogicalInvocationKey = c.LogicalInvocationKey with { TenantId = "tenant-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_UserId()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            LogicalInvocationKey = c.LogicalInvocationKey with { UserId = "user-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_AgentId()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            LogicalInvocationKey = c.LogicalInvocationKey with { AgentId = "agent-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_ExecutionId()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            LogicalInvocationKey = c.LogicalInvocationKey with { ExecutionId = "exec-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_InvocationId()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            LogicalInvocationKey = c.LogicalInvocationKey with { InvocationId = "inv-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    // ── Context scalar mutations ────────────────────────────────────

    [Fact]
    public static void Mutate_AttemptId_Context()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with { AttemptId = "att-mut" });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_InvocationFingerprint()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with { InvocationFingerprint = "fp-mut" });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_ArgumentsHash()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with { ArgumentsHash = "arghash-mut" });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_ArgumentsEvaluated()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with { ArgumentsEvaluated = !c.ArgumentsEvaluated });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_CallOrigin()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            CallOrigin = c.CallOrigin == AgentToolCallOrigin.ExplicitRequest
                ? AgentToolCallOrigin.AutomaticSelection
                : AgentToolCallOrigin.ExplicitRequest
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_AgentRolesHash()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with { AgentRolesHash = "roles-mut" });
        AssertNotEquivalent(left, right);
    }

    // ── Contract identity mutations ─────────────────────────────────

    [Fact]
    public static void Mutate_ToolContract_Id()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            ToolContract = c.ToolContract with { Id = "tool-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_ToolContract_Version()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            ToolContract = c.ToolContract with { Version = c.ToolContract.Version + 1 }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_ToolContract_ContractHash()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            ToolContract = c.ToolContract with { ContractHash = "hash-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_CapabilityContract_Id()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            CapabilityContract = c.CapabilityContract with { Id = "cap-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_CapabilityContract_Version()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            CapabilityContract = c.CapabilityContract with { Version = c.CapabilityContract.Version + 1 }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_CapabilityContract_ContractHash()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            CapabilityContract = c.CapabilityContract with { ContractHash = "caphash-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_InputSchemaContract_Id()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            InputSchemaContract = c.InputSchemaContract with { Id = "in-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_InputSchemaContract_Version()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            InputSchemaContract = c.InputSchemaContract with
            {
                Version = c.InputSchemaContract.Version + 1
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_InputSchemaContract_ContractHash()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            InputSchemaContract = c.InputSchemaContract with { ContractHash = "inhash-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_InputSchemaContract_ToNull()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with { InputSchemaContract = null });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_OutputSchemaContract_Id()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            OutputSchemaContract = c.OutputSchemaContract with { Id = "out-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_OutputSchemaContract_Version()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            OutputSchemaContract = c.OutputSchemaContract with
            {
                Version = c.OutputSchemaContract.Version + 1
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_OutputSchemaContract_ContractHash()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            OutputSchemaContract = c.OutputSchemaContract with { ContractHash = "outhash-mut" }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_OutputSchemaContract_ToNull()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with { OutputSchemaContract = null });
        AssertNotEquivalent(left, right);
    }

    // ── Effective governance mutations ──────────────────────────────

    [Fact]
    public static void Mutate_SelectionPolicy()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                SelectionPolicy = (AgentToolSelectionPolicy)(((int)c.Governance.SelectionPolicy + 1) % 3)
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_SideEffectKind()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                SideEffectKind = (AgentToolSideEffectKind)(((int)c.Governance.SideEffectKind + 1) % 5)
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_EffectiveRisk()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                EffectiveRisk = (CapabilityRiskLevel)(((int)c.Governance.EffectiveRisk + 1) % 4)
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_EffectiveApprovalMode()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                EffectiveApprovalMode = (AgentToolApprovalMode)(((int)c.Governance.EffectiveApprovalMode + 1) % 4)
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_EffectiveAuditMode()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                EffectiveAuditMode = (AgentToolAuditMode)(((int)c.Governance.EffectiveAuditMode + 1) % 3)
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Budget_Category()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                Budget = c.Governance.Budget with { Category = "cat-mut" }
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Budget_CostUnits()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                Budget = c.Governance.Budget with { CostUnits = c.Governance.Budget.CostUnits + 1 }
            }
        });
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Budget_MaxCallsPerExecution()
    {
        var left = SampleRecord();
        var right = WithContext(left, c => c with
        {
            Governance = c.Governance with
            {
                Budget = c.Governance.Budget with { MaxCallsPerExecution = 99 }
            }
        });
        AssertNotEquivalent(left, right);
    }

    // ── Lease mutations ─────────────────────────────────────────────

    [Fact]
    public static void Mutate_Lease_LeaseId()
    {
        var left = SampleRecord();
        var right = left with
        {
            Lease = left.Lease with { LeaseId = "lease-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Lease_AttemptId()
    {
        var left = SampleRecord();
        var right = left with
        {
            Lease = left.Lease with { AttemptId = "leaseatt-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Lease_FencingToken()
    {
        var left = SampleRecord();
        var right = left with
        {
            Lease = left.Lease with { FencingToken = left.Lease.FencingToken + 1 }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Lease_AcquiredAt()
    {
        var left = SampleRecord();
        var right = left with
        {
            Lease = left.Lease with { AcquiredAt = left.Lease.AcquiredAt.AddSeconds(1) }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Lease_ExpiresAt()
    {
        var left = SampleRecord();
        var right = left with
        {
            Lease = left.Lease with { ExpiresAt = left.Lease.ExpiresAt.AddSeconds(1) }
        };
        AssertNotEquivalent(left, right);
    }

    // ── Approval mutations ──────────────────────────────────────────

    [Fact]
    public static void Mutate_Approval_Decision()
    {
        var left = SampleRecord();
        var right = left with
        {
            Approval = left.Approval with
            {
                Decision = left.Approval.Decision == AgentToolApprovalDecision.Approved
                    ? AgentToolApprovalDecision.NotRequired
                    : AgentToolApprovalDecision.Approved
            }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Approval_ClaimState()
    {
        var left = SampleRecord();
        var right = left with
        {
            Approval = left.Approval with
            {
                ClaimState = left.Approval.ClaimState == AgentToolApprovalEvidenceClaimState.Claimed
                    ? AgentToolApprovalEvidenceClaimState.NotApplicable
                    : AgentToolApprovalEvidenceClaimState.Claimed
            }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Approval_EvidenceId()
    {
        var left = SampleRecord();
        var right = left with
        {
            Approval = left.Approval with { EvidenceId = "ev-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Approval_ApproverReference()
    {
        var left = SampleRecord();
        var right = left with
        {
            Approval = left.Approval with { ApproverReference = "appr-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Approval_ReasonCode()
    {
        var left = SampleRecord();
        var right = left with
        {
            Approval = left.Approval with { ReasonCode = "reason-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    // ── Budget reservation mutations ────────────────────────────────

    [Fact]
    public static void Mutate_Reservation_ReservationId()
    {
        var left = SampleRecord();
        var right = left with
        {
            BudgetReservation = left.BudgetReservation with { ReservationId = "res-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Reservation_AttemptId()
    {
        var left = SampleRecord();
        var right = left with
        {
            BudgetReservation = left.BudgetReservation with { AttemptId = "resatt-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Reservation_InvocationFingerprint()
    {
        var left = SampleRecord();
        var right = left with
        {
            BudgetReservation = left.BudgetReservation with { InvocationFingerprint = "resfp-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Reservation_Category()
    {
        var left = SampleRecord();
        var right = left with
        {
            BudgetReservation = left.BudgetReservation with { Category = "rescat-mut" }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Reservation_CostUnits()
    {
        var left = SampleRecord();
        var right = left with
        {
            BudgetReservation = left.BudgetReservation with { CostUnits = left.BudgetReservation.CostUnits + 1 }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Reservation_MaxCallsPerExecution()
    {
        var left = SampleRecord();
        var right = left with
        {
            BudgetReservation = left.BudgetReservation with { MaxCallsPerExecution = 99 }
        };
        AssertNotEquivalent(left, right);
    }

    [Fact]
    public static void Mutate_Reservation_State()
    {
        var left = SampleRecord();
        var right = left with
        {
            BudgetReservation = left.BudgetReservation with
            {
                State = left.BudgetReservation.State == AgentToolBudgetReservationState.Reserved
                    ? AgentToolBudgetReservationState.Committed
                    : AgentToolBudgetReservationState.Reserved
            }
        };
        AssertNotEquivalent(left, right);
    }

    // ── ValidateIdentity tests ──────────────────────────────────────

    [Fact]
    public static void ValidateIdentity_matching_returns_true()
    {
        var record = SampleRecord();
        var identity = SampleIdentity();
        AgentToolGovernancePreDispatchComparer.ValidateIdentity(record, identity)
            .Should().BeTrue();
    }

    [Fact]
    public static void ValidateIdentity_mismatched_AttemptId_returns_false()
    {
        var record = SampleRecord();
        var identity = SampleIdentity() with { AttemptId = "wrong" };
        AgentToolGovernancePreDispatchComparer.ValidateIdentity(record, identity)
            .Should().BeFalse();
    }

    [Fact]
    public static void ValidateIdentity_mismatched_InvocationId_returns_false()
    {
        var record = SampleRecord();
        var identity = SampleIdentity() with
        {
            LogicalInvocationKey = record.Context.LogicalInvocationKey with { InvocationId = "wrong" }
        };
        AgentToolGovernancePreDispatchComparer.ValidateIdentity(record, identity)
            .Should().BeFalse();
    }

    [Fact]
    public static void ValidateIdentity_empty_AttemptId_returns_false()
    {
        var record = SampleRecord();
        var identity = SampleIdentity() with { AttemptId = "" };
        AgentToolGovernancePreDispatchComparer.ValidateIdentity(record, identity)
            .Should().BeFalse();
    }

    [Fact]
    public static void ValidateIdentity_Lease_AttemptId_mismatch_returns_false()
    {
        var record = SampleRecord() with
        {
            Lease = SampleRecord().Lease with { AttemptId = "different" }
        };
        var identity = SampleIdentity();
        AgentToolGovernancePreDispatchComparer.ValidateIdentity(record, identity)
            .Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void AssertNotEquivalent(
        AgentToolGovernancePreDispatchRecord left,
        AgentToolGovernancePreDispatchRecord right)
    {
        AgentToolGovernancePreDispatchComparer.Equivalent(left, right)
            .Should().BeFalse("mutating a single dispatch-authorizing fact must break equivalence");
    }

    private static AgentToolGovernancePreDispatchRecord WithContext(
        AgentToolGovernancePreDispatchRecord record,
        Func<AgentToolGovernanceAuditContext, AgentToolGovernanceAuditContext> transform)
    {
        return record with { Context = transform(record.Context) };
    }

    internal static AgentToolGovernancePreDispatchRecord SampleRecord()
    {
        return new AgentToolGovernancePreDispatchRecord
        {
            Context = SampleContext(),
            Lease = SampleLease(),
            Approval = SampleApproval(),
            BudgetReservation = SampleReservation()
        };
    }

    internal static AgentToolPreDispatchIdentity SampleIdentity()
    {
        return new AgentToolPreDispatchIdentity(
            SampleContext().LogicalInvocationKey,
            SampleContext().AttemptId);
    }

    internal static AgentToolGovernanceAuditContext SampleContext()
    {
        return new AgentToolGovernanceAuditContext
        {
            LogicalInvocationKey = new AgentToolLogicalInvocationKey(
                "tenant-1", "user-1", "agent-1", "exec-1", "inv-1"),
            AttemptId = "att-1",
            InvocationFingerprint = "fp-abc",
            ArgumentsHash = "arghash-001",
            ArgumentsEvaluated = true,
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            AgentRolesHash = "roles-hash-001",
            ToolContract = new AgentToolContractIdentity("tool-1", 1, "toolhash"),
            CapabilityContract = new AgentToolContractIdentity("cap-1", 2, "caphash"),
            InputSchemaContract = new AgentToolSchemaContractIdentity("in-1", 1, "inhash"),
            OutputSchemaContract = new AgentToolSchemaContractIdentity("out-1", 1, "outhash"),
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.AutomaticAllowed,
                AgentToolSideEffectKind.InternalWrite,
                CapabilityRiskLevel.Medium,
                AgentToolApprovalMode.Required,
                new AgentToolBudgetRequirement
                {
                    Category = "compute",
                    CostUnits = 5,
                    MaxCallsPerExecution = 3
                },
                AgentToolAuditMode.Required)
        };
    }

    internal static AgentToolInvocationLease SampleLease()
    {
        return new AgentToolInvocationLease
        {
            AttemptId = "att-1",
            LeaseId = "lease-001",
            FencingToken = 42,
            AcquiredAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 1, 1, 12, 5, 0, TimeSpan.Zero)
        };
    }

    internal static AgentToolApprovalResult SampleApproval()
    {
        return new AgentToolApprovalResult
        {
            Decision = AgentToolApprovalDecision.Approved,
            ClaimState = AgentToolApprovalEvidenceClaimState.Claimed,
            EvidenceId = "ev-001",
            ApproverReference = "appr-001",
            ReasonCode = "approved-by-policy"
        };
    }

    internal static AgentToolBudgetReservation SampleReservation()
    {
        return new AgentToolBudgetReservation
        {
            ReservationId = "res-001",
            AttemptId = "att-1",
            InvocationFingerprint = "fp-abc",
            Category = "compute",
            CostUnits = 5,
            MaxCallsPerExecution = 3,
            State = AgentToolBudgetReservationState.Reserved
        };
    }
}
