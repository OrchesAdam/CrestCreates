using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Application.Contracts.DTOs.Settings;

public class SettingValueDto
{
    public string Name { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public SettingValueType ValueType { get; set; }

    public bool IsEncrypted { get; set; }

    public SettingScope AllowedScopes { get; set; }

    public SettingScope? Scope { get; set; }

    public string? ProviderKey { get; set; }

    public string? TenantId { get; set; }

    public bool HasValue { get; set; }

    public string? Value { get; set; }

    public string MaskedValue { get; set; } = string.Empty;
}
