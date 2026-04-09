using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class User : MustHaveTenantOrganizationEntity<Guid>
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSuperAdmin { get; set; } = false;
    public int AccessFailedCount { get; set; }
    public bool LockoutEnabled { get; set; } = true;
    public DateTime? LockoutEndTime { get; set; }
    public DateTime? LastLoginTime { get; set; }
    public DateTime? LastPasswordChangeTime { get; set; }

    public User()
    {
    }

    public User(Guid id, string userName, string email, string tenantId)
    {
        Id = id;
        UserName = userName;
        Email = email;
        TenantId = tenantId;
    }
}
