namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Produces stable, deterministic hashes for a given descriptor.
///
/// The builder must:
/// <list type="number">
/// <item>Produce deterministic output for equivalent descriptors (same structural content = same hash).</item>
/// <item>Be AoT-friendly: use explicit, switch-based field extraction with string concatenation
///     and SHA-256 — no <see cref="System.Text.Json.JsonSerializer"/> dependency.</item>
/// <item>Maintain stable canonical ordering for collections where order is semantically irrelevant,
///     using <see cref="System.StringComparer.Ordinal"/>.</item>
/// <item>Clearly separate <see cref="DescriptorStableHashes.ContractHash"/> (externally observable contract)
///     from <see cref="DescriptorStableHashes.DefinitionHash"/> (any definition-level change).</item>
/// </list>
///
/// Consumers include: source generators, descriptor package builder, descriptor draft tooling,
/// samples/golden scenarios, migration/compatibility tests, and future AI/control-plane descriptor editors.
/// </summary>
public interface IDescriptorStableHashBuilder
{
    /// <summary>
    /// Computes all stable hashes for <paramref name="descriptor"/>.
    /// The returned <see cref="DescriptorStableHashes"/> will never be null.
    /// </summary>
    DescriptorStableHashes Build(IDescriptor descriptor);
}
