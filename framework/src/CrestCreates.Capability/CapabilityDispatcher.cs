using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability;

internal sealed class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly ICapabilityResolver _resolver;
    private readonly ICapabilityPipeline _pipeline;

    public CapabilityDispatcher(
        ICapabilityResolver resolver,
        ICapabilityPipeline pipeline)
    {
        _resolver = resolver;
        _pipeline = pipeline;
    }

    public async Task<CapabilityExecutionResult> DispatchAsync(
        IVersionedDescriptor descriptor,
        InvocationSource source,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(descriptor.Id, input, ctx =>
        {
            ctx.CapabilityId = descriptor.Id;
            ctx.CapabilityName = descriptor.Name;
            ctx.CapabilityVersion = descriptor.Version;
            ctx.CapabilityContractHash = descriptor.ContractHash;
            ctx.InvocationSource = source;
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
        var descriptor = _resolver.Resolve(capabilityId);
        return await DispatchAsync(descriptor, source, input, configureContext, ct);
    }
}
