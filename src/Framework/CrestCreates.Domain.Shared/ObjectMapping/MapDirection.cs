namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Defines the direction of object mapping.
/// </summary>
public enum MapDirection
{
    /// <summary>
    /// Only generate ToTarget method (creates new instance).
    /// </summary>
    Create = 1,

    /// <summary>
    /// Only generate Apply method (updates existing instance).
    /// </summary>
    Apply = 2,

    /// <summary>
    /// Generate both ToTarget and Apply methods (default).
    /// </summary>
    Both = 3
}
