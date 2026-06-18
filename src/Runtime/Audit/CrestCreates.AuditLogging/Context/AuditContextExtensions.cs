using System;
using CrestCreates.Domain.AuditLog;

namespace CrestCreates.AuditLogging.Context;

/// <summary>
/// AuditContext 扩展方法 — 依赖 Domain 实体的转换逻辑留在实现层
/// </summary>
internal static class AuditContextExtensions
{
    /// <summary>
    /// 将上下文转换为AuditLog实体
    /// </summary>
    public static AuditLog ToAuditLog(this AuditContext context)
    {
        var status = context.IsException ? AuditLogStatus.Failure : AuditLogStatus.Success;
        var duration = (long)(DateTime.UtcNow - context.StartTime).TotalMilliseconds;

        return new AuditLog(Guid.NewGuid())
        {
            Duration = duration,
            ExecutionTime = context.ExecutionTime,
            TraceId = context.TraceId,
            UserId = context.UserId,
            UserName = context.UserName,
            TenantId = context.TenantId,
            ClientIpAddress = context.ClientIpAddress,
            HttpMethod = context.HttpMethod,
            Url = context.Url,
            ServiceName = context.ServiceName,
            MethodName = context.MethodName,
            Parameters = context.Parameters,
            ReturnValue = context.ReturnValue,
            ExceptionMessage = context.ExceptionMessage,
            ExceptionStackTrace = context.ExceptionStackTrace,
            Status = (int)status,
            CreationTime = DateTime.UtcNow,
            ExtraProperties = context.ExtraProperties
        };
    }
}
