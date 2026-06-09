using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public sealed record EventRegistrySnapshot(
    FrozenDictionary<string, ImmutableArray<GeneratedEventDescriptor>> ByName,
    FrozenDictionary<Type, GeneratedEventDescriptor> ByPayloadType);
