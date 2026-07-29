using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using Microsoft.Extensions.DependencyInjection;
using Rougamo;
using Rougamo.Context;
using Rougamo.Metadatas;

namespace CrestCreates.AuditLogging.Interceptors;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
[Optimization(ForceSync = ForceSync.OnEntry)]
public class AuditedMoAttribute : AsyncMoAttribute
{
    private const string StateKey = "crestcreates.accountability.method-state";
    private readonly string? _actionName;

    public AuditedMoAttribute(string? actionName = null, bool includeParameters = true, bool includeResult = false)
    {
        _actionName = actionName;
    }

    public override ValueTask OnEntryAsync(MethodContext context)
    {
        try
        {
            var runtime = ResolveRuntime(context);
            if (runtime is null) return ValueTask.CompletedTask;
            var methodId = $"{context.Method.DeclaringType?.FullName ?? "unknown"}.{context.Method.Name}";
            var descriptor = new AuditedMethodInvocationDescriptor
            {
                MethodId = methodId,
                ActionName = _actionName ?? methodId,
                StartedAt = DateTimeOffset.UtcNow
            };
            context.Datas[StateKey] = runtime.Enter(descriptor);
        }
        catch
        {
            // A post-fact bridge failure must never prevent the business method.
        }

        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnSuccessAsync(MethodContext context)
    {
        if (!TryGetState(context, out var runtime, out var state)) return;
        try
        {
            runtime.SetOutcome(state, new AuditedMethodInvocationOutcome
            {
                Kind = AuditedMethodOutcomeKind.Succeeded
            });
            await runtime.ExitAsync(state).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the already-produced business result.
        }
    }

    public override async ValueTask OnExceptionAsync(MethodContext context)
    {
        if (!TryGetState(context, out var runtime, out var state)) return;
        try
        {
            runtime.SetOutcome(state, new AuditedMethodInvocationOutcome
            {
                Kind = context.Exception is OperationCanceledException
                    ? AuditedMethodOutcomeKind.Cancelled
                    : AuditedMethodOutcomeKind.Failed,
                SafeCode = context.Exception is OperationCanceledException ? "METHOD_CANCELLED" : "METHOD_EXCEPTION"
            });
            await runtime.ExitAsync(state).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original business exception/cancellation.
        }
    }

    private static bool TryGetState(MethodContext context, out IAuditedMethodAccountabilityRuntime runtime, out IAuditedMethodInvocationState state)
    {
        runtime = ResolveRuntime(context)!;
        var value = context.Datas.Contains(StateKey) ? context.Datas[StateKey] : null;
        if (runtime is null || value is not IAuditedMethodInvocationState)
        {
            state = null!;
            return false;
        }
        state = (IAuditedMethodInvocationState)value;
        return true;
    }

    private static IAuditedMethodAccountabilityRuntime? ResolveRuntime(MethodContext context)
    {
        return AuditedMethodAccountabilityRuntimeContext.Current
            ?? MethodContextPinnedExtensions.GetService<IAuditedMethodAccountabilityRuntime>(context);
    }
}
