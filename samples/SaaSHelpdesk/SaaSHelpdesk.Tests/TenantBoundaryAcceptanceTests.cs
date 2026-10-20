using CrestCreates.Domain.Permission;
using CrestCreates.Security.Abstractions;
using Microsoft.EntityFrameworkCore;
using SaaSHelpdesk.EntityFrameworkCore;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 H04: unknown, disabled and mismatched tenants are rejected by the existing tenant
/// middleware/boundary before any business service or write transaction runs. Database selection
/// itself is proven separately by <see cref="DualTenantDatabaseIsolationTests"/>, which keeps the
/// production DbContext options factory instead of the fixture's shared connection.
/// </summary>
public class TenantBoundaryAcceptanceTests : BaseTest
{
    private const string PlainUserPassword = "Tenant123!";

    public TenantBoundaryAcceptanceTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task H04_unknown_tenant_is_rejected_before_business_and_persists_nothing()
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await HelpdeskTestData.CreateCustomerAsync(
            Factory.Services.GetRequiredService<IServiceScopeFactory>());

        var accepted = await PostTicketAsync(client, customerId);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK,
            "the payload is otherwise valid, so the unknown-tenant rejection is the only variable");

        var before = await CountTicketsAsync();
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await PostTicketAsync(client, customerId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountTicketsAsync()).Should().Be(before,
            "an unknown tenant must be rejected before the business service or its unit of work");
    }

    [Fact]
    public async Task H04_disabled_tenant_is_rejected_before_business_and_persists_nothing()
    {
        var tenantName = "disabled_" + Guid.NewGuid().ToString("N")[..12];
        await CreateTenantAsync(tenantName, isActive: false);
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await HelpdeskTestData.CreateCustomerAsync(
            Factory.Services.GetRequiredService<IServiceScopeFactory>());
        var before = await CountTicketsAsync();
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantName);

        var response = await PostTicketAsync(client, customerId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountTicketsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task H04_authenticated_user_cannot_cross_into_another_tenant()
    {
        var tenantName = "active_" + Guid.NewGuid().ToString("N")[..12];
        await CreateTenantAsync(tenantName, isActive: true);
        var userName = "plain_" + Guid.NewGuid().ToString("N")[..12];
        await CreatePlainHostUserAsync(userName);
        var (client, _) = await CreateAuthenticatedClientAsync(userName, PlainUserPassword, HostTenantId);
        var customerId = await HelpdeskTestData.CreateCustomerAsync(
            Factory.Services.GetRequiredService<IServiceScopeFactory>());
        var before = await CountTicketsAsync();
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantName);

        var response = await PostTicketAsync(client, customerId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("Message").GetString().Should().Be(
            "当前用户无权访问该租户上下文",
            "the tenant boundary must reject the cross-tenant request, not defer to capability authorization");
        (await CountTicketsAsync()).Should().Be(before);
    }

    private async Task<HttpResponseMessage> PostTicketAsync(HttpClient client, Guid customerId)
        => await client.PostAsync("/api/ticket", new StringContent(
            $$"""
              {"title":"tenant boundary ticket","description":"tenant boundary description",
               "priority":2,"type":2,"customerId":"{{customerId}}"}
              """,
            Encoding.UTF8,
            "application/json"));

    private async Task CreateTenantAsync(string name, bool isActive)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var tenant = new Tenant(Guid.NewGuid(), name)
        {
            IsActive = isActive,
            LifecycleState = isActive ? TenantLifecycleState.Active : TenantLifecycleState.Suspended,
            CreationTime = DateTime.UtcNow,
        };
        db.Tenants.Add(tenant);
        db.TenantConnectionStrings.Add(new TenantConnectionString(
            Guid.NewGuid(), tenant.Id, TenantConnectionString.DefaultName, Factory.ConnectionString));
        await db.SaveChangesAsync();
    }

    private async Task CreatePlainHostUserAsync(string userName)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tenantId = (await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == "host")).Id.ToString();
        db.Users.Add(new User(Guid.NewGuid(), userName, userName + "@helpdesk.test", tenantId)
        {
            PasswordHash = hasher.HashPassword(PlainUserPassword),
            IsActive = true,
            IsSuperAdmin = false,
            CreationTime = DateTime.UtcNow,
            LastPasswordChangeTime = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> CountTicketsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        return await db.Tickets.CountAsync();
    }
}
