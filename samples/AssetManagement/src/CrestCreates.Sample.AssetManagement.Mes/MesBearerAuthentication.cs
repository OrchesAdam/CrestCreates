using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CrestCreates.Sample.AssetManagement.Mes;

internal sealed class MesTenantCredentials
{
    private readonly (string TenantId, byte[] TokenHash)[] _credentials;

    public MesTenantCredentials(IConfigurationSection tenants)
    {
        var credentials = new List<(string TenantId, byte[] TokenHash)>();
        foreach (var tenant in tenants.GetChildren())
        {
            var tenantId = tenant["TenantId"];
            var token = tenant["BearerToken"];
            if (string.IsNullOrWhiteSpace(tenantId) || tenantId != tenantId.Trim()
                || string.IsNullOrEmpty(token) || !token.All(IsBearerCharacter))
            {
                throw new InvalidOperationException("Each Mes:Tenants entry requires a TenantId and a valid BearerToken.");
            }

            var tokenHash = Hash(token);
            if (credentials.Exists(credential => CryptographicOperations.FixedTimeEquals(credential.TokenHash, tokenHash)))
                throw new InvalidOperationException("Mes:Tenants bearer credentials must be unique.");

            credentials.Add((tenantId, tokenHash));
        }

        if (credentials.Count == 0)
            throw new InvalidOperationException("Configure at least one Mes:Tenants bearer credential.");

        _credentials = credentials.ToArray();
    }

    public string? FindTenant(string token)
    {
        var hash = Hash(token);
        string? tenantId = null;
        foreach (var credential in _credentials)
        {
            if (CryptographicOperations.FixedTimeEquals(credential.TokenHash, hash))
                tenantId = credential.TenantId;
        }

        return tenantId;
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static bool IsBearerCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/' or '=';
}

internal sealed class MesBearerAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    MesTenantCredentials credentials) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Bearer";
    public const string TenantClaim = "tenantid";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization;
        if (header.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (header.Count != 1 || !AuthenticationHeaderValue.TryParse(header[0], out var authorization)
            || !string.Equals(authorization.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase)
            || authorization.Parameter is not { } token || credentials.FindTenant(token) is not { } tenantId)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid MES bearer credential."));
        }

        var identity = new ClaimsIdentity([new Claim(TenantClaim, tenantId)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = SchemeName;
        return base.HandleChallengeAsync(properties);
    }
}
