using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

public static class AssetProductionEvolutionBaseline
{
    public static IReadOnlyList<IDescriptor> GetDescriptors() =>
    [
        ..AssetApprovedRelease.GetBaseline(),
        AssetDescriptorCatalog.MaintenanceInitialHumanTask,
        AssetProductionDescriptors.TrialTask,
        AssetProductionDescriptors.QualityTask,
        AssetProductionDescriptors.ReleaseInputSchema,
        AssetProductionDescriptors.ReleaseOutputSchema,
        AssetProductionDescriptors.ReleaseCapability
    ];

    internal static MetadataContextPackRequest CreateContextRequest(string tenantId, string intent) => new()
    {
        TenantId = tenantId,
        Intent = intent,
        Scope = MetadataContextPackScope.DirectDependencies,
        FocusDescriptors = AssetApprovedRelease.PinBaseline(GetDescriptors()).Select(pin => pin.Ref).ToArray(),
        IncludeStableHashes = true,
        IncludeGovernanceState = true
    };

    internal static void RequireContext(MetadataContextPack pack)
    {
        var expected = AssetApprovedRelease.PinBaseline(GetDescriptors()).ToDictionary(pin => pin.Ref);
        if (pack.Descriptors.Count != expected.Count ||
            pack.Diagnostics.Any(diagnostic => diagnostic.Severity.CompareTo(SeverityLevel.Warning) >= 0) ||
            pack.Descriptors.Any(entry => !expected.Remove(entry.Ref, out var pin) ||
                !entry.IsFocus || entry.Hashes?.ContractHash != pin.ContractHash ||
                entry.Hashes.DefinitionHash != pin.DefinitionHash))
        {
            throw new InvalidOperationException("Asset production discovery must match the compiled descriptor baseline.");
        }
    }

    internal static bool MatchesProposedManifest(DescriptorManifest manifest, WorkflowDescriptor workflow)
    {
        var expected = GetDescriptors().Where(descriptor => descriptor != AssetDescriptorCatalog.MaintenanceWorkflow)
            .Append(workflow).Cast<IVersionedDescriptor>()
            .Select(descriptor => (descriptor.Kind, Pin: AssetApprovedRelease.Pin(descriptor)))
            .ToDictionary(entry => entry.Pin.Ref);
        return manifest.DescriptorCount == expected.Count && manifest.DescriptorEntries.Count == expected.Count &&
            manifest.DescriptorEntries.All(entry => expected.Remove(entry.Ref, out var descriptor) &&
                entry.Kind == descriptor.Kind && entry.ContractHash == descriptor.Pin.ContractHash.Value &&
                entry.DefinitionHash == descriptor.Pin.DefinitionHash.Value);
    }
}
