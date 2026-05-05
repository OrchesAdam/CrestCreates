namespace CrestCreates.Domain.Shared.Features;

public static class FeatureManagementErrorCodes
{
    public const string UndefinedFeature = "Crest.FeatureManagement.UndefinedFeature";
    public const string InvalidValue = "Crest.FeatureManagement.InvalidValue";
    public const string UnsupportedScope = "Crest.FeatureManagement.UnsupportedScope";
    public const string CrossTenantAccessDenied = "Crest.FeatureManagement.CrossTenantAccessDenied";
    public const string MissingTenantContext = "Crest.FeatureManagement.MissingTenantContext";
}
