using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

[Obsolete("Use IDescriptorStableHashBuilder instead. Inject via DI (AddDescriptorStableHash) and call Build(descriptor) to get DescriptorStableHashes.")]
public static class DescriptorHashComputer
{
    private static readonly DescriptorStableHashBuilder Builder = new();

    public static string ComputeContractHash(IDescriptor descriptor)
        => Builder.Build(descriptor).ContractHash;

    public static string ComputeDefinitionHash(IDescriptor descriptor)
        => Builder.Build(descriptor).DefinitionHash;
}
