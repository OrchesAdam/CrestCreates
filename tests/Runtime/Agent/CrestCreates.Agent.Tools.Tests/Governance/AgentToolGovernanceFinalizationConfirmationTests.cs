using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Governance;

/// <summary>
/// Issue #73 P1 remediation — the Finalizer must not be a second, looser
/// semantic authority. <see cref="AgentToolPreDispatchFinalizer.ResolveAuditConfirmation"/>
/// confirms an exact <c>Completed</c> finalization ONLY through the canonical
/// <see cref="AgentToolGovernancePreDispatchComparer.Equivalent(...)"/>; the
/// narrow identity-only check is reserved for classifying an already-persisted
/// Indeterminate for the same logical attempt.
/// </summary>
public class AgentToolGovernanceFinalizationConfirmationTests
{
    private static AgentToolLogicalInvocationKey SampleKey()
        => new("tenant-1", "user-1", "agent-1", "execution-1", "invocation-1");

    private static AgentToolGovernanceContext StubGovernance()
        => GovernanceTestData.Context(
            attemptId: "attempt-1",
            fingerprint: "fingerprint-1",
            invocationId: "invocation-1");

    private static AgentToolGovernanceAuditContext StubAuditContext()
        => GovernanceTestData.AuditContext(StubGovernance());

    private static AgentToolInvocationLease StubLease()
        => GovernanceTestData.Lease("attempt-1");

    private static AgentToolBudgetReservation StubReservation()
        => GovernanceTestData.Reservation(
            StubGovernance(),
            AgentToolBudgetReservationState.Released);

    private static AgentToolInvocationOutcome StubOutcome()
        => new()
        {
            Kind = AgentToolInvocationOutcomeKind.InProgress,
            Code = "AGENT_TOOL_INVOCATION_NOT_ACQUIRED",
            Message = "The tool invocation could not acquire execution ownership.",
            StructuredOutput = null
        };

    private static AgentToolGovernanceFinalizationRecord StubFinalization()
        => new()
        {
            AuditId = "audit-1",
            Context = StubAuditContext(),
            Lease = StubLease(),
            DispatchStarted = false,
            BudgetReservation = StubReservation(),
            AttemptState = AgentToolGovernanceAttemptFinalState.Released,
            InvocationState = null,
            Outcome = StubOutcome(),
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(StubOutcome()),
            AuditFacts = Array.Empty<AgentToolAuditFact>(),
            ReasonCode = "released_no_dispatch"
        };

    private static AgentToolGovernanceFinalizationResult Finalized(
        AgentToolGovernanceFinalizationRecord record)
        => new()
        {
            Status = AgentToolGovernanceFinalizationStatus.Finalized,
            Record = record
        };

    [Fact]
    public void ExactFinalization_Should_ConfirmCompleted()
    {
        var record = StubFinalization();

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(record),
            record);

        confirmation.Should().Be(AgentToolAuditConfirmation.Completed);
    }

    [Fact]
    public void ChangedLeaseExpiry_Should_NotConfirmCompleted()
    {
        var expected = StubFinalization();
        var actual = expected with
        {
            Lease = StubLease() with
            {
                ExpiresAt = DateTimeOffset.Parse("2026-08-01T00:06:00Z")
            }
        };

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(actual),
            expected);

        // Lease timing is part of the dispatch-authorizing fact set; a changed
        // expiry must not be accepted as an exact Completed confirmation.
        confirmation.Should().NotBe(AgentToolAuditConfirmation.Completed);
    }

    [Fact]
    public void ChangedOutcome_Should_NotConfirmCompleted()
    {
        var expected = StubFinalization();
        var changedOutcome = StubOutcome() with
        {
            Message = "A different message."
        };
        var actual = expected with
        {
            Outcome = changedOutcome,
            // A lying/stale hash does not rescue the mismatched content.
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(changedOutcome)
        };

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(actual),
            expected);

        confirmation.Should().NotBe(AgentToolAuditConfirmation.Completed);
    }

    [Fact]
    public void ChangedAuditFacts_Should_NotConfirmCompleted()
    {
        var expected = StubFinalization();
        var actual = expected with
        {
            AuditFacts = new[]
            {
                new AgentToolAuditFact
                {
                    Code = "fact-count",
                    Value = "1",
                    Kind = AgentToolAuditFactKind.Internal
                }
            }
        };

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(actual),
            expected);

        confirmation.Should().NotBe(AgentToolAuditConfirmation.Completed);
    }

    [Fact]
    public void MatchingHashWithDifferentContent_Should_NotConfirmCompleted()
    {
        var expected = StubFinalization();
        // Same OutcomeHash digest but different structured content: the hash is
        // computed over the outcome shape (kind/code/issues), so a different
        // message/structured output with identical shape still hashes equal.
        // The canonical comparer compares the direct outcome content too, so a
        // content mismatch must never confirm Completed even with an equal hash.
        var changedOutcome = StubOutcome() with
        {
            Message = "Same shape, different user-facing message."
        };
        var actual = expected with
        {
            Outcome = changedOutcome,
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(changedOutcome)
        };

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(actual),
            expected);

        confirmation.Should().NotBe(AgentToolAuditConfirmation.Completed);
    }

    [Fact]
    public void MatchingHashWithDifferentContent_Should_NotConfirmCompleted_WithSameComputedHash()
    {
        // Explicitly pin the "hash can match while content differs" scenario:
        // the OutcomeHash field is identical but the outcome content differs.
        // The canonical comparer must reject it on direct content comparison.
        var expected = StubFinalization();
        var changedOutcome = StubOutcome() with
        {
            // Same kind + code + issues → identical digest, but different
            // content (the hasher excludes the user-facing message).
            Message = "Same shape, different user-facing message."
        };
        var actual = expected with
        {
            Outcome = changedOutcome,
            OutcomeHash = AgentToolGovernanceOutcomeHasher.Compute(changedOutcome)
        };
        actual.OutcomeHash.Should().Be(expected.OutcomeHash);

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(actual),
            expected);

        confirmation.Should().NotBe(AgentToolAuditConfirmation.Completed);
    }

    [Fact]
    public void ExistingIndeterminateForSameIdentity_Should_ReturnIndeterminate()
    {
        // The invoker expects a Released no-dispatch finalization. The auditor
        // already persisted an Indeterminate for the SAME logical attempt (it
        // was fenced before this retry). Identity matches, exact content does
        // not, and the persisted state is Indeterminate → Indeterminate.
        var expected = StubFinalization();
        var actual = expected with
        {
            AttemptState = AgentToolGovernanceAttemptFinalState.Indeterminate,
            InvocationState = AgentToolInvocationTerminalState.Indeterminate,
            ReasonCode = "audit_failure"
        };

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(actual),
            expected);

        confirmation.Should().Be(AgentToolAuditConfirmation.Indeterminate);
    }

    [Fact]
    public void ExistingIndeterminateForDifferentIdentity_Should_NotConfirmCompleted()
    {
        // The persisted Indeterminate belongs to a different attempt — identity
        // check fails, so it is neither exact nor indeterminate.
        var expected = StubFinalization();
        var actual = expected with
        {
            AttemptState = AgentToolGovernanceAttemptFinalState.Indeterminate,
            InvocationState = AgentToolInvocationTerminalState.Indeterminate,
            ReasonCode = "audit_failure",
            Context = StubAuditContext() with
            {
                AttemptId = "other-attempt"
            }
        };

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            Finalized(actual),
            expected);

        confirmation.Should().Be(AgentToolAuditConfirmation.Conflict);
    }

    [Fact]
    public void NotFinalized_Should_ReturnUnconfirmed()
    {
        var record = StubFinalization();

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.NotFinalized,
                Record = null
            },
            record);

        confirmation.Should().Be(AgentToolAuditConfirmation.Unconfirmed);
    }

    [Fact]
    public void NullRecord_Should_ReturnUnconfirmed()
    {
        var record = StubFinalization();

        var confirmation = AgentToolPreDispatchFinalizer.ResolveAuditConfirmation(
            new AgentToolGovernanceFinalizationResult
            {
                Status = AgentToolGovernanceFinalizationStatus.Finalized,
                Record = null
            },
            record);

        confirmation.Should().Be(AgentToolAuditConfirmation.Unconfirmed);
    }
}
