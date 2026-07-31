using CrestCreates.Metadata.Abstractions.Persistence;

namespace CrestCreates.Metadata.Abstractions.Runtime;

/// <summary>
/// Validates optional immutable snapshot evidence without treating it as an
/// executable definition source. Runtime execution still resolves the Pin from
/// the activated Registry.
/// </summary>
public static class RuntimeDescriptorPinEvidence
{
    public static async Task ValidateAsync(
        RuntimeDescriptorPin pin,
        IDescriptorSnapshotStore? snapshots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pin);
        pin.EnsureValid();
        if (pin.SnapshotId is null)
            return;

        if (snapshots is null)
        {
            throw new RuntimeDescriptorPinValidationException(
                "Descriptor snapshot evidence is required but no Snapshot Store is registered.");
        }

        var entry = await snapshots.GetEntryAsync(pin.SnapshotId, pin.Ref, cancellationToken).ConfigureAwait(false);
        if (entry is null
            || !string.Equals(entry.ContractHash, pin.ContractHash.Value, StringComparison.Ordinal)
            || !string.Equals(entry.DefinitionHash, pin.DefinitionHash.Value, StringComparison.Ordinal))
        {
            throw new RuntimeDescriptorPinValidationException(
                "Descriptor snapshot evidence does not match the executable Pin.");
        }
    }
}
