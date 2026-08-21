using CrestCreates.Accountability.Abstractions.Contracts;

namespace CrestCreates.Accountability.Abstractions.Preparation;

/// <summary>Prepares an audit candidate without performing any sink I/O.</summary>
public interface IAuditEnvelopePreparer
{
    ValueTask<AuditEnvelopePreparationResult> PrepareAsync(
        AuditEnvelope candidate,
        CancellationToken cancellationToken = default);
}
