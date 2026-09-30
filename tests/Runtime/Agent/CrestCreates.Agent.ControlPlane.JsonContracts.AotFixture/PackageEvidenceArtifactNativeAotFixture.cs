using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

internal static class PackageEvidenceArtifactNativeAotFixture
{
    public static bool Run(
        AgentPackageArtifactEnvelope firstPackage,
        AgentPackageArtifactEnvelope secondPackage,
        AgentEvidenceArtifactEnvelope secondEvidence,
        IAgentPackageArtifactValidator validator,
        IDescriptorPackageSerializer packageSerializer)
    {
        try
        {
            var envelopeJson = JsonSerializer.Serialize(
                firstPackage,
                AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope);
            var restoredPackage = JsonSerializer.Deserialize(
                envelopeJson,
                AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope);
            if (restoredPackage is null || !StringComparer.Ordinal.Equals(
                    envelopeJson,
                    JsonSerializer.Serialize(restoredPackage, AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope)))
                return Fail("source-generated package envelope roundtrip changed retained content");

            var secondPackageJson = JsonSerializer.Serialize(
                secondPackage,
                AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope);
            var restoredSecondPackage = JsonSerializer.Deserialize(
                secondPackageJson,
                AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope);
            if (restoredSecondPackage is null || !StringComparer.Ordinal.Equals(
                    secondPackageJson,
                    JsonSerializer.Serialize(restoredSecondPackage, AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope)))
                return Fail("source-generated second package envelope roundtrip changed retained content");

            var evidenceJson = JsonSerializer.Serialize(
                secondEvidence,
                AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope);
            var restoredEvidence = JsonSerializer.Deserialize(
                evidenceJson,
                AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope);
            if (restoredEvidence is null || !StringComparer.Ordinal.Equals(
                    evidenceJson,
                    JsonSerializer.Serialize(restoredEvidence, AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope)))
                return Fail("source-generated evidence envelope roundtrip changed retained content");

            validator.ValidatePackage(restoredPackage);
            validator.ValidatePackage(restoredSecondPackage);
            validator.ValidateEvidence(restoredEvidence, restoredSecondPackage);

            var officialPackage = packageSerializer.Deserialize(restoredPackage.PackageJson);
            if (officialPackage.Hashes is null || officialPackage.EvidenceEnvelope is null
                || officialPackage.Hashes.PackageManifestHash != restoredPackage.ProjectedPreview.PackageManifestHash
                || officialPackage.Hashes.PackageEvidenceHash != restoredPackage.ProjectedPreview.PackageEvidenceHash
                || officialPackage.Hashes.PackageEvidenceEnvelopeHash != restoredPackage.ProjectedPreview.PackageEvidenceEnvelopeHash)
                return Fail("the official package serializer did not roundtrip the retained package and canonical hashes");

            // Whitespace remains valid package JSON, so this isolates the separate
            // artifact-content digest from the official three package hashes.
            if (!RejectsTamperedContent(() => validator.ValidatePackage(
                    restoredPackage with { PackageJson = restoredPackage.PackageJson + " " }),
                    "content integrity hash"))
                return Fail("package artifact content digest did not cover the exact retained PackageJson bytes");

            // CapturedAt is not part of the package association checks; rejection here
            // demonstrates that the evidence content profile covers its full envelope.
            if (!RejectsTamperedContent(() => validator.ValidateEvidence(
                    restoredEvidence with { CapturedAt = restoredEvidence.CapturedAt.AddTicks(1) }, restoredSecondPackage),
                    "content integrity hash"))
                return Fail("evidence artifact content digest did not cover its immutable envelope");

            Console.WriteLine("PackageEvidenceArtifactGeneratedEnvelopeAndOfficialSerializer:PASS");
            return true;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static bool RejectsTamperedContent(Action validate, string expectedMessage)
    {
        try
        {
            validate();
            return false;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool Fail(string message)
    {
        Console.Error.WriteLine($"FAIL [PackageEvidenceArtifactNativeAot]: {message}");
        return false;
    }
}
