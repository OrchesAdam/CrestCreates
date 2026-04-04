using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.OpenIddict;

public class OpenIddictAuthorization : Entity<Guid>
{
    public string? ApplicationId { get; set; }
    public string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
    public string? CreationDate { get; set; }
    public string? Properties { get; set; }
    public string? Scopes { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
    public string? TenantId { get; set; }
}
