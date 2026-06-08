using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Capability;

public sealed class CapabilityPipeline : ICapabilityPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICapabilityRegistry _registry;
    private readonly CapabilityPipelineBuilder _builder;

    public CapabilityPipeline(
        IServiceProvider serviceProvider,
        ICapabilityRegistry registry,
        CapabilityPipelineBuilder builder)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
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
                if (ctx.Items.TryGetValue("__handler", out var h) && h is ICapabilityHandler marker)
                {
                    var handlerInterface = marker.GetType().GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType
                            && i.GetGenericTypeDefinition() == typeof(ICapabilityHandler<,>));

                    if (handlerInterface != null)
                    {
                        var inputArg = ctx.Input;
                        var method = handlerInterface.GetMethod("ExecuteAsync")!;
                        var task = (Task)method.Invoke(marker, new[] { inputArg, ctx.CancellationToken })!;
                        await task.ConfigureAwait(false);

                        var resultProp = task.GetType().GetProperty("Result");
                        var output = resultProp?.GetValue(task);

                        return CapabilityExecutionResult.Success(
                            output,
                            DateTimeOffset.UtcNow - startedAt);
                    }
                }

                return CapabilityExecutionResult.Failure(
                    "HANDLER_NOT_FOUND",
                    $"No handler found for capability '{capabilityName}'.",
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
