using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Sanitization;
using CrestCreates.Agent.Memory.Curation;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;

/// <summary>
/// PostgreSQL implementation of the provider-neutral contract driver. Composes
/// the real base provider, Agent Memory runtime, and explicit durable Agent
/// Memory persistence; rebuilds the provider to prove restart durability.
/// </summary>
public sealed class PostgreSqlAgentMemoryContractDriver : IAgentMemoryDurabilityContractDriver
{
    private readonly PostgreSqlRuntimeSchemaLease _lease;
    private ServiceProvider _provider = null!;
    private IAgentConversationStore _conversationStore = null!;
    private IAgentTaskHistoryStore _taskStore = null!;
    private IAgentCompressedContextStore _contextStore = null!;
    private IAgentMemoryStore _memoryStore = null!;
    private IAgentMemoryContentSanitizer _sanitizer = null!;

    public PostgreSqlAgentMemoryContractDriver(PostgreSqlRuntimeSchemaLease lease)
    {
        _lease = lease;
        RebuildProviderAsync().AsTask().GetAwaiter().GetResult();
    }

    public ServiceProvider Provider => _provider;

    public IAgentConversationStore ConversationStore => _conversationStore;
    public IAgentTaskHistoryStore TaskStore => _taskStore;
    public IAgentCompressedContextStore ContextStore => _contextStore;
    public IAgentMemoryStore MemoryStore => _memoryStore;
    public IAgentMemoryContentSanitizer Sanitizer => _sanitizer;

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        // Each test owns a fresh schema lease, so Reset is a no-op for the
        // shared cases; durability tests call RebuildProviderAsync explicitly.
        return ValueTask.CompletedTask;
    }

    public async ValueTask RebuildProviderAsync(CancellationToken cancellationToken = default)
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _provider = BuildProvider(_lease.Options);
        _conversationStore = _provider.GetRequiredService<IAgentConversationStore>();
        _taskStore = _provider.GetRequiredService<IAgentTaskHistoryStore>();
        _contextStore = _provider.GetRequiredService<IAgentCompressedContextStore>();
        _memoryStore = _provider.GetRequiredService<IAgentMemoryStore>();
        _sanitizer = new RejectingSanitizer(_provider.GetRequiredService<IAgentMemoryContentSanitizer>());
    }

    public ValueTask<IAgentMemoryStoreContractDriver> CreateFreshReaderAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IAgentMemoryStoreContractDriver>(this);

    public ValueTask<AgentMemoryRevisionObservation> ReadRawRevisionAsync(
        AgentMemoryArtifactKind artifactKind,
        string tenantId,
        string artifactId,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Revision observation is a PostgreSQL-only test seam; use the dedicated runner.");

    public AgentMemoryCandidateExpectation PrepareCandidateExpectation(AgentMemoryCandidate candidate)
    {
        var hashes = _provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();
        return new AgentMemoryCandidateExpectation
        {
            CandidateId = candidate.CandidateId,
            ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
        };
    }

    public AgentMemoryItemExpectation PrepareMemoryExpectation(AgentMemoryItem memory)
    {
        var hashes = _provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();
        return new AgentMemoryItemExpectation
        {
            MemoryId = memory.MemoryId,
            ExpectedStateHash = hashes.ComputeMemoryStateHash(memory)
        };
    }

    public AgentMemoryPromotionPlan PreparePromotionPlan(
        AgentMemoryCandidate candidate,
        string newMemoryId,
        AgentMemoryOperationRequest operation)
    {
        var projector = _provider.GetRequiredService<DefaultAgentMemoryCurationProjector>();
        var hashes = _provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();
        var memory = projector.ProjectPromotedMemory(candidate, newMemoryId, operation);
        return new AgentMemoryPromotionPlan
        {
            Candidate = PrepareCandidateExpectation(candidate),
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = candidate.CanonicalContentHash,
            ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(memory),
            Operation = operation
        };
    }

    public AgentMemorySupersessionPlan PrepareSupersessionPlan(
        AgentMemoryItem targetMemory,
        AgentMemoryCandidate replacementCandidate,
        string newMemoryId,
        AgentMemoryOperationRequest operation)
    {
        var projector = _provider.GetRequiredService<DefaultAgentMemoryCurationProjector>();
        var hashes = _provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();
        var superseding = projector.ProjectSupersedingMemory(
            replacementCandidate, targetMemory.MemoryId, newMemoryId, operation);
        return new AgentMemorySupersessionPlan
        {
            TargetMemory = PrepareMemoryExpectation(targetMemory),
            ReplacementCandidate = PrepareCandidateExpectation(replacementCandidate),
            NewMemoryId = newMemoryId,
            ExpectedMemoryContentHash = replacementCandidate.CanonicalContentHash,
            ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(superseding),
            Operation = operation
        };
    }

    public AgentMemoryItem ProjectPromotedMemory(AgentMemoryCandidate candidate, string newMemoryId, AgentMemoryOperationRequest operation)
        => _provider.GetRequiredService<DefaultAgentMemoryCurationProjector>()
            .ProjectPromotedMemory(candidate, newMemoryId, operation);

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    internal static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection()
            .AddSingleton<ICanonicalHashComputer>(new DeterministicHashComputer())
            .AddSingleton<DefaultAgentMemoryContentSanitizer>()
            .AddSingleton<IAgentMemoryContentSanitizer>(
                sp => new RejectingSanitizer(sp.GetRequiredService<DefaultAgentMemoryContentSanitizer>()))
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .BuildServiceProvider();

    private sealed class DeterministicHashComputer : ICanonicalHashComputer
    {
        public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Deterministic(descriptor, scope, "contract");

        public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Deterministic(descriptor, scope, "definition");

        public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
        {
            using var stream = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
                projection.WriteCanonicalJson(writer);
            var digest = System.Security.Cryptography.SHA256.HashData(stream.ToArray());
            return new CanonicalHash
            {
                Value = Convert.ToHexString(digest).ToLowerInvariant(),
                Algorithm = "SHA-256",
                AlgorithmVersion = projection.Metadata.AlgorithmVersion,
                ArtifactKind = projection.Metadata.ArtifactKind,
                Scope = projection.Metadata.Scope,
                Purpose = projection.Metadata.Purpose,
                ContractVersion = projection.Metadata.ContractVersion,
                CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
            };
        }

        private static CanonicalHash Deterministic(IDescriptor descriptor, CanonicalHashScope scope, string kind)
            => new()
            {
                Value = $"{kind}-{descriptor.GetType().Name}-{Guid.NewGuid():N}",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = kind,
                Scope = scope.ToString(),
                Purpose = kind,
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "test-v1"
            };
    }

    /// <summary>Wraps the real sanitizer and rejects the contract sentinel so
    /// shared sanitization cases observe deterministic rejection.</summary>
    private sealed class RejectingSanitizer : IAgentMemoryContentSanitizer
    {
        private readonly IAgentMemoryContentSanitizer _inner;

        public RejectingSanitizer(IAgentMemoryContentSanitizer inner)
        {
            _inner = inner;
        }

        public SanitizedAgentContent Sanitize(string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs)
        {
            if (content.Contains(AgentMemoryPersistenceContractMarkers.RejectedContentSentinel, StringComparison.Ordinal))
            {
                return new SanitizedAgentContent
                {
                    SanitizedContent = string.Empty,
                    CanonicalContentHash = _inner.Sanitize(tenantId, string.Empty, sourceRefs).CanonicalContentHash,
                    Rejected = true,
                    RedactionKinds = [],
                    Diagnostics =
                    [
                        new AgentMemoryDiagnostic
                        {
                            Code = AgentMemoryDiagnosticCodes.ContentRejected,
                            Message = "Content rejected by contract fixture.",
                            Severity = CrestCreates.Core.Abstractions.Identity.SeverityLevel.Warning
                        }
                    ]
                };
            }
            return _inner.Sanitize(tenantId, content, sourceRefs);
        }
    }
}

