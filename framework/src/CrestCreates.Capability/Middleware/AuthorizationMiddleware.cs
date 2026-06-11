using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class AuthorizationMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ICapabilityAuthorizationService _authService;

    public AuthorizationMiddleware(ICapabilityAuthorizationService authService)
    {
        _authService = authService;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var authorized = await _authService.AuthorizeAsync(
            context.CapabilityName, context.UserId, context.RequiredPermissions, context.CancellationToken)
            .ConfigureAwait(false);

        if (!authorized)
        {
            return CapabilityExecutionResult.Failure(
                "UNAUTHORIZED",
                $"User '{context.UserId}' is not authorized for capability '{context.CapabilityName}'.",
                TimeSpan.Zero);
        }

        return await next(context).ConfigureAwait(false);
    }
}
