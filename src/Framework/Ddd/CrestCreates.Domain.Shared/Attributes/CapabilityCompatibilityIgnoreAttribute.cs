using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// Excludes a method from compatibility projection when the class has [CapabilityCompatibilityProjection].
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class CapabilityCompatibilityIgnoreAttribute : Attribute
    {
    }
}
