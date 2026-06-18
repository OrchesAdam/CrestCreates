using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Capability;

internal sealed class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly ICapabilityResolver _resolver;
    private readonly ICapabilityPipeline _pipeline;
    private readonly ITenantContext? _tenantContext;
    private readonly ICurrentUser? _currentUser;

    public CapabilityDispatcher(
        ICapabilityResolver resolver,
        ICapabilityPipeline pipeline,
        ITenantContext? tenantContext = null,
        ICurrentUser? currentUser = null)
    {
        _resolver = resolver;
        _pipeline = pipeline;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    public async Task<CapabilityExecutionResult> DispatchAsync(
        CapabilityDescriptor descriptor,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(descriptor.Id, input, ctx =>
        {
            ctx.InvocationSource = source;
            ctx.TenantId = _tenantContext?.CurrentTenantId;
            ctx.UserId = _currentUser?.Id;
            configureContext?.Invoke(ctx);
        }, ct);
    }

    public async Task<CapabilityExecutionResult> DispatchAsync(
        string capabilityId,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var descriptor = _resolver.Resolve(CapabilityRef.Parse(capabilityId));
        return await DispatchAsync(descriptor, source, input, configureContext, ct);
    }
}
