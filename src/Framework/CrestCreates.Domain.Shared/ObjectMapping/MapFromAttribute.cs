using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Explicitly specifies the source property for the target property.
/// Use nameof() for compile-time safety.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MapFromAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the source property to map from.
    /// </summary>
    public string SourceProperty { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapFromAttribute"/> class.
    /// </summary>
    /// <param name="sourceProperty">The name of the source property (use nameof()).</param>
    public MapFromAttribute(string sourceProperty)
    {
        SourceProperty = sourceProperty ?? throw new ArgumentNullException(nameof(sourceProperty));
    }
}
