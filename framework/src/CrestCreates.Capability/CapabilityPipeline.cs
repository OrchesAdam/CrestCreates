using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Capability;

public sealed class CapabilityPipeline : ICapabilityPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICapabilityRegistry _registry;
    private readonly ICapabilityHandlerResolver _handlerResolver;
    private readonly CapabilityPipelineBuilder _builder;

    public CapabilityPipeline(
        IServiceProvider serviceProvider,
        ICapabilityRegistry registry,
        ICapabilityHandlerResolver handlerResolver,
        CapabilityPipelineBuilder builder)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _handlerResolver = handlerResolver;
        _builder = builder;
    }

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetActiveVersion(capabilityName)
            ?? _registry.GetByName(capabilityName);

        if (descriptor == null)
        {
            return CapabilityExecutionResult.Failure(
                "CAPABILITY_NOT_FOUND",
                $"Capability '{capabilityName}' is not registered.",
                TimeSpan.Zero);
        }

        var context = new CapabilityExecutionContext
        {
            CapabilityName = descriptor.Name,
            CapabilityVersion = descriptor.Version,
            CapabilityContractHash = descriptor.ContractHash,
            Input = input,
            CancellationToken = ct
        };
        configureContext?.Invoke(context);

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            CapabilityPipelineDelegate handler = async (ctx) =>
            {
                var invoker = _handlerResolver.Resolve(capabilityName);
                if (invoker == null)
                {
                    return CapabilityExecutionResult.Failure(
                        "HANDLER_NOT_FOUND",
                        $"No handler registered for capability '{capabilityName}'.",
                        DateTimeOffset.UtcNow - startedAt);
                }

                var output = await invoker.InvokeAsync(ctx.Input, ctx.CancellationToken)
                    .ConfigureAwait(false);

                return CapabilityExecutionResult.Success(
                    output,
                    DateTimeOffset.UtcNow - startedAt);
            };

            var middlewareTypes = _builder.MiddlewareTypes;
            for (int i = middlewareTypes.Count - 1; i >= 0; i--)
            {
                var middlewareType = middlewareTypes[i];
                var middleware = (ICapabilityPipelineMiddleware)_serviceProvider.GetRequiredService(middlewareType);
                var next = handler;
                handler = (ctx) => middleware.InvokeAsync(ctx, next);
            }

            return await handler(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CapabilityExecutionResult.Timeout(DateTimeOffset.UtcNow - startedAt);
        }
        catch (Exception ex)
        {
            return CapabilityExecutionResult.Failure(
                "PIPELINE_ERROR",
                ex.Message,
                DateTimeOffset.UtcNow - startedAt);
        }
    }
}
