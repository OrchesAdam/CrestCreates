using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Context;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.Workflow.Tests;

internal static class WorkflowTestAccountability
{
    public static AuditOperationContextAccessor CreateContexts() => new();

    public static WorkflowLifecycleEventFactory CreateEvents()
        => new(new SequentialIdentity(), new FixedHashBuilder());

    public static WorkflowLifecycleEventPublisher CreatePublisher(
        IEnumerable<IWorkflowLifecycleObserver>? observers = null,
        TimeSpan? timeout = null)
        => new(
            observers ?? [],
            new DefaultWorkflowPostCommitNotificationBudget(new WorkflowPostCommitNotificationOptions
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(5)
            }),
            NullLogger<WorkflowLifecycleEventPublisher>.Instance);

    private sealed class SequentialIdentity : IAuditIdentityGenerator
    {
        private int _sequence;
        public string CreateOperationId() => $"operation-{Interlocked.Increment(ref _sequence)}";
        public string CreateAuditId() => $"audit-{Interlocked.Increment(ref _sequence)}";
    }

    private sealed class FixedHashBuilder : IDescriptorStableHashBuilder
    {
        public DescriptorStableHashes Build(IDescriptor descriptor)
            => new()
            {
                ContractHash = Hash("Contract"),
                DefinitionHash = Hash("Definition")
            };

        private static CanonicalHash Hash(string purpose)
            => new()
            {
                Value = "aabb",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "Descriptor",
                DescriptorKind = "Workflow",
                Scope = "InternalFull",
                Purpose = purpose,
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "descriptor-v1"
            };
    }
}
