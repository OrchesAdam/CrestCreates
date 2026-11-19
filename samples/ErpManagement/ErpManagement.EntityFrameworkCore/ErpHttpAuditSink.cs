using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Domain.AuditLog;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ErpManagement.EntityFrameworkCore;

public sealed class ErpHttpAuditSink(
    PostgreSqlAuditSink canonical,
    IServiceScopeFactory scopes,
    ICurrentTenant currentTenant,
    ITenantProvider tenants) : IAuditSink
{
    public string Id => canonical.Id;

    public async ValueTask<AuditSinkWriteResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var result = await canonical.WriteAsync(envelope, cancellationToken);
        if (result.Status == AuditSinkWriteStatus.Conflict || envelope.Action.Kind != "http.request")
            return result;

        var auditId = Guid.Parse(envelope.AuditId);
        await ProjectAsync(envelope, auditId, cancellationToken);
        return result;
    }

    private async Task ProjectAsync(AuditEnvelope envelope, Guid auditId, CancellationToken cancellationToken)
    {
        using var hostFrame = currentTenant.Change(null);
        ITenantInfo? tenant = null;
        if (envelope.TenantId is { } tenantId)
        {
            tenant = await tenants.GetTenantAsync(tenantId, cancellationToken);
            if (tenant is null || !string.Equals(tenant.Id, tenantId, StringComparison.Ordinal))
                throw new InvalidOperationException("The audit tenant no longer resolves to its recorded identity.");
        }

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

        using var tenantFrame = currentTenant.Change(tenant);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpManagementDbContext>();
        db.AuditLogs.Add(log);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_AuditLogs" })
        {
            if (!await db.AuditLogs.AnyAsync(x => x.Id == auditId, cancellationToken))
                throw;
        }
    }
}
