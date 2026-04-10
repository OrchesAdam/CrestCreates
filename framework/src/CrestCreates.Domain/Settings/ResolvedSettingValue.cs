using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public class ResolvedSettingValue
{
    public string Name { get; init; } = string.Empty;

    public string? Value { get; init; }

    public SettingScope? Scope { get; init; }

    public string? ProviderKey { get; init; }

    public string? TenantId { get; init; }

    public bool IsEncrypted { get; init; }
}
