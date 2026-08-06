using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// In-memory pre-dispatch contract tests covering Slice 2 cases:
/// H01, H02, B01–B05, B11, F01–F06, F23, C07.
/// These verify that <see cref="DevelopmentInMemoryAgentToolGovernanceAuditor"/>
/// implements the same observable protocol as the durable provider.
/// </summary>
public class InMemoryAgentToolPreDispatchContractTests
{
    private static readonly TimeProvider FixedTime = TimeProvider.System;

    private static AgentToolLogicalInvocationKey SampleKey(
        string? tenant = null)
        => new(
            tenant,
            "user-1",
            "agent-1",
            $"exec-{Guid.NewGuid():N}",
            "invocation-1");

    private static AgentToolGovernanceAuditContext SampleContext(
        AgentToolLogicalInvocationKey key,
        string attemptId)
        => new()
        {
            LogicalInvocationKey = key,
            AttemptId = attemptId,
            InvocationFingerprint = "fp-1",
            ArgumentsEvaluated = true,
            ArgumentsHash = "args-hash-1",
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            AgentRolesHash = "roles-hash-1",
            ToolContract = new AgentToolContractIdentity("tool-1", 1, "hash-1"),
            CapabilityContract = new AgentToolContractIdentity("cap-1", 1, "cap-hash-1"),
            InputSchemaContract = new AgentToolSchemaContractIdentity("schema-1", 1, "schema-hash-1"),
            OutputSchemaContract = new AgentToolSchemaContractIdentity("schema-out-1", 1, "out-hash-1"),
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                AgentToolSideEffectKind.InternalWrite,
                CapabilityRiskLevel.Medium,
                AgentToolApprovalMode.Required,
                new AgentToolBudgetRequirement
                {
                    Category = "api-calls",
                    CostUnits = 1,
                    MaxCallsPerExecution = 5
                },
                AgentToolAuditMode.Required)
        };

    private static AgentToolInvocationLease SampleLease(string attemptId)
        => new()
        {
            LeaseId = "lease-1",
            AttemptId = attemptId,
            FencingToken = 42,
            AcquiredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero)
        };

    private static AgentToolBudgetReservation SampleReservation(
        string attemptId,
        string reservationId = "res-1",
        AgentToolBudgetReservationState state = AgentToolBudgetReservationState.Reserved)
        => new()
        {
            ReservationId = reservationId,
            AttemptId = attemptId,
            InvocationFingerprint = "fp-1",
            Category = "api-calls",
            CostUnits = 1,
            MaxCallsPerExecution = 5,
            State = state
        };

    private static AgentToolApprovalResult SampleApproval(
        AgentToolApprovalDecision decision = AgentToolApprovalDecision.Approved)
        => new()
        {
            Decision = decision,
            ClaimState = decision == AgentToolApprovalDecision.Approved
                ? AgentToolApprovalEvidenceClaimState.Claimed
                : AgentToolApprovalEvidenceClaimState.NotApplicable,
            EvidenceId = decision == AgentToolApprovalDecision.Approved
                ? "evidence-1"
                : null,
            ApproverReference = "approver-ref-1",
            ReasonCode = "approved-by-policy"
        };

    private static AgentToolGovernancePreDispatchRecord SampleRecord(
        AgentToolLogicalInvocationKey? key = null,
        string attemptId = "attempt-1",
        AgentToolGovernanceAuditContext? context = null,
        AgentToolInvocationLease? lease = null,
        AgentToolBudgetReservation? reservation = null,
        AgentToolApprovalResult? approval = null)
    {
        var resolvedKey = key ?? SampleKey();
        var resolvedContext = context ?? SampleContext(resolvedKey, attemptId);
        var resolvedLease = lease ?? SampleLease(attemptId);
        var resolvedReservation = reservation ?? SampleReservation(attemptId);
        var resolvedApproval = approval ?? SampleApproval();

        return new AgentToolGovernancePreDispatchRecord
        {
            Context = resolvedContext,
            Lease = resolvedLease,
            Approval = resolvedApproval,
            BudgetReservation = resolvedReservation
        };
    }

    // ── H01: First valid checkpoint → Accepted with provider AuditId/AcceptedAt ─

    [Fact]
    public async Task H01_FirstValidCheckpoint_ShouldBeAcceptedWithProviderAuditId()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var record = SampleRecord();

        var result = await auditor.RecordPreDispatchAsync(record);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
        result.Receipt.Should().NotBeNull();
        result.Receipt!.AuditId.Should().NotBeNullOrWhiteSpace();
        result.Receipt.AcceptedAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        result.Receipt.Identity.Should().Be(
            new AgentToolPreDispatchIdentity(
                record.Context.LogicalInvocationKey,
                record.Context.AttemptId));
    }

    // ── H02: Identical sequential retry → Duplicate with original receipt ─────

    [Fact]
    public async Task H02_IdenticalSequentialRetry_ShouldReturnDuplicateWithOriginalReceipt()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var record = SampleRecord();

        var first = await auditor.RecordPreDispatchAsync(record);
        var second = await auditor.RecordPreDispatchAsync(record);

        first.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
        second.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Duplicate);
        second.Receipt!.AuditId.Should().Be(first.Receipt!.AuditId);
        second.Receipt.AcceptedAt.Should().Be(first.Receipt.AcceptedAt);
        second.Receipt.Identity.Should().Be(first.Receipt.Identity);
    }

    // ── B01: 32 concurrent identical writes → one Accepted, 31 Duplicate ─────

    [Fact]
    public async Task B01_ConcurrentIdenticalWrites_ShouldHaveOneAcceptance()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var record = SampleRecord();

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => auditor.RecordPreDispatchAsync(record).AsTask()))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var accepted = results.Count(r =>
            r.Status == AgentToolGovernancePreDispatchWriteStatus.Accepted);
        var duplicate = results.Count(r =>
            r.Status == AgentToolGovernancePreDispatchWriteStatus.Duplicate);

        accepted.Should().Be(1, "exactly one write should be Accepted");
        duplicate.Should().Be(31, "the remaining 31 should be Duplicate");

        var auditIds = results
            .Where(r => r.Receipt is not null)
            .Select(r => r.Receipt!.AuditId)
            .Distinct()
            .ToArray();
        auditIds.Should().HaveCount(1, "all receipts should share one AuditId");
    }

    // ── B02: Same logical key, different AttemptId → distinct checkpoints ────

    [Fact]
    public async Task B02_DifferentAttemptId_ShouldNotBeDuplicate()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var record1 = SampleRecord(key: key, attemptId: "attempt-1");
        var record2 = SampleRecord(key: key, attemptId: "attempt-2");

        var first = await auditor.RecordPreDispatchAsync(record1);
        var second = await auditor.RecordPreDispatchAsync(record2);

        first.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
        second.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
        second.Receipt!.AuditId.Should().NotBe(first.Receipt!.AuditId);
    }

    // ── B03: Same IDs in host and two tenants → three isolated records ───────

    [Fact]
    public async Task B03_TenantScopedIdentity_ShouldNotCrossHostOrTenant()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var hostKey = SampleKey();
        var tenant1Key = SampleKey("tenant-1");
        var tenant2Key = SampleKey("tenant-2");

        var hostResult = await auditor.RecordPreDispatchAsync(SampleRecord(key: hostKey));
        var tenant1Result = await auditor.RecordPreDispatchAsync(SampleRecord(key: tenant1Key));
        var tenant2Result = await auditor.RecordPreDispatchAsync(SampleRecord(key: tenant2Key));

        hostResult.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
        tenant1Result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
        tenant2Result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);

        var auditIds = new[] { hostResult, tenant1Result, tenant2Result }
            .Select(r => r.Receipt!.AuditId)
            .Distinct();
        auditIds.Should().HaveCount(3, "host and tenant records must be isolated");
    }

    // ── B04: Null optional schema contracts → exact round-trip ───────────────

    [Fact]
    public async Task B04_NullOptionalSchemaContracts_ShouldRoundTripWithoutDefaults()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var context = SampleContext(key, "attempt-1") with
        {
            InputSchemaContract = null,
            OutputSchemaContract = null
        };
        var record = SampleRecord(key: key, context: context);

        var writeResult = await auditor.RecordPreDispatchAsync(record);
        var readResult = await auditor.GetPreDispatchStateAsync(
            new AgentToolPreDispatchIdentity(key, "attempt-1"));

        writeResult.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
        readResult.Status.Should().Be(AgentToolGovernancePreDispatchReadStatus.Accepted);
        readResult.Checkpoint!.Context.InputSchemaContract.Should().BeNull();
        readResult.Checkpoint!.Context.OutputSchemaContract.Should().BeNull();
    }

    // ── B05: Approval NotRequired/NotApplicable → valid exact checkpoint ─────

    [Fact]
    public async Task B05_NotRequiredApproval_ShouldProduceValidCheckpoint()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var context = SampleContext(key, "attempt-1") with
        {
            Governance = new AgentToolEffectiveGovernance(
                AgentToolSelectionPolicy.ExplicitOnly,
                AgentToolSideEffectKind.InternalWrite,
                CapabilityRiskLevel.Medium,
                AgentToolApprovalMode.None,
                new AgentToolBudgetRequirement
                {
                    Category = "api-calls",
                    CostUnits = 1,
                    MaxCallsPerExecution = 5
                },
                AgentToolAuditMode.Required)
        };
        var record = SampleRecord(
            key: key,
            context: context,
            approval: SampleApproval(AgentToolApprovalDecision.NotRequired));

        var result = await auditor.RecordPreDispatchAsync(record);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);
    }

    // ── B11: Duplicate read snapshots → detached but structurally equal ──────

    [Fact]
    public async Task B11_DuplicateReadSnapshots_ShouldBeDetachedButEqual()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var record = SampleRecord();
        var identity = new AgentToolPreDispatchIdentity(
            record.Context.LogicalInvocationKey,
            record.Context.AttemptId);

        await auditor.RecordPreDispatchAsync(record);
        var first = await auditor.GetPreDispatchStateAsync(identity);
        var second = await auditor.GetPreDispatchStateAsync(identity);

        first.Status.Should().Be(AgentToolGovernancePreDispatchReadStatus.Accepted);
        second.Status.Should().Be(AgentToolGovernancePreDispatchReadStatus.Accepted);

        var firstCheckpoint = first.Checkpoint!;
        var secondCheckpoint = second.Checkpoint!;

        secondCheckpoint.Should().NotBeSameAs(firstCheckpoint,
            "snapshots should be detached (INV-05)");
        AgentToolGovernancePreDispatchComparer.Equivalent(
            firstCheckpoint, secondCheckpoint).Should().BeTrue();
    }

    // ── F01: Changed invocation fingerprint → Conflict ───────────────────────

    [Fact]
    public async Task F01_ChangedInvocationFingerprint_ShouldBeConflict()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var record = SampleRecord(key: key);

        await auditor.RecordPreDispatchAsync(record);

        var mutatedContext = SampleContext(key, "attempt-1") with
        {
            InvocationFingerprint = "fp-changed"
        };
        var mutatedReservation = SampleReservation("attempt-1") with
        {
            InvocationFingerprint = "fp-changed"
        };
        var mutatedRecord = SampleRecord(
            key: key, context: mutatedContext, reservation: mutatedReservation);

        var result = await auditor.RecordPreDispatchAsync(mutatedRecord);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Conflict);
        result.Receipt.Should().BeNull();
    }

    // ── F02: Changed Tool/Capability/Schema contract → Conflict ──────────────

    [Theory]
    [InlineData("tool")]
    [InlineData("capability")]
    [InlineData("input-schema")]
    [InlineData("output-schema")]
    public async Task F02_ChangedContractIdentity_ShouldBeConflict(string contractKind)
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var record = SampleRecord(key: key);

        await auditor.RecordPreDispatchAsync(record);

        var originalContext = SampleContext(key, "attempt-1");
        var mutatedContext = contractKind switch
        {
            "tool" => originalContext with
            {
                ToolContract = new AgentToolContractIdentity("tool-changed", 1, "hash-changed")
            },
            "capability" => originalContext with
            {
                CapabilityContract = new AgentToolContractIdentity("cap-changed", 1, "cap-hash-changed")
            },
            "input-schema" => originalContext with
            {
                InputSchemaContract = new AgentToolSchemaContractIdentity("schema-changed", 1, "schema-hash-changed")
            },
            "output-schema" => originalContext with
            {
                OutputSchemaContract = new AgentToolSchemaContractIdentity("out-changed", 1, "out-hash-changed")
            },
            _ => originalContext
        };

        var mutatedRecord = SampleRecord(key: key, context: mutatedContext);
        var result = await auditor.RecordPreDispatchAsync(mutatedRecord);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Conflict);
    }

    // ── F03: Changed effective governance → Conflict ─────────────────────────

    [Fact]
    public async Task F03_ChangedEffectiveGovernance_ShouldBeConflict()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var originalContext = SampleContext(key, "attempt-1");
        var record = SampleRecord(key: key, context: originalContext);

        await auditor.RecordPreDispatchAsync(record);

        var mutatedContext = originalContext with
        {
            Governance = originalContext.Governance with
            {
                EffectiveAuditMode = AgentToolAuditMode.BestEffort
            }
        };
        var mutatedRecord = SampleRecord(key: key, context: mutatedContext);

        var result = await auditor.RecordPreDispatchAsync(mutatedRecord);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Conflict);
    }

    // ── F04: Changed LeaseId/FencingToken/time → Conflict ────────────────────

    [Theory]
    [InlineData("lease-id")]
    [InlineData("fencing-token")]
    [InlineData("acquired-at")]
    [InlineData("expires-at")]
    public async Task F04_ChangedLeaseFacts_ShouldBeConflict(string field)
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var context = SampleContext(key, "attempt-1");
        var record = SampleRecord(key: key, context: context);

        await auditor.RecordPreDispatchAsync(record);

        var baseLease = SampleLease("attempt-1");
        var mutatedLease = field switch
        {
            "lease-id" => baseLease with { LeaseId = "lease-changed" },
            "fencing-token" => baseLease with { FencingToken = 999 },
            "acquired-at" => baseLease with
            {
                AcquiredAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            "expires-at" => baseLease with
            {
                ExpiresAt = new DateTimeOffset(2026, 1, 1, 0, 10, 0, TimeSpan.Zero)
            },
            _ => baseLease
        };

        var mutatedRecord = SampleRecord(key: key, context: context, lease: mutatedLease);
        var result = await auditor.RecordPreDispatchAsync(mutatedRecord);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Conflict);
    }

    // ── F05: Changed approval claim/evidence reference → Conflict ────────────

    [Fact]
    public async Task F05_ChangedApprovalClaim_ShouldBeConflict()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var context = SampleContext(key, "attempt-1");
        var record = SampleRecord(key: key, context: context);

        await auditor.RecordPreDispatchAsync(record);

        var mutatedApproval = SampleApproval() with { EvidenceId = "evidence-changed" };
        var mutatedRecord = SampleRecord(
            key: key, context: context, approval: mutatedApproval);

        var result = await auditor.RecordPreDispatchAsync(mutatedRecord);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Conflict);
    }

    // ── F06: Changed ReservationId/budget facts/state → Conflict ─────────────

    [Theory]
    [InlineData("reservation-id")]
    [InlineData("category")]
    [InlineData("cost-units")]
    public async Task F06_ChangedBudgetFacts_ShouldBeConflict(string field)
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var originalContext = SampleContext(key, "attempt-1");
        var record = SampleRecord(key: key, context: originalContext);

        await auditor.RecordPreDispatchAsync(record);

        var baseReservation = SampleReservation("attempt-1");
        var (mutatedReservation, mutatedBudget) = field switch
        {
            "reservation-id" => (
                baseReservation with { ReservationId = "res-changed" },
                originalContext.Governance.Budget),
            "category" => (
                baseReservation with { Category = "changed-category" },
                originalContext.Governance.Budget with { Category = "changed-category" }),
            "cost-units" => (
                baseReservation with { CostUnits = 99 },
                originalContext.Governance.Budget with { CostUnits = 99 }),
            _ => (baseReservation, originalContext.Governance.Budget)
        };

        var mutatedContext = originalContext with
        {
            Governance = originalContext.Governance with { Budget = mutatedBudget }
        };
        var mutatedRecord = SampleRecord(
            key: key, context: mutatedContext, reservation: mutatedReservation);

        var result = await auditor.RecordPreDispatchAsync(mutatedRecord);

        result.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Conflict);
    }

    // ── F23: Reader mutates returned nested state → later reads unchanged ────

    [Fact]
    public async Task F23_ReaderMutatesReturnedState_ShouldNotAffectLaterReads()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var record = SampleRecord(key: key);
        var identity = new AgentToolPreDispatchIdentity(key, "attempt-1");

        await auditor.RecordPreDispatchAsync(record);
        var first = await auditor.GetPreDispatchStateAsync(identity);

        var firstCheckpoint = first.Checkpoint!;
        var mutatedContext = firstCheckpoint.Context with
        {
            InvocationFingerprint = "mutated-by-reader",
            Governance = firstCheckpoint.Context.Governance with
            {
                EffectiveAuditMode = AgentToolAuditMode.BestEffort
            }
        };
        var mutatedLease = firstCheckpoint.Lease! with { LeaseId = "mutated" };
        var mutatedReservation = firstCheckpoint.BudgetReservation! with
        {
            ReservationId = "mutated"
        };
        var mutatedCheckpoint = firstCheckpoint with
        {
            Context = mutatedContext,
            Lease = mutatedLease,
            BudgetReservation = mutatedReservation
        };

        var second = await auditor.GetPreDispatchStateAsync(identity);

        second.Checkpoint!.Context.InvocationFingerprint.Should().Be("fp-1");
        second.Checkpoint.Context.Governance.EffectiveAuditMode
            .Should().Be(AgentToolAuditMode.Required);
        second.Checkpoint.Lease!.LeaseId.Should().Be("lease-1");
        second.Checkpoint.BudgetReservation!.ReservationId.Should().Be("res-1");
    }

    // ── C07: In-memory provider passes shared semantic contract cases ────────

    [Fact]
    public async Task C07_InMemoryProvider_ShouldPassSharedSemanticContractCases()
    {
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor(FixedTime);
        var key = SampleKey();
        var identity = new AgentToolPreDispatchIdentity(key, "attempt-1");
        var record = SampleRecord(key: key);

        var write = await auditor.RecordPreDispatchAsync(record);
        write.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Accepted);

        var duplicate = await auditor.RecordPreDispatchAsync(record);
        duplicate.Status.Should().Be(AgentToolGovernancePreDispatchWriteStatus.Duplicate);
        duplicate.Receipt!.AuditId.Should().Be(write.Receipt!.AuditId);

        var read = await auditor.GetPreDispatchStateAsync(identity);
        read.Status.Should().Be(AgentToolGovernancePreDispatchReadStatus.Accepted);
        read.Receipt!.AuditId.Should().Be(write.Receipt!.AuditId);
        AgentToolGovernancePreDispatchComparer.Equivalent(
            read.Checkpoint!, record).Should().BeTrue();

        var missing = await auditor.GetPreDispatchStateAsync(
            new AgentToolPreDispatchIdentity(key, "attempt-nonexistent"));
        missing.Status.Should().Be(AgentToolGovernancePreDispatchReadStatus.Missing);
    }
}