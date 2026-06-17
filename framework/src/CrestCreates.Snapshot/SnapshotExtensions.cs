using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Snapshot;

/// <summary>
/// AOT-safe snapshot helpers for collections of <see cref="ISnapshotable{T}"/> models.
/// All helpers are deterministic and use ordinal comparison for string keys.
/// </summary>
public static class SnapshotExtensions
{
    /// <summary>
    /// Creates a defensive copy of a list where each element snapshots itself.
    /// Returns a new <see cref="IReadOnlyList{T}"/>; mutating the source
    /// or its elements after snapshot does not affect the result.
    /// </summary>
    public static IReadOnlyList<T> SnapshotList<T>(this IEnumerable<T> source)
        where T : ISnapshotable<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Select(item => item.Snapshot()).ToArray();
    }

    /// <summary>
    /// Creates a defensive copy of a dictionary where each value snapshots itself.
    /// Keys are reused (assumed immutable). Returns a new
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
    /// </summary>
    public static IReadOnlyDictionary<TKey, TValue> SnapshotDictionary<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> source,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        where TValue : ISnapshotable<TValue>
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Snapshot(), comparer);
    }

    /// <summary>
    /// Creates a defensive copy of a string-to-string dictionary
    /// using <see cref="StringComparer.Ordinal"/> for deterministic key comparison
    /// and enumeration order. String values are immutable and reused; only the
    /// dictionary container is copied. Always returns an independent container,
    /// never a shared static empty.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SnapshotStringDictionary(
        this IReadOnlyDictionary<string, string>? source)
    {
        if (source is null or { Count: 0 })
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(
            source.OrderBy(kvp => kvp.Key, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }
}
