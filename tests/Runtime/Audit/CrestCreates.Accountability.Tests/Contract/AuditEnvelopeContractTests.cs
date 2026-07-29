using System.Collections.Immutable;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Validation;
using CrestCreates.Accountability.Validation;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Tests.Contract;

public sealed class AuditEnvelopeContractTests
{
    [Fact]
    public void RequiresAuditIdCorrelationActorActionTargetOutcome()
    {
        var cases = new (string Path, Func<AuditEnvelope, AuditEnvelope> Mutate)[]
        {
            ("AuditId", value => value with { AuditId = string.Empty }),
            ("OccurredAt", value => value with { OccurredAt = default }),
            ("CorrelationId", value => value with { CorrelationId = string.Empty }),
            ("Actor.Kind", value => value with { Actor = value.Actor with { Kind = string.Empty } }),
            ("Actor.Id", value => value with { Actor = value.Actor with { Id = string.Empty } }),
            ("Action.Kind", value => value with { Action = value.Action with { Kind = string.Empty } }),
            ("Action.Name", value => value with { Action = value.Action with { Name = string.Empty } }),
            ("Target.Kind", value => value with { Target = value.Target with { Kind = string.Empty } }),
            ("Target.Id", value => value with { Target = value.Target with { Id = string.Empty } }),
            ("Outcome", value => value with { Outcome = null! })
        };

        foreach (var (path, mutate) in cases)
        {
            var result = new AuditEnvelopeValidator().ValidateCandidate(mutate(CreateEnvelope()));
            result.Issues.Should().Contain(x => x.Path == path, $"{path} is required");
        }
    }

    [Fact]
    public void AllowsTenantlessSystemFact()
    {
        var result = new AuditEnvelopeValidator().ValidateCandidate(CreateEnvelope() with { TenantId = null });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void RejectsSelfParentAndDuplicateReferences()
    {
        var duplicateRuntime = new AuditRuntimeReference("service", "same");
        var result = new AuditEnvelopeValidator().ValidateCandidate(CreateEnvelope() with
        {
            ParentAuditId = "audit-1",
            PreviousAuditId = "audit-1",
            Runtime = new AuditRuntimeContext { References = [duplicateRuntime, duplicateRuntime] },
            Evidence =
            [
                new AuditEvidenceReference { Kind = "test", Id = "same" },
                new AuditEvidenceReference { Kind = "test", Id = "same" }
            ]
        });

        result.Issues.Count(x => x.Code == "AUDIT_SELF_RELATION").Should().Be(2);
        result.Issues.Should().Contain(x => x.Code == "AUDIT_DUPLICATE_REFERENCE" && x.Path == "Runtime.References");
        result.Issues.Should().Contain(x => x.Code == "AUDIT_DUPLICATE_REFERENCE" && x.Path == "Evidence");
    }

    [Fact]
    public void EnforcesAllHardLimits()
    {
        using var oversizedJson = JsonDocument.Parse(JsonSerializer.Serialize(
            new string('x', AuditContractLimits.MaxPayloadBytes + 1)));
        var cases = new (string Path, AuditEnvelope Envelope)[]
        {
            ("AuditId", CreateEnvelope() with { AuditId = new string('a', AuditContractLimits.MaxIdentifierLength + 1) }),
            ("Actor.Kind", CreateEnvelope() with { Actor = new AuditActor { Kind = new string('a', AuditContractLimits.MaxSemanticKindLength + 1), Id = "id" } }),
            ("Action.Name", CreateEnvelope() with { Action = new AuditAction { Kind = "test.action", Name = new string('a', AuditContractLimits.MaxActionNameLength + 1) } }),
            ("Outcome.SafeSummary", CreateEnvelope() with { Outcome = new AuditOutcome { Status = "succeeded", SafeSummary = new string('a', AuditContractLimits.MaxSafeSummaryLength + 1) } }),
            ("Tags", CreateEnvelope() with { Tags = Enumerable.Range(0, AuditContractLimits.MaxTags + 1).ToImmutableSortedDictionary(i => $"k{i}", _ => "v", StringComparer.Ordinal) }),
            ("Tags.Key", CreateEnvelope() with { Tags = AuditTagMap.Empty.Add(new string('k', AuditContractLimits.MaxTagKeyLength + 1), "v") }),
            ("Tags.Value", CreateEnvelope() with { Tags = AuditTagMap.Empty.Add("k", new string('v', AuditContractLimits.MaxTagValueLength + 1)) }),
            ("Runtime.References", CreateEnvelope() with { Runtime = new AuditRuntimeContext { References = [.. Enumerable.Range(0, AuditContractLimits.MaxRuntimeReferences + 1).Select(i => new AuditRuntimeReference("service", $"s{i}"))] } }),
            ("Descriptors.Items", CreateEnvelope() with { Descriptors = new AuditDescriptorContext { Items = [.. Enumerable.Range(0, AuditContractLimits.MaxDescriptorReferences + 1).Select(i => new AuditDescriptorReference { Kind = "test", Id = $"d{i}", Version = 1 })] } }),
            ("Evidence", CreateEnvelope() with { Evidence = [.. Enumerable.Range(0, AuditContractLimits.MaxEvidenceReferences + 1).Select(i => new AuditEvidenceReference { Kind = "test", Id = $"e{i}" })] }),
            ("DataSnapshot.Artifacts", CreateEnvelope() with { DataSnapshot = new AuditDataSnapshot { CapturePolicyId = "policy", CapturePolicyVersion = 1, Artifacts = [.. Enumerable.Range(0, AuditContractLimits.MaxDataArtifacts + 1).Select(i => new AuditDataArtifact { Kind = $"artifact.a{i}" })] } }),
            ("Payload.Data", CreateEnvelope() with { Payload = new AuditPayload { Kind = "test.payload", Version = 1, Data = oversizedJson.RootElement.Clone() } })
        };

        foreach (var (path, envelope) in cases)
        {
            var result = new AuditEnvelopeValidator().ValidateCandidate(envelope);
            result.Issues.Should().Contain(x => x.Path == path, $"{path} must enforce its frozen limit");
        }

        AuditContractLimits.MaxSafeEnvelopeBytes.Should().BePositive();
        AuditContractLimits.MaxCandidateEnvelopeBytes.Should().BeGreaterThanOrEqualTo(AuditContractLimits.MaxSafeEnvelopeBytes);
        AuditContractLimits.MaxSingleArtifactBytes.Should().Be(AuditContractLimits.MaxPayloadBytes);
    }

    [Fact]
    public void RejectsUnknownOutcomeStatus()
    {
        var result = new AuditEnvelopeValidator().ValidateCandidate(CreateEnvelope() with
        {
            Outcome = new AuditOutcome { Status = "future-status" }
        });
        result.Issues.Should().Contain(x => x.Code == "AUDIT_UNKNOWN_OUTCOME_STATUS");
    }

    private static AuditEnvelope CreateEnvelope()
        => new()
        {
            AuditId = "audit-1",
            OccurredAt = DateTimeOffset.UnixEpoch,
            CorrelationId = "correlation-1",
            Actor = new AuditActor { Kind = "system", Id = "system" },
            Action = new AuditAction { Kind = "test.action", Name = "test" },
            Target = new AuditTarget { Kind = "test.target", Id = "target-1" },
            Outcome = new AuditOutcome { Status = "succeeded" }
        };
}
