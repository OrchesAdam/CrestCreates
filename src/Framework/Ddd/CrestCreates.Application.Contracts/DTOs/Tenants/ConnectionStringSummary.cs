using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class ConnectionStringSummary
{
    public int TotalCount { get; set; }
    public bool HasDefaultConnectionString { get; set; }
    public string? DefaultConnectionStringMasked { get; set; }
    public List<string> NamedConnectionStrings { get; set; } = new();
}
