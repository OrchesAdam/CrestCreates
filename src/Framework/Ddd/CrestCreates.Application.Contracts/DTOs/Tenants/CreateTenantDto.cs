using System.ComponentModel.DataAnnotations;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class CreateTenantDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DefaultConnectionString { get; set; }
}
