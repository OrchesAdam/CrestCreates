using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Json;
using CrestCreates.Agent.Memory.Accountability.CanonicalHashing;
using CrestCreates.Agent.Memory.Accountability.Options;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Agent.Memory.Accountability.Production;

/// <summary>
/// The one real Agent Memory Accountability producer. Each publish is one
/// independent, bounded attempt that maps a completed Memory terminal result
/// onto the unified <see cref="AuditEnvelope"/> contract and calls
/// <see cref="IAuditRecorder"/> exactly once. Only contract violations throw;
/// recorder failures, timeouts, and sink outcomes never change the original
/// Memory result and are observed through bounded safe diagnostics.
/// </summary>
public sealed class AgentMemoryAccountabilityProducer : IAgentMemoryAccountabilityProducer
{
    private readonly IAuditRecorder _recorder;
    private readonly AgentMemoryAccountabilityOptions _options;
    private readonly AgentMemoryAccountabilityAuditIdProjector _auditIdProjector;
    private readonly ILogger<AgentMemoryAccountabilityProducer>? _logger;

    public AgentMemoryAccountabilityProducer(
        IAuditRecorder recorder,
        AgentMemoryAccountabilityOptions options,
        AgentMemoryAccountabilityAuditIdProjector auditIdProjector,
        ILogger<AgentMemoryAccountabilityProducer>? logger = null)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _auditIdProjector = auditIdProjector ?? throw new ArgumentNullException(nameof(auditIdProjector));
        _logger = logger;
    }

    public ValueTask PublishRecallAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryRecallAccountabilityPayload payload)
    {
        ValidateContract(identity, context, payload.OperationId);

        var action = new AuditAction { Kind = "agent-memory.recall", Name = "recall" };
        var target = new AuditTarget { Kind = "agent-memory-pack", Id = payload.OperationId };
        var outcome = payload.Result switch
        {
            "completed" => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Succeeded,
                Code = payload.ReturnedCount == 0 ? "empty" : "completed"
            },
            _ => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Rejected,
                Code = payload.StableFailureCode ?? "rejected"
            }
        };

        var envelope = BuildEnvelope(
            identity, context, action, target, outcome,
            AgentMemoryAccountabilityPayloadKinds.Recall, payload,
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryRecallAccountabilityPayload);

        return PublishAsync(envelope);
    }

    public ValueTask PublishCurationAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryCurationAccountabilityPayload payload)
    {
        ValidateContract(identity, context, payload.OperationId);

        var (actionKind, actionName, targetKind, targetId) = payload.Operation switch
        {
            "promote" => ("agent-memory.promote", "promote", "agent-memory-candidate", payload.CandidateId),
            "reject" => ("agent-memory.reject", "reject", "agent-memory-candidate", payload.CandidateId),
            "supersede" => ("agent-memory.supersede", "supersede", "agent-memory", payload.MemoryId),
            "archive" => ("agent-memory.archive", "archive", "agent-memory", payload.MemoryId),
            _ => throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.ProducerContractInvalid.Value}: unknown curation operation {payload.Operation}")
        };

        var action = new AuditAction { Kind = actionKind, Name = actionName };
        var target = new AuditTarget { Kind = targetKind, Id = targetId ?? payload.OperationId };
        var outcome = payload.Result switch
        {
            "committed" => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Succeeded,
                Code = "committed"
            },
            "conflict" => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Rejected,
                Code = payload.StableFailureCode ?? "conflict"
            },
            _ => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Rejected,
                Code = payload.StableFailureCode ?? "rejected"
            }
        };

        var envelope = BuildEnvelope(
            identity, context, action, target, outcome,
            AgentMemoryAccountabilityPayloadKinds.Curation, payload,
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryCurationAccountabilityPayload);

        return PublishAsync(envelope);
    }

    public ValueTask PublishSourceExpansionAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemorySourceExpansionAccountabilityPayload payload)
    {
        ValidateContract(identity, context, payload.OperationId);

        var action = new AuditAction { Kind = "agent-memory.source-expand", Name = "source-expand" };
        var target = new AuditTarget { Kind = "agent-memory-source", Id = payload.SourceId };
        var outcome = payload.Status switch
        {
            "expanded" => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Succeeded,
                Code = "expanded"
            },
            "redacted" => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Rejected,
                Code = "redacted"
            },
            var status => new AuditOutcome
            {
                Status = AuditOutcomeStatuses.Rejected,
                Code = status
            }
        };

        var envelope = BuildEnvelope(
            identity, context, action, target, outcome,
            AgentMemoryAccountabilityPayloadKinds.SourceExpansion, payload,
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemorySourceExpansionAccountabilityPayload);

        return PublishAsync(envelope);
    }

    private AuditEnvelope BuildEnvelope<T>(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AuditAction action,
        AuditTarget target,
        AuditOutcome outcome,
        string payloadKind,
        T payload,
        JsonTypeInfo<T> typeInfo)
    {
        var runtimeReferences = ImmutableArray.CreateBuilder<AuditRuntimeReference>();
        var isMcp = string.Equals(context.InvocationSource, AuditInvocationSources.Mcp, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(context.InvocationId))
            runtimeReferences.Add(new AuditRuntimeReference(isMcp ? "mcp-invocation" : "agent-invocation", context.InvocationId));
        if (!string.IsNullOrWhiteSpace(context.SessionId))
            runtimeReferences.Add(new AuditRuntimeReference(isMcp ? "mcp-session" : "agent-session", context.SessionId));

        return new AuditEnvelope
        {
            ContractVersion = 1,
            AuditId = _auditIdProjector.ComputeAuditId(
                context.TenantId,
                action.Kind,
                identity.OperationId,
                payloadKind,
                AgentMemoryAccountabilityPayloadKinds.Version),
            OccurredAt = identity.OccurredAt,
            TenantId = context.TenantId,
            CorrelationId = context.CorrelationId!,
            CausationId = context.CausationId,
            ParentAuditId = context.ParentAuditId,
            Actor = new AuditActor
            {
                Kind = MapActorKind(context.ActorKind),
                Id = context.ActorId,
                // DisplayName is non-accountability input. Never persist it in
                // the envelope (it would also become part of RecordHash).
            },
            Action = action,
            Target = target,
            Outcome = outcome,
            Runtime = new AuditRuntimeContext
            {
                InvocationSource = MapInvocationSource(context.InvocationSource),
                ExecutionId = identity.OperationId,
                References = runtimeReferences.ToImmutable()
            },
            Descriptors = AuditDescriptorContext.Empty,
            Evidence = [],
            Payload = new AuditPayload
            {
                Kind = payloadKind,
                Version = AgentMemoryAccountabilityPayloadKinds.Version,
                Data = JsonSerializer.SerializeToElement(payload, typeInfo)
            },
            Tags = AuditTagMap.Empty
        };
    }

    private async ValueTask PublishAsync(AuditEnvelope envelope)
    {
        using var budget = new CancellationTokenSource();
        budget.CancelAfter(_options.WriteTimeout);
        AuditRecordResult result;
        Task<AuditRecordResult>? recordingTask = null;
        try
        {
            recordingTask = _recorder.RecordAsync(envelope, budget.Token).AsTask();
            // Recorder implementations are required to observe the token, but
            // the Memory contract cannot delegate its hard deadline to an
            // arbitrary provider. WaitAsync makes the producer's finite budget
            // an actual upper bound even when a recorder ignores cancellation.
            result = await recordingTask.WaitAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (recordingTask is not null)
                _ = ObserveLateRecorderFailureAsync(recordingTask);
            LogSafe(AgentMemoryAccountabilityDiagnosticCodes.Timeout.Value, envelope.AuditId, envelope.Action?.Kind, envelope.Payload?.Kind);
            return;
        }
        catch (Exception)
        {
            LogSafe(AgentMemoryAccountabilityDiagnosticCodes.RecorderFailed.Value, envelope.AuditId, envelope.Action?.Kind, envelope.Payload?.Kind);
            return;
        }

        LogOutcome(result, envelope);
    }

    private static async Task ObserveLateRecorderFailureAsync(Task<AuditRecordResult> recordingTask)
    {
        try
        {
            await recordingTask.ConfigureAwait(false);
        }
        catch
        {
            // The bounded producer attempt has already completed. Observe a
            // late provider fault without allowing it to escape or become an
            // unobserved task exception.
        }
    }

    private void LogOutcome(AuditRecordResult result, AuditEnvelope envelope)
    {
        // Conflict and Duplicate are detected from the per-sink outcome rather than
        // the aggregate RecordStatus: a single-sink Conflict surfaces as Failed at
        // the recorder level but must still be observable as a distinct conflict
        // diagnostic (spec §12).
        var code = result.SinkResults.Any(x => x.Status == AuditSinkWriteStatus.Conflict)
            ? AgentMemoryAccountabilityDiagnosticCodes.Conflict
            : result.SinkResults.Any(x => x.Status == AuditSinkWriteStatus.Duplicate)
                ? AgentMemoryAccountabilityDiagnosticCodes.Duplicate
                : result.Status switch
                {
                    AuditRecordStatus.Recorded => AgentMemoryAccountabilityDiagnosticCodes.Recorded,
                    AuditRecordStatus.PartiallyRecorded => AgentMemoryAccountabilityDiagnosticCodes.SinkFailed,
                    AuditRecordStatus.Rejected => AgentMemoryAccountabilityDiagnosticCodes.RecorderRejected,
                    AuditRecordStatus.NoSinkConfigured => AgentMemoryAccountabilityDiagnosticCodes.NoSink,
                    _ => AgentMemoryAccountabilityDiagnosticCodes.SinkFailed
                };
        LogSafe(code.Value, envelope.AuditId, envelope.Action?.Kind, envelope.Payload?.Kind);
    }

    private void LogSafe(string? code, string? auditId, string? actionKind, string? payloadKind)
    {
        if (_logger is null || string.IsNullOrWhiteSpace(code))
            return;
        _logger.LogInformation(
            "{Code} AuditId={AuditId} ActionKind={ActionKind} PayloadKind={PayloadKind}",
            code, auditId, actionKind, payloadKind);
    }

    private static void ValidateContract(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        string payloadOperationId)
    {
        if (string.IsNullOrWhiteSpace(identity.OperationId)
            || !string.Equals(identity.OperationId, payloadOperationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.ProducerContractInvalid.Value}: OperationId mismatch between identity and payload");
        }
        if (identity.OccurredAt == default)
        {
            throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.ProducerContractInvalid.Value}: OccurredAt must be supplied");
        }
        if (string.IsNullOrWhiteSpace(context.TenantId)
            || string.IsNullOrWhiteSpace(context.ActorId)
            || string.IsNullOrWhiteSpace(context.ActorKind)
            || string.IsNullOrWhiteSpace(context.CorrelationId)
            || string.IsNullOrWhiteSpace(context.InvocationSource))
        {
            throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.ProducerContractInvalid.Value}: trusted tenant, actor, and correlation context are required");
        }
        if (!IsStableActorKind(context.ActorKind)
            || !IsStableInvocationSource(context.InvocationSource))
        {
            throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.ProducerContractInvalid.Value}: actor and invocation source must use explicit stable mappings");
        }
    }

    private static bool IsStableActorKind(string actorKind)
        => actorKind is AuditActorKinds.User or AuditActorKinds.Anonymous or AuditActorKinds.System
            or AuditActorKinds.Workflow or AuditActorKinds.HumanTask or AuditActorKinds.Agent
            or AuditActorKinds.Integration or AuditActorKinds.Scheduler or AuditActorKinds.McpClient
            or AuditActorKinds.Unknown;

    private static string MapActorKind(string actorKind)
        => actorKind switch
        {
            AuditActorKinds.User => AuditActorKinds.User,
            AuditActorKinds.Anonymous => AuditActorKinds.Anonymous,
            AuditActorKinds.System => AuditActorKinds.System,
            AuditActorKinds.Workflow => AuditActorKinds.Workflow,
            AuditActorKinds.HumanTask => AuditActorKinds.HumanTask,
            AuditActorKinds.Agent => AuditActorKinds.Agent,
            AuditActorKinds.Integration => AuditActorKinds.Integration,
            AuditActorKinds.Scheduler => AuditActorKinds.Scheduler,
            AuditActorKinds.McpClient => AuditActorKinds.McpClient,
            AuditActorKinds.Unknown => AuditActorKinds.Unknown,
            _ => throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.ProducerContractInvalid.Value}: unsupported actor kind")
        };

    private static bool IsStableInvocationSource(string? invocationSource)
        => invocationSource is AuditInvocationSources.Http or AuditInvocationSources.Workflow
            or AuditInvocationSources.HumanTask or AuditInvocationSources.Agent
            or AuditInvocationSources.Mcp or AuditInvocationSources.Integration
            or AuditInvocationSources.System;

    private static string MapInvocationSource(string? invocationSource)
        => invocationSource switch
        {
            AuditInvocationSources.Http => AuditInvocationSources.Http,
            AuditInvocationSources.Workflow => AuditInvocationSources.Workflow,
            AuditInvocationSources.HumanTask => AuditInvocationSources.HumanTask,
            AuditInvocationSources.Agent => AuditInvocationSources.Agent,
            AuditInvocationSources.Mcp => AuditInvocationSources.Mcp,
            AuditInvocationSources.Integration => AuditInvocationSources.Integration,
            AuditInvocationSources.System => AuditInvocationSources.System,
            _ => throw new InvalidOperationException(
                $"{AgentMemoryAccountabilityDiagnosticCodes.ProducerContractInvalid.Value}: unsupported invocation source")
        };
}
