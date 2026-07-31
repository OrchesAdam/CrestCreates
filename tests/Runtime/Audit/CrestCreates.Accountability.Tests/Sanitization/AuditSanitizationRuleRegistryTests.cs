using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Abstractions.Validation;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.Recording;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Tests.Sanitization;

public sealed class AuditSanitizationRuleRegistryTests
{
    [Fact]
    public void RejectsDuplicatePayloadKindOwnerAtStartup()
        => DuplicateKindOwnersFailAtConstruction();

    [Fact]
    public void RejectsDuplicateArtifactKindOwnerAtStartup()
    {
        var action = () => new AuditDataArtifactSanitizationRuleRegistry(
            [new ArtifactRule("same", []), new ArtifactRule("same", [])]);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void HasNoWildcardOrPassThroughFallback()
        => UnknownKindIsRejectedWithoutFallback();

    [Fact]
    public void RejectsUnknownKindsWithStableIssueCode()
        => UnknownKindIsRejectedWithoutFallback();

    [Fact]
    public void RejectsRuleExceptionWithStableIssueCode()
    {
        var registry = new AuditPayloadSanitizationRuleRegistry([new ThrowingPayloadRule("throws")]);
        var action = () => registry.Sanitize(Payload("throws"));

        action.Should().Throw<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_SANITIZATION_RULE_FAILED");
    }

    [Fact]
    public async Task RevalidatesRuleOutputAsSafeSnapshot()
    {
        var sanitizer = new DefaultAuditSanitizer(
            new AuditPayloadSanitizationRuleRegistry([new OversizedPayloadRule("payload.safe")]),
            new AuditDataArtifactSanitizationRuleRegistry([]));
        var envelope = CreateEnvelope() with { Payload = Payload("payload.safe") };

        var result = await CreateRecorder(sanitizer).RecordAsync(envelope);

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().Contain(x => x.Code == "AUDIT_LIMIT_EXCEEDED" && x.Path == "Payload.Data");
    }

    [Fact]
    public async Task DistinguishesCompositionPolicyVersionFromRuleVersion()
    {
        var sanitizer = new DefaultAuditSanitizer(
            new AuditPayloadSanitizationRuleRegistry([new Rule("payload.versioned", 7)]),
            new AuditDataArtifactSanitizationRuleRegistry([]),
            policyId: "composition",
            policyVersion: 3);

        var result = await sanitizer.SanitizeAsync(CreateEnvelope() with
        {
            Payload = Payload("payload.versioned")
        });

        result.Stamp.PolicyVersion.Should().Be(3);
        result.Stamp.AppliedRuleIds.Should().ContainSingle().Which.Should().Be("payload:payload.versioned:v7");
    }

    [Fact]
    public void PayloadRuleCannotChangePayloadKindOrVersion()
    {
        var registry = new AuditPayloadSanitizationRuleRegistry([new RewritingPayloadRule("payload.original")]);
        var action = () => registry.Sanitize(Payload("payload.original"));

        action.Should().Throw<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_SANITIZER_REWROTE_PROTECTED_FACT");
    }

    [Fact]
    public void ArtifactRuleCannotChangeArtifactKind()
    {
        var registry = new AuditDataArtifactSanitizationRuleRegistry([new RewritingArtifactRule("artifact.original")]);
        var action = () => registry.Sanitize(new AuditDataArtifact { Kind = "artifact.original" });

        action.Should().Throw<AuditSanitizationException>()
            .Which.Code.Should().Be("AUDIT_SANITIZER_REWROTE_PROTECTED_FACT");
    }

    [Fact]
    public async Task PayloadRuleReturningNullIsRejected()
    {
        var sanitizer = new DefaultAuditSanitizer(
            new AuditPayloadSanitizationRuleRegistry([new NullPayloadRule("payload.null")]),
            new AuditDataArtifactSanitizationRuleRegistry([]));
        var result = await CreateRecorder(sanitizer).RecordAsync(CreateEnvelope() with
        {
            Payload = Payload("payload.null")
        });

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().ContainSingle(x => x.Code == "AUDIT_SANITIZED_OUTPUT_INVALID");
    }

    [Fact]
    public async Task ArtifactRuleReturningNullIsRejected()
    {
        var sanitizer = new DefaultAuditSanitizer(
            new AuditPayloadSanitizationRuleRegistry([]),
            new AuditDataArtifactSanitizationRuleRegistry([new NullArtifactRule("artifact.null")]));
        var result = await CreateRecorder(sanitizer).RecordAsync(CreateEnvelope(
            new AuditDataArtifact { Kind = "artifact.null" }));

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().ContainSingle(x => x.Code == "AUDIT_SANITIZED_OUTPUT_INVALID");
    }

    [Fact]
    public void DuplicateKindOwnersFailAtConstruction()
    {
        var action = () => new AuditPayloadSanitizationRuleRegistry([new Rule("same", 1), new Rule("same", 2)]);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UnknownKindIsRejectedWithoutFallback()
    {
        var registry = new AuditPayloadSanitizationRuleRegistry([new Rule("known", 1)]);
        var action = () => registry.Sanitize(new AuditPayload { Kind = "unknown", Version = 1, Data = JsonDocument.Parse("{}").RootElement.Clone() });
        action.Should().Throw<AuditSanitizationException>().Which.Code.Should().Be("AUDIT_UNKNOWN_SANITIZATION_RULE");
    }

    [Fact]
    public async Task DuplicateArtifactKindRejectedBeforeRuleInvocation()
    {
        var rule = new ArtifactRule("artifact.same", []);
        var sanitizer = CreateSanitizer(rule);
        var recorder = CreateRecorder(sanitizer);

        var result = await recorder.RecordAsync(CreateEnvelope(
            new AuditDataArtifact { Kind = "artifact.same" },
            new AuditDataArtifact { Kind = "artifact.same" }));

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().Contain(x =>
            x.Code == "AUDIT_DUPLICATE_REFERENCE"
            && x.Path == "DataSnapshot.Artifacts.Kind");
        rule.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ArtifactRulesExecuteInOrdinalKindOrder()
    {
        var order = new List<string>();
        var sanitizer = CreateSanitizer(
            new ArtifactRule("artifact.z", order),
            new ArtifactRule("artifact.a", order));

        await sanitizer.SanitizeAsync(CreateEnvelope(
            new AuditDataArtifact { Kind = "artifact.z" },
            new AuditDataArtifact { Kind = "artifact.a" }));

        order.Should().Equal("artifact.a", "artifact.z");
    }

    [Fact]
    public async Task ArtifactInputPermutationDoesNotChangeSafeFact()
    {
        var sanitizer = CreateSanitizer(
            new ArtifactRule("artifact.z", []),
            new ArtifactRule("artifact.a", []));
        var first = await sanitizer.SanitizeAsync(CreateEnvelope(
            new AuditDataArtifact { Kind = "artifact.z" },
            new AuditDataArtifact { Kind = "artifact.a" }));
        var second = await sanitizer.SanitizeAsync(CreateEnvelope(
            new AuditDataArtifact { Kind = "artifact.a" },
            new AuditDataArtifact { Kind = "artifact.z" }));

        first.Envelope.DataSnapshot!.Artifacts.Select(x => x.Kind)
            .Should().Equal("artifact.a", "artifact.z");
        second.Envelope.DataSnapshot!.Artifacts.Should().Equal(first.Envelope.DataSnapshot.Artifacts);
    }

    private static DefaultAuditSanitizer CreateSanitizer(params IAuditDataArtifactSanitizationRule[] rules)
        => new(
            new AuditPayloadSanitizationRuleRegistry([]),
            new AuditDataArtifactSanitizationRuleRegistry(rules));

    private static DefaultAuditRecorder CreateRecorder(IAuditSanitizer sanitizer)
    {
        var writer = new AccountabilityCanonicalProjectionWriter();
        return new DefaultAuditRecorder(
            new AuditEnvelopeValidator(),
            sanitizer,
            new FixedHasher(),
            writer,
            [new NoopSink()],
            new AccountabilityOptions());
    }

    private static AuditEnvelope CreateEnvelope(params AuditDataArtifact[] artifacts)
        => new()
        {
            AuditId = "audit-1",
            OccurredAt = DateTimeOffset.UnixEpoch,
            CorrelationId = "correlation-1",
            Actor = new AuditActor { Kind = "system", Id = "system" },
            Action = new AuditAction { Kind = "test.action", Name = "test" },
            Target = new AuditTarget { Kind = "test.target", Id = "target-1" },
            Outcome = new AuditOutcome { Status = "succeeded" },
            DataSnapshot = new AuditDataSnapshot
            {
                CapturePolicyId = "test-policy",
                CapturePolicyVersion = 1,
                Artifacts = [.. artifacts]
            }
        };

    private static AuditPayload Payload(string kind)
        => new()
        {
            Kind = kind,
            Version = 1,
            Data = JsonDocument.Parse("{}").RootElement.Clone()
        };

    private sealed class Rule(string kind, int version) : IAuditPayloadSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion { get; } = version;
        public AuditPayload Sanitize(AuditPayload payload) => payload;
    }

    private sealed class ThrowingPayloadRule(string kind) : IAuditPayloadSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion => 1;
        public AuditPayload Sanitize(AuditPayload payload) => throw new InvalidOperationException("rule failed");
    }

    private sealed class RewritingPayloadRule(string kind) : IAuditPayloadSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion => 1;
        public AuditPayload Sanitize(AuditPayload payload) => payload with { Kind = "payload.changed", Version = 2 };
    }

    private sealed class OversizedPayloadRule(string kind) : IAuditPayloadSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion => 1;

        public AuditPayload Sanitize(AuditPayload payload)
        {
            using var document = JsonDocument.Parse($"\"{new string('x', AuditContractLimits.MaxPayloadBytes + 1)}\"");
            return payload with { Data = document.RootElement.Clone() };
        }
    }

    private sealed class NullPayloadRule(string kind) : IAuditPayloadSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion => 1;
        public AuditPayload Sanitize(AuditPayload payload) => null!;
    }

    private sealed class ArtifactRule(string kind, ICollection<string> order) : IAuditDataArtifactSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion => 1;
        public int Calls { get; private set; }

        public AuditDataArtifact Sanitize(AuditDataArtifact artifact)
        {
            Calls++;
            order.Add(Kind);
            return artifact;
        }
    }

    private sealed class RewritingArtifactRule(string kind) : IAuditDataArtifactSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion => 1;
        public AuditDataArtifact Sanitize(AuditDataArtifact artifact) => artifact with { Kind = "artifact.changed" };
    }

    private sealed class NullArtifactRule(string kind) : IAuditDataArtifactSanitizationRule
    {
        public string Kind { get; } = kind;
        public int RuleVersion => 1;
        public AuditDataArtifact Sanitize(AuditDataArtifact artifact) => null!;
    }

    private sealed class FixedHasher : IAuditIntegrityHasher
    {
        public CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope)
            => new()
            {
                Value = "hash",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "AccountabilityRecord",
                Scope = "InternalFull",
                Purpose = "AuditEvidence",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "accountability-record-hash-v1"
            };
    }

    private sealed class NoopSink : IAuditSink
    {
        public string Id => "noop";

        public ValueTask<AuditSinkWriteResult> WriteAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditSinkWriteResult
            {
                SinkId = Id,
                AuditId = envelope.AuditId,
                Integrity = envelope.Integrity!,
                Status = AuditSinkWriteStatus.Accepted
            });
    }
}
