using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Registry;

public sealed record RegistrySnapshot<TDescriptor>(
    FrozenDictionary<string, TDescriptor> ById,
    FrozenDictionary<string, ImmutableArray<TDescriptor>> ByName,
    FrozenDictionary<DescriptorKey, TDescriptor> ByVersion,
    ImmutableArray<TDescriptor> All,
    ImmutableDictionary<Type, IRegistryIndex> CustomIndexes)
    where TDescriptor : class, IDescriptor;
