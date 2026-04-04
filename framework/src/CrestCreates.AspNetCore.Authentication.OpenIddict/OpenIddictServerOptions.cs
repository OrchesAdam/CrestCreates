using System;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

public class OpenIddictServerOptions
{
    public string Issuer { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public bool EnableAuthorizationCodeFlow { get; set; } = true;
    public bool EnableClientCredentialsFlow { get; set; } = true;
    public bool EnablePasswordFlow { get; set; } = true;
    public bool EnableRefreshTokenFlow { get; set; } = true;
}
