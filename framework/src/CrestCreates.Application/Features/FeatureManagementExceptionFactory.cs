using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

internal static class FeatureManagementExceptionFactory
{
    public static CrestBusinessException UndefinedFeature(string name)
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.UndefinedFeature,
            $"未定义的功能特性: {name}");
    }

    public static CrestBusinessException InvalidValue(string name, FeatureValueType valueType, string? value)
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.InvalidValue,
            $"功能特性 '{name}' 的值 '{value}' 不是有效的 {valueType} 值");
    }

    public static CrestBusinessException UnsupportedScope(string name, FeatureScope scope)
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.UnsupportedScope,
            $"功能特性 '{name}' 不支持作用域 {scope}");
    }

    public static CrestBusinessException MissingTenantContext()
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.MissingTenantContext,
            "当前租户上下文不存在");
    }
}
