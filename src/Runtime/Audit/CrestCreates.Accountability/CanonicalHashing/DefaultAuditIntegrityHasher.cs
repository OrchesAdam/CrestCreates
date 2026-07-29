using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.CanonicalHashing;

public sealed class DefaultAuditIntegrityHasher : IAuditIntegrityHasher
{
    private readonly ICanonicalHashComputer _hashComputer;
    private readonly AccountabilityCanonicalProjectionWriter _projectionWriter;

    public DefaultAuditIntegrityHasher(
        ICanonicalHashComputer hashComputer,
        AccountabilityCanonicalProjectionWriter projectionWriter)
    {
        _hashComputer = hashComputer;
        _projectionWriter = projectionWriter;
    }

    public CanonicalHash Compute(AuditEnvelope envelope)
        => _hashComputer.ComputeFromProjection(_projectionWriter.CreateProjection(envelope));
}
