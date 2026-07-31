using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Recording;
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

namespace CrestCreates.Accountability.Tests.Contract;

public sealed class AuditDataContractTests
{
    [Fact]
    public void DefaultsToNoCapture()
    {
        var envelope = CreateEnvelope();
        envelope.DataSnapshot.Should().BeNull();
        envelope.Payload.Should().BeNull();
    }

    [Fact]
    public async Task RejectsUnknownRawPayload()
    {
        using var json = JsonDocument.Parse("{\"raw\":\"secret\"}");
        var result = await CreateRecorder(new DefaultAuditSanitizer(
                new AuditPayloadSanitizationRuleRegistry([]),
                new AuditDataArtifactSanitizationRuleRegistry([])))
            .RecordAsync(CreateEnvelope() with
            {
                Payload = new AuditPayload
                {
                    Kind = "unknown.payload",
                    Version = 1,
                    Data = json.RootElement.Clone()
                }
            });

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().ContainSingle(x => x.Code == "AUDIT_UNKNOWN_SANITIZATION_RULE");
    }

    [Fact]
    public void RejectsDuplicateJsonProperties()
    {
        using var json = JsonDocument.Parse("{\"same\":1,\"same\":2}");
        var validation = new AuditEnvelopeValidator().ValidateCandidate(CreateEnvelope() with
        {
            Payload = new AuditPayload
            {
                Kind = "test.payload",
                Version = 1,
                Data = json.RootElement.Clone()
            }
        });

        validation.Issues.Should().Contain(x =>
            x.Code == "AUDIT_INVALID_JSON_VALUE" && x.Path == "Payload.Data");
    }

    [Fact]
    public void RequiresCapturePolicySanitizerPolicyAndRuleVersions()
    {
        var validator = new AuditEnvelopeValidator();
        var capture = validator.ValidateCandidate(CreateEnvelope() with
        {
            DataSnapshot = new AuditDataSnapshot
            {
                CapturePolicyId = "policy",
                CapturePolicyVersion = 0,
                Artifacts = []
            }
        });
        var sanitized = validator.ValidateSafeSnapshot(CreateEnvelope() with
        {
            Sanitization = new AuditSanitizationStamp
            {
                PolicyId = "policy",
                PolicyVersion = 0,
                AppliedRuleIds = []
            }
        });

        capture.Issues.Should().Contain(x => x.Code == "AUDIT_INVALID_CAPTURE_POLICY_VERSION");
        sanitized.Issues.Should().Contain(x => x.Code == "AUDIT_INVALID_SANITIZATION_POLICY_VERSION");
        var invalidRule = () => new AuditPayloadSanitizationRuleRegistry([new InvalidPayloadRule()]);
        invalidRule.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequiresContentHashBasis()
    {
        var validation = new AuditEnvelopeValidator().ValidateCandidate(CreateEnvelope() with
        {
            DataSnapshot = new AuditDataSnapshot
            {
                CapturePolicyId = "policy",
                CapturePolicyVersion = 1,
                Artifacts = [new AuditDataArtifact { Kind = "test.artifact", ContentHash = Hash }]
            }
        });

        validation.Issues.Should().Contain(x =>
            x.Code == "AUDIT_INVALID_HASH_METADATA"
            && x.Path!.EndsWith("ContentHashBasis", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnforcesPayloadAndArtifactLimitsBeforeAndAfterSanitization()
    {
        using var oversized = JsonDocument.Parse(JsonSerializer.Serialize(
            new string('x', AuditContractLimits.MaxPayloadBytes + 1)));
        var before = await CreateRecorder(new PassThroughSanitizer()).RecordAsync(CreateEnvelope() with
        {
            Payload = new AuditPayload
            {
                Kind = "test.payload",
                Version = 1,
                Data = oversized.RootElement.Clone()
            }
        });
        var after = await CreateRecorder(new OversizedOutputSanitizer()).RecordAsync(CreateEnvelope());

        before.Status.Should().Be(AuditRecordStatus.Rejected);
        before.Issues.Should().Contain(x => x.Code == "AUDIT_LIMIT_EXCEEDED" && x.Path == "Payload.Data");
        after.Status.Should().Be(AuditRecordStatus.Rejected);
        after.Issues.Should().Contain(x => x.Code == "AUDIT_LIMIT_EXCEEDED" && x.Path == "Outcome.SafeSummary");
    }

    [Fact]
    public async Task RejectsOversizedCandidateBeforeInvokingRules()
    {
        using var oversized = JsonDocument.Parse(JsonSerializer.Serialize(
            new string('x', AuditContractLimits.MaxPayloadBytes + 1)));
        var sanitizer = new CountingSanitizer();

        var result = await CreateRecorder(sanitizer).RecordAsync(CreateEnvelope() with
        {
            Payload = new AuditPayload
            {
                Kind = "test.payload",
                Version = 1,
                Data = oversized.RootElement.Clone()
            }
        });

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        sanitizer.Calls.Should().Be(0);
    }

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

    private static CanonicalHash Hash { get; } = new()
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

    private sealed class InvalidPayloadRule : IAuditPayloadSanitizationRule
    {
        public string Kind => "test.payload";
        public int RuleVersion => 0;
        public AuditPayload Sanitize(AuditPayload payload) => payload;
    }

    private sealed class PassThroughSanitizer : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(
            AuditEnvelope candidate,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result(candidate));
    }

    private sealed class CountingSanitizer : IAuditSanitizer
    {
        public int Calls { get; private set; }
        public ValueTask<AuditSanitizationResult> SanitizeAsync(
            AuditEnvelope candidate,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(Result(candidate));
        }
    }

    private sealed class OversizedOutputSanitizer : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(
            AuditEnvelope candidate,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result(candidate with
            {
                Outcome = candidate.Outcome with
                {
                    SafeSummary = new string('x', AuditContractLimits.MaxSafeSummaryLength + 1)
                }
            }));
    }

    private static AuditSanitizationResult Result(AuditEnvelope envelope)
        => new()
        {
            Envelope = envelope,
            Stamp = new AuditSanitizationStamp
            {
                PolicyId = "test",
                PolicyVersion = 1,
                AppliedRuleIds = []
            }
        };

    private sealed class FixedHasher : IAuditIntegrityHasher
    {
        public CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope) => Hash;
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
