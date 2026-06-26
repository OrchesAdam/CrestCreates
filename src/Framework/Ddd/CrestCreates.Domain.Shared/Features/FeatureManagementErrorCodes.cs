using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Domain.Shared.Features;

public static class FeatureManagementErrorCodes
{
    private const string UndefinedFeatureValue = "Crest.FeatureManagement.UndefinedFeature";
    public static ErrorCode UndefinedFeature { get; } = new(UndefinedFeatureValue);

    private const string InvalidValueValue = "Crest.FeatureManagement.InvalidValue";
    public static ErrorCode InvalidValue { get; } = new(InvalidValueValue);

    private const string UnsupportedScopeValue = "Crest.FeatureManagement.UnsupportedScope";
    public static ErrorCode UnsupportedScope { get; } = new(UnsupportedScopeValue);

    private const string CrossTenantAccessDeniedValue = "Crest.FeatureManagement.CrossTenantAccessDenied";
    public static ErrorCode CrossTenantAccessDenied { get; } = new(CrossTenantAccessDeniedValue);

    private const string MissingTenantContextValue = "Crest.FeatureManagement.MissingTenantContext";
    public static ErrorCode MissingTenantContext { get; } = new(MissingTenantContextValue);
}
