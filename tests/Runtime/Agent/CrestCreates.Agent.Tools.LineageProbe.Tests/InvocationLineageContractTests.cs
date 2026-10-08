using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Json;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Validation;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Tests;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.LineageProbe.Tests;

/// <summary>
/// #116 Invocation Lineage Contract — GREEN Acceptance Tests
///
/// These tests verify:
///   1. The real AgentToolInvoker.BuildInvocationLineage produces correct lineage (Blocking 1)
///   2. The real AgentToolInvoker.InvokeAsync propagates lineage through the main chain (Blocking 1)
///   3. Contradictory lineage inputs are rejected at the trusted boundary (Blocking 2)
///   4. Persistence compatibility: JSON round-trip, canonical hash, legacy v1, protected-fact comparison (Blocking 3)
/// </summary>
public sealed class InvocationLineageContractTests
{
    // ──────────────────────────────────────────────────────────────
    // Blocking 1 — Real main chain: BuildInvocationLineage
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void RootInvocation_Should_Express_Explicit_Root_Semantic()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-root",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = true,
            ParentInvocationId = null
        };

        var lineage = AgentToolInvoker.BuildInvocationLineage(execution);

        lineage.Should().NotBeNull("root invocation must express explicit lineage");
        lineage!.Kind.Should().Be(InvocationLineageKind.Root);
        lineage.ParentInvocationId.Should().BeNull("root has no parent");
    }

    [Fact]
    public void ChildInvocation_Should_Preserve_Explicit_Parent_Identity()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-child",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = "invocation-parent"
        };

        var lineage = AgentToolInvoker.BuildInvocationLineage(execution);

        lineage.Should().NotBeNull("child invocation must preserve parent identity");
        lineage!.Kind.Should().Be(InvocationLineageKind.Child);
        lineage.ParentInvocationId.Should().Be("invocation-parent");
    }

    [Fact]
    public void GenericCausation_Should_Remain_Unchanged_WhenParentInvocationIsRecorded()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-child",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            CausationId = "agent-decision-1",
            IsRootInvocation = false,
            ParentInvocationId = "invocation-parent"
        };

        var lineage = AgentToolInvoker.BuildInvocationLineage(execution);

        lineage.Should().NotBeNull();
        lineage!.Kind.Should().Be(InvocationLineageKind.Child);
        lineage.ParentInvocationId.Should().Be("invocation-parent");

        execution.CausationId.Should().Be("agent-decision-1",
            "generic CausationId must not be reinterpreted as parent invocation identity");
    }

    [Fact]
    public void NestedInvocation_Should_Reconstruct_MultiLevel_Lineage()
    {
        var executionA = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-A",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = true
        };

        var executionB = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-B",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = "invocation-A"
        };

        var executionC = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-C",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = "invocation-B"
        };

        var lineageA = AgentToolInvoker.BuildInvocationLineage(executionA);
        var lineageB = AgentToolInvoker.BuildInvocationLineage(executionB);
        var lineageC = AgentToolInvoker.BuildInvocationLineage(executionC);

        lineageA!.Kind.Should().Be(InvocationLineageKind.Root);
        lineageA.ParentInvocationId.Should().BeNull();

        lineageB!.Kind.Should().Be(InvocationLineageKind.Child);
        lineageB.ParentInvocationId.Should().Be("invocation-A");

        lineageC!.Kind.Should().Be(InvocationLineageKind.Child);
        lineageC.ParentInvocationId.Should().Be("invocation-B");
    }

    [Fact]
    public void LegacyInvocation_WithoutLineage_Should_Map_To_Unknown()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-legacy",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = null,
            ParentInvocationId = null
        };

        var lineage = AgentToolInvoker.BuildInvocationLineage(execution);

        lineage.Should().BeNull("legacy invocation without lineage maps to null/Unknown");
    }

    // ──────────────────────────────────────────────────────────────
    // Blocking 1 — Real main chain: E2E through AgentToolInvoker
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task E2E_RootInvocation_Should_PropagateLineageThroughRealInvoker()
    {
        var harness = await InvokeWithLineage(isRoot: true, parentInvocationId: null);

        harness.Context.InvocationLineage.Should().NotBeNull("root lineage must propagate through real invoker");
        harness.Context.InvocationLineage!.Kind.Should().Be(InvocationLineageKind.Root);
        harness.Context.InvocationLineage.ParentInvocationId.Should().BeNull();
    }

    [Fact]
    public async Task E2E_ChildInvocation_Should_PreserveParentIdentityThroughRealInvoker()
    {
        var harness = await InvokeWithLineage(isRoot: false, parentInvocationId: "invocation-parent");

        harness.Context.InvocationLineage.Should().NotBeNull("child lineage must propagate through real invoker");
        harness.Context.InvocationLineage!.Kind.Should().Be(InvocationLineageKind.Child);
        harness.Context.InvocationLineage.ParentInvocationId.Should().Be("invocation-parent");
    }

    [Fact]
    public async Task E2E_ChildInvocation_Should_SeparateCausationFromParentIdentity()
    {
        var harness = await InvokeWithLineage(
            isRoot: false,
            parentInvocationId: "invocation-parent",
            causationId: "agent-decision-1");

        harness.Context.InvocationLineage.Should().NotBeNull();
        harness.Context.InvocationLineage!.Kind.Should().Be(InvocationLineageKind.Child);
        harness.Context.InvocationLineage.ParentInvocationId.Should().Be("invocation-parent");
        harness.Context.CausationId.Should().Be("agent-decision-1",
            "CausationId must remain independent from ParentInvocationId");
    }

    // ──────────────────────────────────────────────────────────────
    // Blocking 2 — Contradictory input must fail-closed
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void RootWithParent_Should_Throw()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = true,
            ParentInvocationId = "invocation-parent"
        };

        var act = () => AgentToolInvoker.BuildInvocationLineage(execution);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*IsRootInvocation*ParentInvocationId*");
    }

    [Fact]
    public void SelfParent_Should_Throw()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = "invocation-1"
        };

        var act = () => AgentToolInvoker.BuildInvocationLineage(execution);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*self-parent*");
    }

    [Fact]
    public void ExplicitNonRootWithoutParent_Should_Throw()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = null
        };

        var act = () => AgentToolInvoker.BuildInvocationLineage(execution);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*IsRootInvocation=false*ParentInvocationId*");
    }

    [Fact]
    public void ExplicitNonRootWithWhitespaceParent_Should_Throw()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = "   "
        };

        var act = () => AgentToolInvoker.BuildInvocationLineage(execution);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NullRootWithWhitespaceParent_Should_Throw()
    {
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = null,
            ParentInvocationId = "   "
        };

        var act = () => AgentToolInvoker.BuildInvocationLineage(execution);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*whitespace*");
    }

    [Fact]
    public void OversizedParent_Should_Throw_AtTrustedBoundary()
    {
        var oversizedParent = new string('x', AuditContractLimits.MaxIdentifierLength + 1);
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = oversizedParent
        };

        var act = () => AgentToolInvoker.BuildInvocationLineage(execution);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{AuditContractLimits.MaxIdentifierLength}*");
    }

    [Fact]
    public void MaxLengthParent_Should_BeAccepted()
    {
        var maxParent = new string('p', AuditContractLimits.MaxIdentifierLength);
        var execution = new AgentExecutionContext
        {
            ExecutionId = "execution-1",
            InvocationId = "invocation-1",
            AgentId = "agent-1",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
            CallOrigin = AgentToolCallOrigin.ExplicitRequest,
            IsRootInvocation = false,
            ParentInvocationId = maxParent
        };

        var lineage = AgentToolInvoker.BuildInvocationLineage(execution);

        lineage.Should().NotBeNull();
        lineage!.Kind.Should().Be(InvocationLineageKind.Child);
        lineage.ParentInvocationId.Should().Be(maxParent);
    }

    // ──────────────────────────────────────────────────────────────
    // Blocking 2 — Validator rejects contradictory AuditEnvelope lineage
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void NullLegacyLineage_And_ExplicitUnknown_Should_Have_One_CanonicalMeaning()
    {
        var nullLineage = (AuditInvocationLineage?)null;
        var explicitUnknown = new AuditInvocationLineage(InvocationLineageKind.Unknown, null);

        nullLineage.Should().BeNull("null is the canonical Unknown representation");

        var validator = new AuditEnvelopeValidator();
        var envelope = CreateMinimalEnvelope(explicitUnknown);

        var result = validator.ValidateCandidate(envelope);

        result.IsValid.Should().BeFalse("explicit Kind.Unknown must be rejected");
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "AUDIT_INVALID_LINEAGE_KIND" &&
            issue.Path != null && issue.Path.Contains("UnknownMustBeNull"));
    }

    [Fact]
    public void Validator_Should_Reject_RootWithParent()
    {
        var lineage = new AuditInvocationLineage(InvocationLineageKind.Root, "invocation-parent");
        var validator = new AuditEnvelopeValidator();
        var envelope = CreateMinimalEnvelope(lineage);

        var result = validator.ValidateCandidate(envelope);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(issue =>
            issue.Code == "AUDIT_INVALID_LINEAGE_COMBINATION" &&
            issue.Path != null && issue.Path.Contains("RootWithParent"));
    }

    [Fact]
    public void Validator_Should_Reject_ChildWithoutParent()
    {
        var lineage = new AuditInvocationLineage(InvocationLineageKind.Child, null);
        var validator = new AuditEnvelopeValidator();
        var envelope = CreateMinimalEnvelope(lineage);

        var result = validator.ValidateCandidate(envelope);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(issue =>
            issue.Code == "AUDIT_INVALID_LINEAGE_COMBINATION" &&
            issue.Path != null && issue.Path.Contains("ChildWithoutParent"));
    }

    [Fact]
    public void Validator_Should_Accept_ValidRootAndChild()
    {
        var validator = new AuditEnvelopeValidator();

        var rootEnvelope = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Root, null));
        var childEnvelope = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Child, "invocation-parent"));

        validator.ValidateCandidate(rootEnvelope).IsValid.Should().BeTrue("valid Root lineage");
        validator.ValidateCandidate(childEnvelope).IsValid.Should().BeTrue("valid Child lineage");
    }

    // ──────────────────────────────────────────────────────────────
    // Blocking 3 — Persistence compatibility
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void JsonRoundTrip_Should_Preserve_RootLineage()
    {
        var envelope = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Root, null));

        var json = JsonSerializer.Serialize(envelope, AccountabilityJsonSerializerContext.Default.AuditEnvelope);
        var restored = JsonSerializer.Deserialize(json, AccountabilityJsonSerializerContext.Default.AuditEnvelope);

        restored.Should().NotBeNull();
        restored!.Runtime!.InvocationLineage.Should().NotBeNull();
        restored.Runtime!.InvocationLineage!.Kind.Should().Be(InvocationLineageKind.Root);
        restored.Runtime.InvocationLineage.ParentInvocationId.Should().BeNull();
    }

    [Fact]
    public void JsonRoundTrip_Should_Preserve_ChildLineage()
    {
        var envelope = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Child, "invocation-parent"));

        var json = JsonSerializer.Serialize(envelope, AccountabilityJsonSerializerContext.Default.AuditEnvelope);
        var restored = JsonSerializer.Deserialize(json, AccountabilityJsonSerializerContext.Default.AuditEnvelope);

        restored.Should().NotBeNull();
        restored!.Runtime!.InvocationLineage.Should().NotBeNull();
        restored.Runtime!.InvocationLineage!.Kind.Should().Be(InvocationLineageKind.Child);
        restored.Runtime.InvocationLineage.ParentInvocationId.Should().Be("invocation-parent");
    }

    [Fact]
    public void LegacyV1_EnvelopeWithoutLineage_Should_Deserialize_AsNullUnknown()
    {
        var envelopeWithoutLineage = CreateMinimalEnvelope(lineage: null);

        var json = JsonSerializer.Serialize(envelopeWithoutLineage, AccountabilityJsonSerializerContext.Default.AuditEnvelope);

        json.Should().NotContain("invocationLineage",
            "v1 envelope without lineage should not include the field (WhenWritingNull)");

        var restored = JsonSerializer.Deserialize(json, AccountabilityJsonSerializerContext.Default.AuditEnvelope);

        restored.Should().NotBeNull();
        restored!.Runtime!.InvocationLineage.Should().BeNull(
            "legacy v1 envelope without lineage decodes as null (canonical Unknown)");
    }

    [Fact]
    public void ParentChange_Should_AlterCanonicalHash()
    {
        var writer = new AccountabilityCanonicalProjectionWriter();

        var envelopeRoot = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Root, null));
        var envelopeChild = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Child, "invocation-parent"));

        var jsonRoot = WriteCanonicalJson(writer, envelopeRoot);
        var jsonChild = WriteCanonicalJson(writer, envelopeChild);

        jsonRoot.Should().NotBe(jsonChild,
            "changing lineage from Root to Child must alter the canonical hash");
    }

    [Fact]
    public void NullLineage_And_ExplicitLineage_Should_ProduceDifferentCanonicalHashes()
    {
        var writer = new AccountabilityCanonicalProjectionWriter();

        var envelopeNull = CreateMinimalEnvelope(lineage: null);
        var envelopeRoot = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Root, null));

        var jsonNull = WriteCanonicalJson(writer, envelopeNull);
        var jsonRoot = WriteCanonicalJson(writer, envelopeRoot);

        jsonNull.Should().NotBe(jsonRoot,
            "null lineage and explicit Root must produce different canonical hashes");
    }

    [Fact]
    public void ProtectedFactComparer_Should_DetectLineageDifference()
    {
        var envelopeRoot = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Root, null));
        var envelopeChild = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Child, "invocation-parent"));
        var envelopeRootDuplicate = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Root, null));

        AuditProtectedFactComparer.AreEqual(envelopeRoot, envelopeRootDuplicate)
            .Should().BeTrue("identical lineage must be equal");
        AuditProtectedFactComparer.AreEqual(envelopeRoot, envelopeChild)
            .Should().BeFalse("different lineage must not be equal");
    }

    [Fact]
    public void ProtectedFactComparer_Should_TreatNullAndExplicitLineage_AsDifferent()
    {
        var envelopeNull = CreateMinimalEnvelope(lineage: null);
        var envelopeRoot = CreateMinimalEnvelope(
            new AuditInvocationLineage(InvocationLineageKind.Root, null));

        AuditProtectedFactComparer.AreEqual(envelopeNull, envelopeRoot)
            .Should().BeFalse("null lineage and explicit Root must differ in protected-fact comparison");
    }

    [Fact]
    public void FrozenHistoricalV1_FixtureJson_Should_DeserializeCorrectly()
    {
        var frozenV1Json = """
        {
          "contractVersion": 1,
          "auditId": "frozen-audit-1",
          "occurredAt": "2026-07-15T12:00:00+00:00",
          "correlationId": "frozen-correlation-1",
          "actor": { "kind": "user", "id": "user-1" },
          "action": { "kind": "http.request", "name": "GET /items" },
          "target": { "kind": "http-route", "id": "GET /items" },
          "outcome": { "status": "succeeded" },
          "runtime": {
            "invocationSource": "internal",
            "executionId": "frozen-exec-1",
            "duration": "00:00:01.0000000",
            "references": []
          },
          "descriptors": { "items": [] },
          "evidence": [],
          "tags": {}
        }
        """;

        var restored = JsonSerializer.Deserialize(frozenV1Json, AccountabilityJsonSerializerContext.Default.AuditEnvelope);

        restored.Should().NotBeNull("frozen v1 fixture must deserialize");
        restored!.AuditId.Should().Be("frozen-audit-1");
        restored.Runtime.Should().NotBeNull();
        restored.Runtime!.InvocationLineage.Should().BeNull(
            "frozen v1 fixture has no lineage field — must decode as null (canonical Unknown)");
        restored.Runtime.ExecutionId.Should().Be("frozen-exec-1");
    }

    [Fact]
    public void FrozenHistoricalV1_FixtureCanonicalHash_Should_BeReadable()
    {
        var frozenV1Json = """
        {
          "contractVersion": 1,
          "auditId": "frozen-audit-1",
          "occurredAt": "2026-07-15T12:00:00+00:00",
          "correlationId": "frozen-correlation-1",
          "actor": { "kind": "user", "id": "user-1" },
          "action": { "kind": "http.request", "name": "GET /items" },
          "target": { "kind": "http-route", "id": "GET /items" },
          "outcome": { "status": "succeeded" },
          "runtime": {
            "invocationSource": "internal",
            "executionId": "frozen-exec-1",
            "duration": "00:00:01.0000000",
            "references": []
          },
          "descriptors": { "items": [] },
          "evidence": [],
          "tags": {}
        }
        """;

        var restored = JsonSerializer.Deserialize(frozenV1Json, AccountabilityJsonSerializerContext.Default.AuditEnvelope);
        restored.Should().NotBeNull();

        var writer = new AccountabilityCanonicalProjectionWriter();
        var projection = writer.CreateProjection(restored!);

        projection.Metadata.CanonicalShapeVersion.Should().Be("accountability-record-hash-v2");
        projection.Metadata.AlgorithmVersion.Should().Be("sha256-canonical-json-v1");

        var canonicalJson = WriteCanonicalJson(writer, restored!);
        canonicalJson.Should().Contain("invocationLineage",
            "canonical projection must include lineage field (as null) even for v1 fixtures");
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static AuditEnvelope CreateMinimalEnvelope(AuditInvocationLineage? lineage)
        => new()
        {
            ContractVersion = 1,
            AuditId = "audit-1",
            OccurredAt = DateTimeOffset.Parse("2026-07-29T00:00:00Z", CultureInfo.InvariantCulture),
            CorrelationId = "correlation-1",
            Actor = new AuditActor { Kind = AuditActorKinds.User, Id = "user-1" },
            Action = new AuditAction { Kind = AuditActionKinds.HttpRequest, Name = "test" },
            Target = new AuditTarget { Kind = "test", Id = "test-1" },
            Outcome = new AuditOutcome { Status = AuditOutcomeStatuses.Succeeded },
            Runtime = new AuditRuntimeContext
            {
                InvocationSource = "test",
                ExecutionId = "execution-1",
                References = [],
                InvocationLineage = lineage
            },
            Descriptors = new AuditDescriptorContext { Items = [] },
            Evidence = [],
            Tags = AuditTagMap.Empty
        };

    private static string WriteCanonicalJson(AccountabilityCanonicalProjectionWriter writer, AuditEnvelope envelope)
    {
        using var stream = new MemoryStream();
        using var jsonWriter = new Utf8JsonWriter(stream);
        writer.Write(envelope, jsonWriter);
        jsonWriter.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<LineageE2EHarness> InvokeWithLineage(
        bool? isRoot,
        string? parentInvocationId,
        string? causationId = null)
    {
        var capability = AgentToolRuntimeTestFixture.Capability(
            $"lineage-cap-{Guid.NewGuid():N}");
        var tool = AgentToolRuntimeTestFixture.Tool(
            $"lineage-tool-{Guid.NewGuid():N}",
            capability.Id,
            $"lineage.tool.{Guid.NewGuid():N}");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);

        var snapshot = AgentToolRuntimeTestFixture.SnapshotBuilder(
            AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
            AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
            AgentToolRuntimeTestFixture.BuildSchemaRegistry())
            .Build();
        var snapshots = new AgentToolRuntimeSnapshotProvider();
        snapshots.Publish(snapshot);

        var execution = new MutableExecutionContextAccessor
        {
            CurrentValue = new AgentExecutionContext
            {
                ExecutionId = "execution-1",
                InvocationId = "invocation-1",
                AgentId = "agent-1",
                AgentRoles = new HashSet<string>(StringComparer.Ordinal) { "operator" },
                CallOrigin = AgentToolCallOrigin.ExplicitRequest,
                IsRootInvocation = isRoot,
                ParentInvocationId = parentInvocationId,
                CausationId = causationId
            }
        };

        var dispatcher = new LineageRecordingDispatcher();
        var gate = new DevelopmentInMemoryAgentToolInvocationGate();
        var budget = new LineageRecordingBudgetGate();
        var auditor = new DevelopmentInMemoryAgentToolGovernanceAuditor();

        var invoker = new AgentToolInvoker(
            snapshots,
            execution,
            new LineageTestCurrentUser(),
            new LineageTestTenantContext(),
            gate,
            gate,
            new FailClosedAgentToolApprovalGate(),
            budget,
            auditor,
            dispatcher,
            new SchemaValidator(),
            new AgentToolInvocationFingerprintBuilder(),
            new AgentCapabilityIdempotencyKeyBuilder(),
            new AgentToolResultMapper());

        var result = await invoker.InvokeAsync(new AgentToolInvocationRequest(tool.ToolName));
        result.Kind.Should().Be(AgentToolInvocationOutcomeKind.Succeeded);

        return new LineageE2EHarness(dispatcher.CapturedContext!);
    }

    private sealed record LineageE2EHarness(CapabilityExecutionContext Context);

    private sealed class MutableExecutionContextAccessor : IAgentExecutionContextAccessor
    {
        public AgentExecutionContext? CurrentValue { get; set; }
        public AgentExecutionContext? Current => CurrentValue;
    }

    private sealed class LineageTestCurrentUser : ICurrentUser
    {
        public string Id => "user-1";
        public string UserName => "user";
        public bool IsAuthenticated => true;
        public string TenantId => "tenant-1";
        public string[] Roles => [];
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }

    private sealed class LineageTestTenantContext : ITenantContext
    {
        public string? CurrentTenantId => "tenant-1";
    }

    private sealed class LineageRecordingDispatcher : ICapabilityDispatcher
    {
        public CapabilityExecutionContext? CapturedContext { get; private set; }

        public Task<CapabilityExecutionResult> DispatchAsync(
            CapabilityDescriptor descriptor,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
        {
            var context = new CapabilityExecutionContext
            {
                ServiceProvider = LineageEmptyServiceProvider.Instance,
                UserId = "user-1"
            };
            configureContext?.Invoke(context);
            CapturedContext = context;
            return Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        }

        public Task<CapabilityExecutionResult> DispatchAsync(
            string capabilityId,
            InvocationSource source,
            object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class LineageEmptyServiceProvider : IServiceProvider
    {
        public static LineageEmptyServiceProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class LineageRecordingBudgetGate : IAgentToolBudgetGate
    {
        private AgentToolBudgetReservation? _reservation;

        public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
            AgentToolBudgetReserveRequest request,
            CancellationToken cancellationToken = default)
        {
            _reservation = new AgentToolBudgetReservation
            {
                ReservationId = "reservation-1",
                AttemptId = request.Context.AttemptId,
                InvocationFingerprint = request.Context.InvocationFingerprint,
                Category = request.Context.Governance.Budget.Category,
                CostUnits = request.Context.Governance.Budget.CostUnits,
                MaxCallsPerExecution = request.Context.Governance.Budget.MaxCallsPerExecution,
                State = AgentToolBudgetReservationState.Reserved
            };
            return ValueTask.FromResult(new AgentToolBudgetReserveResult
            {
                Status = AgentToolBudgetReserveStatus.Reserved,
                Reservation = _reservation
            });
        }

        public ValueTask<AgentToolBudgetReservation> FinalizeAsync(
            AgentToolBudgetFinalizeRequest request,
            CancellationToken cancellationToken = default)
        {
            _reservation = _reservation! with { State = request.RequestedState };
            return ValueTask.FromResult(_reservation);
        }

        public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(
            AgentToolPreDispatchIdentity identity,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolBudgetReservationReadResult
            {
                Status = AgentToolBudgetReadStatus.Reserved
            });
    }
}
