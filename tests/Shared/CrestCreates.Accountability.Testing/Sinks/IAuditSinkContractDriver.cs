using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;

namespace CrestCreates.Accountability.Testing.Sinks;

public interface IAuditSinkContractDriver
{
    IAuditSink CreateSink();

    AuditEnvelope CreateEnvelope(string auditId, string integrityValue);

    ValueTask<AuditEnvelope?> ReadAsync(
        IAuditSink sink,
        string auditId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AuditEnvelope>> ReadAllAsync(
        IAuditSink sink,
        CancellationToken cancellationToken = default);
}
