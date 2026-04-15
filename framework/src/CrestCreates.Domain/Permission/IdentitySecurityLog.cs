using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class IdentitySecurityLog : Entity<Guid>
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? TenantId { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool IsSucceeded { get; set; }
    public string? Detail { get; set; }
    public string? ClientIpAddress { get; set; }
    public DateTime CreationTime { get; set; }

    public IdentitySecurityLog()
    {
    }

    public IdentitySecurityLog(Guid id, string action, bool isSucceeded, DateTime creationTime)
    {
        Id = id;
        Action = action;
        IsSucceeded = isSucceeded;
        CreationTime = creationTime;
    }
}

/// <summary>
/// 统一身份安全日志写入接口，定义在 Domain 层供 Application 层使用。
/// 实现由认证基础设施提供，统一写入 IdentitySecurityLog 表。
/// </summary>
public interface IIdentitySecurityLogWriter
{
    Task WriteAsync(
        Guid? userId,
        string? userName,
        string? tenantId,
        string action,
        bool isSucceeded,
        string? detail = null,
        CancellationToken cancellationToken = default);
}
