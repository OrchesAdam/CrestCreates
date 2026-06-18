using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// Specifies the source property name when it differs from the target property name.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MapNameAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the source property to map from.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapNameAttribute"/> class.
    /// </summary>
    /// <param name="sourceName">The name of the source property.</param>
    public MapNameAttribute(string sourceName)
    {
        SourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
    }
}
