using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.Integration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ErpManagement.Web.Integration;

public sealed class IntegrationApiKeyAuthenticationHandler(
    IOptionsMonitor<IntegrationApiKeyOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ITenantProvider tenants) : AuthenticationHandler<IntegrationApiKeyOptions>(options, logger, encoder)
{
    internal const string KeyHeader = "X-Integration-Api-Key";
    private const string MesDevelopmentKey = "mes-dev-key";
    private const string CrmDevelopmentKey = "crm-dev-key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(KeyHeader, out var values) || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0]))
            return InvalidCredentials();

        var key = values[0]!;
        if (!environment.IsDevelopment() && (key == MesDevelopmentKey || key == CrmDevelopmentKey))
            return InvalidCredentials();

        var clients = configuration.GetSection("Integration:Clients");
        var mes = clients.GetSection(ErpIntegrationClients.Mes);
        var crm = clients.GetSection(ErpIntegrationClients.Crm);
        var matchesMes = Matches(key, mes["ApiKey"]);
        var matchesCrm = Matches(key, crm["ApiKey"]);
        if (matchesMes == matchesCrm)
            return InvalidCredentials();

        var client = matchesMes ? mes : crm;
        var configuredTenant = client["TenantId"] ?? configuration["SeedIdentity:TenantId"] ?? "host";
        var tenant = await tenants.GetTenantAsync(configuredTenant, Context.RequestAborted);
        if (tenant is null || !Guid.TryParse(tenant.Id, out var tenantId) || tenantId == Guid.Empty)
            return InvalidCredentials();

        var identity = new ClaimsIdentity(
        [
            new Claim(ErpIntegrationClients.ClaimType, client.Key),
            new Claim("tenantid", tenant.Id),
            new Claim(ClaimTypes.Name, $"ERP integration ({client.Key})")
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private static bool Matches(string supplied, string? configured)
        => !string.IsNullOrWhiteSpace(configured) && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(configured));

    private static AuthenticateResult InvalidCredentials() => AuthenticateResult.Fail("Invalid integration credentials.");
}
