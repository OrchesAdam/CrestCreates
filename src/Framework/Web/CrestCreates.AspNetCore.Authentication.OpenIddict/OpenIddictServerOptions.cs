using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

public class OpenIddictServerOptions
{
    public const string SectionName = "OpenIddict";

    public string Issuer { get; set; } = string.Empty;
    public bool EnableDevelopmentEphemeralKeys { get; set; }
    public bool EnableDevelopmentPublicPasswordClient { get; set; }

    // Keep previous keys during rotation until all tokens protected by them expire.
    // Programmatic key/certificate instances remain owned by the caller.
    public List<SecurityKey> SigningKeys { get; } = new();
    public List<SecurityKey> EncryptionKeys { get; } = new();
    public List<X509Certificate2> SigningCertificates { get; } = new();
    public List<X509Certificate2> EncryptionCertificates { get; } = new();
    public List<OpenIddictCertificateOptions> SigningCertificateFiles { get; set; } = new();
    public List<OpenIddictCertificateOptions> EncryptionCertificateFiles { get; set; } = new();
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public bool EnableAuthorizationCodeFlow { get; set; } = true;
    public bool EnableClientCredentialsFlow { get; set; } = true;
    public bool EnablePasswordFlow { get; set; } = true;
    public bool EnableRefreshTokenFlow { get; set; } = true;
}

public sealed class OpenIddictCertificateOptions
{
    public string Path { get; set; } = string.Empty;
    public string? Password { get; set; }
}
