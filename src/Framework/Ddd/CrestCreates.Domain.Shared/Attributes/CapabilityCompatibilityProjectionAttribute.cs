using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// Marks a [CrestService] class or method for compatibility projection to Capability Pipeline.
    /// Class-level: all eligible methods projected.
    /// Method-level: projected only that method (class need not have the attribute).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class CapabilityCompatibilityProjectionAttribute : Attribute
    {
        /// <summary>
        /// Override the capability ID prefix.
        /// Default: service name (stripped AppService/Service suffix) in kebab-case,
        /// prefixed with "compat.appservice.".
        /// Example: BookAppService → compat.appservice.book
        /// </summary>
        public string? CapabilityIdPrefix { get; init; }

        /// <summary>
        /// Override the route prefix.
        /// Default: derived from [DynamicApiRoute] or service name convention.
        /// </summary>
        public string? RoutePrefix { get; init; }
    }
}
