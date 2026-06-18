namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Bootstrap task. Not limited to registries — Schema, Projection, Cache, AI Index can also use this.
/// </summary>
public interface IBootstrapTask
{
    /// <summary>
    /// Unique task identifier for dependency declaration.
    /// Examples: "event-registry", "capability-registry"
    /// </summary>
    string TaskId { get; }

    /// <summary>
    /// Task type for logging and diagnostics.
    /// </summary>
    Type ServiceType { get; }

    /// <summary>
    /// Dependencies declared by TaskId.
    /// </summary>
    IReadOnlyList<string> Dependencies { get; }

    /// <summary>
    /// If true, failure terminates startup. If false, failure logs warning and continues.
    /// </summary>
    bool IsRequired { get; }

    Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct);
}
