namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolPermissionRequirement
{
    public required string PermissionName { get; init; }
    public string? DescriptorKindConstraint { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// The category of the tool requesting authorization.
    /// Used by mode-driven authorization to determine category-level defaults
    /// (e.g., mutating vs. read-only vs. activation handoff).
    /// </summary>
    public AgentToolCategory? ToolCategory { get; init; }

    /// <summary>
    /// Whether the tool is read-only (does not persist state changes).
    /// Used by mode-driven authorization to distinguish read tools from mutating tools
    /// within the same category.
    /// </summary>
    public bool IsReadOnly { get; init; }
}
