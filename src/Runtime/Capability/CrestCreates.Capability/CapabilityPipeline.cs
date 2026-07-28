using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Capability;

public sealed class CapabilityPipeline : ICapabilityPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICapabilityRegistry _registry;
    private readonly ICapabilityHandlerResolver _handlerResolver;
    private readonly CapabilityPipelineBuilder _builder;
    private readonly IDescriptorStableHashBuilder _hashBuilder;

    public CapabilityPipeline(
        IServiceProvider serviceProvider,
        ICapabilityRegistry registry,
        ICapabilityHandlerResolver handlerResolver,
        CapabilityPipelineBuilder builder,
        IDescriptorStableHashBuilder hashBuilder)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _handlerResolver = handlerResolver;
        _builder = builder;
        _hashBuilder = hashBuilder;
    }

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string capabilityIdOrName,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetById(capabilityIdOrName)
            ?? _registry.GetActiveVersion(capabilityIdOrName)
            ?? _registry.GetByName(capabilityIdOrName);

        if (descriptor == null)
        {
            return CapabilityExecutionResult.Failure(
                "CAPABILITY_NOT_FOUND",
                $"Capability '{capabilityIdOrName}' is not registered.",
                TimeSpan.Zero);
        }

        return await ExecuteAsync(descriptor, input, configureContext, ct).ConfigureAwait(false);
    }

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityDescriptor descriptor,
        object? input = null,
        Action<CapabilityExecutionContext>? configureContext = null,
        CancellationToken ct = default)
    {
        var context = new CapabilityExecutionContext
        {
            CapabilityId = descriptor.Id,
            CapabilityName = descriptor.Name,
            CapabilityVersion = descriptor.Version,
            CapabilityContractHash = _hashBuilder.Build(descriptor).ContractHash.Value,
            Input = input,
            CancellationToken = ct,
            ServiceProvider = _serviceProvider
        };
        configureContext?.Invoke(context);

        context.RequiredPermissions = descriptor.Permissions;

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            CapabilityPipelineDelegate handler = async (ctx) =>
            {
                var invoker = _handlerResolver.Resolve(descriptor.Id);
                if (invoker == null)
                {
                    return CapabilityExecutionResult.Failure(
                        "HANDLER_NOT_FOUND",
                        $"No handler registered for capability '{descriptor.Id}'.",
                        DateTimeOffset.UtcNow - startedAt);
                }

                var output = invoker is ICapabilityContextAwareHandlerInvoker contextAwareInvoker
                    ? await InvokeWithContextAccessorAsync(contextAwareInvoker, ctx)
                    : await invoker.InvokeAsync(ctx.Input, ctx.CancellationToken)
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
        catch (CapabilityFailureException ex)
        {
            return CapabilityExecutionResult.Failure(
                ex.ErrorCode,
                ex.Message,
                DateTimeOffset.UtcNow - startedAt,
                ex.Issues);
        }
        catch (Exception ex)
        {
            return CapabilityExecutionResult.Failure(
                "PIPELINE_ERROR",
                ex.Message,
                DateTimeOffset.UtcNow - startedAt);
        }
    }

    private async Task<object?> InvokeWithContextAccessorAsync(
        ICapabilityContextAwareHandlerInvoker invoker,
        CapabilityExecutionContext context)
    {
        var accessor = _serviceProvider.GetService<CapabilityExecutionContextAccessor>();
        accessor?.Set(context);
        try
        {
            return await invoker.InvokeAsync(context, context.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            accessor?.Clear(context);
        }
    }
}
