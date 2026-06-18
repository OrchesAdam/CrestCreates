namespace CrestCreates.Snapshot.Abstractions;

/// <summary>
/// AOT-safe snapshot contract for models that require defensive copies
/// at store/registry/runtime boundaries.
/// <para>
/// Snapshot means safe boundary copy — explicit, deterministic, and deep enough
/// to protect internal state from external mutation. It is NOT a generic deep clone.
/// </para>
/// <para>
/// The returned object must not share mutable reference state with this instance.
/// Immutable values may be reused. Shared references are allowed only when the
/// referenced object is immutable, stateless, or intentionally shared infrastructure
/// (e.g., ILogger, IServiceProvider, FrozenDictionary), and the model documents that choice.
/// </para>
/// </summary>
/// <typeparam name="T">The concrete type producing the snapshot.</typeparam>
public interface ISnapshotable<T>
    where T : ISnapshotable<T>
{
    /// <summary>
    /// Creates a defensive copy of this instance.
    /// </summary>
    T Snapshot();
}
