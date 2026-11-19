using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Domain.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ErpManagement.EntityFrameworkCore;

public sealed class ErpHttpAuditSink(IServiceScopeFactory scopes) : IAuditSink
{
    public string Id => "erp-http-audit";

    public async ValueTask<AuditSinkWriteResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var integrity = envelope.Integrity ?? throw new InvalidOperationException("An HTTP audit envelope requires integrity.");
        if (!Guid.TryParse(envelope.AuditId, out var auditId) || envelope.Runtime.InvocationSource != "http")
            return Result(envelope.AuditId, integrity, AuditSinkWriteStatus.Accepted);

        var log = new AuditLog(auditId)
        {
            Duration = (long)(envelope.Runtime.Duration ?? TimeSpan.Zero).TotalMilliseconds,
            UserId = envelope.Actor.Id,
            UserName = envelope.Actor.DisplayName,
            TenantId = envelope.TenantId,
            HttpMethod = envelope.Tags.GetValueOrDefault("http.method"),
            Url = envelope.Tags.GetValueOrDefault("http.route"),
            ServiceName = envelope.Action.Kind,
            MethodName = envelope.Action.Name,
            Status = envelope.Outcome.Status == "succeeded" ? (int)AuditLogStatus.Success : (int)AuditLogStatus.Failure,
            ExecutionTime = envelope.OccurredAt.UtcDateTime,
            CreationTime = envelope.OccurredAt.UtcDateTime,
            TraceId = envelope.Runtime.TraceId
        };

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        db.AuditLogs.Add(log);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result(envelope.AuditId, integrity, AuditSinkWriteStatus.Accepted);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_AuditLogs" })
        {
            db.Entry(log).State = EntityState.Detached;
            if (!await db.AuditLogs.AnyAsync(x => x.Id == auditId, cancellationToken))
                throw;
            return Result(envelope.AuditId, integrity, AuditSinkWriteStatus.Duplicate);
        }
    }

    private AuditSinkWriteResult Result(string auditId, CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash integrity, AuditSinkWriteStatus status)
        => new() { SinkId = Id, AuditId = auditId, Integrity = integrity, Status = status };
}
