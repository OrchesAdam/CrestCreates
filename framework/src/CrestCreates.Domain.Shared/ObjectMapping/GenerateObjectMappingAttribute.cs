using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Marks a static partial class as an object mapping declaration.
/// The source generator will produce mapping methods for the specified types.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateObjectMappingAttribute : Attribute
{
    /// <summary>
    /// Gets the source type to map from.
    /// </summary>
    public Type SourceType { get; }

    /// <summary>
    /// Gets the target type to map to.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets or sets the mapping direction. Default is Both.
    /// </summary>
    public MapDirection Direction { get; set; } = MapDirection.Both;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerateObjectMappingAttribute"/> class.
    /// </summary>
    /// <param name="sourceType">The source type to map from.</param>
    /// <param name="targetType">The target type to map to.</param>
    public GenerateObjectMappingAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
    }
}
