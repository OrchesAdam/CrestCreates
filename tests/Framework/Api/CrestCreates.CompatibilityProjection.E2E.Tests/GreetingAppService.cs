using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CompatibilityProjection.E2E;

[CapabilityCompatibilityProjection(
    ContractType = typeof(GreetingAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/greeting",
    CapabilityIdPrefix = "compat.appservice.greeting")]
public class GreetingAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "greet",
        Kind = HttpProjectionOperationKind.Query)]
    public Task<GreetingResponse> GreetAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GreetingResponse { Message = $"Hello, {name}!" });
    }

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "greeting/{name}",
        Kind = HttpProjectionOperationKind.Query)]
    public Task<GreetingResponse> GetGreetingAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GreetingResponse { Message = $"Hello, {name}!" });
    }

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "list-greetings",
        Kind = HttpProjectionOperationKind.Query)]
    public Task<List<GreetingResponse>> ListGreetingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<GreetingResponse>
        {
            new() { Message = "Hello, World!" },
            new() { Message = "Hello, CrestCreates!" }
        });
    }

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "delete-greeting",
        Kind = HttpProjectionOperationKind.Command)]
    public Task DeleteGreetingAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
