using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CapabilityEndpoint.AotFixture;

/// <summary>
/// Self-contract formal projection used by the NativeAOT HTTP gate. The generated contribution
/// owns the endpoint descriptors, typed binding/invoker/result mapper and JSON roots.
/// </summary>
[CapabilityCompatibilityProjection(
    ContractType = typeof(GreetingAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/greeting",
    CapabilityIdPrefix = "compat.appservice.greeting")]
public class GreetingAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "process-greeting",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required)]
    public Task<GreetingResponse> ProcessGreetingAsync(GreetingRequest input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GreetingResponse { Message = $"Hello, {input.Name}!" });
    }

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "list-greetings",
        Kind = HttpProjectionOperationKind.Query)]
    public Task<List<GreetingResponse>> ListGreetingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<GreetingResponse>());
    }
}
