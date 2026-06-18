using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public class SettingValueEntry
{
    public string Name { get; init; } = string.Empty;

    public string? Value { get; init; }

    public string ProviderType { get; init; } = string.Empty;

    public SettingScope Scope { get; init; }

    public string ProviderKey { get; init; } = string.Empty;

    public string? TenantId { get; init; }

    public bool IsEncrypted { get; init; }
}
