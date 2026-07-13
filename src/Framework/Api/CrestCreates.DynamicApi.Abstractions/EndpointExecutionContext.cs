namespace CrestCreates.DynamicApi;

/// <summary>
/// Decouples the result contract from Capability pipeline internals.
/// Compatibility result mappers consume this context instead of
/// raw <c>CapabilityExecutionResult</c>, keeping DynamicApi.Abstractions
/// free of Capability.Abstractions references.
/// </summary>
public sealed class EndpointExecutionContext
{
    public object? Output { get; init; }
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
