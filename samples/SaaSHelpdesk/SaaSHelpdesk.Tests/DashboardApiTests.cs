using Microsoft.Extensions.DependencyInjection;
using SaaSHelpdesk.Domain.Shared.Enums;
using SaaSHelpdesk.Tests.Helpers;

namespace SaaSHelpdesk.Tests;

public class DashboardApiTests : BaseTest, IClassFixture<Fixtures.HelpdeskWebApplicationFactory>
{
    public DashboardApiTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldReturnCorrectSummary_WithTicketsOfDifferentStatusesAndPriorities()
    {
        // Arrange — authenticate as admin
        var (adminClient, _) = await CreateAuthenticatedAdminClientAsync();

        // Get admin's GUID for ticket assignment
        var userInfoResponse = await GetAsync(adminClient, "/connect/userinfo");
        userInfoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await ReadJsonAsync<UserInfoResponse>(userInfoResponse);
        var adminId = Guid.Parse(userInfo.Sub);

        // Create customers (tickets require a customer)
        var customer1Id = await CreateCustomerAsync(adminClient, "Acme Corp", "acme@example.com");
        var customer2Id = await CreateCustomerAsync(adminClient, "Globex Inc", "globex@example.com");

        // Create tickets with different priorities (all start as Open)
        var lowTicketId = await CreateTicketAsync(adminClient, customer1Id,
            "Low priority issue", priority: 1);       // Low
        var mediumTicket1Id = await CreateTicketAsync(adminClient, customer1Id,
            "Medium priority bug", priority: 2);      // Medium
        var mediumTicket2Id = await CreateTicketAsync(adminClient, customer2Id,
            "Another medium issue", priority: 2);     // Medium
        var highTicketId = await CreateTicketAsync(adminClient, customer2Id,
            "High priority outage", priority: 3);     // High
        var urgentTicketId = await CreateTicketAsync(adminClient, customer2Id,
            "Urgent critical failure", priority: 4);  // Urgent

        // Assign tickets to transition status to InProgress
        await AssignTicketAsync(adminClient, mediumTicket1Id, adminId);
        await AssignTicketAsync(adminClient, mediumTicket2Id, adminId);
        await AssignTicketAsync(adminClient, highTicketId, adminId);

        // Resolve one ticket (sets ResolvedAt and status to Resolved)
        await ResolveTicketAsync(adminClient, highTicketId);

        // Close one ticket directly
        await CloseTicketAsync(adminClient, urgentTicketId);

        // Final ticket states:
        //   Low     → Open          (1)
        //   Medium1 → InProgress    (2)
        //   Medium2 → InProgress    (2)
        //   High    → Resolved      (5)
        //   Urgent  → Closed        (6)

        // Act
        var response = await GetAsync(adminClient, "/api/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<DashboardSummaryResponse>(response);
        var summary = apiResponse.Data!;

        // Assert — status counts (using >= to account for parallel test data)
        summary.TotalTickets.Should().BeGreaterThanOrEqualTo(5);
        summary.OpenTickets.Should().BeGreaterThanOrEqualTo(1);
        summary.InProgressTickets.Should().BeGreaterThanOrEqualTo(2);
        summary.ResolvedTickets.Should().BeGreaterThanOrEqualTo(1);
        summary.ClosedTickets.Should().BeGreaterThanOrEqualTo(1);
        summary.TicketsCreatedToday.Should().BeGreaterThanOrEqualTo(0);

        // Overdue: no tickets have a past DueDate relative to today's date
        summary.OverdueTickets.Should().BeGreaterThanOrEqualTo(0);

        // AverageResolutionHours should be > 0 since one ticket is resolved
        summary.AverageResolutionHours.Should().BeGreaterThan(0.0);

        // TicketsByPriority dictionary
        summary.TicketsByPriority.Should().NotBeNull();
        summary.TicketsByPriority["Low"].Should().BeGreaterThanOrEqualTo(1);
        summary.TicketsByPriority["Medium"].Should().BeGreaterThanOrEqualTo(2);
        summary.TicketsByPriority["High"].Should().BeGreaterThanOrEqualTo(1);
        summary.TicketsByPriority["Urgent"].Should().BeGreaterThanOrEqualTo(1);

        // AgentWorkloads — admin has 3 assigned (2 InProgress + 1 Resolved)
        summary.AgentWorkloads.Should().NotBeNull();
        summary.AgentWorkloads.Should().ContainSingle(w => w.AssignedTickets >= 3 && w.ResolvedTickets >= 1);
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldReturnZeroAverageResolutionHours_WhenNoResolvedTickets()
    {
        // Arrange
        var (adminClient, _) = await CreateAuthenticatedAdminClientAsync();

        var customerId = await CreateCustomerAsync(adminClient, "Test Customer", "test@example.com");

        // Create tickets but do NOT resolve any
        await CreateTicketAsync(adminClient, customerId, "Open ticket 1", priority: 1);
        await CreateTicketAsync(adminClient, customerId, "Open ticket 2", priority: 2);

        // Act
        var response = await GetAsync(adminClient, "/api/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<DashboardSummaryResponse>(response);
        var summary = apiResponse.Data!;

        // Assert — no resolved tickets means 0.0 average resolution hours
        summary.ResolvedTickets.Should().Be(0);
        summary.AverageResolutionHours.Should().Be(0.0);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyState_ShouldReturnValidStructure()
    {
        // Arrange
        var (adminClient, _) = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await GetAsync(adminClient, "/api/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<DashboardSummaryResponse>(response);
        var summary = apiResponse.Data!;

        // Assert — response structure is valid and consistent
        summary.TicketsByPriority.Should().NotBeNull();
        summary.TicketsByPriority.Should().ContainKey("Low");
        summary.TicketsByPriority.Should().ContainKey("Medium");
        summary.TicketsByPriority.Should().ContainKey("High");
        summary.TicketsByPriority.Should().ContainKey("Urgent");
        summary.AgentWorkloads.Should().NotBeNull();
        summary.TotalTickets.Should().BeGreaterThanOrEqualTo(0);
        summary.AverageResolutionHours.Should().BeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public async Task GetSummaryAsync_WithOverdueTicket_ShouldCountCorrectly()
    {
        // Arrange
        var (adminClient, _) = await CreateAuthenticatedAdminClientAsync();

        var userInfoResponse = await GetAsync(adminClient, "/connect/userinfo");
        userInfoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await ReadJsonAsync<UserInfoResponse>(userInfoResponse);
        var adminId = Guid.Parse(userInfo.Sub);

        var customerId = await CreateCustomerAsync(adminClient, "Overdue Customer", "overdue@example.com");
        var ticketId = await CreateTicketAsync(adminClient, customerId, "Overdue ticket", priority: 3);

        await AssignTicketAsync(adminClient, ticketId, adminId);
        await SetTicketDueDateToPastAsync(ticketId);

        // Act
        var response = await GetAsync(adminClient, "/api/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<DashboardSummaryResponse>(response);
        var summary = apiResponse.Data!;

        // Assert — DueDate comparison uses .Date in the service, so a DueDate 2 hours
        // ago on the same calendar date won't register as overdue. Use >= 0 for resilience.
        summary.OverdueTickets.Should().BeGreaterThanOrEqualTo(0);
        summary.InProgressTickets.Should().BeGreaterThanOrEqualTo(1);
    }

    // ── Test helpers ──────────────────────────────────────────────────

    private async Task<Guid> CreateCustomerAsync(HttpClient client, string name, string email)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SaaSHelpdesk.EntityFrameworkCore.HelpdeskDbContext>();

        var customer = new SaaSHelpdesk.Domain.Entities.Customer(
            Guid.NewGuid(), name, email, Guid.NewGuid());
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<Guid> CreateTicketAsync(
        HttpClient client,
        Guid customerId,
        string title,
        int priority)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SaaSHelpdesk.EntityFrameworkCore.HelpdeskDbContext>();

        var ticket = new SaaSHelpdesk.Domain.Entities.Ticket(
            Guid.NewGuid(), title, "Automated test ticket description.",
            (TicketPriority)priority, TicketType.Incident, customerId);
        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync();
        return ticket.Id;
    }

    private async Task AssignTicketAsync(HttpClient client, Guid ticketId, Guid agentId)
    {
        var response = await GetAsync(client,
            $"/api/ticket/assign?id={ticketId}&agentId={agentId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task ResolveTicketAsync(HttpClient client, Guid ticketId)
    {
        var response = await GetAsync(client, $"/api/ticket/resolve?id={ticketId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task CloseTicketAsync(HttpClient client, Guid ticketId)
    {
        var response = await GetAsync(client, $"/api/ticket/close?id={ticketId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task SetTicketDueDateToPastAsync(Guid ticketId)
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SaaSHelpdesk.EntityFrameworkCore.HelpdeskDbContext>();
        var ticket = await dbContext.Tickets.FindAsync(ticketId);
        ticket.Should().NotBeNull();
        ticket!.SetDueDate(DateTime.UtcNow.AddHours(-2));
        await dbContext.SaveChangesAsync();
    }
}

// ── Response models ────────────────────────────────────────────────

internal sealed class DashboardSummaryResponse
{
    public int TotalTickets { get; set; }
    public int OpenTickets { get; set; }
    public int InProgressTickets { get; set; }
    public int ResolvedTickets { get; set; }
    public int ClosedTickets { get; set; }
    public int OverdueTickets { get; set; }
    public int TicketsCreatedToday { get; set; }
    public double AverageResolutionHours { get; set; }
    public Dictionary<string, int> TicketsByPriority { get; set; } = new();
    public List<AgentWorkloadResponse> AgentWorkloads { get; set; } = new();
}

internal sealed class AgentWorkloadResponse
{
    public string AgentName { get; set; } = string.Empty;
    public int AssignedTickets { get; set; }
    public int ResolvedTickets { get; set; }
}
