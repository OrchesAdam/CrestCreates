using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using ServerOptions = OpenIddict.Server.OpenIddictServerOptions;

namespace CrestCreates.Web.Tests;

public class OpenIddictConfigurationTests
{
    [Fact]
    public async Task Production_host_cannot_start_without_keys()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Services.AddOpenIddictServer();
        await using var app = builder.Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
    }

    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", false, true)]
    [InlineData("Staging", false, true)]
    public void Development_optins_require_actual_development_environment(string environment, bool ephemeral, bool publicClient)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Services.AddOpenIddictServer(o =>
        {
            o.EnableDevelopmentEphemeralKeys = ephemeral;
            o.EnableDevelopmentPublicPasswordClient = publicClient;
        });
        using var provider = builder.Services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue);
        Assert.Contains("Development", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Module_and_extension_share_one_registration_in_both_orders(bool moduleFirst)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        var module = new OpenIddictModule();
        if (moduleFirst) module.OnConfigureServices(builder.Services);
        builder.Services.AddOpenIddictServer(o =>
        {
            o.EnableDevelopmentEphemeralKeys = true;
            o.Issuer = "https://issuer.example.test/";
            o.EnableClientCredentialsFlow = false;
        });
        if (!moduleFirst) module.OnConfigureServices(builder.Services);
        using var provider = builder.Services.BuildServiceProvider();
        var server = provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue;
        Assert.Single(server.SigningCredentials);
        Assert.Single(server.EncryptionCredentials);
        Assert.Equal(new Uri("https://issuer.example.test/"), server.Issuer);
        Assert.DoesNotContain(GrantTypes.ClientCredentials, server.GrantTypes);
        Assert.Contains(GrantTypes.AuthorizationCode, server.GrantTypes);
        Assert.Contains(ResponseTypes.Code, server.ResponseTypes);
        Assert.Single(builder.Services.Where(x => x.ServiceType == typeof(IDataSeeder) && x.ImplementationType == typeof(HostOpenIddictDataSeeder)));
    }

    [Fact]
    public void Development_without_optin_also_requires_keys()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Services.AddOpenIddictServer();
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue);
    }

    [Theory]
    [InlineData(true, X509KeyUsageFlags.KeyEncipherment)]
    [InlineData(false, X509KeyUsageFlags.DigitalSignature)]
    public void Certificate_key_usage_must_allow_its_configured_purpose(bool signing, X509KeyUsageFlags usage)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=wrong-usage-fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Services.AddOpenIddictServer(o =>
        {
            o.EnableDevelopmentEphemeralKeys = true;
            if (signing) o.SigningCertificates.Add(certificate);
            else o.EncryptionCertificates.Add(certificate);
        });
        using var provider = builder.Services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue);
        Assert.Contains("key usage", exception.Message);
    }

    [Fact]
    public void Certificate_loading_failure_does_not_disclose_path_or_provider_exception()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Services.AddOpenIddictServer(o => o.SigningCertificateFiles.Add(new OpenIddictCertificateOptions
        { Path = "/nonexistent/PRIVATE_CERTIFICATE_PATH.pfx", Password = "PRIVATE_CERTIFICATE_PASSWORD" }));
        using var provider = builder.Services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue);
        Assert.DoesNotContain("PRIVATE_", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Platform_configuration_loads_real_external_certificates_and_issuer()
    {
        using var signingRsa = RSA.Create(2048);
        using var encryptionRsa = RSA.Create(2048);
        using var signing = new CertificateRequest("CN=sign-fixture", signingRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3));
        using var encryption = new CertificateRequest("CN=encrypt-fixture", encryptionRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3));
        var signPath = Path.GetTempFileName();
        var encryptPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(signPath, signing.Export(X509ContentType.Pfx, "fixture-only"));
            File.WriteAllBytes(encryptPath, encryption.Export(X509ContentType.Pfx, "fixture-only"));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenIddict:Issuer"] = "https://platform.example.test/",
                ["OpenIddict:SigningCertificateFiles:0:Path"] = signPath,
                ["OpenIddict:SigningCertificateFiles:0:Password"] = "fixture-only",
                ["OpenIddict:EncryptionCertificateFiles:0:Path"] = encryptPath,
                ["OpenIddict:EncryptionCertificateFiles:0:Password"] = "fixture-only",
                ["OpenIddict:AccessTokenLifetimeMinutes"] = "17"
            });
            builder.AddCrestWeb();
            using var provider = builder.Services.BuildServiceProvider();
            var server = provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue;
            Assert.Equal(new Uri("https://platform.example.test/"), server.Issuer);
            Assert.Equal(TimeSpan.FromMinutes(17), server.AccessTokenLifetime);
            Assert.Equal(signing.Thumbprint, Assert.IsType<Microsoft.IdentityModel.Tokens.X509SecurityKey>(Assert.Single(server.SigningCredentials).Key).Certificate.Thumbprint);
            Assert.Equal(encryption.Thumbprint, Assert.IsType<Microsoft.IdentityModel.Tokens.X509SecurityKey>(Assert.Single(server.EncryptionCredentials).Key).Certificate.Thumbprint);
        }
        finally
        {
            File.Delete(signPath);
            File.Delete(encryptPath);
        }
    }

    [Theory]
    [InlineData("Production", false, false)]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    public async Task Public_password_client_is_seeded_only_by_development_optin(string environment, bool enabled, bool created)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        var applications = new Mock<IOpenIddictApplicationManager>();
        var scopes = new Mock<IOpenIddictScopeManager>();
        scopes.Setup(x => x.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new object());
        OpenIddictApplicationDescriptor? descriptor = null;
        applications.Setup(x => x.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((d, _) => descriptor = d).ReturnsAsync(new object());
        var seeder = new HostOpenIddictDataSeeder(applications.Object, scopes.Object, NullLogger<HostOpenIddictDataSeeder>.Instance,
            new OpenIddictServerOptions { EnableDevelopmentPublicPasswordClient = enabled }, builder.Environment);
        await seeder.SeedAsync();
        Assert.Equal(created, descriptor is not null);
        if (descriptor is not null)
        {
            Assert.Equal(ClientTypes.Public, descriptor.ClientType);
            Assert.Contains(Permissions.GrantTypes.Password, descriptor.Permissions);
            Assert.DoesNotContain(Permissions.GrantTypes.ClientCredentials, descriptor.Permissions);
        }
    }

    [Fact]
    public async Task Seeder_rejects_production_public_client_before_any_store_call()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        var applications = new Mock<IOpenIddictApplicationManager>(MockBehavior.Strict);
        var scopes = new Mock<IOpenIddictScopeManager>(MockBehavior.Strict);
        var seeder = new HostOpenIddictDataSeeder(applications.Object, scopes.Object, NullLogger<HostOpenIddictDataSeeder>.Instance,
            new OpenIddictServerOptions { EnableDevelopmentPublicPasswordClient = true }, builder.Environment);
        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());
        applications.VerifyNoOtherCalls();
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public void Production_without_explicit_keys_fails_closed()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Services.AddOpenIddictServer();
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue);
    }
}
