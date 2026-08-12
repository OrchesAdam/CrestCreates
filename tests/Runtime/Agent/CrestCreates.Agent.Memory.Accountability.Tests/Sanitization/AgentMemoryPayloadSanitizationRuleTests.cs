using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Json;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Agent.Memory.Accountability.Sanitization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Sanitization;

/// <summary>
/// §11 — the three frozen payload sanitization rules validate the exact field
/// matrices, hard bounds, protected semantic fields, and generated-JSON-only
/// parsing. A rejected rule never rewrites the payload and never falls back to
/// candidate JSON.
/// </summary>
public sealed class AgentMemoryPayloadSanitizationRuleTests
{
    private static readonly JsonTypeInfoProvider Infos = new();

    private sealed class JsonTypeInfoProvider
    {
        public System.Text.Json.Serialization.Metadata.JsonTypeInfo<AgentMemoryRecallAccountabilityPayload> Recall
            => AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryRecallAccountabilityPayload;

        public System.Text.Json.Serialization.Metadata.JsonTypeInfo<AgentMemoryCurationAccountabilityPayload> Curation
            => AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryCurationAccountabilityPayload;

        public System.Text.Json.Serialization.Metadata.JsonTypeInfo<AgentMemorySourceExpansionAccountabilityPayload> SourceExpansion
            => AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemorySourceExpansionAccountabilityPayload;
    }

    // ------------------------------------------------------------------
    // Recall rule: field matrix
    // ------------------------------------------------------------------

    [Fact]
    public void RecallCompleted_Should_PassAndPreserveProtectedFields()
    {
        var rule = new RecallPayloadSanitizationRule();
        var input = AccountabilityTestFixture.CreateAuditPayload(
            AccountabilityTestFixture.CreateRecallPayload(result: "completed"),
            Infos.Recall,
            AgentMemoryAccountabilityPayloadKinds.Recall);

        var result = rule.Sanitize(input);

        result.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Recall);
        result.Version.Should().Be(AgentMemoryAccountabilityPayloadKinds.Version);
        result.Data.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void Recall_Should_RejectDuplicateJsonProperties()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = AccountabilityTestFixture.CreateRecallPayload(result: "completed");
        var serialized = JsonSerializer.Serialize(payload, Infos.Recall);
        var duplicateJson = serialized[..^1] + ",\"operationId\":\"second-operation\"}";
        using var document = JsonDocument.Parse(duplicateJson);
        var input = new AuditPayload
        {
            Kind = AgentMemoryAccountabilityPayloadKinds.Recall,
            Version = AgentMemoryAccountabilityPayloadKinds.Version,
            Data = document.RootElement.Clone()
        };

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_PAYLOAD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Data");
    }

    [Fact]
    public void RecallRejected_Should_PassWithStableFailureCode()
    {
        var rule = new RecallPayloadSanitizationRule();
        var input = AccountabilityTestFixture.CreateAuditPayload(
            AccountabilityTestFixture.CreateRecallPayload(result: "rejected"),
            Infos.Recall,
            AgentMemoryAccountabilityPayloadKinds.Recall);

        var result = rule.Sanitize(input);

        result.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Recall);
    }

    [Fact]
    public void RecallRejected_Should_RequireStableFailureCode()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "rejected",
            StableFailureCode = null,
            EffectivePackHash = null,
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.StableFailureCode");
    }

    [Fact]
    public void RecallRejected_Should_ForbidEffectivePackHash()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "rejected",
            StableFailureCode = "resource-unavailable",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.EffectivePackHash");
    }

    [Fact]
    public void RecallCompleted_Should_RejectInvalidHashMetadata()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = new CanonicalHash
            {
                Value = "aabb",
                Algorithm = "   ",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "AccountabilityRecord",
                Scope = "InternalFull",
                Purpose = "AuditEvidence",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "accountability-record-hash-v1"
            },
            ReturnedCount = 2,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_HASH_METADATA_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.EffectivePackHash");
    }

    [Fact]
    public void Recall_Should_RejectUnknownResult()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "maybe",
            EffectivePackHash = null,
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Result");
    }

    [Fact]
    public void Recall_Should_RejectNegativeReturnedCount()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = -1,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.ReturnedCount");
    }

    [Fact]
    public void Recall_Should_RejectNegativeMaximumCount()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = -1,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.MaximumCount");
    }

    [Fact]
    public void Recall_Should_RejectNegativeCharacterBudget()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = -1,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.CharacterBudget");
    }

    [Fact]
    public void Recall_Should_RequireMinimumConfidence()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "   "
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.MinimumConfidence");
    }

    [Fact]
    public void Recall_Should_RequireOperationId()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = "   ",
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.OperationId");
    }

    // ------------------------------------------------------------------
    // Recall rule: hard bounds
    // ------------------------------------------------------------------

    [Fact]
    public void Recall_Should_RejectTooLongIdentifier()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = new string('x', AgentMemoryAccountabilityPayloadKinds.MaxIdentifierLength + 1),
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_IDENTIFIER_TOO_LONG");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.OperationId");
    }

    [Fact]
    public void Recall_Should_RejectTooManyDiagnosticCodes()
    {
        var rule = new RecallPayloadSanitizationRule();
        var codes = Enumerable.Range(0, AgentMemoryAccountabilityPayloadKinds.MaxDiagnosticCodes + 1)
            .Select(i => $"code{i:00}")
            .ToArray();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            DiagnosticCodes = codes,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_CODE_LIMIT_EXCEEDED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.DiagnosticCodes");
    }

    [Fact]
    public void Recall_Should_RejectUnsortedDiagnosticCodes()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            DiagnosticCodes = new[] { "b-code", "a-code" },
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_CODES_NOT_SORTED_OR_DUPLICATE");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.DiagnosticCodes");
    }

    [Fact]
    public void Recall_Should_RejectDuplicateDiagnosticCodes()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            DiagnosticCodes = new[] { "a-code", "a-code" },
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_CODES_NOT_SORTED_OR_DUPLICATE");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.DiagnosticCodes");
    }

    [Fact]
    public void Recall_Should_RejectTooLongCode()
    {
        var rule = new RecallPayloadSanitizationRule();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            DiagnosticCodes = new[] { new string('c', AgentMemoryAccountabilityPayloadKinds.MaxCodeLength + 1) },
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_CODE_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.DiagnosticCodes[0]");
    }

    [Fact]
    public void Recall_Should_RejectTooManyRequestedKinds()
    {
        var rule = new RecallPayloadSanitizationRule();
        var kinds = Enumerable.Range(0, AgentMemoryAccountabilityPayloadKinds.MaxRequestedKinds + 1)
            .Select(i => $"kind-{i}")
            .ToArray();
        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Result = "completed",
            EffectivePackHash = AccountabilityTestFixture.CreateEffectivePackHash(),
            ReturnedCount = 0,
            WasTruncated = false,
            RequestedKinds = kinds,
            MaximumCount = 10,
            CharacterBudget = 2000,
            MinimumConfidence = "0.5"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Recall, AgentMemoryAccountabilityPayloadKinds.Recall);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_REQUESTED_KINDS_LIMIT_EXCEEDED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.RequestedKinds");
    }

    // ------------------------------------------------------------------
    // Curation rule: field matrix
    // ------------------------------------------------------------------

    [Fact]
    public void CurationPromoteCommitted_Should_PassAndPreserveProtectedFields()
    {
        var rule = new CurationPayloadSanitizationRule();
        var input = AccountabilityTestFixture.CreateAuditPayload(
            AccountabilityTestFixture.CreateCurationPayload(operation: "promote", result: "committed"),
            Infos.Curation,
            AgentMemoryAccountabilityPayloadKinds.Curation);

        var result = rule.Sanitize(input);

        result.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Curation);
        result.Version.Should().Be(AgentMemoryAccountabilityPayloadKinds.Version);
    }

    [Fact]
    public void CurationRejectRejected_Should_PassWithStableFailureCode()
    {
        var rule = new CurationPayloadSanitizationRule();
        var input = AccountabilityTestFixture.CreateAuditPayload(
            AccountabilityTestFixture.CreateCurationPayload(operation: "reject", result: "rejected"),
            Infos.Curation,
            AgentMemoryAccountabilityPayloadKinds.Curation);

        var result = rule.Sanitize(input);

        result.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Curation);
    }

    [Fact]
    public void CurationSupersede_Should_RequireOldAndNewIdentity()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "supersede",
            MemoryId = null,
            ReplacementCandidateId = null,
            NewMemoryId = "new-memory-1",
            Result = "committed",
            ResultingState = "superseded"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.MemoryId");
    }

    [Fact]
    public void CurationArchive_Should_RequireMemoryId()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "archive",
            MemoryId = null,
            NewMemoryId = "memory-1",
            Result = "committed",
            ResultingState = "archived"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.MemoryId");
    }

    [Fact]
    public void CurationArchiveCommitted_FromActive_ShouldPass()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = AccountabilityTestFixture.CreateCurationPayload(
            operation: "archive", result: "committed", memoryId: "memory-active");
        var input = AccountabilityTestFixture.CreateAuditPayload(
            payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var result = rule.Sanitize(input);

        result.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Curation);
    }

    [Fact]
    public void CurationArchiveCommitted_FromSuperseded_ShouldPass()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = AccountabilityTestFixture.CreateCurationPayload(
            operation: "archive", result: "committed", memoryId: "memory-superseded") with
        {
            PreviousState = "superseded"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(
            payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var result = rule.Sanitize(input);

        result.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Curation);
    }

    [Fact]
    public void Curation_Should_AlwaysValidateNewMemoryId()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "promote",
            CandidateId = "candidate-1",
            NewMemoryId = "   ",
            Result = "committed",
            PreviousState = "candidate",
            ResultingState = "promoted"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.NewMemoryId");
    }

    [Fact]
    public void CurationCommitted_Should_RequireResultingState()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "promote",
            CandidateId = "candidate-1",
            NewMemoryId = "new-memory-1",
            Result = "committed",
            ResultingState = null
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.ResultingState");
    }

    [Fact]
    public void CurationRejected_Should_RequireStableFailureCode()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "reject",
            CandidateId = "candidate-1",
            NewMemoryId = null,
            Result = "rejected",
            StableFailureCode = null
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.StableFailureCode");
    }

    [Fact]
    public void Curation_Should_RejectUnknownOperation()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "demote",
            CandidateId = "candidate-1",
            NewMemoryId = null,
            Result = "committed",
            ResultingState = "promoted"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Operation");
    }

    [Fact]
    public void Curation_Should_RejectUnknownResult()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "promote",
            CandidateId = "candidate-1",
            NewMemoryId = null,
            Result = "maybe"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Result");
    }

    [Fact]
    public void Curation_Should_RejectInvalidExpectedContentHash()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "promote",
            CandidateId = "candidate-1",
            NewMemoryId = "memory-1",
            ExpectedContentHash = new CanonicalHash
            {
                Value = "aabb",
                Algorithm = "SHA-256",
                AlgorithmVersion = "   ",
                ArtifactKind = "AccountabilityRecord",
                Scope = "InternalFull",
                Purpose = "AuditEvidence",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "accountability-record-hash-v1"
            },
            Result = "committed",
            PreviousState = "candidate",
            ResultingState = "promoted"
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_HASH_METADATA_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.ExpectedContentHash");
    }

    [Fact]
    public void Curation_Should_RejectInvalidSanitizationState()
    {
        var rule = new CurationPayloadSanitizationRule();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "promote",
            CandidateId = "candidate-1",
            NewMemoryId = "memory-1",
            Result = "committed",
            PreviousState = "candidate",
            ResultingState = "active",
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "mystery",
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            }
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Sanitization.State");
    }

    [Fact]
    public void Curation_Should_RejectTooManyRedactionCodes()
    {
        var rule = new CurationPayloadSanitizationRule();
        var codes = Enumerable.Range(0, AgentMemoryAccountabilityPayloadKinds.MaxRedactionCodes + 1)
            .Select(i => $"code{i:00}")
            .ToArray();
        var payload = new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            Operation = "promote",
            CandidateId = "candidate-1",
            NewMemoryId = "memory-1",
            Result = "committed",
            PreviousState = "candidate",
            ResultingState = "active",
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "redacted",
                RedactionCodes = codes,
                DiagnosticCodes = Array.Empty<string>()
            }
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.Curation, AgentMemoryAccountabilityPayloadKinds.Curation);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_REDACTION_LIMIT_EXCEEDED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Sanitization.RedactionCodes");
    }

    // ------------------------------------------------------------------
    // Source Expansion rule: field matrix
    // ------------------------------------------------------------------

    [Fact]
    public void SourceExpansionExpanded_Should_PassAndPreserveProtectedFields()
    {
        var rule = new SourceExpansionPayloadSanitizationRule();
        var input = AccountabilityTestFixture.CreateAuditPayload(
            AccountabilityTestFixture.CreateSourceExpansionPayload(status: "expanded"),
            Infos.SourceExpansion,
            AgentMemoryAccountabilityPayloadKinds.SourceExpansion);

        var result = rule.Sanitize(input);

        result.Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.SourceExpansion);
        result.Version.Should().Be(AgentMemoryAccountabilityPayloadKinds.Version);
    }

    [Fact]
    public void SourceExpansionRedacted_Should_ForbidEffectiveVisibleContentHash()
    {
        var rule = new SourceExpansionPayloadSanitizationRule();
        var payload = new AgentMemorySourceExpansionAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            SourceKind = "ConversationTurn",
            SourceId = "source-1",
            Status = "redacted",
            EffectiveVisibleContentHash = AccountabilityTestFixture.CreateEffectiveContentHash(),
            MaximumCharacters = 4000,
            WasTruncated = false,
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "redacted",
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            }
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.SourceExpansion, AgentMemoryAccountabilityPayloadKinds.SourceExpansion);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.EffectiveVisibleContentHash");
    }

    [Fact]
    public void SourceExpansion_Should_RejectUnknownStatus()
    {
        var rule = new SourceExpansionPayloadSanitizationRule();
        var payload = new AgentMemorySourceExpansionAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            SourceKind = "ConversationTurn",
            SourceId = "source-1",
            Status = "halfway",
            MaximumCharacters = 4000,
            WasTruncated = false,
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "none",
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            }
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.SourceExpansion, AgentMemoryAccountabilityPayloadKinds.SourceExpansion);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Status");
    }

    [Fact]
    public void SourceExpansion_Should_RequireSourceKind()
    {
        var rule = new SourceExpansionPayloadSanitizationRule();
        var payload = new AgentMemorySourceExpansionAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            SourceKind = "   ",
            SourceId = "source-1",
            Status = "not-found",
            MaximumCharacters = 4000,
            WasTruncated = false,
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "none",
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            }
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.SourceExpansion, AgentMemoryAccountabilityPayloadKinds.SourceExpansion);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.SourceKind");
    }

    [Fact]
    public void SourceExpansion_Should_RequireSourceId()
    {
        var rule = new SourceExpansionPayloadSanitizationRule();
        var payload = new AgentMemorySourceExpansionAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            SourceKind = "ConversationTurn",
            SourceId = "   ",
            Status = "not-found",
            MaximumCharacters = 4000,
            WasTruncated = false,
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "none",
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            }
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.SourceExpansion, AgentMemoryAccountabilityPayloadKinds.SourceExpansion);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_REQUIRED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.SourceId");
    }

    [Fact]
    public void SourceExpansion_Should_RejectNegativeMaximumCharacters()
    {
        var rule = new SourceExpansionPayloadSanitizationRule();
        var payload = new AgentMemorySourceExpansionAccountabilityPayload
        {
            OperationId = AccountabilityTestFixture.FixedOperationId,
            SourceKind = "ConversationTurn",
            SourceId = "source-1",
            Status = "expanded",
            EffectiveVisibleContentHash = AccountabilityTestFixture.CreateEffectiveContentHash(),
            MaximumCharacters = -1,
            WasTruncated = false,
            Sanitization = new AgentMemoryAccountabilitySanitizationSummary
            {
                State = "none",
                RedactionCodes = Array.Empty<string>(),
                DiagnosticCodes = Array.Empty<string>()
            }
        };
        var input = AccountabilityTestFixture.CreateAuditPayload(payload, Infos.SourceExpansion, AgentMemoryAccountabilityPayloadKinds.SourceExpansion);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_FIELD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.MaximumCharacters");
    }

    // ------------------------------------------------------------------
    // Shared: version, unknown members, generated JSON only
    // ------------------------------------------------------------------

    [Fact]
    public void AnyRule_Should_RejectUnsupportedPayloadVersion()
    {
        var rule = new RecallPayloadSanitizationRule();
        var input = AccountabilityTestFixture.CreateAuditPayload(
            AccountabilityTestFixture.CreateRecallPayload(),
            Infos.Recall,
            AgentMemoryAccountabilityPayloadKinds.Recall,
            version: AgentMemoryAccountabilityPayloadKinds.MaxPayloadVersion + 1);

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_PAYLOAD_VERSION_UNSUPPORTED");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Version");
    }

    [Fact]
    public void AnyRule_Should_RejectUnknownMember()
    {
        var rule = new RecallPayloadSanitizationRule();
        var valid = AccountabilityTestFixture.CreateRecallPayload();
        var element = JsonSerializer.SerializeToElement(valid, Infos.Recall);
        var node = element.Deserialize<JsonObject>();
        node!["maliciousProperty"] = "injected";
        var data = JsonSerializer.SerializeToElement(node);
        var input = new AuditPayload
        {
            Kind = AgentMemoryAccountabilityPayloadKinds.Recall,
            Version = AgentMemoryAccountabilityPayloadKinds.Version,
            Data = data
        };

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_PAYLOAD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Data");
    }

    [Fact]
    public void AnyRule_Should_RejectNonObjectPayloadData()
    {
        var rule = new CurationPayloadSanitizationRule();
        var input = new AuditPayload
        {
            Kind = AgentMemoryAccountabilityPayloadKinds.Curation,
            Version = AgentMemoryAccountabilityPayloadKinds.Version,
            Data = JsonSerializer.SerializeToElement("not-an-object")
        };

        var ex = Record.Exception(() => rule.Sanitize(input));

        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_PAYLOAD_INVALID");
        ex.Should().BeOfType<AuditSanitizationException>()
            .Which.Path.Should().Be("Payload.Data");
    }

    [Fact]
    public void Rules_Should_ExposeExactKindAndRuleVersion()
    {
        new RecallPayloadSanitizationRule().Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Recall);
        new RecallPayloadSanitizationRule().RuleVersion.Should().Be(1);

        new CurationPayloadSanitizationRule().Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.Curation);
        new CurationPayloadSanitizationRule().RuleVersion.Should().Be(1);

        new SourceExpansionPayloadSanitizationRule().Kind.Should().Be(AgentMemoryAccountabilityPayloadKinds.SourceExpansion);
        new SourceExpansionPayloadSanitizationRule().RuleVersion.Should().Be(1);
    }
}
