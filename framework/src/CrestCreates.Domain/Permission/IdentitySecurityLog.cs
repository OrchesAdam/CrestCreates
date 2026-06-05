using System;
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
