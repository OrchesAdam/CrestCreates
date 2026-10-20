using CrestCreates.Capability.Abstractions;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Identity;
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
    private readonly ICapabilityExceptionClassifier? _exceptionClassifier;

    public CapabilityPipeline(
        IServiceProvider serviceProvider,
        ICapabilityRegistry registry,
        ICapabilityHandlerResolver handlerResolver,
        CapabilityPipelineBuilder builder,
        IDescriptorStableHashBuilder hashBuilder,
        ICapabilityExceptionClassifier? exceptionClassifier = null)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _handlerResolver = handlerResolver;
        _builder = builder;
        _hashBuilder = hashBuilder;
        _exceptionClassifier = exceptionClassifier;
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
        var contractHash = _hashBuilder.Build(descriptor).ContractHash;
        var operationContext = _serviceProvider.GetService<IAuditOperationContextAccessor>()?.Current;
        var rootCorrelationId = _serviceProvider.GetService<IAuditIdentityGenerator>()?.CreateOperationId()
            ?? Guid.NewGuid().ToString("N");
        var context = new CapabilityExecutionContext
        {
            CapabilityId = descriptor.Id,
            CapabilityName = descriptor.Name,
            CapabilityVersion = descriptor.Version,
            CapabilityContractHash = contractHash.Value,
            AccountabilityContract = contractHash,
            CorrelationId = operationContext?.CorrelationId ?? rootCorrelationId,
            CausationId = operationContext?.OperationId,
            ParentAuditId = operationContext?.EnclosingAuditId,
            AccountabilityActor = operationContext?.Actor,
            InvocationSource = ParseInvocationSource(operationContext?.InvocationSource),
            TenantId = operationContext?.TenantId,
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
            var facts = new CapabilityFailureFacts(
                CapabilityFailureKind.Cancelled,
                "Crest.Request.Cancelled",
                "请求已取消。");
            return CapabilityExecutionResult.Cancelled(
                DateTimeOffset.UtcNow - startedAt, facts, context.AuditRecordId);
        }
        catch (CapabilityFailureException ex)
        {
            // The result keeps the capability/domain code every transport already understands.
            // The host classifier only adds HTTP-facing facts; rewriting the shared result code here
            // would change MCP/native error contracts that never see the Web bridge.
            var facts = _exceptionClassifier?.Classify(ex) ?? ClassifyCapabilityFailure(ex);
            return CapabilityExecutionResult.Failure(
                ex.ErrorCode,
                ex.Message,
                DateTimeOffset.UtcNow - startedAt,
                ex.Issues,
                context.AuditRecordId,
                facts);
        }
        catch (Exception ex)
        {
            // The transport-neutral result keeps its historical PIPELINE_ERROR contract (including
            // the adapter's rejection message for hosts that render it). HTTP hosts that registered
            // the Web bridge consume the bounded facts instead and never surface this message.
            var facts = _exceptionClassifier?.Classify(ex)
                ?? new CapabilityFailureFacts(
                    CapabilityFailureKind.Unknown,
                    "Crest.InternalError",
                    "服务器内部错误。");
            return CapabilityExecutionResult.Failure(
                "PIPELINE_ERROR",
                ex.Message,
                DateTimeOffset.UtcNow - startedAt,
                failureFacts: facts,
                auditRecordId: context.AuditRecordId);
        }
    }

    private static CapabilityFailureFacts ClassifyCapabilityFailure(CapabilityFailureException exception)
    {
        var facts = exception.ErrorCode switch
        {
            "UNAUTHORIZED" => new CapabilityFailureFacts(
                CapabilityFailureKind.Unauthorized, "Crest.Auth.Unauthorized", "当前请求未认证。",
                JoinIssueDetails(exception.Issues)),
            "CAPABILITY_FORBIDDEN" or "CAPABILITY_INVOCATION_SOURCE_FORBIDDEN" => new CapabilityFailureFacts(
                CapabilityFailureKind.Forbidden, "Crest.Auth.Forbidden", "没有权限执行当前操作。",
                JoinIssueDetails(exception.Issues)),
            "CAPABILITY_CONTEXT_REQUIRED" => new CapabilityFailureFacts(
                CapabilityFailureKind.Forbidden, "Crest.Auth.Forbidden", "没有可用的受信租户与用户上下文。"),
            "CAPABILITY_RESOURCE_NOT_FOUND" or "CAPABILITY_NOT_FOUND" => new CapabilityFailureFacts(
                CapabilityFailureKind.NotFound, "Crest.Entity.NotFound", "资源不存在。",
                JoinIssueDetails(exception.Issues)),
            "CAPABILITY_VALIDATION_FAILED" => new CapabilityFailureFacts(
                CapabilityFailureKind.ValidationFailed, "Crest.Validation.Failed", "数据验证失败。",
                JoinIssueDetails(exception.Issues)),
            "CAPABILITY_DECISION_CONFLICT" or "CAPABILITY_DECISION_STATE_INVALID" => new CapabilityFailureFacts(
                CapabilityFailureKind.Conflict, "Crest.Concurrency.Conflict", "并发冲突。"),
            _ => new CapabilityFailureFacts(
                CapabilityFailureKind.InvalidRequest, exception.ErrorCode, exception.Message,
                JoinIssueDetails(exception.Issues)),
        };
        return facts;
    }

    private static string? JoinIssueDetails(IReadOnlyList<CapabilityExecutionIssue> issues)
        => issues.Count == 0
            ? null
            : string.Join("; ", issues.Select(issue => issue.Message));

    private static InvocationSource ParseInvocationSource(string? value)
        => value switch
        {
            "http" => InvocationSource.Http,
            "workflow" => InvocationSource.Workflow,
            "human-task" => InvocationSource.HumanTask,
            "agent" => InvocationSource.Agent,
            "mcp" => InvocationSource.Mcp,
            "integration" => InvocationSource.Event,
            "system" => InvocationSource.Internal,
            _ => InvocationSource.Internal
        };

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
