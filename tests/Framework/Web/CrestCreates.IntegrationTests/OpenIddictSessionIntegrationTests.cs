using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using System.Text.Json;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Security.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Testcontainers.PostgreSql;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CrestCreates.IntegrationTests;

// Real Kestrel, OpenIddict managers and PostgreSQL store. Identity repository leaves are test doubles;
// this does not establish EF identity repository / tenant middleware / password lockout race coverage.
public class OpenIddictSessionIntegrationTests
{
    [Fact]
    public async Task Persistent_keys_refresh_rotation_and_session_logout_use_the_official_chain()
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await database.StartAsync();
        using var signingRsa = RSA.Create(2048);
        using var encryptionRsa = RSA.Create(2048);
        using var nextSigningRsa = RSA.Create(2048);
        using var nextEncryptionRsa = RSA.Create(2048);
        var signing = new RsaSecurityKey(signingRsa) { KeyId = "sign-old" };
        var encryption = new RsaSecurityKey(encryptionRsa) { KeyId = "encrypt-old" };
        var nextSigning = new RsaSecurityKey(nextSigningRsa) { KeyId = "sign-new" };
        var nextEncryption = new RsaSecurityKey(nextEncryptionRsa) { KeyId = "encrypt-new" };
        var user = new User(Guid.NewGuid(), "alice", "alice@example.test", "tenant-a")
        { IsActive = true, IsSuperAdmin = true, PasswordHash = "fixture-hash" };
        var roles = new List<Role> { new(Guid.NewGuid(), "old-admin", "tenant-a") };
        var connection = database.GetConnectionString();
        var calls = new IdentityCalls();
        await using var hostA = await StartHost(connection, user, roles, new[] { signing }, new[] { encryption }, initialize: true, calls: calls);
        using var clientA = Client(hostA);
        using (var publicAttempt = await clientA.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "password", ["client_id"] = "fixture-public", ["username"] = "alice", ["password"] = "fixture-password" })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, publicAttempt.StatusCode);
            using var error = JsonDocument.Parse(await publicAttempt.Content.ReadAsStringAsync());
            Assert.Equal(Errors.UnauthorizedClient, error.RootElement.GetProperty("error").GetString());
            Assert.Equal(0, calls.UserReads);
        }
        var original = await Password(clientA);
        Assert.Equal("sign-old", new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(original.Access).Kid);
        var untouchedFamily = await Password(clientA);
        Assert.Equal(HttpStatusCode.OK, (await Probe(clientA, original.Access)).StatusCode);

        await using (var hostB = await StartHost(connection, user, roles, new[] { signing }, new[] { encryption }))
        {
            using var clientB = Client(hostB);
            Assert.Equal(HttpStatusCode.OK, (await Probe(clientB, original.Access)).StatusCode); // T01
            var crossHost = await Refresh(clientB, original.Refresh);
            Assert.Equal(HttpStatusCode.OK, crossHost.StatusCode);
            original = await Tokens(crossHost);
        }
        await hostA.StopAsync();
        await using (var wrongKeys = await StartHost(connection, user, roles, new[] { nextSigning }, new[] { nextEncryption }))
        {
            using var negative = Client(wrongKeys);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Probe(negative, original.Access)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(negative, original.Refresh)).StatusCode);
        }
        await using var restarted = await StartHost(connection, user, roles,
            new[] { nextSigning, signing }, new[] { nextEncryption, encryption });
        using var client = Client(restarted);
        Assert.Equal(HttpStatusCode.OK, (await Probe(client, original.Access)).StatusCode); // T02/T06 old signing key
        var rotated = await Refresh(client, original.Refresh); // old encryption key survives rotation
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        original = await Tokens(rotated);
        Assert.Equal("sign-new", new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(original.Access).Kid);
        Assert.Equal(HttpStatusCode.OK, (await Probe(client, original.Access)).StatusCode);

        user.IsActive = false;
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(client, original.Refresh)).StatusCode); // T04
        user.IsActive = true;
        user.IsSuperAdmin = false;
        user.OrganizationId = Guid.NewGuid();
        roles.Clear();
        roles.Add(new Role(Guid.NewGuid(), "reader", "tenant-a"));
        var changed = await Refresh(client, original.Refresh);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var fresh = await Tokens(changed);
        using (var response = await Probe(client, fresh.Access))
        {
            using var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("false", claims.RootElement.GetProperty("superAdmin").GetString());
            Assert.Equal(new[] { "reader" }, claims.RootElement.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
            Assert.Equal(user.OrganizationId.ToString(), claims.RootElement.GetProperty("organization").GetString());
        } // T05
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/connect/logout", null)).StatusCode);
        using (var get = new HttpRequestMessage(HttpMethod.Get, "/connect/logout"))
        {
            get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh.Access);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(get)).StatusCode);
        }
        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/connect/logout?userId=" + Guid.NewGuid()))
        {
            logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh.Access);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(logout)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(client, fresh.Refresh)).StatusCode); // T03
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, untouchedFamily.Refresh)).StatusCode);
        // Local validation does not enable token-entry validation: already issued access remains usable.
        Assert.Equal(HttpStatusCode.OK, (await Probe(client, fresh.Access)).StatusCode);
    }

    [Fact]
    public async Task Mixed_certificate_RSA_rotation_rejects_expired_issuance_and_retains_historical_decryption()
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await database.StartAsync();
        using var oldRsa = RSA.Create(2048);
        using var currentRsa = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;
        using var expired = new CertificateRequest("CN=historical-fixture", oldRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(now.AddHours(-3), now.AddHours(-1));
        using var current = new CertificateRequest("CN=current-fixture", currentRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(now.AddDays(-1), now.AddDays(3));
        using var future = new CertificateRequest("CN=future-fixture", currentRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(now.AddDays(1), now.AddDays(4));
        var oldKey = new X509SecurityKey(expired);
        var currentKey = new X509SecurityKey(current);
        var futureKey = new X509SecurityKey(future);
        var rsaKey = new RsaSecurityKey(currentRsa) { KeyId = "mixed-current-rsa" };
        var user = new User(Guid.NewGuid(), "alice", "alice@example.test", "tenant-a")
        { IsActive = true, PasswordHash = "fixture-hash" };
        var roles = new List<Role>();
        var connection = database.GetConnectionString();
        (string Access, string Refresh) original;
        // Issue the historical artifact while the very same certificate was valid; no wall-clock sleeps.
        await using (var oldHost = await StartHost(connection, user, roles, new SecurityKey[] { rsaKey },
            new SecurityKey[] { oldKey }, initialize: true, timeProvider: new FixtureTimeProvider(now.AddHours(-2))))
        {
            using var client = Client(oldHost);
            original = await Password(client);
            Assert.Equal(oldKey.KeyId, new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(original.Refresh).Kid);
        }
        foreach (var signingExpired in new[] { true, false })
        {
            var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => StartHost(connection, user, roles,
                signingExpired ? new SecurityKey[] { rsaKey, oldKey } : new SecurityKey[] { rsaKey },
                signingExpired ? new SecurityKey[] { rsaKey } : new SecurityKey[] { rsaKey, oldKey }));
            Assert.Equal($"OpenIddict selected {(signingExpired ? "signing" : "encryption")} certificate is outside its validity period.",
                Assert.Single(exception.Failures));
        }
        await using (var futureHost = await StartHost(connection, user, roles, new SecurityKey[] { futureKey, rsaKey },
            new SecurityKey[] { futureKey, rsaKey }))
        {
            using var client = Client(futureHost);
            var tokens = await Password(client);
            Assert.Equal(rsaKey.KeyId, new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(tokens.Access).Kid);
            Assert.Equal(rsaKey.KeyId, new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(tokens.Refresh).Kid);
        }
        await using var rotated = await StartHost(connection, user, roles, new SecurityKey[] { rsaKey, currentKey },
            new SecurityKey[] { rsaKey, oldKey, currentKey });
        using var rotatedClient = Client(rotated);
        using var response = await Refresh(rotatedClient, original.Refresh);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await Tokens(response);
        Assert.Equal(currentKey.KeyId, new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(refreshed.Access).Kid);
        Assert.Equal(currentKey.KeyId, new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(refreshed.Refresh).Kid);
        Assert.Equal(HttpStatusCode.OK, (await Probe(rotatedClient, refreshed.Access)).StatusCode);
        var options = rotated.Services.GetRequiredService<IOptionsMonitor<global::OpenIddict.Server.OpenIddictServerOptions>>().CurrentValue;
        Assert.Contains(oldKey, options.TokenValidationParameters.TokenDecryptionKeys);
    }

    private sealed class FixtureTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Refresh_rejects_changed_user_tenant_and_request_tenant_but_allows_host_in_host_context()
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await database.StartAsync();
        using var signingRsa = RSA.Create(2048);
        using var encryptionRsa = RSA.Create(2048);
        SecurityKey[] signing = { new RsaSecurityKey(signingRsa) { KeyId = "boundary-sign" } };
        SecurityKey[] encryption = { new RsaSecurityKey(encryptionRsa) { KeyId = "boundary-encrypt" } };
        var user = new User(Guid.NewGuid(), "alice", "alice@example.test", "tenant-a")
        { IsActive = true, PasswordHash = "fixture-hash" };
        var roles = new List<Role>();
        var connection = database.GetConnectionString();
        await using var tenantA = await StartHost(connection, user, roles, signing, encryption, initialize: true);
        using var clientA = Client(tenantA);
        var token = await Password(clientA);
        user.TenantId = "tenant-b";
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(clientA, token.Refresh)).StatusCode);
        user.TenantId = "tenant-a";
        await using var tenantB = await StartHost(connection, user, roles, signing, encryption, currentTenant: "tenant-b");
        using var clientB = Client(tenantB);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(clientB, token.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(clientA, token.Refresh)).StatusCode);

        var hostUser = new User(Guid.NewGuid(), "alice", "host@example.test", null!)
        { IsActive = true, PasswordHash = "fixture-hash" };
        await using var host = await StartHost(connection, hostUser, roles, signing, encryption, currentTenant: null);
        using var hostClient = Client(host);
        var hostToken = await Password(hostClient);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(hostClient, hostToken.Refresh)).StatusCode);
        var secondHostToken = await Password(hostClient);
        await using var hostInTenant = await StartHost(connection, hostUser, roles, signing, encryption, currentTenant: "tenant-b");
        using var hostInTenantClient = Client(hostInTenant);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(hostInTenantClient, secondHostToken.Refresh)).StatusCode);
    }

    private static async Task<WebApplication> StartHost(string connection, User user, List<Role> roles,
        SecurityKey[] signing, SecurityKey[] encryption, bool initialize = false, IdentityCalls? calls = null,
        string? currentTenant = "tenant-a", TimeProvider? timeProvider = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        if (timeProvider is not null) builder.Services.AddSingleton(timeProvider);
        builder.Services.AddDbContext<OpenIddictDbContext>(o => o.UseNpgsql(connection));
        builder.Services.AddOpenIddictServer(o =>
        {
            o.Issuer = "https://auth.example.test/";
            o.SigningKeys.AddRange(signing);
            o.EncryptionKeys.AddRange(encryption);
        });
        // HTTP is restricted to this loopback fixture; production keeps OpenIddict's HTTPS requirement.
        builder.Services.AddOpenIddict().AddServer(o => o.UseAspNetCore().DisableTransportSecurityRequirement());
        builder.Services.AddOpenIddictAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IIdentityClaimsBuilder, IdentityClaimsBuilder>();
        var users = new Mock<IUserRepository>();
        users.Setup(x => x.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .Callback(() => { if (calls is not null) calls.UserReads++; }).ReturnsAsync(user);
        users.Setup(x => x.GetAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        builder.Services.AddSingleton(users.Object);
        var roleRepository = new Mock<IRoleRepository>();
        roleRepository.Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => roles.ToList());
        builder.Services.AddSingleton(roleRepository.Object);
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(x => x.VerifyPassword("fixture-hash", "fixture-password")).Returns(true);
        builder.Services.AddSingleton(hasher.Object);
        builder.Services.AddSingleton(Mock.Of<IIdentitySecurityLogWriter>());
        builder.Services.AddScoped<ICurrentTenant>(sp =>
        {
            var tenant = new Mock<ICurrentTenant>();
            tenant.SetupGet(x => x.Id).Returns(currentTenant);
            return tenant.Object;
        });
        var app = builder.Build();
        if (initialize)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "fixture-client", ClientType = ClientTypes.Confidential, ClientSecret = "fixture-client-secret",
                Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.Password,
                    Permissions.GrantTypes.RefreshToken, Permissions.Prefixes.Scope + Scopes.Profile }
            });
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "fixture-public", ClientType = ClientTypes.Public,
                Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.Password }
            });
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCrestOpenIddictEndpoints();
        app.MapGet("/probe", async (HttpContext context) =>
        {
            var result = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            if (!result.Succeeded) return Results.Unauthorized();
            return Results.Ok(new { roles = result.Principal!.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray(),
                superAdmin = result.Principal.FindFirst("is_super_admin")?.Value,
                organization = result.Principal.FindFirst("org_id")?.Value });
        });
        try
        {
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class IdentityCalls { public int UserReads; }

    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };
    private static async Task<(string Access, string Refresh)> Password(HttpClient client)
    {
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "password", ["client_id"] = "fixture-client", ["client_secret"] = "fixture-client-secret", ["username"] = "alice",
            ["password"] = "fixture-password", ["scope"] = "offline_access profile" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Tokens(response);
    }
    private static Task<HttpResponseMessage> Refresh(HttpClient client, string refresh) => client.PostAsync("/connect/token",
        new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token",
            ["client_id"] = "fixture-client", ["client_secret"] = "fixture-client-secret", ["refresh_token"] = refresh }));
    private static async Task<(string Access, string Refresh)> Tokens(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("access_token").GetString()!, json.RootElement.GetProperty("refresh_token").GetString()!);
    }
    private static Task<HttpResponseMessage> Probe(HttpClient client, string access)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return client.SendAsync(request);
    }
}
