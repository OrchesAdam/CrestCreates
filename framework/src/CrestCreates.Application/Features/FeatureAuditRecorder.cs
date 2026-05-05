using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;

namespace CrestCreates.Application.Features;

public class FeatureAuditRecorder : IFeatureAuditRecorder
{
    private const string ExtraPropertyKey = "FeatureChanges";

    public Task RecordAsync(FeatureAuditEntry entry, CancellationToken cancellationToken = default)
    {
        var context = AuditContext.Current;
        if (context is null)
        {
            return Task.CompletedTask;
        }

        if (!context.ExtraProperties.TryGetValue(ExtraPropertyKey, out var value) ||
            value is not List<FeatureAuditEntry> entries)
        {
            entries = new List<FeatureAuditEntry>();
            context.ExtraProperties[ExtraPropertyKey] = entries;
        }

        entries.Add(entry);
        return Task.CompletedTask;
    }
}
