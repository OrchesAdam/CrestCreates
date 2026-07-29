using System.Diagnostics;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Capability.Middleware;

internal sealed class AuditMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ILogger<AuditMiddleware> _logger;
    private readonly IAuditRecorder _recorder;
    private readonly IAuditIdentityGenerator _identity;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly TimeProvider _timeProvider;

    public AuditMiddleware(
        ILogger<AuditMiddleware> logger,
        IAuditRecorder recorder,
        IAuditIdentityGenerator identity,
        IAuditOperationContextAccessor contexts,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _recorder = recorder;
        _identity = identity;
        _contexts = contexts;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var executionId = _identity.CreateOperationId();
        var auditId = _identity.CreateAuditId();
        context.ExecutionId = executionId;
        var operationScope = _contexts.Push(new AuditOperationContext
        {
            CorrelationId = context.CorrelationId,
            OperationId = executionId,
            EnclosingAuditId = auditId,
            Actor = context.AccountabilityActor ?? ResolveActor(context),
            TenantId = context.TenantId,
            InvocationSource = MapSource(context.InvocationSource),
            InitiatingOperationId = executionId,
            InitiatingAuditId = auditId
        });
        var sw = Stopwatch.StartNew();
        CapabilityExecutionResult? result = null;
        Exception? unhandledException = null;
        CapabilityFailureException? capabilityFailure = null;
        bool cancelled = false;

        try
        {
            result = await next(context);
            return result;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        catch (CapabilityFailureException ex)
        {
            capabilityFailure = ex;
            throw;
        }
        catch (Exception ex)
        {
            unhandledException = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            try
            {
                var errorCode = cancelled
                    ? "CANCELLED"
                    : result?.ErrorCode
                      ?? capabilityFailure?.ErrorCode
                      ?? (result?.Status == CapabilityExecutionStatus.TimedOut ? "CAPABILITY_TIMEOUT" : null)
                      ?? (unhandledException is not null ? "UNHANDLED_EXCEPTION" : null);

                var record = await _recorder.RecordAsync(
                    CreateEnvelope(context, auditId, executionId, result, errorCode, sw.Elapsed, cancelled),
                    CancellationToken.None).ConfigureAwait(false);
                context.AuditRecordId = record.IsAccepted ? record.AuditId : null;
                if (result is not null)
                    result.AuditRecordId = context.AuditRecordId;
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to record audit for capability '{CapabilityId}'", context.CapabilityId);
            }
            finally
            {
                operationScope?.Dispose();
            }
        }
    }

    private AuditEnvelope CreateEnvelope(
        CapabilityExecutionContext context,
        string auditId,
        string executionId,
        CapabilityExecutionResult? result,
        string? errorCode,
        TimeSpan duration,
        bool cancelled)
    {
        var actor = context.AccountabilityActor ?? ResolveActor(context);
        var status = cancelled
            ? "cancelled"
            : result?.Status switch
        {
            CapabilityExecutionStatus.Succeeded => "succeeded",
            CapabilityExecutionStatus.TimedOut => "failed",
            _ => "failed"
        };
        return new AuditEnvelope
        {
            AuditId = auditId,
            OccurredAt = _timeProvider.GetUtcNow(),
            TenantId = context.TenantId,
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
            ParentAuditId = context.ParentAuditId,
            Actor = actor,
            Action = new AuditAction { Kind = "capability.execute", Name = context.CapabilityId },
            Target = new AuditTarget { Kind = "capability", Id = context.CapabilityId, Version = context.CapabilityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            Outcome = new AuditOutcome { Status = status, Code = errorCode },
            Runtime = new AuditRuntimeContext
            {
                InvocationSource = MapSource(context.InvocationSource),
                ExecutionId = executionId,
                Duration = duration,
                References = context.AccountabilityRuntimeReferences.IsDefault ? [] : context.AccountabilityRuntimeReferences
            },
            Descriptors = new AuditDescriptorContext
            {
                Items = [new AuditDescriptorReference { Kind = "capability", Id = context.CapabilityId, Version = context.CapabilityVersion, ContractHash = context.AccountabilityContract }]
            },
            Evidence = [],
            Tags = AuditTagMap.Empty
        };
    }

    private static AuditActor ResolveActor(CapabilityExecutionContext context)
    {
        var kind = context.InvocationSource switch
        {
            InvocationSource.Http => context.UserId is null ? "anonymous" : "user",
            InvocationSource.Workflow => "workflow",
            InvocationSource.HumanTask => "human-task",
            InvocationSource.Agent => "unknown",
            InvocationSource.Mcp => "unknown",
            InvocationSource.Event => "integration",
            InvocationSource.BackgroundJob => "scheduler",
            InvocationSource.Internal => "system",
            _ => "unknown"
        };
        var id = kind == "user" ? context.UserId! : kind is "anonymous" or "system" ? kind : "unknown";
        return new AuditActor { Kind = kind, Id = id };
    }

    private static string MapSource(InvocationSource source)
        => source switch
        {
            InvocationSource.Http => "http",
            InvocationSource.Workflow => "workflow",
            InvocationSource.HumanTask => "human-task",
            InvocationSource.Agent => "agent",
            InvocationSource.Mcp => "mcp",
            InvocationSource.Event => "integration",
            InvocationSource.BackgroundJob => "system",
            InvocationSource.Internal => "system",
            _ => "system"
        };

}
