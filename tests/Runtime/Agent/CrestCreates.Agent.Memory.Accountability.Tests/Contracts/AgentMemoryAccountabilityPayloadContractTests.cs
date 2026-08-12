using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Json;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.Sanitization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Contracts;

/// <summary>
/// §15.1 — the frozen payload contract. These tests pin the wire kinds, versions,
/// generated JSON roots, and the field matrix that the producer and sanitization
/// rules share. The contracts are NativeAOT-first: all serialization flows through
/// the generated <see cref="AgentMemoryAccountabilityJsonSerializerContext"/>.
/// </summary>
public class AgentMemoryAccountabilityPayloadContractTests
{
    [Fact]
    public void PayloadKindsAndVersions_Should_BeFrozen()
    {
        AgentMemoryAccountabilityPayloadKinds.Recall.Should().Be("agent-memory.recall.result");
        AgentMemoryAccountabilityPayloadKinds.Curation.Should().Be("agent-memory.curation.result");
        AgentMemoryAccountabilityPayloadKinds.SourceExpansion.Should().Be("agent-memory.source-expansion.result");
        AgentMemoryAccountabilityPayloadKinds.Version.Should().Be(1);
        AgentMemoryAccountabilityPayloadKinds.MaxPayloadVersion.Should().Be(1);
    }

    [Fact]
    public void Payloads_Should_RoundTrip_WithGeneratedJsonTypes()
    {
        var context = AgentMemoryAccountabilityJsonSerializerContext.Default;

        var recall = AccountabilityTestFixture.CreateRecallPayload();
        var recallJson = JsonSerializer.Serialize(recall, context.AgentMemoryRecallAccountabilityPayload);
        var recallBack = JsonSerializer.Deserialize(recallJson, context.AgentMemoryRecallAccountabilityPayload);
        recallBack.Should().BeEquivalentTo(recall);

        var curation = AccountabilityTestFixture.CreateCurationPayload();
        var curationJson = JsonSerializer.Serialize(curation, context.AgentMemoryCurationAccountabilityPayload);
        var curationBack = JsonSerializer.Deserialize(curationJson, context.AgentMemoryCurationAccountabilityPayload);
        curationBack.Should().BeEquivalentTo(curation);

        var sourceExpansion = AccountabilityTestFixture.CreateSourceExpansionPayload();
        var sourceJson = JsonSerializer.Serialize(sourceExpansion, context.AgentMemorySourceExpansionAccountabilityPayload);
        var sourceBack = JsonSerializer.Deserialize(sourceJson, context.AgentMemorySourceExpansionAccountabilityPayload);
        sourceBack.Should().BeEquivalentTo(sourceExpansion);
    }

    [Fact]
    public void PayloadContext_Should_ContainExactPublicRoots()
    {
        var context = AgentMemoryAccountabilityJsonSerializerContext.Default;

        AssertMetadataObjectRoot(context.AgentMemoryOperationIdentity, typeof(AgentMemoryOperationIdentity));
        AssertMetadataObjectRoot(context.AgentMemoryAccountabilitySanitizationSummary, typeof(AgentMemoryAccountabilitySanitizationSummary));
        AssertMetadataObjectRoot(context.AgentMemoryRecallAccountabilityPayload, typeof(AgentMemoryRecallAccountabilityPayload));
        AssertMetadataObjectRoot(context.AgentMemoryCurationAccountabilityPayload, typeof(AgentMemoryCurationAccountabilityPayload));
        AssertMetadataObjectRoot(context.AgentMemorySourceExpansionAccountabilityPayload, typeof(AgentMemorySourceExpansionAccountabilityPayload));
        AssertMetadataObjectRoot(context.CanonicalHash, typeof(CanonicalHash));

        // The exact contract exposes no unrelated roots.
        // Note: typeof(object) has a built-in System.Text.Json type info, so the
        // negative sentinel is a local type that the generated context never registers.
        context.GetTypeInfo(typeof(UnregisteredPayloadSentinel)).Should().BeNull();
    }

    private sealed class UnregisteredPayloadSentinel;

    private static void AssertMetadataObjectRoot(JsonTypeInfo? typeInfo, Type expectedType)
    {
        typeInfo.Should().NotBeNull();
        typeInfo!.Type.Should().Be(expectedType);
        typeInfo.Kind.Should().Be(JsonTypeInfoKind.Object);
    }

    [Fact]
    public void CurationOperationFieldMatrix_Should_BeExact()
    {
        // Promote requires the candidate identity.
        AssertRuleFailure(
            () => Sanitize("promote", candidateId: null, memoryId: "memory-1", replacementCandidateId: null),
            "AUDIT_FIELD_REQUIRED",
            "CandidateId");

        // Reject requires the candidate identity.
        AssertRuleFailure(
            () => Sanitize("reject", candidateId: null, memoryId: "memory-1", replacementCandidateId: null),
            "AUDIT_FIELD_REQUIRED",
            "CandidateId");

        // Supersede requires both memory identity and the replacement candidate.
        AssertRuleFailure(
            () => Sanitize("supersede", candidateId: "candidate-1", memoryId: null, replacementCandidateId: null),
            "AUDIT_FIELD_REQUIRED",
            "MemoryId");
        AssertRuleFailure(
            () => Sanitize("supersede", candidateId: "candidate-1", memoryId: "memory-1", replacementCandidateId: null),
            "AUDIT_FIELD_REQUIRED",
            "ReplacementCandidateId");

        // Archive requires the memory identity.
        AssertRuleFailure(
            () => Sanitize("archive", candidateId: "candidate-1", memoryId: null, replacementCandidateId: null),
            "AUDIT_FIELD_REQUIRED",
            "MemoryId");

        // NewMemoryId is always required, even for operations that do not declare it optional.
        AssertRuleFailure(
            () => Sanitize("promote", candidateId: "candidate-1", memoryId: null, replacementCandidateId: null, newMemoryId: null),
            "AUDIT_FIELD_REQUIRED",
            "NewMemoryId");

        // Unknown operations are rejected.
        AssertRuleFailure(
            () => Sanitize("explode", candidateId: "candidate-1", memoryId: null, replacementCandidateId: null),
            "AUDIT_FIELD_INVALID",
            "Operation");
    }

    [Fact]
    public void SanitizationSummary_Should_NotInventPolicyIdentity()
    {
        var summary = new AgentMemoryAccountabilitySanitizationSummary
        {
            State = "redacted",
            RedactionCodes = new[] { "pii-email" },
            DiagnosticCodes = new[] { "diagnostic-1" }
        };

        var json = JsonSerializer.Serialize(summary, AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryAccountabilitySanitizationSummary);
        using var document = JsonDocument.Parse(json);

        var propertyNames = document.RootElement.EnumerateObject().Select(prop => prop.Name).ToArray();
        propertyNames.Should().BeEquivalentTo(new[] { "state", "redactionCodes", "diagnosticCodes" });

        // No policy identity may leak into the sanitization summary.
        var lowered = json.ToLowerInvariant();
        lowered.Should().NotContain("ruleset");
        lowered.Should().NotContain("policyid");
        lowered.Should().NotContain("ruleversion");
    }

    [Fact]
    public void Payloads_Should_RejectUnknownFieldsAndVersions()
    {
        var context = AgentMemoryAccountabilityJsonSerializerContext.Default;

        // Unknown JSON members must be rejected by the generated context.
        const string unknown = """{"operationId":"op-1","result":"completed","bogusField":42}""";
        var act = () => JsonSerializer.Deserialize(unknown, context.AgentMemoryRecallAccountabilityPayload);
        act.Should().Throw<JsonException>();

        // Unknown payload versions must be rejected by the sanitization rules.
        var payload = AccountabilityTestFixture.CreateRecallPayload();
        var auditPayload = AccountabilityTestFixture.CreateAuditPayload(
            payload,
            context.AgentMemoryRecallAccountabilityPayload,
            AgentMemoryAccountabilityPayloadKinds.Recall,
            version: AgentMemoryAccountabilityPayloadKinds.MaxPayloadVersion + 1);

        var rule = new RecallPayloadSanitizationRule();
        var ruleAct = () => rule.Sanitize(auditPayload);
        var ex = ruleAct.Should().Throw<AuditSanitizationException>().Which;
        ex.Code.Should().Be("AUDIT_PAYLOAD_VERSION_UNSUPPORTED");
    }

    [Fact]
    public void Payloads_Should_NotExposeForbiddenRawProperties()
    {
        var forbidden = new[] { "Reason", "Explanation", "GrantId", "Content", "RawContent" };

        AssertPayloadHasNoProperty<AgentMemoryRecallAccountabilityPayload>(forbidden);
        AssertPayloadHasNoProperty<AgentMemoryCurationAccountabilityPayload>(forbidden);
        AssertPayloadHasNoProperty<AgentMemorySourceExpansionAccountabilityPayload>(forbidden);
    }

    private static void AssertRuleFailure(Func<AuditPayload> build, string code, string pathFragment)
    {
        var rule = new CurationPayloadSanitizationRule();
        var act = () => rule.Sanitize(build());
        var ex = act.Should().Throw<AuditSanitizationException>().Which;
        ex.Code.Should().Be(code);
        ex.Path.Should().Contain(pathFragment);
    }

    private static AuditPayload Sanitize(
        string operation,
        string? candidateId,
        string? memoryId,
        string? replacementCandidateId,
        string? newMemoryId = "new-memory-1")
        => AccountabilityTestFixture.CreateAuditPayload(
            AccountabilityTestFixture.CreateCurationPayload(
                operation: operation,
                candidateId: candidateId,
                memoryId: memoryId,
                replacementCandidateId: replacementCandidateId,
                newMemoryId: newMemoryId),
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryCurationAccountabilityPayload,
            AgentMemoryAccountabilityPayloadKinds.Curation);

    private static void AssertPayloadHasNoProperty<T>(IReadOnlyCollection<string> forbidden)
    {
        var properties = typeof(T).GetProperties().Select(prop => prop.Name).ToList();
        foreach (var name in forbidden)
        {
            properties.Should().NotContain(name, $"payload type {typeof(T).Name} must not expose {name}");
        }
    }
}
