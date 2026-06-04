namespace CrestCreates.OpenApi;

public sealed class CrestOpenApiOptions
{
    public bool EnableOpenApiDocument { get; set; } = true;

    public bool EnableUi { get; set; } = true;

    public bool EnableAuthentication { get; set; } = true;

    public bool EnableTenantHeader { get; set; } = true;

    public string DocumentTitle { get; set; } = "CrestCreates API";

    public string DocumentVersion { get; set; } = "v1";
}
