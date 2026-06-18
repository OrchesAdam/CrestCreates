namespace CrestCreates.OpenApi;

public sealed class CrestOpenApiOptions
{
    public bool EnableOpenApiDocument { get; set; } = true;

    public bool EnableUi { get; set; } = true;

    public bool EnableAuthentication { get; set; } = true;

    public bool EnableTenantHeader { get; set; } = true;

    public string DocumentTitle { get; set; } = "CrestCreates API";

    public string DocumentVersion { get; set; } = "v1";

    /// <summary>
    /// Default Bearer token for Development environment, pre-filled in Scalar UI.
    /// Should only be set in appsettings.Development.json — never in production.
    /// </summary>
    public string? DefaultBearerToken { get; set; }

    /// <summary>
    /// Default username for Development environment, used with OAuth2 password flow in Scalar UI.
    /// Should only be set in appsettings.Development.json — never in production.
    /// </summary>
    public string? DefaultUsername { get; set; }

    /// <summary>
    /// Default password for Development environment, used with OAuth2 password flow in Scalar UI.
    /// Should only be set in appsettings.Development.json — never in production.
    /// </summary>
    public string? DefaultPassword { get; set; }

    /// <summary>
    /// Default tenant ID for Development environment, pre-filled in X-Tenant-Id header in Scalar UI.
    /// Should only be set in appsettings.Development.json — never in production.
    /// </summary>
    public string? DefaultTenantId { get; set; }
}
