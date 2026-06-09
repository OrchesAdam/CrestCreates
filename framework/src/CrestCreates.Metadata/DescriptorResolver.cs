using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorResolver : IDescriptorResolver
{
    private readonly IReadOnlyDictionary<Type, Func<string, IDescriptor?>> _resolvers;

    public DescriptorResolver(IReadOnlyDictionary<Type, Func<string, IDescriptor?>> resolvers)
    {
        _resolvers = resolvers;
    }

    public TDescriptor? Resolve<TDescriptor>(string id)
        where TDescriptor : IDescriptor
    {
        if (_resolvers.TryGetValue(typeof(TDescriptor), out var resolver))
            return (TDescriptor?)resolver(id);
        return default;
    }

    public TDescriptor? Resolve<TDescriptor>(IDescriptorRef reference)
        where TDescriptor : IDescriptor
    {
        return Resolve<TDescriptor>(reference.Id);
    }

    public IReadOnlyList<TDescriptor> Query<TDescriptor>(DescriptorQuery query)
        where TDescriptor : IDescriptor
    {
        // Phase 3 placeholder -- Phase 5~7 will implement
        return Array.Empty<TDescriptor>();
    }
}
