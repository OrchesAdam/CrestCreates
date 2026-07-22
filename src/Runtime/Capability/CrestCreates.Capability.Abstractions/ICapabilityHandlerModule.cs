namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Composable handler module. Each assembly with capability handlers
/// generates one implementation. DI collects all modules; the factory
/// builds a composed CapabilityHandlerResolver from them ordered by Id.
/// </summary>
public interface ICapabilityHandlerModule
{
    /// <summary>
    /// Unique module identifier — typically the assembly name.
    /// Determines application order (alphabetical by Id).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Apply this module's handler definitions to the composed resolver.
    /// Called once during resolver construction.
    /// </summary>
    void Apply(CapabilityHandlerResolver resolver);
}
