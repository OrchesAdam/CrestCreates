using CrestCreates.Runtime.Persistence.Testing.Evidence;
using CrestCreates.Runtime.Persistence.Testing.Manifest;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

public sealed class Phase9cExecutableEvidenceTests
{
    [Fact]
    public void CriticalPhase9cEvidence_ShouldBeBoundToExecutableSources()
    {
        Phase9cEvidenceLedger.ValidateFrozenManifest();
        var root = FindRepositoryRoot();
        var ledger = new Phase9cEvidenceLedger();
        var bindings = new (string Name, string Runner, string Source, string ProductionMarker)[]
        {
            ("NonCooperative_OptionalHandler_Should_Not_Prevent_ReliableAckProgress", "HumanTask.Tests", "tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests/OptionalLocalEventCompatibilityTests.cs", "OptionalTrackerCapacity_ShouldReserveBeforeStartingWork"),
            ("Procurement_ExactDecisionReplay_Should_Be_Duplicate", "Procurement.Tests", "samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/ProcurementHumanTaskIntegration.cs", "OutboxRequiredConsumerResult.Duplicate"),
            ("ActivationReview_ExactDecisionReplay_Should_Be_Duplicate", "Agent.ControlPlane.Tests", "src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DescriptorActivationReviewHumanTaskEventHandler.cs", "ReviewPayloadInvalid"),
            ("DB_CompositionPreflight_Should_Run_After_RuntimeSchemaCompatibility", "PostgreSql.Tests", "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlHumanTaskCompletionObligationPreflight.cs", "ValidateAsync"),
            ("WorkflowContinuationAcceptance_Integrity_Should_Use_FrozenV1Projection", "PostgreSql.Tests", "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlWorkflowContinuationAcceptanceStore.cs", "WorkflowContinuationAcceptanceCanonicalWriter.Compute"),
            ("Persisted_HumanTaskPayload_Should_Dispatch_Under_NativeAot", "PostgreSql.AotFixture.Tests", "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/Program.cs", "CRESTCREATES_HUMANTASK_RELIABLE_DELIVERY_OK")
        };

        foreach (var binding in bindings)
        {
            var source = Path.Combine(root, binding.Source);
            source.Should().MatchRegex(@"^.+\.(cs|csx)$");
            ledger.RecordExecutable(
                binding.Name,
                binding.Runner,
                "executable",
                source,
                () => File.Exists(source)
                    && new FileInfo(source).Length > 0
                    && File.ReadAllText(source).Contains(binding.ProductionMarker, StringComparison.Ordinal));
        }

        ledger.ValidateExecutableEvidence(bindings.Select(binding => binding.Name));
    }

    [Fact]
    public void FrozenPhase9cEvidence_ShouldCloseOnlyAfterAllCiRunnersComplete()
    {
        // The ordinary focused test invocation intentionally does not claim
        // closure. CI sets these variables only after the owning test slices
        // have completed successfully; this prevents a source-only or
        // manually-populated ledger from making the PR appear complete.
        if (!string.Equals(Environment.GetEnvironmentVariable("PHASE9C_EVIDENCE_CLOSURE"), "1", StringComparison.Ordinal))
            return;

        Phase9cEvidenceLedger.ValidateFrozenManifest();
        var completedRunners = (Environment.GetEnvironmentVariable("PHASE9C_EVIDENCE_RUNNERS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        completedRunners.Should().Contain(Phase9cEvidenceRunnerCatalog.RunnerNames);

        var ledger = new Phase9cEvidenceLedger();
        var names = Phase9cAcceptanceManifest.NormativeNames
            .Concat(Phase9cSupplementalAcceptanceManifest.Names)
            .ToArray();
        foreach (var runner in Phase9cEvidenceRunnerCatalog.RunnerNames)
        {
            var owned = names.Where(name => string.Equals(
                Phase9cEvidenceRunnerCatalog.RunnerFor(name), runner, StringComparison.Ordinal));
            ledger.RecordRunnerBatch(
                owned,
                runner,
                Phase9cEvidenceRunnerCatalog.EvidenceVectorFor(runner),
                $"runner:{runner}",
                () => completedRunners.Contains(runner));
        }

        ledger.ValidateFrozenClosure();
        ledger.Entries.Should().HaveCount(170);
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
