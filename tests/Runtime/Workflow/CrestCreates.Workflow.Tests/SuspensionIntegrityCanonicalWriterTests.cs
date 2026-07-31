using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class SuspensionIntegrityCanonicalWriterTests
{
    [Fact]
    public void ShouldDistinguishDelimiterBearingFields()
    {
        var scope = new RuntimeTenantScope("tenant-a");
        var operationA = "op-1";
        var operationB = "op\n1";

        var before = NewWorkflow("tenant-a", "wf-1");
        var after = NewWorkflow("tenant-a", "wf-1", status: WorkflowInstanceStatus.Suspended);
        var task = NewTask("tenant-a", "task-1", before.Key);

        var hashA = ComputeIntegrity(scope, operationA, before, after, task);
        var hashB = ComputeIntegrity(scope, operationB, before, after, task);

        hashA.Should().NotBe(hashB,
            "different operation IDs must produce different hashes even when one contains a newline");
    }

    [Fact]
    public void ShouldDistinguishDifferentStructuredPins()
    {
        var scope = new RuntimeTenantScope("tenant-a");
        var before = NewWorkflow("tenant-a", "wf-1", Pin("ns-x", "id-x", 1));
        var task = NewTask("tenant-a", "task-1", before.Key);

        var afterWithPin1 = NewWorkflow("tenant-a", "wf-1", Pin("ns-a", "id-a", 1), WorkflowInstanceStatus.Suspended);
        var afterWithPin2 = NewWorkflow("tenant-a", "wf-1", Pin("ns-a", "id-b", 1), WorkflowInstanceStatus.Suspended);

        var hash1 = ComputeIntegrity(scope, "op-1", before, afterWithPin1, task);
        var hash2 = ComputeIntegrity(scope, "op-1", before, afterWithPin2, task);

        hash1.Should().NotBe(hash2,
            "different descriptor pin refs must produce different hashes");
    }

    [Fact]
    public void ShouldBeStableAcrossDictionaryInsertionOrder()
    {
        var scope = new RuntimeTenantScope("tenant-a");
        var before = NewWorkflow("tenant-a", "wf-1", Pin("workflow", "approval", 1));
        var task = NewTask("tenant-a", "task-1", before.Key);

        var vars1 = new Dictionary<string, RuntimeStateValue>
        {
            ["zebra"] = StateValue("type-z", "payload-z"),
            ["alpha"] = StateValue("type-a", "payload-a"),
            ["mango"] = StateValue("type-m", "payload-m"),
        };
        var vars2 = new Dictionary<string, RuntimeStateValue>
        {
            ["mango"] = StateValue("type-m", "payload-m"),
            ["zebra"] = StateValue("type-z", "payload-z"),
            ["alpha"] = StateValue("type-a", "payload-a"),
        };

        var after1 = NewWorkflow("tenant-a", "wf-1", Pin("workflow", "approval", 1), WorkflowInstanceStatus.Suspended, vars1);
        var after2 = NewWorkflow("tenant-a", "wf-1", Pin("workflow", "approval", 1), WorkflowInstanceStatus.Suspended, vars2);

        var hash1 = ComputeIntegrity(scope, "op-1", before, after1, task);
        var hash2 = ComputeIntegrity(scope, "op-1", before, after2, task);

        hash1.Should().Be(hash2,
            "identical state in different insertion order must produce the same hash");
    }

    [Fact]
    public void ShouldIncludeCompleteHashMetadata()
    {
        var scope = new RuntimeTenantScope("tenant-a");
        var before = NewWorkflow("tenant-a", "wf-1", Pin("workflow", "approval", 1));
        var after = NewWorkflow("tenant-a", "wf-1", Pin("workflow", "approval", 1), WorkflowInstanceStatus.Suspended);
        var task = NewTask("tenant-a", "task-1", before.Key);

        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = "RuntimeSuspension",
                Purpose = "Integrity",
                Scope = "InternalFull",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "runtime-suspension-v1"
            },
            writer => SuspensionIntegrityCanonicalWriter.Write(writer, scope, "op-1", before, after, task));

        var hash = new TestCanonicalHashComputer().ComputeFromProjection(projection);

        hash.Algorithm.Should().Be("SHA-256");
        hash.ArtifactKind.Should().Be("RuntimeSuspension");
        hash.Purpose.Should().Be("Integrity");
        hash.Scope.Should().Be("InternalFull");
        hash.ContractVersion.Should().Be("canonical-hash-v1");
        hash.CanonicalShapeVersion.Should().Be("runtime-suspension-v1");
        hash.Value.Should().NotBeNullOrEmpty();
    }

    private static string ComputeIntegrity(
        RuntimeTenantScope scope,
        string operationId,
        WorkflowInstance before,
        WorkflowInstance after,
        HumanTaskInstance task)
    {
        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = "RuntimeSuspension",
                Purpose = "Integrity",
                Scope = "InternalFull",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "runtime-suspension-v1"
            },
            writer => SuspensionIntegrityCanonicalWriter.Write(writer, scope, operationId, before, after, task));

        return new TestCanonicalHashComputer().ComputeFromProjection(projection).Value;
    }

    private static WorkflowInstance NewWorkflow(string? tenantId, string id, RuntimeDescriptorPin? pin = null,
        WorkflowInstanceStatus status = WorkflowInstanceStatus.Running,
        Dictionary<string, RuntimeStateValue>? variables = null) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowPin = pin ?? Pin("workflow", "approval", 1),
        Status = status,
        Variables = variables ?? new Dictionary<string, RuntimeStateValue>(),
    };

    private static HumanTaskInstance NewTask(string? tenantId, string id, RuntimeInstanceKey workflowKey) => new()
    {
        Key = new RuntimeInstanceKey(tenantId, id),
        WorkflowKey = workflowKey,
        WorkflowStepId = "review",
        HumanTaskPin = Pin("humantask", "review", 1),
        Input = StateValue("human-input", "payload"),
    };

    private static RuntimeDescriptorPin Pin(string ns, string id, int version) => new()
    {
        Ref = new DescriptorRef(ns, id, version),
        ContractHash = Hash("contract", "Contract", "Test"),
        DefinitionHash = Hash("definition", "Definition", "Test"),
    };

    private static RuntimeStateValue StateValue(string typeId, string payload) => new()
    {
        TypeId = typeId,
        JsonPayload = payload,
    };

    private static CanonicalHash Hash(string value, string purpose, string kind) => new()
    {
        Value = value,
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        DescriptorKind = kind,
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "test-v1"
    };

    private sealed class TestCanonicalHashComputer : ICanonicalHashComputer
    {
        public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
            => throw new NotSupportedException();
        public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
            => throw new NotSupportedException();
        public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
        {
            var bufferWriter = new ArrayBufferWriter<byte>(4096);
            using var jsonWriter = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = true
            });
            projection.WriteCanonicalJson(jsonWriter);
            jsonWriter.Flush();

            return new CanonicalHash
            {
                Value = Convert.ToHexString(SHA256.HashData(bufferWriter.WrittenSpan)).ToLowerInvariant(),
                Algorithm = "SHA-256",
                AlgorithmVersion = projection.Metadata.AlgorithmVersion,
                ArtifactKind = projection.Metadata.ArtifactKind,
                Scope = projection.Metadata.Scope,
                Purpose = projection.Metadata.Purpose,
                ContractVersion = projection.Metadata.ContractVersion,
                CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
            };
        }
    }
}
