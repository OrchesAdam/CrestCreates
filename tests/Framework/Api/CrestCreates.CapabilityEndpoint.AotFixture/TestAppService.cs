using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CapabilityEndpoint.AotFixture;

/// <summary>
/// Test service with [CrestService] + [CapabilityCompatibilityProjection] attributes.
/// The CodeGenerator will produce compatibility projection code for this service,
/// including RegisterBodyType calls for the body types used by its methods.
/// </summary>
[CrestService]
[CapabilityCompatibilityProjection]
public class GreetingAppService
{
    /// <summary>
    /// POST endpoint with body parameter — the key AOT test case.
    /// "Process" prefix maps to POST by convention analyzer.
    /// Generated code calls CapabilityEndpointJsonTypeInfoResolver.Resolve&lt;GreetingRequest&gt;()
    /// + CapabilityEndpointBodyReader.ReadCompatibilityBodyAsync().
    /// </summary>
    public Task<GreetingResponse> ProcessGreetingAsync(GreetingRequest input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GreetingResponse { Message = $"Hello, {input.Name}!" });
    }

    /// <summary>
    /// GET endpoint with no parameters — tests no-param envelope handling.
    /// </summary>
    public Task<List<GreetingResponse>> ListGreetingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<GreetingResponse>());
    }
}
