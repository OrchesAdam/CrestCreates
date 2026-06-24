using System;
using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class TenantDiagnosticsDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TenantHealthStatus OverallStatus { get; set; } = new();
    public TenantStatusDetails Status { get; set; } = new();
    public ConnectionStringSummary ConnectionStrings { get; set; } = new();
    public AdminSummary Admin { get; set; } = new();
    public Statistics Statistics { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime DiagnosedAt { get; set; }
}
