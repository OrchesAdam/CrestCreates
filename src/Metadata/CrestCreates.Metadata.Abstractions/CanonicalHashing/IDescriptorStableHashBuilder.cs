namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Produces stable, deterministic hashes for a given descriptor using <see cref="ICanonicalHashComputer"/>.
/// </summary>
public interface IDescriptorStableHashBuilder
{
    /// <summary>
    /// Computes all stable hashes for <paramref name="descriptor"/>.
    /// The returned <see cref="DescriptorStableHashes"/> will never be null.
    /// </summary>
    DescriptorStableHashes Build(IDescriptor descriptor);
}
