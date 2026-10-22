using System;

namespace CrestCreates.Domain.Shared.Attributes
{
    /// <summary>
    /// Marks a class for explicit-host compatibility projection to the Capability Pipeline.
    /// The anchor declares a <see cref="ContractType"/> (interface or the anchor class itself for a
    /// self-contract declaration) and the formal HTTP/JSON toolchain materializes its endpoints.
    /// With <see cref="ContractType"/> the declaration may be a local anchor that does not implement
    /// the contract; the contract (and its DTOs) may come from a referenced assembly (#129 addendum).
    /// Method-level projection belonged to the retired legacy front-end and is rejected with
    /// CCHTTP001; declare a class-level projection instead.
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
        /// The closed interface to project, or the projection class itself for a self-contract
        /// declaration. Interface mode: only the contract and its inherited interfaces contribute
        /// actions and the annotated class is a declaration anchor. Self-contract mode (ContractType
        /// equals the annotated class): only public instance methods explicitly marked with
        /// <see cref="AppServiceHttpActionAttribute"/> contribute actions; unmarked helper methods are
        /// never exposed. In both modes the business service is registered once by its owning
        /// feature/module, and the host activation verifies that registration before mapping
        /// (CCHTTP007); it never creates a second instance.
        /// </summary>
        public Type? ContractType { get; init; }

        /// <summary>
        /// How the projection is activated. New declarations use <see cref="HttpProjectionRegistrationMode.ExplicitHost"/>.
        /// </summary>
        public HttpProjectionRegistrationMode RegistrationMode { get; init; } = HttpProjectionRegistrationMode.ExplicitHost;
    }
}
