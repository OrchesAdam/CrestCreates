using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Accountability.Recording;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.CanonicalHashing;
using CrestCreates.Agent.Memory.Accountability.Options;
using CrestCreates.Agent.Memory.Accountability.Production;
using CrestCreates.Agent.Memory.Accountability.Sanitization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.Extensions.Logging;
using Moq;

namespace CrestCreates.Agent.Memory.Accountability.Tests;

/// <summary>
/// Shared deterministic fixture for the Agent Memory Accountability bridge tests.
/// The canonical hash computer returns a deterministic digest computed over the
/// exact canonical projection, so AuditIds and record hashes are reproducible
/// while still exercising the real canonical hash runtime path.
/// </summary>
internal static class AccountabilityTestFixture
{
    public const string FixedTenantId = "tenant-a";
    public const string FixedActorId = "actor-1";
    public const string FixedAgentId = "agent-1";
    public const string FixedSessionId = "session-1";
    public const string FixedInvocationId = "invocation-1";
    public const string FixedCorrelationId = "correlation-1";
    public const string FixedCausationId = "causation-1";
    public const string FixedOperationId = "op_0123456789abcdef";

    public static readonly DateTimeOffset FixedOccurredAt = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Computes the deterministic digest the mocked computer returns for a projection.</summary>
    public static string ComputeDigest(CanonicalHashProjectionResult projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            projection.WriteCanonicalJson(writer);
            writer.Flush();
        }

        var hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    /// <summary>
    /// A mock computer whose result is fully derived from the canonical projection,
    /// so identity tests can reason about what the audit id must be.
    /// </summary>
    public static Mock<ICanonicalHashComputer> CreateHashComputer()
    {
        var mock = new Mock<ICanonicalHashComputer>();
        mock.Setup(computer => computer.ComputeFromProjection(It.IsAny<CanonicalHashProjectionResult>()))
            .Returns((CanonicalHashProjectionResult projection) => new CanonicalHash
            {
                Value = ComputeDigest(projection),
                Algorithm = "SHA-256",
                AlgorithmVersion = projection.Metadata.AlgorithmVersion,
                ArtifactKind = projection.Metadata.ArtifactKind,
                DescriptorKind = projection.Metadata.DescriptorKind,
                Scope = projection.Metadata.Scope,
                Purpose = projection.Metadata.Purpose,
                ContractVersion = projection.Metadata.ContractVersion,
                CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
            });
        return mock;
    }

    /// <summary>A structurally valid canonical hash with all eight required fields.</summary>
    public static CanonicalHash CreateValidHash(string value = "aabb", string? descriptorKind = null) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "AccountabilityRecord",
        DescriptorKind = descriptorKind,
        Scope = "InternalFull",
        Purpose = "AuditEvidence",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "accountability-record-hash-v1"
    };

    public static CanonicalHash CreateEffectivePackHash(string value = "aabb") => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = AgentMemoryAccountabilityPayloadKinds.EffectivePackArtifactKind,
        Scope = AgentMemoryAccountabilityPayloadKinds.EffectivePackScope,
        Purpose = AgentMemoryAccountabilityPayloadKinds.EffectivePackPurpose,
        ContractVersion = AgentMemoryAccountabilityPayloadKinds.EffectivePackContractVersion,
        CanonicalShapeVersion = AgentMemoryAccountabilityPayloadKinds.EffectivePackCanonicalShapeVersion
    };

    public static CanonicalHash CreateEffectiveContentHash(string value = "aabb") => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = AgentMemoryAccountabilityPayloadKinds.EffectiveContentArtifactKind,
        Scope = AgentMemoryAccountabilityPayloadKinds.EffectiveContentScope,
        Purpose = AgentMemoryAccountabilityPayloadKinds.EffectiveContentPurpose,
        ContractVersion = AgentMemoryAccountabilityPayloadKinds.EffectiveContentContractVersion,
        CanonicalShapeVersion = AgentMemoryAccountabilityPayloadKinds.EffectiveContentCanonicalShapeVersion
    };

    public static AgentMemoryInvocationContext CreateContext(
        string tenantId = FixedTenantId,
        string actorId = FixedActorId,
        string? agentId = FixedAgentId,
        string? sessionId = FixedSessionId,
        string? invocationId = FixedInvocationId,
        string? correlationId = FixedCorrelationId,
        string? causationId = FixedCausationId,
        string? invocationSource = "agent",
        string? parentAuditId = null)
        => new()
        {
            TenantId = tenantId,
            ActorId = actorId,
            ActorKind = "agent",
            AgentId = agentId,
            SessionId = sessionId,
            InvocationId = invocationId,
            CorrelationId = correlationId,
            CausationId = causationId,
            ParentAuditId = parentAuditId,
            InvocationSource = invocationSource,
            DisplayName = null,
            TraceAttributes = new Dictionary<string, string>()
        };

    public static AgentMemoryOperationIdentity CreateIdentity(
        string? operationId = null,
        DateTimeOffset? occurredAt = null)
        => new()
        {
            OperationId = operationId ?? FixedOperationId,
            OccurredAt = occurredAt ?? FixedOccurredAt
        };

    public static AgentMemoryRecallAccountabilityPayload CreateRecallPayload(
        string operationId = FixedOperationId,
        string result = "completed",
        int returnedCount = 2,
        CanonicalHash? effectivePackHash = null,
        string? stableFailureCode = null,
        string? minimumConfidence = "0.5")
    {
        if (result == "completed")
        {
            return new AgentMemoryRecallAccountabilityPayload
            {
                OperationId = operationId,
                Result = result,
                EffectivePackHash = effectivePackHash ?? CreateEffectivePackHash(),
                ReturnedCount = returnedCount,
                WasTruncated = false,
                DiagnosticCodes = Array.Empty<string>(),
                RequestedKinds = Array.Empty<string>(),
                MaximumCount = 10,
                CharacterBudget = 2000,
                MinimumConfidence = minimumConfidence!
            };
        }

        return new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = operationId,
            Result = result,
            StableFailureCode = stableFailureCode ?? "resource-unavailable",
            EffectivePackHash = null,
            ReturnedCount = 0,
            WasTruncated = false,
            DiagnosticCodes = Array.Empty<string>(),
            RequestedKinds = Array.Empty<string>(),
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = minimumConfidence!
        };
    }

    public static AgentMemoryCurationAccountabilityPayload CreateCurationPayload(
        string operationId = FixedOperationId,
        string operation = "promote",
        string result = "committed",
        string? candidateId = "candidate-1",
        string? memoryId = "memory-1",
        string? replacementCandidateId = null,
        string? newMemoryId = "new-memory-1",
        string? resultingState = null,
        string? stableFailureCode = null)
        => new()
        {
            OperationId = operationId,
            Operation = operation,
            CandidateId = operation is "promote" or "reject" ? candidateId : null,
            MemoryId = operation is "supersede" or "archive" ? memoryId : null,
            ReplacementCandidateId = operation == "supersede" ? replacementCandidateId : null,
            NewMemoryId = operation is "promote" or "supersede" ? newMemoryId : null,
            ExpectedCandidateStateHash = null,
            ExpectedMemoryStateHash = null,
            ExpectedReplacementStateHash = null,
            ExpectedContentHash = null,
            PreviousState = result == "committed" ? operation switch
            {
                "promote" or "reject" => "candidate",
                "supersede" or "archive" => "active",
                _ => null
            } : null,
            ResultingState = result == "committed" ? resultingState ?? operation switch
            {
                "promote" => "active",
                "reject" => "rejected",
                "supersede" => "superseded",
                "archive" => "archived",
                _ => null
            } : null,
            Result = result,
            StableFailureCode = result switch
            {
                "conflict" => stableFailureCode ?? "state-conflict",
                "rejected" => stableFailureCode ?? "resource-unavailable",
                _ => null
            },
            Sanitization = null
        };

    public static AgentMemorySourceExpansionAccountabilityPayload CreateSourceExpansionPayload(
        string operationId = FixedOperationId,
        string status = "expanded",
        string sourceKind = "ConversationTurn",
        string sourceId = "source-1",
        CanonicalHash? effectiveVisibleContentHash = null,
        int maximumCharacters = 4000,
        bool wasTruncated = false,
        string sanitizationState = "none")
        => new()
        {
            OperationId = operationId,
            SourceKind = sourceKind,
            SourceId = sourceId,
            RangeStart = null,
            RangeEnd = null,
            Status = status,
            EffectiveVisibleContentHash = status == "expanded" ? effectiveVisibleContentHash ?? CreateEffectiveContentHash() : null,
            MaximumCharacters = maximumCharacters,
            WasTruncated = wasTruncated,
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = sanitizationState,
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            },
            DiagnosticCodes = Array.Empty<string>()
        };

    /// <summary>
    /// Serializes a typed payload through the generated JSON context and wraps it
    /// in an <see cref="AuditPayload"/> for rule-level validation tests.
    /// </summary>
    public static AuditPayload CreateAuditPayload<T>(
        T payload,
        JsonTypeInfo<T> typeInfo,
        string kind,
        int version = AgentMemoryAccountabilityPayloadKinds.Version)
    {
        var data = JsonSerializer.SerializeToElement(payload, typeInfo);
        return new AuditPayload { Kind = kind, Version = version, Data = data };
    }

    /// <summary>Builds the real recorder pipeline used by the producer under test.</summary>
    public static DefaultAuditRecorder CreateRecorder(
        IEnumerable<IAuditSink> sinks,
        ICanonicalHashComputer hashComputer,
        TimeProvider? timeProvider = null,
        AccountabilityOptions? options = null)
    {
        var validator = new AuditEnvelopeValidator();
        var projectionWriter = new AccountabilityCanonicalProjectionWriter();
        var payloadRules = new AuditPayloadSanitizationRuleRegistry(new IAuditPayloadSanitizationRule[]
        {
            new RecallPayloadSanitizationRule(),
            new CurationPayloadSanitizationRule(),
            new SourceExpansionPayloadSanitizationRule()
        });
        var artifactRules = new AuditDataArtifactSanitizationRuleRegistry(Array.Empty<IAuditDataArtifactSanitizationRule>());
        var sanitizer = new DefaultAuditSanitizer(payloadRules, artifactRules);
        var hasher = new DefaultAuditIntegrityHasher(hashComputer, projectionWriter);

        return new DefaultAuditRecorder(
            validator,
            sanitizer,
            hasher,
            projectionWriter,
            sinks,
            options ?? new AccountabilityOptions(),
            timeProvider);
    }

    public sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    public sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages;

        public bool HasMessage(string code) => _messages.Any(message => message.Contains(code, StringComparison.Ordinal));

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                _messages.Add(formatter(state, exception));
        }
    }

    /// <summary>
    /// A complete producer wired to a real recorder and an in-memory sink,
    /// with a recording logger. Recorder and sink can be overridden for the
    /// timeout / throw / no-sink / custom-sink scenarios.
    /// </summary>
    public sealed class ProducerHarness : IDisposable
    {
        public ProducerHarness(
            AgentMemoryAccountabilityOptions? options = null,
            IAuditRecorder? recorder = null,
            InMemoryAuditSink? sink = null,
            TimeProvider? timeProvider = null)
        {
            HashComputer = CreateHashComputer();
            Projector = new AgentMemoryAccountabilityAuditIdProjector(HashComputer.Object);
            Options = options ?? new AgentMemoryAccountabilityOptions();
            Logger = new RecordingLogger<AgentMemoryAccountabilityProducer>();
            Sink = sink ?? (recorder is null ? new InMemoryAuditSink(timeProvider: timeProvider) : null);
            Recorder = recorder ?? CreateRecorder(
                Sink is not null ? new IAuditSink[] { Sink } : Array.Empty<IAuditSink>(),
                HashComputer.Object,
                timeProvider);
            Producer = new AgentMemoryAccountabilityProducer(Recorder, Options, Projector, Logger);
        }

        public Mock<ICanonicalHashComputer> HashComputer { get; }

        public AgentMemoryAccountabilityAuditIdProjector Projector { get; }

        public AgentMemoryAccountabilityOptions Options { get; }

        public IAuditRecorder Recorder { get; }

        public InMemoryAuditSink? Sink { get; private set; }

        public RecordingLogger<AgentMemoryAccountabilityProducer> Logger { get; }

        public AgentMemoryAccountabilityProducer Producer { get; }

        public IReadOnlyList<AuditEnvelope> Records => Sink?.GetRecords() ?? Array.Empty<AuditEnvelope>();

        public IReadOnlyList<string> Messages => Logger.Messages;

        public bool HasMessage(string code) => Logger.HasMessage(code);

        public void Dispose() => Sink = null;
    }
}
