using System;

namespace CrestCreates.Accountability.Abstractions.Retention;

/// <summary>
/// Explicit retention partition. Host is only the formal store's null-TenantId partition;
/// there is no AllTenants value and no runtime guessing from empty strings or names.
/// </summary>
public sealed record AuditRetentionScope
{
    public const string HostKind = "host";
    public const string TenantKind = "tenant";

    public required string Kind { get; init; }

    public string? TenantId { get; init; }

    public static AuditRetentionScope Host { get; } = new() { Kind = HostKind };

    public static AuditRetentionScope ForTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("A tenant retention scope requires an authoritative tenant id.", nameof(tenantId));
        var scope = new AuditRetentionScope { Kind = TenantKind, TenantId = tenantId };
        scope.EnsureValid();
        return scope;
    }

    public bool IsHost => string.Equals(Kind, HostKind, StringComparison.Ordinal);

    public void EnsureValid()
    {
        if (IsHost)
        {
            if (TenantId is not null)
                throw new ArgumentException("The host retention scope cannot carry a tenant id.");
            return;
        }

        if (!string.Equals(Kind, TenantKind, StringComparison.Ordinal))
            throw new ArgumentException($"Unknown retention scope kind '{Kind}'.");
        if (string.IsNullOrWhiteSpace(TenantId))
            throw new ArgumentException("A tenant retention scope requires a tenant id.");
    }

    public string StableSignature()
        => IsHost ? HostKind : $"{TenantKind}:{TenantId}";
}
