using CrestCreates.Runtime.Persistence.Testing.Evidence;
using CrestCreates.Runtime.Persistence.Testing.Manifest;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

public sealed class Phase9cExecutableEvidenceTests
{
    [Fact]
    public void FrozenCaseMatrix_ShouldRetainAllRcaEvidenceSurfaces()
    {
        Phase9cEvidenceLedger.ValidateFrozenManifest();
        Phase9cEvidenceRunnerCatalog.ValidateAuthority();
        Phase9cEvidenceRunnerCatalog.RequiredTuples.Select(tuple => tuple.AcceptanceName)
            .Distinct(StringComparer.Ordinal).Should().HaveCount(170);
        Phase9cEvidenceRunnerCatalog.AcceptanceCaseBindings
            .Select(binding => $"{binding.CaseId}/{binding.AcceptanceName}")
            .Should().OnlyHaveUniqueItems();
        Phase9cEvidenceRunnerCatalog.RequiredTuples.Count.Should().Be(444);

        var rca01 = Phase9cEvidenceRunnerCatalog.ForAcceptance("WorkflowContinuationAcceptance_Integrity_Should_Use_FrozenV1Projection");
        rca01.Select(tuple => tuple.Runner).Should().BeEquivalentTo(["WF", "SH", "IM", "PG", "BND"]);
        rca01.Select(tuple => tuple.CaseId).Distinct(StringComparer.Ordinal).Should().Equal("RCA01");

        var rca02 = Phase9cEvidenceRunnerCatalog.ForAcceptance("Same_CompletionEventId_WithChangedOutcomeOrResult_Should_Conflict");
        rca02.Select(tuple => tuple.Runner).Should().BeEquivalentTo(["WF", "SH", "IM", "PG"]);
        rca02.Select(tuple => tuple.CaseId).Distinct(StringComparer.Ordinal).Should().Equal("RCA02");
    }

    [Fact]
    public void CriticalPhase9cSources_ShouldRemainBoundToProductionCode()
    {
        Phase9cEvidenceLedger.ValidateFrozenManifest();
        var root = FindRepositoryRoot();
        var bindings = new (string Name, string Source, string ProductionMarker)[]
        {
            ("NonCooperative_OptionalHandler_Should_Not_Prevent_ReliableAckProgress", "tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/OptionalLocalEventCompatibilityTests.cs", "OptionalTrackerCapacity_ShouldReserveBeforeStartingWork"),
            ("Procurement_ExactDecisionReplay_Should_Be_Duplicate", "samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/ProcurementHumanTaskIntegration.cs", "OutboxRequiredConsumerResult.Duplicate"),
            ("ActivationReview_ExactDecisionReplay_Should_Be_Duplicate", "src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DescriptorActivationReviewHumanTaskEventHandler.cs", "ReviewPayloadInvalid"),
            ("DB_CompositionPreflight_Should_Run_After_RuntimeSchemaCompatibility", "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlHumanTaskCompletionObligationPreflight.cs", "ValidateAsync"),
            ("WorkflowContinuationAcceptance_Integrity_Should_Use_FrozenV1Projection", "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlWorkflowContinuationAcceptanceStore.cs", "WorkflowContinuationAcceptanceCanonicalWriter.Compute"),
            ("Persisted_HumanTaskPayload_Should_Dispatch_Under_NativeAot", "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/Program.cs", "CRESTCREATES_HUMANTASK_RELIABLE_DELIVERY_OK")
        };

        foreach (var binding in bindings)
        {
            var source = Path.Combine(root, binding.Source);
            source.Should().MatchRegex(@"^.+\.(cs|csx)$");
            // This is a source wiring guard only. It deliberately does not
            // record a tuple: source presence is not execution evidence.
            Phase9cEvidenceRunnerCatalog.ForAcceptance(binding.Name).Should().NotBeEmpty();
            File.Exists(source).Should().BeTrue();
            new FileInfo(source).Length.Should().BeGreaterThan(0);
            File.ReadAllText(source).Should().Contain(binding.ProductionMarker);
        }
    }

    [Fact]
    public void FrozenPhase9cEvidence_ShouldCloseOnlyAfterAllRequiredTuplesExecute()
    {
        // The ordinary focused test invocation intentionally does not claim
        // closure. CI enables this only after all test processes have emitted
        // their assertion-produced JSONL tuples.
        if (!string.Equals(Environment.GetEnvironmentVariable("PHASE9C_EVIDENCE_CLOSURE"), "1", StringComparison.Ordinal))
            return;

        var artifactDirectory = Environment.GetEnvironmentVariable("PHASE9C_EVIDENCE_ARTIFACT_DIR");
        artifactDirectory.Should().NotBeNullOrWhiteSpace();
        var ledger = Phase9cEvidenceLedger.ReadJsonLines(Directory.EnumerateFiles(artifactDirectory!, "*.jsonl"));
        ledger.ValidateFrozenClosure();
        ledger.Entries.Should().HaveCount(Phase9cEvidenceRunnerCatalog.RequiredTuples.Count);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
