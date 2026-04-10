using System;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class TenantConnectionStringDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MaskedValue { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

public class CreateTenantConnectionStringDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class UpdateTenantConnectionStringDto
{
    public string Value { get; set; } = string.Empty;
}
