using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorProviderRegistry
{
    private static readonly ConcurrentBag<object> _providers = new();

    public static void Register<T>(IDescriptorProvider<T> provider) where T : class, IDescriptor
        => _providers.Add(provider);

    public static IReadOnlyList<IDescriptorProvider<T>> GetProviders<T>() where T : class, IDescriptor
        => _providers.OfType<IDescriptorProvider<T>>().ToList();
}
