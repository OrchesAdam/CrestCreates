using System.Collections.Immutable;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public static class AuditTagMap
{
    public static ImmutableSortedDictionary<string, string> Empty { get; }
        = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
}
