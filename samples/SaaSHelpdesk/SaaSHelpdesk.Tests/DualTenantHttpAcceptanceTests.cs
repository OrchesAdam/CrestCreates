using System.Net.Http.Headers;
using CrestCreates.Domain.Permission;
using CrestCreates.Security.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;
using SaaSHelpdesk.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 S3-R1-HTTP129-01: the real Program pipeline (routing, tenant resolution, authentication,
/// capability projection, AppService and the unique unit of work) against two schemas of one
/// PostgreSQL database. Unlike the shared-connection fixture this host keeps the production
/// AddDbContext options contributor, so the tenant connection is selected from the resolved tenant.
/// </summary>
public sealed class DualTenantHttpAcceptanceTests : IAsyncLifetime
{
    private const string TenantName = "tenant-b";
    private const string TenantPassword = "Tenant123!";
    private const string TenantUserName = "tenant-admin";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("helpdesk_dual_http")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private WebApplicationFactory<global::Program> _factory = null!;
    private string _hostConnection = null!;
    private string _tenantConnection = null!;
    private Guid _tenantId;
    private Guid _tenantCustomerId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var baseConnection = _postgres.GetConnectionString();
        var hostSchema = "http_host_" + Guid.NewGuid().ToString("N")[..10];
        var tenantSchema = "http_tenant_" + Guid.NewGuid().ToString("N")[..10];
        CreateSchema(baseConnection, hostSchema);
        CreateSchema(baseConnection, tenantSchema);
        _hostConnection = baseConnection + ";Search Path=" + hostSchema;
        _tenantConnection = baseConnection + ";Search Path=" + tenantSchema;

        _factory = new WebApplicationFactory<global::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _hostConnection,
                        ["CrestLogging:EnableFile"] = "false",
                        ["AuditLogging:IsEnabledForGetRequests"] = "false",
                        ["SeedIdentity:TenantId"] = "host",
                    }));
            });

        // Starts the real host (module initialization, migrations, host seeding) against the host schema.
        using (var warmup = _factory.CreateClient())
        {
            var health = await warmup.GetAsync("/health");
            health.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _tenantId = Guid.NewGuid();
        await CreateTenantSchemaAsync();
        await RegisterTenantAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task H04_http_requests_select_the_resolved_tenant_and_isolate_reads_and_writes()
    {
        var tenantClient = await SignInTenantAdminAsync();
        var payload = $$"""
            {"title":"tenant b http ticket","description":"tenant b http ticket description",
             "priority":2,"type":2,"customerId":"{{_tenantCustomerId}}"}
            """;
        var created = await tenantClient.PostAsync("/api/ticket", new StringContent(
            payload, Encoding.UTF8, "application/json"));
        var createdBody = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.OK, createdBody);
        var ticketId = Guid.Parse(JsonDocument.Parse(createdBody)
            .RootElement.GetProperty("data").GetProperty("id").GetString()!);

        (await CountByTitleAsync(_tenantConnection, "tenant b http ticket")).Should().Be(1);
        (await CountByTitleAsync(_hostConnection, "tenant b http ticket")).Should().Be(0,
            "a tenant B write must never land in the host schema");

        var tenantList = await tenantClient.GetAsync("/api/ticket/all");
        tenantList.StatusCode.Should().Be(HttpStatusCode.OK);
        (await tenantList.Content.ReadAsStringAsync()).Should().Contain("tenant b http ticket");

        var hostClient = await SignInHostAdminAsync();
        var hostList = await hostClient.GetAsync("/api/ticket/all");
        hostList.StatusCode.Should().Be(HttpStatusCode.OK);
        (await hostList.Content.ReadAsStringAsync()).Should().NotContain("tenant b http ticket",
            "the host schema must not observe tenant B rows");

        // Missing precondition and stale stamp are rejected without touching the tenant row.
        var missingStamp = await tenantClient.DeleteAsync($"/api/ticket/{ticketId}");
        missingStamp.StatusCode.Should().Be((HttpStatusCode)428);
        var persisted = await ReadStampAsync(_tenantConnection, ticketId);
        persisted.Exists.Should().BeTrue();
        var stale = await tenantClient.PutAsync($"/api/ticket/{ticketId}", new StringContent(
            $$"""
              {"title":"stale tenant update","description":"stale tenant update description",
               "priority":2,"type":2,"categoryId":null,"concurrencyStamp":"00000000-0000-0000-0000-000000000000"}
              """,
            Encoding.UTF8,
            "application/json"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        persisted = await ReadStampAsync(_tenantConnection, ticketId);
        persisted.Title.Should().Be("tenant b http ticket");
        persisted.Exists.Should().BeTrue();

        // A provider-level failure after the in-memory write rolls the tenant transaction back.
        var stampBeforeFailure = persisted.Stamp;
        var invalidCategory = await tenantClient.PutAsync($"/api/ticket/{ticketId}", new StringContent(
            $$"""
              {"title":"failed tenant update","description":"failed tenant update description",
               "priority":2,"type":2,"categoryId":"{{Guid.NewGuid()}}","concurrencyStamp":"{{stampBeforeFailure}}"}
              """,
            Encoding.UTF8,
            "application/json"));
        invalidCategory.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        persisted = await ReadStampAsync(_tenantConnection, ticketId);
        persisted.Title.Should().Be("tenant b http ticket");
        persisted.Stamp.Should().Be(stampBeforeFailure,
            "the failed commit must roll back the tenant transaction and leave the stored stamp untouched");
    }

    private async Task<HttpClient> SignInTenantAdminAsync()
        => await SignInAsync(TenantUserName, TenantPassword, TenantName);

    private async Task<HttpClient> SignInHostAdminAsync()
        => await SignInAsync("admin", "Admin123!", "host");

    private async Task<HttpClient> SignInAsync(string userName, string password, string tenant)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = userName,
            ["password"] = password,
            ["client_id"] = "crestcreates-client",
            ["scope"] = "openid profile offline_access",
        }));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString());
        return client;
    }

    private async Task CreateTenantSchemaAsync()
    {
        await using var tenantDb = new HelpdeskDbContext(OptionsFor(_tenantConnection));
        await tenantDb.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tenantKey = _tenantId.ToString();
        var role = new Role(Guid.NewGuid(), "Administrators", tenantKey)
        {
            DisplayName = "Administrators",
            IsActive = true,
            CreationTime = DateTime.UtcNow,
        };
        var user = new User(Guid.NewGuid(), TenantUserName, TenantUserName + "@helpdesk.test", tenantKey)
        {
            PasswordHash = hasher.HashPassword(TenantPassword),
            IsActive = true,
            IsSuperAdmin = true,
            CreationTime = DateTime.UtcNow,
            LastPasswordChangeTime = DateTime.UtcNow,
        };
        tenantDb.Roles.Add(role);
        tenantDb.Users.Add(user);
        tenantDb.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id, tenantKey));
        _tenantCustomerId = Guid.NewGuid();
        tenantDb.Customers.Add(new Customer(_tenantCustomerId, "Tenant HTTP Customer", "tenant-http@test.local", _tenantId));
        await tenantDb.SaveChangesAsync();
    }

    private async Task RegisterTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var hostDb = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        hostDb.Tenants.Add(new Tenant(_tenantId, TenantName)
        {
            DisplayName = TenantName,
            IsActive = true,
            LifecycleState = TenantLifecycleState.Active,
            CreationTime = DateTime.UtcNow,
        });
        hostDb.TenantConnectionStrings.Add(new TenantConnectionString(
            Guid.NewGuid(), _tenantId, TenantConnectionString.DefaultName, _tenantConnection));
        await hostDb.SaveChangesAsync();
    }

    private static async Task<int> CountByTitleAsync(string connectionString, string title)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "Tickets" WHERE "Title" = @title;""";
        command.Parameters.AddWithValue("title", title);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<(bool Exists, string Title, string Stamp)> ReadStampAsync(
        string connectionString,
        Guid ticketId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Title", "ConcurrencyStamp" FROM "Tickets" WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", ticketId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (true, reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1))
            : (false, string.Empty, string.Empty);
    }

    private static void CreateSchema(string connectionString, string schema)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        command.ExecuteNonQuery();
    }

    private static DbContextOptions<HelpdeskDbContext> OptionsFor(string connectionString)
        => new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseNpgsql(connectionString)
            .Options;
}
