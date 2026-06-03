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

        // Assert — status counts
        summary.TotalTickets.Should().Be(5);
        summary.OpenTickets.Should().Be(1);
        summary.InProgressTickets.Should().Be(2);
        summary.ResolvedTickets.Should().Be(1);
        summary.ClosedTickets.Should().Be(1);
        summary.TicketsCreatedToday.Should().Be(5);

        // Overdue: no tickets have a past DueDate
        summary.OverdueTickets.Should().Be(0);

        // AverageResolutionHours should be > 0 since one ticket is resolved
        summary.AverageResolutionHours.Should().BeGreaterThan(0.0);

        // TicketsByPriority dictionary
        summary.TicketsByPriority.Should().NotBeNull();
        summary.TicketsByPriority["Low"].Should().Be(1);
        summary.TicketsByPriority["Medium"].Should().Be(2);
        summary.TicketsByPriority["High"].Should().Be(1);
        summary.TicketsByPriority["Urgent"].Should().Be(1);

        // AgentWorkloads — admin has 3 assigned (2 InProgress + 1 Resolved)
        summary.AgentWorkloads.Should().NotBeNull();
        summary.AgentWorkloads.Should().ContainSingle();
        summary.AgentWorkloads[0].AssignedTickets.Should().Be(3);
        summary.AgentWorkloads[0].ResolvedTickets.Should().Be(1);
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

    // ── Test helpers ──────────────────────────────────────────────────

    private async Task<Guid> CreateCustomerAsync(HttpClient client, string name, string email)
    {
        var response = await PostAsync(client, "/api/customer", new { name, email });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<CustomerDto>(response);
        return apiResponse.Data!.Id;
    }

    private async Task<Guid> CreateTicketAsync(
        HttpClient client,
        Guid customerId,
        string title,
        int priority)
    {
        var payload = new
        {
            title,
            description = "Automated test ticket description.",
            priority,
            type = 2, // Incident
            customerId
        };
        var response = await PostAsync(client, "/api/ticket", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<TicketDto>(response);
        return apiResponse.Data!.Id;
    }

    private async Task AssignTicketAsync(HttpClient client, Guid ticketId, Guid agentId)
    {
        var response = await PostAsync<object>(client,
            $"/api/ticket/{ticketId}/assign?agentId={agentId}", null!);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task ResolveTicketAsync(HttpClient client, Guid ticketId)
    {
        var response = await PostAsync<object>(client, $"/api/ticket/{ticketId}/resolve", null!);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task CloseTicketAsync(HttpClient client, Guid ticketId)
    {
        var response = await PostAsync<object>(client, $"/api/ticket/{ticketId}/close", null!);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
