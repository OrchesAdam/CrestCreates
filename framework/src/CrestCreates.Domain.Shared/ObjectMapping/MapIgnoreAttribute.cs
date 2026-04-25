using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// When applied to a property, excludes it from automatic mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MapIgnoreAttribute : Attribute
{
}