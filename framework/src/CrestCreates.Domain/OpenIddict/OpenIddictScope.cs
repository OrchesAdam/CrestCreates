using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.OpenIddict;

public class OpenIddictScope : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
    public string? Description { get; set; }
    public string? Descriptions { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNames { get; set; }
    public string? Properties { get; set; }
    public string? Resources { get; set; }
    public string? TenantId { get; set; }
}
