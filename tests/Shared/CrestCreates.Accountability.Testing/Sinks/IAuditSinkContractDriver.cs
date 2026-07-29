using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;

namespace CrestCreates.Accountability.Testing.Sinks;

public interface IAuditSinkContractDriver
{
    IAuditSink CreateSink();

    AuditEnvelope CreateEnvelope(string auditId, string integrityValue);
}
