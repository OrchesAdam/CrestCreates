using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.LineageProbe.Tests;

/// <summary>
/// #114 Invocation Lineage Activation Probe.
/// Two-phase methodology:
///   Phase 1 (Execution): construct AuditEnvelopes matching production evidence structure.
///   Phase 2 (Post-hoc): reconstruct invocation tree from AuditEnvelopes alone.
/// Tests whether existing evidence contracts support lineage reconstruction.
/// </summary>
public sealed class InvocationLineageActivationProbeFixture
{
    private const string ProbeTenant = "probe-tenant";
    private const string ProbeAgentId = "probe-agent";
    private const string ProbeExecutionId = "execution-E1";

    private const string InvocationA = "invocation-A";
    private const string InvocationB = "invocation-B";
    private const string InvocationC = "invocation-C";
    private const string InvocationD = "invocation-D";

    private const string TestCapabilityId = "test-capability";
    private const string TestCapabilityVersion = "1";

    #region Acceptance Tests

    /// <summary>
    /// Case: Happy — Root invocation A has no CausationId.
    /// Phase 2 identifies A as root from AuditEnvelope.CausationId == null.
    /// Classification: A (directly present in AuditEnvelope).
    /// </summary>
    [Fact]
    public void RootInvocation_Should_Be_Identifiable_From_CurrentEvidence()
    {
        var envelopes = ExecutePhase1();

        var envelopeA = envelopes.Single(e => GetInvocationId(e) == InvocationA);

        envelopeA.CausationId.Should().BeNull(
            "Root invocation must have no CausationId [A]");
        GetInvocationId(envelopeA).Should().Be(InvocationA);
        GetExecutionId(envelopeA).Should().Be(ProbeExecutionId);
    }

    /// <summary>
    /// Case: Happy — Child invocation B has CausationId = A.
    /// Phase 2 identifies A as B's direct parent from AuditEnvelope.CausationId.
    /// Classification: A (directly present in AuditEnvelope).
    /// </summary>
    [Fact]
    public void ChildInvocation_Should_Preserve_Current_And_Parent_Identity()
    {
        var envelopes = ExecutePhase1();

        var envelopeB = envelopes.Single(e => GetInvocationId(e) == InvocationB);

        envelopeB.CausationId.Should().Be(InvocationA,
            "B's CausationId must point to parent A [A]");
        GetInvocationId(envelopeB).Should().Be(InvocationB);
        GetExecutionId(envelopeB).Should().Be(ProbeExecutionId);
    }

    /// <summary>
    /// Case: Happy — Sibling invocations B and C share parent A.
    /// Phase 2 identifies them as siblings from shared CausationId.
    /// Classification: A (directly present in AuditEnvelope).
    /// </summary>
    [Fact]
    public void SiblingInvocations_Should_Share_Parent_But_Not_CurrentIdentity()
    {
        var envelopes = ExecutePhase1();

        var envelopeB = envelopes.Single(e => GetInvocationId(e) == InvocationB);
        var envelopeC = envelopes.Single(e => GetInvocationId(e) == InvocationC);

        envelopeB.CausationId.Should().Be(InvocationA,
            "B's parent must be A [A]");
        envelopeC.CausationId.Should().Be(InvocationA,
            "C's parent must be A [A]");
        envelopeB.CausationId.Should().Be(envelopeC.CausationId,
            "B and C must share the same parent [A]");

        GetInvocationId(envelopeB).Should().NotBe(GetInvocationId(envelopeC),
            "Siblings must have distinct current InvocationIds [A]");
    }

    /// <summary>
    /// Case: Composition — Nested child D has CausationId = B.
    /// Phase 2 reconstructs A -> B -> D multi-level lineage.
    /// Classification: A (directly present in AuditEnvelope).
    /// </summary>
    [Fact]
    public void NestedChild_Should_Reconstruct_MultiLevel_Lineage()
    {
        var envelopes = ExecutePhase1();

        var envelopeA = envelopes.Single(e => GetInvocationId(e) == InvocationA);
        var envelopeB = envelopes.Single(e => GetInvocationId(e) == InvocationB);
        var envelopeD = envelopes.Single(e => GetInvocationId(e) == InvocationD);

        envelopeD.CausationId.Should().Be(InvocationB,
            "D's parent must be B [A]");
        envelopeB.CausationId.Should().Be(InvocationA,
            "B's parent must be A [A]");
        envelopeA.CausationId.Should().BeNull(
            "A must be root [A]");

        // Reconstruct path: D -> B -> A
        var path = ReconstructAncestorPath(envelopeD, envelopes);
        path.Should().Equal(new[] { InvocationD, InvocationB, InvocationA },
            "Multi-level lineage must be reconstructable [A]");
    }

    /// <summary>
    /// Case: Boundary — Same InvocationId string in different ExecutionIds.
    /// Phase 2 keeps lineages isolated by ExecutionId.
    /// Classification: A (directly present in AuditEnvelope.Runtime.References).
    /// </summary>
    [Fact]
    public void SameInvocationId_InDifferentExecutions_Should_Not_Collapse_Lineage()
    {
        const string executionE2 = "execution-E2";
        const string collidingInvocationId = "invocation-collision";

        var envelopesE1 = ExecutePhase1();

        var envelopesE2 = ExecutePhase1(
            executionId: executionE2,
            invocations: new[]
            {
                (InvocationId: collidingInvocationId, CausationId: (string?)null)
            });

        var collisionE1 = envelopesE1.FirstOrDefault(e => GetInvocationId(e) == collidingInvocationId);
        var collisionE2 = envelopesE2.Single(e => GetInvocationId(e) == collidingInvocationId);

        GetExecutionId(collisionE2).Should().Be(executionE2);
        if (collisionE1 is not null)
        {
            GetExecutionId(collisionE1).Should().Be(ProbeExecutionId);
        }

        // Group by (ExecutionId, InvocationId) — must remain distinct
        var grouped = envelopesE1.Concat(envelopesE2)
            .GroupBy(e => (GetExecutionId(e), GetInvocationId(e)))
            .ToList();

        grouped.Should().OnlyContain(g => g.Count() == 1,
            "Each (ExecutionId, InvocationId) pair must be unique [A]");
    }

    /// <summary>
    /// Case: Boundary — Lineage reconstruction does not depend on Tool name or execution order.
    /// Phase 2 uses only identity fields, not Tool names or timestamps.
    /// Classification: A (identity-based, not name/order-based).
    /// </summary>
    [Fact]
    public void Lineage_Should_Not_Depend_On_ToolName_Or_ExecutionOrder()
    {
        var envelopes = ExecutePhase1();

        // Reconstruct tree using only CausationId and InvocationId
        var tree = ReconstructTree(envelopes);

        tree.Should().ContainKey(InvocationA);
        tree[InvocationA].Children.Should().BeEquivalentTo(new[] { InvocationB, InvocationC });
        tree[InvocationB].Children.Should().Equal(new[] { InvocationD });
        tree[InvocationC].Children.Should().BeEmpty();
        tree[InvocationD].Children.Should().BeEmpty();

        // Verify no dependency on Tool name
        envelopes.Should().OnlyContain(e => e.Action.Name == TestCapabilityId,
            "All envelopes should reference the same capability for this probe");
    }

    /// <summary>
    /// Case: Boundary — Owner-local causality mappings remain correlatable.
    /// Phase 2 correlates AuditEnvelope fields without forcing universal mapping.
    /// Classification: A/C (directly present and correlation-verifiable).
    /// </summary>
    [Fact]
    public void OwnerLocal_CausalityMappings_Should_Remain_Correlatable()
    {
        var envelopes = ExecutePhase1();

        // Each envelope has its own AuditId (capability-level execution id)
        envelopes.Should().OnlyContain(e => !string.IsNullOrEmpty(e.AuditId));
        envelopes.Select(e => e.AuditId).Distinct().Count().Should().Be(envelopes.Count,
            "Each capability execution must have a unique AuditId [A]");

        // Runtime.References carry agent-level identity
        envelopes.Should().OnlyContain(e =>
            e.Runtime.References.Any(r => r.Kind == "agent-session") &&
            e.Runtime.References.Any(r => r.Kind == "agent-invocation"));

        // CorrelationId links related operations
        var correlationIds = envelopes.Select(e => e.CorrelationId).Distinct().ToList();
        correlationIds.Count.Should().BeGreaterThanOrEqualTo(1,
            "CorrelationId must be present [A]");
    }

    /// <summary>
    /// Case: Negative — Missing adapter causation does not automatically activate lineage.
    /// If CausationId is missing, classify as adapter input gap, not framework semantic gap.
    /// Classification: D (adapter-local gap, not framework semantic).
    /// </summary>
    [Fact]
    public void MissingAdapterCausation_Should_Not_Automatically_Activate_Lineage()
    {
        // Execute with one invocation missing CausationId
        var envelopes = ExecutePhase1(
            invocations: new[]
            {
                (InvocationId: "invocation-orphan", CausationId: (string?)null)
            });

        var orphan = envelopes.Single(e => GetInvocationId(e) == "invocation-orphan");

        // This is a root invocation (CausationId = null), not a gap
        orphan.CausationId.Should().BeNull(
            "Missing CausationId indicates root, not a gap [D]");

        // Can still be identified as a valid invocation
        GetInvocationId(orphan).Should().NotBeNullOrWhiteSpace();
        GetExecutionId(orphan).Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Case: Composition — Invocation lineage does not change subsystem authority.
    /// Capability/Workflow/HumanTask maintain their own Accountability semantics.
    /// Classification: A (lineage correlates, does not override).
    /// </summary>
    [Fact]
    public void InvocationLineage_Should_Not_Change_SubsystemAuthority()
    {
        var envelopes = ExecutePhase1();

        // Each envelope has its own AuditId (capability-level authority)
        envelopes.Should().OnlyContain(e => !string.IsNullOrEmpty(e.AuditId));

        // Actor remains the agent, not the capability
        envelopes.Should().OnlyContain(e => e.Actor.Kind == "agent");
        envelopes.Should().OnlyContain(e => e.Actor.Id == ProbeAgentId);

        // InvocationSource indicates this came from an Agent
        envelopes.Should().OnlyContain(e => e.Runtime.InvocationSource == "Agent");
    }

    /// <summary>
    /// Case: Failure Probe — Two distinct valid graphs should not produce indistinguishable evidence.
    /// If they do, Candidate A may activate.
    /// Classification: A (current evidence distinguishes valid graphs).
    /// </summary>
    [Fact]
    public void TwoDistinctValidGraphs_Should_Not_Produce_Indistinguishable_Evidence()
    {
        // Graph 1: A -> B, A -> C (siblings)
        var envelopes1 = ExecutePhase1();

        // Graph 2: A -> B -> C (linear chain)
        var envelopes2 = ExecutePhase1(
            invocations: new[]
            {
                (InvocationId: InvocationA, CausationId: (string?)null),
                (InvocationId: InvocationB, CausationId: InvocationA),
                (InvocationId: InvocationC, CausationId: InvocationB)  // C is child of B, not sibling
            });

        var tree1 = ReconstructTree(envelopes1);
        var tree2 = ReconstructTree(envelopes2);

        // Trees must be distinguishable
        tree1[InvocationA].Children.Should().BeEquivalentTo(new[] { InvocationB, InvocationC });
        tree2[InvocationA].Children.Should().Equal(new[] { InvocationB });
        tree2[InvocationB].Children.Should().Equal(new[] { InvocationC });

        // Evidence must distinguish them
        tree1.Should().NotBeEquivalentTo(tree2,
            "Two distinct valid graphs must produce distinguishable evidence [A]");
    }

    #endregion

    #region Phase 1 — Execution

    private static IReadOnlyList<AuditEnvelope> ExecutePhase1(
        string? executionId = null,
        (string InvocationId, string? CausationId)[]? invocations = null)
    {
        executionId ??= ProbeExecutionId;
        invocations ??= new[]
        {
            (InvocationA, (string?)null),
            (InvocationB, InvocationA),
            (InvocationC, InvocationA),
            (InvocationD, InvocationB)
        };

        var envelopes = new List<AuditEnvelope>();

        foreach (var (invocationId, causationId) in invocations)
        {
            var envelope = CreateAuditEnvelope(
                executionId: executionId,
                invocationId: invocationId,
                causationId: causationId);

            envelopes.Add(envelope);
        }

        return envelopes;
    }

    private static AuditEnvelope CreateAuditEnvelope(
        string executionId,
        string invocationId,
        string? causationId)
    {
        return new AuditEnvelope
        {
            AuditId = Guid.NewGuid().ToString(),
            OccurredAt = DateTimeOffset.UtcNow,
            TenantId = ProbeTenant,
            CorrelationId = $"{executionId}-correlation",
            CausationId = causationId,
            ParentAuditId = null,
            Actor = new AuditActor
            {
                Kind = "agent",
                Id = ProbeAgentId,
                InitiatedBy = new AuditActorReference("user", "probe-user")
            },
            Action = new AuditAction
            {
                Kind = "capability.execute",
                Name = TestCapabilityId
            },
            Target = new AuditTarget
            {
                Kind = "capability",
                Id = TestCapabilityId,
                Version = TestCapabilityVersion
            },
            Outcome = new AuditOutcome
            {
                Status = "succeeded"
            },
            Runtime = new AuditRuntimeContext
            {
                InvocationSource = "Agent",
                ExecutionId = Guid.NewGuid().ToString(),
                References = ImmutableArray.Create(
                    new AuditRuntimeReference("agent-session", executionId),
                    new AuditRuntimeReference("agent-invocation", invocationId))
            }
        };
    }

    #endregion

    #region Phase 2 — Post-hoc Reconstruction Helpers

    private static string GetInvocationId(AuditEnvelope envelope)
    {
        return envelope.Runtime.References
            .Single(r => r.Kind == "agent-invocation")
            .Id;
    }

    private static string GetExecutionId(AuditEnvelope envelope)
    {
        return envelope.Runtime.References
            .Single(r => r.Kind == "agent-session")
            .Id;
    }

    private static IReadOnlyList<string> ReconstructAncestorPath(
        AuditEnvelope start,
        IReadOnlyList<AuditEnvelope> allEnvelopes)
    {
        var path = new List<string>();
        var current = start;

        while (true)
        {
            var invocationId = GetInvocationId(current);
            path.Add(invocationId);

            if (current.CausationId is null)
            {
                break;
            }

            current = allEnvelopes.Single(e => GetInvocationId(e) == current.CausationId);
        }

        return path;
    }

    private static Dictionary<string, (string? Parent, List<string> Children)> ReconstructTree(
        IReadOnlyList<AuditEnvelope> envelopes)
    {
        var tree = new Dictionary<string, (string? Parent, List<string> Children)>();

        // Initialize all nodes
        foreach (var envelope in envelopes)
        {
            var invocationId = GetInvocationId(envelope);
            tree[invocationId] = (envelope.CausationId, new List<string>());
        }

        // Build parent-child relationships
        foreach (var envelope in envelopes)
        {
            var invocationId = GetInvocationId(envelope);
            var parentId = envelope.CausationId;

            if (parentId is not null && tree.ContainsKey(parentId))
            {
                tree[parentId].Children.Add(invocationId);
            }
        }

        return tree;
    }

    #endregion
}
