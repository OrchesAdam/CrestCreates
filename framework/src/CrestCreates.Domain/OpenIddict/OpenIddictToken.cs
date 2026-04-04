using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.OpenIddict;

public class OpenIddictToken : Entity<Guid>
{
    public string? ApplicationId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
    public string? CreationDate { get; set; }
    public string? ExpirationDate { get; set; }
    public string? Payload { get; set; }
    public string? Properties { get; set; }
    public string? RedemptionDate { get; set; }
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
    public string? TenantId { get; set; }
}
