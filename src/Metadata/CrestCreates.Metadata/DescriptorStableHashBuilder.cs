using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

/// <summary>
/// Adapter that implements <see cref="IDescriptorStableHashBuilder"/> by delegating
/// to <see cref="ICanonicalHashComputer"/>. The old pipe-delimited hash logic has been
/// retired — all hash computation now goes through the canonical hash runtime.
/// </summary>
public sealed class DescriptorStableHashBuilder : IDescriptorStableHashBuilder
{
    private readonly ICanonicalHashComputer _hashComputer;

    public DescriptorStableHashBuilder(ICanonicalHashComputer hashComputer)
    {
        _hashComputer = hashComputer;
    }

    /// <inheritdoc />
    public DescriptorStableHashes Build(IDescriptor descriptor)
    {
        return new DescriptorStableHashes
        {
            ContractHash = _hashComputer.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull),
            DefinitionHash = _hashComputer.ComputeDefinitionHash(descriptor, CanonicalHashScope.InternalFull)
        };
    }
}
