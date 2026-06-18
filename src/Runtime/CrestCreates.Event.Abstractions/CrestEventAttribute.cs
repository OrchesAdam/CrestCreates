namespace CrestCreates.Event.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CrestEventAttribute : Attribute
{
    public string? Id { get; init; }                     // Explicit stable identity. Default: SHA256(Name). Survives name changes.
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; } = 1;              // defaults to 1; explicit for v2+
    public EventScope Scope { get; init; }              // required — no default
    public EventReliability Reliability { get; init; }  // AtLeastOnce (default)
    public bool RequiresIdempotency { get; init; }      // consumer-side dedup
    public EventImportance Importance { get; init; }    // Normal (default)
    public string? Description { get; init; }
    public bool IsAuditable { get; init; }
    public bool IsReplayable { get; init; }
    public bool IsPublic { get; init; }
    public string? CapabilityId { get; init; }          // DSL: string only
}
