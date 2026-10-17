using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// Marks a [CrestService] class or method for compatibility projection to Capability Pipeline.
    /// Class-level: all eligible methods projected.
    /// Method-level: projected only that method (class need not have the attribute).
    /// With <see cref="ContractType"/> the declaration may be a local anchor that does not implement
    /// the contract; the contract (and its DTOs) may come from a referenced assembly (#129 addendum).
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

        /// <summary>
        /// The accessibly closed interface to project. When set, only this contract and its inherited
        /// interfaces contribute actions; the annotated class is a declaration anchor (it is neither
        /// instantiated nor DI-registered by the projection).
        /// </summary>
        public Type? ContractType { get; init; }

        /// <summary>
        /// How the projection is activated. New declarations use <see cref="HttpProjectionRegistrationMode.ExplicitHost"/>.
        /// </summary>
        public HttpProjectionRegistrationMode RegistrationMode { get; init; } = HttpProjectionRegistrationMode.ExplicitHost;
    }
}
