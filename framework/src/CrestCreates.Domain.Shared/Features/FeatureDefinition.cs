using System;

namespace CrestCreates.Domain.Shared.Features;

public class FeatureDefinition
{
    public FeatureDefinition(
        string name,
        string groupName,
        string? displayName = null,
        string? description = null,
        string? defaultValue = null,
        FeatureValueType valueType = FeatureValueType.Bool,
        bool isVisible = true,
        bool isEditable = true,
        FeatureScope scopes = FeatureScope.Global)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("功能特性名称不能为空", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentException("功能特性分组不能为空", nameof(groupName));
        }

        Name = name.Trim();
        GroupName = groupName.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultValue = defaultValue;
        ValueType = valueType;
        IsVisible = isVisible;
        IsEditable = isEditable;
        Scopes = scopes;
    }

    public string Name { get; }

    public string GroupName { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public string? DefaultValue { get; }

    public FeatureValueType ValueType { get; }

    public bool IsVisible { get; }

    public bool IsEditable { get; }

    public FeatureScope Scopes { get; }

    public bool SupportsScope(FeatureScope scope)
    {
        return (Scopes & scope) == scope;
    }

    public string? GetNormalizedDefaultValue()
    {
        if (DefaultValue == null)
        {
            return null;
        }

        return ValueType switch
        {
            FeatureValueType.Bool => NormalizeBoolValue(DefaultValue),
            FeatureValueType.Int => NormalizeIntValue(DefaultValue),
            FeatureValueType.String => DefaultValue.Trim(),
            _ => DefaultValue.Trim()
        };
    }

    private static string? NormalizeBoolValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue.ToString().ToLowerInvariant();
        }

        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return "true";
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return "false";
        }

        return null;
    }

    private static string? NormalizeIntValue(string value)
    {
        if (int.TryParse(value, out _))
        {
            return value.Trim();
        }

        return null;
    }
}
