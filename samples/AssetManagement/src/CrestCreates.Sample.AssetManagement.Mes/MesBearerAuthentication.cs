using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CrestCreates.Sample.AssetManagement.Mes;

internal sealed record MesCredential(string TenantId, byte[] TokenHash, ImmutableHashSet<string> Permissions);

internal static class MesPermissions
{
    public const string Trial = "mes.trial";
    public const string Release = "mes.release";
    public const string Consume = "mes.consume";
    public static readonly ImmutableHashSet<string> All = [Trial, Release, Consume];
}

internal sealed class MesTenantCredentials
{
    private readonly MesCredential[] _credentials;

    public MesTenantCredentials(IConfigurationSection tenants)
    {
        var credentials = new List<MesCredential>();
        foreach (var tenant in tenants.GetChildren())
        {
            var tenantId = tenant["TenantId"];
            var token = tenant["BearerToken"];
            var permissions = tenant["Permissions"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            if (string.IsNullOrWhiteSpace(tenantId) || tenantId != tenantId.Trim()
                || string.IsNullOrEmpty(token) || !token.All(IsBearerCharacter)
                || permissions.Length == 0 || permissions.Any(permission => !MesPermissions.All.Contains(permission))
                || permissions.Distinct(StringComparer.Ordinal).Count() != permissions.Length)
            {
                throw new InvalidOperationException("Each Mes:Tenants entry requires a TenantId, a valid BearerToken, and distinct supported Permissions.");
            }

            var tokenHash = Hash(token);
            if (credentials.Exists(credential => CryptographicOperations.FixedTimeEquals(credential.TokenHash, tokenHash)))
                throw new InvalidOperationException("Mes:Tenants bearer credentials must be unique.");

            credentials.Add(new MesCredential(tenantId, tokenHash, [.. permissions]));
        }

        if (credentials.Count == 0)
            throw new InvalidOperationException("Configure at least one Mes:Tenants bearer credential.");

        _credentials = credentials.ToArray();
    }

    public MesCredential? FindCredential(string token)
    {
        var hash = Hash(token);
        MesCredential? credential = null;
        foreach (var candidate in _credentials)
        {
            if (CryptographicOperations.FixedTimeEquals(candidate.TokenHash, hash))
                credential = candidate;
        }

        return credential;
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
    public const string PermissionClaim = "mes.permission";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization;
        if (header.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (header.Count != 1 || !AuthenticationHeaderValue.TryParse(header[0], out var authorization)
            || !string.Equals(authorization.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase)
            || authorization.Parameter is not { } token || credentials.FindCredential(token) is not { } credential)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid MES bearer credential."));
        }

        Claim[] claims = [new(TenantClaim, credential.TenantId), .. credential.Permissions.Select(permission => new Claim(PermissionClaim, permission))];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = SchemeName;
        return base.HandleChallengeAsync(properties);
    }
}
