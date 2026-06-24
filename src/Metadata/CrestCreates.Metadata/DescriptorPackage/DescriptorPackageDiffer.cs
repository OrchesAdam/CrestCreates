using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using Package = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage;

namespace CrestCreates.Metadata.DescriptorPackage;

public sealed class DescriptorPackageDiffer : IDescriptorPackageDiffer
{
    public DescriptorPackageDiff Diff(
        Package before, Package after,
        DescriptorPackageDiffOptions? options = null)
    {
        var beforeRefs = before.Manifest.DescriptorEntries.Select(e => e.Ref).ToHashSet();
        var afterRefs = after.Manifest.DescriptorEntries.Select(e => e.Ref).ToHashSet();

        var addedRefs = afterRefs.Except(beforeRefs).ToList();
        var removedRefs = beforeRefs.Except(afterRefs).ToList();

        var beforeByRef = before.Manifest.DescriptorEntries.ToDictionary(e => e.Ref);
        var afterByRef = after.Manifest.DescriptorEntries.ToDictionary(e => e.Ref);

        var changedEntries = new List<DescriptorDiffEntry>();
        var stateChanges = new List<DescriptorStateChange>();

        foreach (var (refKey, beforeEntry) in beforeByRef)
        {
            if (afterByRef.TryGetValue(refKey, out var afterEntry))
            {
                if (beforeEntry.ContractHash != afterEntry.ContractHash)
                {
                    changedEntries.Add(new DescriptorDiffEntry
                    {
                        Ref = refKey, BeforeContractHash = beforeEntry.ContractHash,
                        AfterContractHash = afterEntry.ContractHash
                    });
                }
                if (beforeEntry.State != afterEntry.State)
                {
                    stateChanges.Add(new DescriptorStateChange
                    {
                        Ref = refKey, FromState = beforeEntry.State, ToState = afterEntry.State
                    });
                }
            }
        }

        var metadataChanges = new List<DescriptorPackageMetadataChange>();
        if (before.Manifest.PackageVersion != after.Manifest.PackageVersion)
            metadataChanges.Add(new DescriptorPackageMetadataChange
                { Field = "PackageVersion", BeforeValue = before.Manifest.PackageVersion,
                  AfterValue = after.Manifest.PackageVersion });
        if (before.Manifest.Name != after.Manifest.Name)
            metadataChanges.Add(new DescriptorPackageMetadataChange
                { Field = "Name", BeforeValue = before.Manifest.Name, AfterValue = after.Manifest.Name });
        if (before.Manifest.Source != after.Manifest.Source)
            metadataChanges.Add(new DescriptorPackageMetadataChange
                { Field = "Source", BeforeValue = before.Manifest.Source, AfterValue = after.Manifest.Source });

        return new DescriptorPackageDiff
        {
            AddedRefs = addedRefs, RemovedRefs = removedRefs,
            ChangedEntries = changedEntries, StateChanges = stateChanges,
            MetadataChanges = metadataChanges,
            BeforeContentHash = before.ContentHash, AfterContentHash = after.ContentHash
        };
    }
}
