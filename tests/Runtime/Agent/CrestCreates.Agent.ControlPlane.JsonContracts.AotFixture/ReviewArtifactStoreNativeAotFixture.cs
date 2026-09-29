using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

internal static class ReviewArtifactStoreNativeAotFixture
{
    public static bool Run(AgentReviewArtifactEnvelope artifact)
    {
        try
        {
            var typeInfo = AgentReviewArtifactEnvelopeJsonSerializerContext.Default.AgentReviewArtifactEnvelope;
            var json = JsonSerializer.Serialize(artifact, typeInfo);
            var restored = JsonSerializer.Deserialize(json, typeInfo);
            if (restored is null)
                return Fail("rich review artifact JSON deserialized to null");

            restored.Validate();
            if (!StringComparer.Ordinal.Equals(json, JsonSerializer.Serialize(restored, typeInfo)))
                return Fail("rich review artifact changed after source-generated JSON roundtrip");

            if (artifact.ProjectedReview.TopologySummary is null
                || artifact.ProjectedReview.MaterializationSummary is null
                || artifact.ReportInput.Topology is null
                || artifact.ReportInput.Materialization is null
                || artifact.OriginalHashInput.SourceBinding is null)
                return Fail("real service artifact did not contain rich projection, report, and original hash facts");

            var projectedHashInput = artifact.ReportInput.ToReviewHashInput();
            var separatedOriginalHashInput = artifact.OriginalHashInput with
            {
                SourceBinding = artifact.OriginalHashInput.SourceBinding with
                {
                    Diagnostics = [new ReviewDiagnosticProjection { Code = "ORIGINAL-HASH-ONLY", Severity = "Info" }]
                }
            };
            var separatedArtifact = artifact with { OriginalHashInput = separatedOriginalHashInput };
            var separatedJson = JsonSerializer.Serialize(separatedArtifact, typeInfo);
            var separatedRestored = JsonSerializer.Deserialize(separatedJson, typeInfo);
            if (separatedRestored is null)
                return Fail("artifact with distinct original hash facts deserialized to null");
            separatedRestored.Validate();
            var originalHashInputJson = JsonSerializer.Serialize(
                separatedOriginalHashInput,
                CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing.DescriptorDraftReviewHashInputJsonSerializerContext.Default.DescriptorDraftReviewHashInput);
            var projectedHashInputJson = JsonSerializer.Serialize(
                projectedHashInput,
                CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing.DescriptorDraftReviewHashInputJsonSerializerContext.Default.DescriptorDraftReviewHashInput);
            if (StringComparer.Ordinal.Equals(originalHashInputJson, projectedHashInputJson))
                return Fail("fixture did not demonstrate independently retained original and projected hash inputs");
            var restoredOriginalHashInputJson = JsonSerializer.Serialize(
                separatedRestored.OriginalHashInput,
                CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing.DescriptorDraftReviewHashInputJsonSerializerContext.Default.DescriptorDraftReviewHashInput);
            if (!StringComparer.Ordinal.Equals(originalHashInputJson, restoredOriginalHashInputJson))
                return Fail("source-generated envelope roundtrip replaced the original hash input");
            if (!StringComparer.Ordinal.Equals(separatedJson, JsonSerializer.Serialize(separatedRestored, typeInfo)))
                return Fail("distinct original and projected hash facts changed after envelope roundtrip");

            var failure = new DescriptorDraftDiagnostic
            {
                Code = new DiagnosticCode("AOT-REVIEW-FAILED"),
                Severity = SeverityLevel.Error,
                Message = "Captured failed review validation."
            };
            var failedReview = artifact.ProjectedReview with
            {
                ValidationResult = DescriptorDraftValidationResult.Failure(failure),
                IsActivationEligible = false
            };
            var failedEnvelope = artifact with
            {
                ReviewResultId = artifact.ReviewResultId + "-failed",
                ProjectedReview = failedReview,
                ReportInput = artifact.ReportInput with
                {
                    IsValid = false,
                    IsActivationEligible = false,
                    ValidationDiagnostics = [failure]
                }
            };
            failedEnvelope.Validate();
            var failedJson = JsonSerializer.Serialize(failedEnvelope, typeInfo);
            var failedRestored = JsonSerializer.Deserialize(failedJson, typeInfo);
            if (failedRestored is null || failedRestored.ProjectedReview.ValidationResult.IsValid
                || failedRestored.ReportInput.IsValid || failedRestored.ProjectedReview.IsActivationEligible
                || failedRestored.ReportInput.IsActivationEligible)
                return Fail("failed review artifact did not preserve its failure state");
            failedRestored.Validate();
            if (!StringComparer.Ordinal.Equals(failedJson, JsonSerializer.Serialize(failedRestored, typeInfo)))
                return Fail("failed review artifact changed after source-generated JSON roundtrip");

            Console.WriteLine("CONTROL_PLANE_REVIEW_ARTIFACT_STORE_MEMORY_NATIVEAOT_OK");
            return true;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static bool Fail(string message)
    {
        Console.Error.WriteLine($"FAIL [ReviewArtifactStoreNativeAot]: {message}");
        return false;
    }
}
