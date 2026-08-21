using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;

namespace CrestCreates.Accountability.Abstractions.Preparation;

public sealed record AuditEnvelopePreparationResult
{
    public required bool IsAccepted { get; init; }
    public AuditEnvelope? Envelope { get; init; }
    public ImmutableArray<AuditRecordIssue> Issues { get; init; } = [];

    public static AuditEnvelopePreparationResult Accepted(AuditEnvelope envelope) => new()
    {
        IsAccepted = true,
        Envelope = envelope
    };

    public static AuditEnvelopePreparationResult Rejected(params AuditRecordIssue[] issues) => new()
    {
        IsAccepted = false,
        Issues = issues.ToImmutableArray()
    };
}
