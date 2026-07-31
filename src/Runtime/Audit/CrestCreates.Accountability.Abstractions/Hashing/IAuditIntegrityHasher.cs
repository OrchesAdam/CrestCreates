using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Abstractions.Hashing;

public interface IAuditIntegrityHasher
{
    CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope);
}
