using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class IdempotencyMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IIdempotenceStore? _store;

    public IdempotencyMiddleware(IIdempotenceStore? store = null)
    {
        _store = store;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_store == null)
            return await next(context).ConfigureAwait(false);

        var cached = await _store.GetResultAsync(context.IdempotencyKey, context.CancellationToken)
            .ConfigureAwait(false);

        if (cached != null)
            return cached;

        var result = await next(context).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await _store.StoreResultAsync(context.IdempotencyKey, result, context.CancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }
}