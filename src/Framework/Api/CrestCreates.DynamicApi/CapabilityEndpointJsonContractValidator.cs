using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Validates that all body types registered in CapabilityEndpointJsonContractRegistry
/// have available JsonTypeInfo in the application's JsonSerializerOptions.
/// Executed once during application startup, before any endpoint receives requests.
/// </summary>
internal sealed class CapabilityEndpointJsonContractValidator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CapabilityEndpointJsonContractValidator>? _logger;

    public CapabilityEndpointJsonContractValidator(
        IServiceProvider serviceProvider,
        ILogger<CapabilityEndpointJsonContractValidator>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Validates all registered body types have JsonTypeInfo available.
    /// Throws InvalidOperationException if any type is missing metadata.
    /// </summary>
    public void Validate()
    {
        var bodyTypes = CapabilityEndpointJsonContractRegistry.GetRegisteredBodyTypes();
        if (bodyTypes.Count == 0)
            return;

        var missing = new List<Type>();

        foreach (var type in bodyTypes)
        {
            var typeInfo = CapabilityEndpointJsonTypeInfoResolver
                .Resolve(_serviceProvider, type);

            if (typeInfo is null)
                missing.Add(type);
        }

        if (missing.Count > 0)
        {
            var missingNames = string.Join(", ", missing.Select(t => t.FullName));
            throw new InvalidOperationException(
                $"The following body types are used by Capability Endpoints but have no " +
                $"JsonTypeInfo registered in the application's JsonSerializerOptions: " +
                $"[{missingNames}]. Add [JsonSerializable(typeof(...))] declarations to " +
                $"your application's JsonSerializerContext, or register an " +
                $"IJsonTypeInfoResolver in the TypeInfoResolverChain.");
        }

        _logger?.LogDebug(
            "Validated JsonTypeInfo for {Count} Capability Endpoint body types.",
            bodyTypes.Count);
    }
}
