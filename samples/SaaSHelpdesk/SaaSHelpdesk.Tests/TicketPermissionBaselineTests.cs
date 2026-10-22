using System.Security.Claims;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.Security.Abstractions;
using Microsoft.EntityFrameworkCore;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;
using SaaSHelpdesk.EntityFrameworkCore;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// Issue #129 rev.2 14.2 permission controls. They separate three things the previous baseline
/// conflated: the permission directory, an actual grant, and what HTTP execution enforces.
/// C-prefix tests describe observed behaviour; F/R-prefix tests lock the frozen target.
/// </summary>
public class TicketPermissionBaselineTests : BaseTest
{
    private const string PlainUserPassword = "Agent123!";

    public TicketPermissionBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void F02_formal_ticket_permission_definitions_are_registered()
    {
        var manager = Factory.Services.GetRequiredService<IPermissionDefinitionManager>();

        foreach (var name in new[] { "Ticket.Create", "Ticket.Get", "Ticket.Search", "Ticket.Update", "Ticket.Delete" })
        {
            manager.GetOrNull(name).Should().NotBeNull(
                "rev.2 freezes exactly these five registered Ticket definitions, and the published Ticket " +
                "endpoints and the inherited CrestAppServiceBase checks reference names that no definition " +
                "provider declares today");
        }

        manager.GetOrNull("Ticket.GetAll").Should().BeNull(
            "Ticket.GetAll is a legacy snapshot name only; rev.2 forbids introducing it as a sixth formal definition");
    }

    [Fact]
    public async Task C11_formal_getall_requires_ticket_search_not_the_legacy_getall_name()
    {
        var userId = await CreatePlainUserAsync();
        var client = await SignInAsPlainUserAsync(userId);

        var withoutGrant = await client.GetAsync("/api/ticket/all");
        await AssertForbiddenAsync(withoutGrant, "Ticket.Search");

        await GrantAsync(userId, "Ticket.GetAll");
        var legacyGrantOnly = await client.GetAsync("/api/ticket/all");
        await AssertForbiddenAsync(legacyGrantOnly, "Ticket.Search",
            "Ticket.GetAll is not a formal definition and must not satisfy the formal endpoint or service gate");

        await GrantAsync(userId, "Ticket.Search");
        var searchGranted = await client.GetAsync("/api/ticket/all");
        searchGranted.StatusCode.Should().Be(HttpStatusCode.OK, await searchGranted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task C12_read_only_grant_cannot_list_tickets()
    {
        var userId = await CreatePlainUserAsync();
        var client = await SignInAsPlainUserAsync(userId);
        await GrantAsync(userId, "Ticket.Get");

        var response = await client.GetAsync("/api/ticket/all");

        await AssertForbiddenAsync(response, "Ticket.Search");
    }

    [Fact]
    public async Task C13_revocation_is_visible_on_the_next_request()
    {
        var userId = await CreatePlainUserAsync();
        var client = await SignInAsPlainUserAsync(userId);
        await GrantAsync(userId, "Ticket.Search");

        (await client.GetAsync("/api/ticket/all")).StatusCode.Should().Be(HttpStatusCode.OK);

        await RevokeAsync(userId, "Ticket.Search");

        var afterRevoke = await client.GetAsync("/api/ticket/all");
        await AssertForbiddenAsync(afterRevoke, "Ticket.Search",
            "the formal projection owns the gate and revocation must be visible on the next request");
    }

    [Fact]
    public async Task R14_search_grant_alone_must_not_perform_ticket_writes()
    {
        var ticketId = await SeedTicketAsync();
        var userId = await CreatePlainUserAsync();
        var client = await SignInAsPlainUserAsync(userId);
        await GrantAsync(userId, "Ticket.Search");

        var response = await client.GetAsync($"/api/ticket/close?id={ticketId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Assign/Resolve/Close are formal Command actions gated by Ticket.Update; a read-only grant must " +
            "not change ticket state");

        var persisted = await ReadTicketStatusAsync(ticketId);
        persisted.Should().Be(TicketStatus.Open, "a rejected write must not change the row");
    }

    [Theory]
    [InlineData("Resolve", false)]
    [InlineData("Resolve", true)]
    [InlineData("Close", false)]
    [InlineData("Close", true)]
    [InlineData("GetByCustomer", false)]
    [InlineData("GetByCustomer", true)]
    public async Task ReviewS3_direct_service_preserves_business_permission_gate(string action, bool allowed)
    {
        var ticketId = await SeedTicketAsync();
        var userId = await CreatePlainUserAsync();
        var client = await SignInAsPlainUserAsync(userId);
        var permission = action == "GetByCustomer" ? "Ticket.Get" : "Ticket.Update";
        if (allowed) await GrantAsync(userId, permission);
        else await GrantAsync(userId, "Ticket.Search");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var tenantId = (await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == "host")).Id.ToString();
        var customerId = (await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId)).CustomerId;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("tenantid", tenantId),
            new Claim("is_super_admin", "false"),
        }, "review-direct"));
        using var frame = scope.ServiceProvider.GetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
        using var tenantFrame = await scope.ServiceProvider
            .GetRequiredService<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>().ChangeAsync(tenantId);
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        (await checker.IsGrantedAsync(permission)).Should().Be(allowed,
            "the real permission service must establish the target condition before direct invocation");
        var service = scope.ServiceProvider.GetRequiredService<SaaSHelpdesk.Application.Contracts.Interfaces.ITicketAppService>();
        var failure = await Record.ExceptionAsync(async () =>
        {
            if (action == "Resolve") await service.ResolveAsync(ticketId);
            else if (action == "Close") await service.CloseAsync(ticketId);
            else await service.GetByCustomerAsync(customerId);
        });
        var persisted = await ReadTicketStatusAsync(ticketId);
        if (allowed)
        {
            failure.Should().BeNull();
            persisted.Should().Be(action == "Resolve" ? TicketStatus.Resolved
                : action == "Close" ? TicketStatus.Closed : TicketStatus.Open);
        }
        else
        {
            failure.Should().BeOfType<CrestPermissionException>(
                $"direct service execution must require {permission}; persisted status was {persisted}");
            persisted.Should().Be(TicketStatus.Open);
        }
    }

    private async Task AssertForbiddenAsync(HttpResponseMessage response, string expectedPermission, string? because = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because ?? body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("Crest.Auth.Forbidden", body);
        document.RootElement.GetProperty("details").ToString().Should().Contain(expectedPermission, body);
    }

    private async Task GrantAsync(string userId, string permissionName)
        => await Factory.Services.GetRequiredService<IPermissionGrantManager>().GrantAsync(new PermissionGrantInfo
        {
            PermissionName = permissionName,
            ProviderType = PermissionGrantProviderType.User,
            ProviderKey = userId,
            Scope = PermissionGrantScope.Global,
        });

    private async Task RevokeAsync(string userId, string permissionName)
        => await Factory.Services.GetRequiredService<IPermissionGrantManager>().RevokeAsync(new PermissionGrantInfo
        {
            PermissionName = permissionName,
            ProviderType = PermissionGrantProviderType.User,
            ProviderKey = userId,
            Scope = PermissionGrantScope.Global,
        });

    private async Task<string> CreatePlainUserAsync()
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tenantId = (await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == "host")).Id.ToString();

        var user = new User(Guid.NewGuid(), $"agent_{Guid.NewGuid():N}"[..18], $"agent_{Guid.NewGuid():N}@helpdesk.test", tenantId)
        {
            PasswordHash = hasher.HashPassword(PlainUserPassword),
            IsActive = true,
            IsSuperAdmin = false,
            CreationTime = DateTime.UtcNow,
            LastPasswordChangeTime = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id.ToString();
    }

    private async Task<Guid> SeedTicketAsync()
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var customerId = await HelpdeskTestData.CreateCustomerAsync(scopeFactory);
        var ticket = new Ticket(
            Guid.NewGuid(),
            "permission baseline ticket title",
            "permission baseline ticket description",
            TicketPriority.Medium,
            TicketType.Incident,
            customerId);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    private async Task<TicketStatus> ReadTicketStatusAsync(Guid ticketId)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId);
        ticket.Should().NotBeNull();
        return ticket!.Status;
    }

    private async Task<HttpClient> SignInAsPlainUserAsync(string userId)
    {
        var client = CreateTenantClient(HostTenantId);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = await UserNameAsync(userId),
            ["password"] = PlainUserPassword,
            ["client_id"] = "test-client",
            ["scope"] = "openid profile offline_access",
        }));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString());
        return client;
    }

    private async Task<string> UserNameAsync(string userId)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == Guid.Parse(userId))).UserName;
    }
}
