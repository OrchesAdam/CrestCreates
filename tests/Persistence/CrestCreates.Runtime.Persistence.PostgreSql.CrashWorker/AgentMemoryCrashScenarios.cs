using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Agent.Memory.Promotion;
using CrestCreates.Agent.Memory;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker;

/// <summary>
/// Phase 9b+ Agent Memory crash scenarios. Each scenario composes the real
/// runtime + durable Store, performs the durable curation writes for one crash
/// window, prints the required sentinel, and then waits so the parent test can
/// kill the process while the durable state is committed (or before COMMIT).
/// </summary>
internal static class AgentMemoryCrashScenarios
{
    public static async Task<int> RunAsync(
        PostgreSqlRuntimePersistenceOptions options,
        string scenario,
        string applicationName,
        string operationId)
    {
        using var provider = new ServiceCollection()
            .AddSingleton<ICanonicalHashComputer>(new DeterministicHashComputer())
            .AddAgentMemoryRuntime()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddCrestCreatesPostgreSqlAgentMemoryPersistence()
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IAgentMemoryStore>();
        var conditional = store as IAgentMemoryConditionalCurationStore
            ?? throw new InvalidOperationException("Selected store must be conditional.");
        var promotion = provider.GetRequiredService<DefaultAgentMemoryPromotionService>();

        var candidate = new AgentMemoryCandidate
        {
            TenantId = "crash-tenant",
            CandidateId = "candidate-crash",
            Kind = AgentMemoryKind.Decision,
            Content = "crash content",
            CanonicalContentHash = DeterministicHashComputer.Hash("content"),
            Confidence = AgentMemoryConfidence.Medium
        };
        await store.CreateCandidateAsync(candidate);

        var operation = new AgentMemoryOperationRequest
        {
            TenantId = "crash-tenant",
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = "crash-tenant",
                ActorId = "crash-worker",
                ActorKind = "system",
                CorrelationId = "crash-correlation",
                InvocationSource = "system"
            },
            Reason = "crash scenario",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = operationId,
                OccurredAt = DateTimeOffset.UnixEpoch
            },
            Explanation = "crash scenario explanation"
        };

        switch (scenario)
        {
            case "agent-memory-before-promote-commit":
            case "agent-memory-after-promote-commit":
            {
                var hashes = provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();
                var promoted = new AgentMemoryItem
                {
                    TenantId = "crash-tenant",
                    MemoryId = "memory-crash",
                    Kind = candidate.Kind,
                    Content = candidate.Content,
                    CanonicalContentHash = candidate.CanonicalContentHash,
                    PromotedAt = operation.Identity.OccurredAt,
                    Confidence = candidate.Confidence,
                    Status = AgentMemoryStatus.Active,
                    IsAuthoritative = false,
                    Tags = candidate.Tags,
                    DescriptorRefs = candidate.DescriptorRefs,
                    SourceRefs = candidate.SourceRefs
                };
                var plan = new AgentMemoryPromotionPlan
                {
                    Candidate = new AgentMemoryCandidateExpectation
                    {
                        CandidateId = candidate.CandidateId,
                        ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
                    },
                    NewMemoryId = "memory-crash",
                    ExpectedMemoryContentHash = candidate.CanonicalContentHash,
                    ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(promoted),
                    Operation = operation
                };

                if (scenario.EndsWith("before-promote-commit", StringComparison.Ordinal))
                {
                    // Durable writes complete, then the parent kills the process
                    // before the provider-owned COMMIT acknowledgement.
                    await conditional.PromoteAsync("crash-tenant", plan);
                    Console.WriteLine("AGENT_MEMORY_BEFORE_PROMOTE_COMMIT");
                }
                else
                {
                    await promotion.PromoteAsync("crash-tenant", plan);
                    Console.WriteLine("AGENT_MEMORY_AFTER_PROMOTE_COMMIT");
                }
                break;
            }
            case "agent-memory-before-supersede-commit":
            case "agent-memory-after-supersede-commit":
            {
                var hashes = provider.GetRequiredService<AgentMemoryCanonicalHashProjector>();
                var promoted = new AgentMemoryItem
                {
                    TenantId = "crash-tenant",
                    MemoryId = "memory-crash",
                    Kind = candidate.Kind,
                    Content = candidate.Content,
                    CanonicalContentHash = candidate.CanonicalContentHash,
                    PromotedAt = operation.Identity.OccurredAt,
                    Confidence = candidate.Confidence,
                    Status = AgentMemoryStatus.Active,
                    IsAuthoritative = false
                };
                var plan = new AgentMemoryPromotionPlan
                {
                    Candidate = new AgentMemoryCandidateExpectation
                    {
                        CandidateId = candidate.CandidateId,
                        ExpectedStateHash = hashes.ComputeCandidateStateHash(candidate)
                    },
                    NewMemoryId = "memory-crash",
                    ExpectedMemoryContentHash = candidate.CanonicalContentHash,
                    ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(promoted),
                    Operation = operation
                };
                var original = await promotion.PromoteAsync("crash-tenant", plan);

                var replacement = new AgentMemoryCandidate
                {
                    TenantId = "crash-tenant",
                    CandidateId = "candidate-replacement",
                    Kind = AgentMemoryKind.Decision,
                    Content = "replacement content",
                    CanonicalContentHash = DeterministicHashComputer.Hash("replacement"),
                    Confidence = AgentMemoryConfidence.High
                };
                await store.CreateCandidateAsync(replacement);

                var superseding = new AgentMemoryItem
                {
                    TenantId = "crash-tenant",
                    MemoryId = "memory-replacement",
                    Kind = replacement.Kind,
                    Content = replacement.Content,
                    CanonicalContentHash = replacement.CanonicalContentHash,
                    PromotedAt = operation.Identity.OccurredAt,
                    Confidence = replacement.Confidence,
                    Status = AgentMemoryStatus.Active,
                    IsAuthoritative = false,
                    SupersedesMemoryId = original.MemoryId
                };
                var supersession = new AgentMemorySupersessionPlan
                {
                    TargetMemory = new AgentMemoryItemExpectation
                    {
                        MemoryId = original.MemoryId,
                        ExpectedStateHash = hashes.ComputeMemoryStateHash(original)
                    },
                    ReplacementCandidate = new AgentMemoryCandidateExpectation
                    {
                        CandidateId = replacement.CandidateId,
                        ExpectedStateHash = hashes.ComputeCandidateStateHash(replacement)
                    },
                    NewMemoryId = "memory-replacement",
                    ExpectedMemoryContentHash = replacement.CanonicalContentHash,
                    ExpectedMemoryStateHash = hashes.ComputeMemoryStateHash(superseding),
                    Operation = operation
                };

                if (scenario.EndsWith("before-supersede-commit", StringComparison.Ordinal))
                {
                    await conditional.SupersedeAsync("crash-tenant", supersession);
                    Console.WriteLine("AGENT_MEMORY_BEFORE_SUPERSEDE_COMMIT");
                }
                else
                {
                    await promotion.SupersedeAsync("crash-tenant", supersession);
                    Console.WriteLine("AGENT_MEMORY_AFTER_SUPERSEDE_COMMIT");
                }
                break;
            }
            default:
                return 3;
        }

        // Wait for the parent to kill the process tree.
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    private sealed class DeterministicHashComputer : ICanonicalHashComputer
    {
        public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Hash($"contract-{descriptor.GetType().Name}");

        public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
            => Hash($"definition-{descriptor.GetType().Name}");

        public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
            => Hash(projection.Metadata.ArtifactKind + "-" + projection.Metadata.Purpose);

        public static CanonicalHash Hash(string value)
            => new()
            {
                Value = value,
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "AgentMemoryCrash",
                Scope = "InternalFull",
                Purpose = "Crash",
                ContractVersion = "memory-hash-v1",
                CanonicalShapeVersion = "crash-v1"
            };
    }
}
