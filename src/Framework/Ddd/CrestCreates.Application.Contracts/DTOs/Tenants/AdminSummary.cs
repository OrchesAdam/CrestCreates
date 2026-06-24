namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class AdminSummary
{
    public bool HasAdmin { get; set; }
    public string? AdminUserId { get; set; }
    public string? AdminUserName { get; set; }
    public string? AdminEmail { get; set; }
    public bool IsAdminActive { get; set; }
}
