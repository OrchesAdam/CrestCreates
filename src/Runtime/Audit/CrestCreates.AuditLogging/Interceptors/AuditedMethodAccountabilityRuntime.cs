using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.AuditLogging.Interceptors;

public sealed class AuditedMethodAccountabilityRuntime : IAuditedMethodAccountabilityRuntime
{
    private readonly IAuditRecorder _recorder;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly IAuditIdentityGenerator _identity;
    private readonly TimeProvider _timeProvider;

    public AuditedMethodAccountabilityRuntime(
        IAuditRecorder recorder,
        IAuditOperationContextAccessor contexts,
        IAuditIdentityGenerator identity,
        TimeProvider? timeProvider = null)
    {
        _recorder = recorder;
        _contexts = contexts;
        _identity = identity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IAuditedMethodInvocationState Enter(AuditedMethodInvocationDescriptor descriptor)
    {
        var parent = _contexts.Current;
        var operationId = _identity.CreateOperationId();
        var auditId = _identity.CreateAuditId();
        var actor = parent?.Actor ?? new AuditActor { Kind = "unknown", Id = "unknown" };
        var operation = new AuditOperationContext
        {
            CorrelationId = parent?.CorrelationId ?? _identity.CreateOperationId(),
            OperationId = operationId,
            Actor = actor,
            InvocationSource = parent?.InvocationSource ?? "system",
            EnclosingAuditId = auditId,
            TenantId = parent?.TenantId,
            InitiatingOperationId = parent?.InitiatingOperationId ?? parent?.OperationId,
            InitiatingAuditId = parent?.InitiatingAuditId ?? parent?.EnclosingAuditId
        };
        return new State(
            descriptor,
            auditId,
            parent?.EnclosingAuditId,
            operation,
            parent?.OperationId,
            parent?.InvocationSource,
            _contexts.Push(operation));
    }

    public void SetOutcome(IAuditedMethodInvocationState state, AuditedMethodInvocationOutcome outcome)
        => ((State)state).Outcome = outcome;

    public ValueTask ExitAsync(IAuditedMethodInvocationState state)
    {
        var invocation = (State)state;
        AuditEnvelope envelope;
        try
        {
            var outcome = invocation.Outcome ?? new AuditedMethodInvocationOutcome { Kind = AuditedMethodOutcomeKind.Failed, SafeCode = "METHOD_NO_OUTCOME" };
            envelope = new AuditEnvelope
            {
                AuditId = invocation.AuditId,
                OccurredAt = _timeProvider.GetUtcNow(),
                TenantId = invocation.Operation.TenantId,
                CorrelationId = invocation.Operation.CorrelationId,
                CausationId = invocation.CausingOperationId,
                ParentAuditId = invocation.ParentAuditId,
                Actor = invocation.Operation.Actor,
                Action = new AuditAction { Kind = "method.invoke", Name = invocation.Descriptor.ActionName },
                Target = new AuditTarget { Kind = "application.method", Id = invocation.Descriptor.MethodId },
                Outcome = new AuditOutcome
                {
                    Status = outcome.Kind switch
                    {
                        AuditedMethodOutcomeKind.Succeeded => "succeeded",
                        AuditedMethodOutcomeKind.Cancelled => "cancelled",
                        _ => "failed"
                    },
                    Code = outcome.SafeCode
                },
                Runtime = new AuditRuntimeContext
                {
                    InvocationSource = invocation.ParentInvocationSource ?? "system",
                    ExecutionId = invocation.Operation.OperationId,
                    Duration = _timeProvider.GetUtcNow() - invocation.Descriptor.StartedAt,
                    References = []
                },
                Descriptors = AuditDescriptorContext.Empty,
                Evidence = [],
                Tags = AuditTagMap.Empty
            };
        }
        finally
        {
            // AsyncLocal restoration must occur synchronously in the caller's
            // ExecutionContext, before the recorder introduces an await boundary.
            invocation.Scope.Dispose();
        }

        return RecordBestEffortAsync(envelope);
    }

    private async ValueTask RecordBestEffortAsync(AuditEnvelope envelope)
    {
        try
        {
            await _recorder.RecordAsync(envelope, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Accountability is post-fact telemetry; a recorder/provider
            // failure must never replace the method result or exception.
        }
    }

    private sealed class State : IAuditedMethodInvocationState
    {
        public State(
            AuditedMethodInvocationDescriptor descriptor,
            string auditId,
            string? parentAuditId,
            AuditOperationContext operation,
            string? causingOperationId,
            string? parentInvocationSource,
            IDisposable scope)
        {
            Descriptor = descriptor;
            AuditId = auditId;
            ParentAuditId = parentAuditId;
            Operation = operation;
            CausingOperationId = causingOperationId;
            ParentInvocationSource = parentInvocationSource;
            Scope = scope;
        }

        public AuditedMethodInvocationDescriptor Descriptor { get; }
        public string AuditId { get; }
        public string? ParentAuditId { get; }
        public AuditOperationContext Operation { get; }
        public string? CausingOperationId { get; }
        public string? ParentInvocationSource { get; }
        public IDisposable Scope { get; }
        public AuditedMethodInvocationOutcome? Outcome { get; set; }
    }
}
