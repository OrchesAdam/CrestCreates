using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Domain.OpenIddict;
using CrestCreates.Security.Abstractions;
using CrestCreates.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;

public static class OpenIddictServiceCollectionExtensions
{
    public static IServiceCollection AddOpenIddictServer(
        this IServiceCollection services,
        Action<OpenIddictServerOptions>? configure = null)
    {
        // Module and host share one configuration, including when the module registers first.
        var existing = services.FirstOrDefault(x => x.ServiceType == typeof(OpenIddictServerOptions))
            ?.ImplementationInstance as OpenIddictServerOptions;
        if (existing is not null)
        {
            configure?.Invoke(existing);
            return services;
        }
        var options = new OpenIddictServerOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<OpenIddictKeyMaterial>();

        services.AddOpenIddict()
            .AddCore(coreOptions =>
            {
                coreOptions.UseEntityFrameworkCore()
                    .UseDbContext<OpenIddictDbContext>()
                    .ReplaceDefaultEntities<
                        OpenIddictApplication,
                        OpenIddictAuthorization,
                        OpenIddictScope,
                        OpenIddictToken,
                        long>();
            })
            .AddServer(serverOptions =>
            {
                serverOptions
                    .SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo");

                serverOptions.RegisterScopes(
                    Scopes.OpenId,
                    Scopes.Profile,
                    Scopes.Email,
                    Scopes.OfflineAccess);

                services.AddOptions<global::OpenIddict.Server.OpenIddictServerOptions>()
                    .Configure<OpenIddictKeyMaterial>((server, material) =>
                    {
                        if (options.EnableAuthorizationCodeFlow)
                        {
                            server.GrantTypes.Add(GrantTypes.AuthorizationCode);
                            server.ResponseTypes.Add(ResponseTypes.Code);
                        }
                        if (options.EnableClientCredentialsFlow) server.GrantTypes.Add(GrantTypes.ClientCredentials);
                        if (options.EnablePasswordFlow) server.GrantTypes.Add(GrantTypes.Password);
                        if (options.EnableRefreshTokenFlow) server.GrantTypes.Add(GrantTypes.RefreshToken);
                        if (!string.IsNullOrWhiteSpace(options.Issuer))
                        {
                            if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) ||
                                issuer.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(issuer.Query) || !string.IsNullOrEmpty(issuer.Fragment))
                                throw new InvalidOperationException("OpenIddict Issuer must be an absolute HTTPS URI without query or fragment.");
                            server.Issuer = issuer;
                        }
                        if (options.AccessTokenLifetimeMinutes <= 0 || options.RefreshTokenLifetimeDays <= 0)
                            throw new InvalidOperationException("OpenIddict token lifetimes must be positive.");
                        server.AccessTokenLifetime = TimeSpan.FromMinutes(options.AccessTokenLifetimeMinutes);
                        server.RefreshTokenLifetime = TimeSpan.FromDays(options.RefreshTokenLifetimeDays);
                        foreach (var key in material.SigningKeys)
                            server.SigningCredentials.Add(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
                        foreach (var key in material.EncryptionKeys)
                            server.EncryptionCredentials.Add(new EncryptingCredentials(key, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
                    })
                    // Validators run after every PostConfigure, including OpenIddict's credential sorting.
                    // Keep historical certificates for validation/decryption; only guard new issuance.
                    .Validate(server => IsSelectedCertificateValid(server.SigningCredentials.FirstOrDefault()?.Key, server.TimeProvider!),
                        "OpenIddict selected signing certificate is outside its validity period.")
                    .Validate(server => IsSelectedCertificateValid(server.EncryptionCredentials.FirstOrDefault()?.Key, server.TimeProvider!),
                        "OpenIddict selected encryption certificate is outside its validity period.")
                    .ValidateOnStart();

                serverOptions.DisableAccessTokenEncryption();

                serverOptions.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();
            })
            .AddValidation(validationOptions =>
            {
                validationOptions.UseAspNetCore();
                validationOptions.UseLocalServer();
            });

        services.AddHttpContextAccessor();
        services.TryAddScoped<IIdentitySecurityLogWriter, IdentitySecurityLogServiceImpl>();
        services.TryAddScoped<IPasswordGrantHandler, PasswordGrantHandlerImpl>();
        services.TryAddScoped<IRefreshTokenGrantHandler, RefreshTokenGrantHandlerImpl>();

        // Register source-generated JSON metadata for OpenIddict DTOs
        // so that OpenAPI schema generation works in AoT/trimming mode.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOpenApiJsonTypeInfoContributor, OpenIddictJsonTypeInfoContributor>());

        // Register host OpenIddict data seeder (scopes + client application)
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CrestCreates.MultiTenancy.Abstract.IDataSeeder, HostOpenIddictDataSeeder>());

        return services;
    }

    private static bool IsSelectedCertificateValid(SecurityKey? key, TimeProvider timeProvider)
    {
        if (key is not X509SecurityKey certificate) return true;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return certificate.Certificate.NotBefore.ToUniversalTime() <= now &&
            certificate.Certificate.NotAfter.ToUniversalTime() >= now;
    }

    public static AuthenticationBuilder AddOpenIddictAuthentication(
        this IServiceCollection services)
    {
        return services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });
    }
}

internal sealed class OpenIddictKeyMaterial : IDisposable
{
    private readonly List<X509Certificate2> _ownedCertificates = new();
    private readonly List<RSA> _ownedRsa = new();
    public List<SecurityKey> SigningKeys { get; } = new();
    public List<SecurityKey> EncryptionKeys { get; } = new();

    public OpenIddictKeyMaterial(OpenIddictServerOptions options, IHostEnvironment environment)
    {
        if ((options.EnableDevelopmentEphemeralKeys || options.EnableDevelopmentPublicPasswordClient) && !environment.IsDevelopment())
            throw new InvalidOperationException("OpenIddict development opt-ins require the Development host environment.");
        if (options.EnableDevelopmentPublicPasswordClient && !options.EnablePasswordFlow)
            throw new InvalidOperationException("The development password client requires the password flow.");
        try
        {
            SigningKeys.AddRange(options.SigningKeys);
            EncryptionKeys.AddRange(options.EncryptionKeys);
            SigningKeys.AddRange(options.SigningCertificates.Select(c => new X509SecurityKey(c)));
            EncryptionKeys.AddRange(options.EncryptionCertificates.Select(c => new X509SecurityKey(c)));
            Load(options.SigningCertificateFiles, SigningKeys);
            Load(options.EncryptionCertificateFiles, EncryptionKeys);
            if (options.EnableDevelopmentEphemeralKeys)
            {
                if (SigningKeys.Count == 0) SigningKeys.Add(Ephemeral());
                if (EncryptionKeys.Count == 0) EncryptionKeys.Add(Ephemeral());
            }
            if (SigningKeys.Count == 0 || EncryptionKeys.Count == 0)
                throw new InvalidOperationException("OpenIddict requires configured signing and encryption keys. Development ephemeral keys require explicit opt-in.");
            ValidateCertificateUsage(SigningKeys, X509KeyUsageFlags.DigitalSignature);
            ValidateCertificateUsage(EncryptionKeys, X509KeyUsageFlags.KeyEncipherment);
            foreach (var key in SigningKeys.Concat(EncryptionKeys))
            {
                if (key.KeySize < 2048 || !key.IsSupportedAlgorithm(SecurityAlgorithms.RsaSha256) ||
                    !key.IsSupportedAlgorithm(SecurityAlgorithms.RsaOAEP) ||
                    (key is RsaSecurityKey rsa && rsa.PrivateKeyStatus != PrivateKeyStatus.Exists) ||
                    (key is X509SecurityKey certificate && !certificate.Certificate.HasPrivateKey))
                    throw new InvalidOperationException("OpenIddict requires RSA private keys of at least 2048 bits for signing and encryption.");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static void ValidateCertificateUsage(IEnumerable<SecurityKey> keys, X509KeyUsageFlags requiredUsage)
    {
        foreach (var certificate in keys.OfType<X509SecurityKey>().Select(k => k.Certificate))
        {
            var usages = certificate.Extensions.OfType<X509KeyUsageExtension>().ToArray();
            if (certificate.Version >= 3 && usages.Length != 0 && !usages.Any(u => u.KeyUsages.HasFlag(requiredUsage)))
                throw new InvalidOperationException("OpenIddict certificate key usage does not permit its configured purpose.");
        }
    }

    private SecurityKey Ephemeral()
    {
        var rsa = RSA.Create(2048);
        _ownedRsa.Add(rsa);
        return new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
    }

    private void Load(IEnumerable<OpenIddictCertificateOptions> files, List<SecurityKey> keys)
    {
        foreach (var file in files)
        {
            X509Certificate2 certificate;
            try
            {
                certificate = X509CertificateLoader.LoadPkcs12FromFile(file.Path, file.Password, X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (Exception exception) when (exception is CryptographicException or System.IO.IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Exclude paths, passwords and provider exception payloads from diagnostics.
                throw new InvalidOperationException("Unable to load configured OpenIddict certificate.");
            }
            _ownedCertificates.Add(certificate);
            keys.Add(new X509SecurityKey(certificate));
        }
    }

    public void Dispose()
    {
        foreach (var certificate in _ownedCertificates) certificate.Dispose();
        foreach (var rsa in _ownedRsa) rsa.Dispose();
    }
}
