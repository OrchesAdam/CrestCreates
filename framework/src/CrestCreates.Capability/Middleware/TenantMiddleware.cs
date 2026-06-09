using CrestCreates.Capability.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Capability.Middleware;

public sealed class TenantMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ITenantContext? _tenantContext;

    public TenantMiddleware(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_tenantContext?.CurrentTenantId != null)
            context.TenantId = _tenantContext.CurrentTenantId;

        return await next(context).ConfigureAwait(false);
    }
}
