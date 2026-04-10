using System;
using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class TenantDiagnosticsDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TenantHealthStatus OverallStatus { get; set; }
    public TenantStatusDetails Status { get; set; } = new();
    public ConnectionStringSummary ConnectionStrings { get; set; } = new();
    public AdminSummary Admin { get; set; } = new();
    public Statistics Statistics { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime DiagnosedAt { get; set; }
}

public class TenantHealthStatus
{
    public bool IsHealthy { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public string Level { get; set; } = "Healthy";
    public List<string> Issues { get; set; } = new();
}

public class TenantStatusDetails
{
    public string LifecycleState { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ArchivedTime { get; set; }
    public DateTime? CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

public class ConnectionStringSummary
{
    public int TotalCount { get; set; }
    public bool HasDefaultConnectionString { get; set; }
    public string? DefaultConnectionStringMasked { get; set; }
    public List<string> NamedConnectionStrings { get; set; } = new();
}

public class AdminSummary
{
    public bool HasAdmin { get; set; }
    public string? AdminUserId { get; set; }
    public string? AdminUserName { get; set; }
    public string? AdminEmail { get; set; }
    public bool IsAdminActive { get; set; }
}

public class Statistics
{
    public int UserCount { get; set; }
    public int RoleCount { get; set; }
    public int PermissionGrantCount { get; set; }
    public int DomainMappingCount { get; set; }
}
