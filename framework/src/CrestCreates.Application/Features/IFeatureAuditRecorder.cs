using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Application.Features;

public interface IFeatureAuditRecorder
{
    Task RecordAsync(FeatureAuditEntry entry, CancellationToken cancellationToken = default);
}
