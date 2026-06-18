using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class RefreshToken : Entity<Guid>
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime ExpirationTime { get; set; }
    public DateTime? RevokedTime { get; set; }

    public bool IsActive => RevokedTime == null && ExpirationTime > DateTime.UtcNow;

    public RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        Guid userId,
        string token,
        DateTime creationTime,
        DateTime expirationTime,
        string? tenantId = null)
    {
        Id = id;
        UserId = userId;
        Token = token;
        CreationTime = creationTime;
        ExpirationTime = expirationTime;
        TenantId = tenantId;
    }
}
