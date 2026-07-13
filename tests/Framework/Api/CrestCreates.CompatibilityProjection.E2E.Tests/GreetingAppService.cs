using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CompatibilityProjection.E2E;

[CrestService]
[CapabilityCompatibilityProjection]
public class GreetingAppService
{
    public Task<GreetingResponse> GreetAsync(GreetingRequest input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GreetingResponse { Message = $"Hello, {input.Name}!" });
    }

    public Task<GreetingResponse> GetGreetingAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GreetingResponse { Message = $"Hello, {name}!" });
    }

    /// <summary>
    /// List method with no non-CancellationToken parameters — verifies that the generator
    /// correctly handles no-param methods without producing empty envelope classes.
    /// </summary>
    public Task<List<GreetingResponse>> ListGreetingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<GreetingResponse>
        {
            new() { Message = "Hello, World!" },
            new() { Message = "Hello, CrestCreates!" }
        });
    }

    public Task DeleteGreetingAsync(string name, CancellationToken cancellationToken = default)
    {
        // void-like method — no return value
        return Task.CompletedTask;
    }
}
