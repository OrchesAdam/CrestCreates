using SaaSHelpdesk.Tests.Helpers;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// End-to-end tests for the Ticket Dynamic API endpoints.
/// Covers all TicketAppService methods including CRUD, assignment, resolution, closure,
/// and query operations. Validates both success paths and error branches.
/// </summary>
public class TicketApiTests : BaseTest
{
    public TicketApiTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    // ── Test data helpers ────────────────────────────────────────────

    private async Task<Guid> CreateTestCustomerAsync(HttpClient client)
    {
        var payload = new
        {
            name = $"Test Customer {Guid.NewGuid():N}"[..20],
            email = $"customer_{Guid.NewGuid():N}@test.com",
            tenantId = Guid.NewGuid(),
            isActive = true
        };

        var response = await PostAsync(client, "/api/customer", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Customer creation failed: {await response.Content.ReadAsStringAsync()}");

        var result = await ReadApiResponseAsync<CustomerDto>(response);
        return result.Data.Id;
    }

    private async Task<Guid> CreateTestCategoryAsync(HttpClient client)
    {
        var payload = new
        {
            name = $"Test Category {Guid.NewGuid():N}"[..20],
            description = "Test category for ticket API tests",
            isActive = true,
            sortOrder = 0
        };

        var response = await PostAsync(client, "/api/category", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Category creation failed: {await response.Content.ReadAsStringAsync()}");

        var result = await ReadApiResponseAsync<CategoryDto>(response);
        return result.Data.Id;
    }

    private async Task<Guid> CreateTestUserAsync(HttpClient client)
    {
        var userName = $"agent_{Guid.NewGuid():N}"[..15];
        var email = $"{userName}@helpdesk.test";
        var (user, _) = await CreateUserAsync(client, userName, email, "Agent123!", HostTenantId);
        return user.Id;
    }

    private async Task CreateActiveSlaPolicyAsync(HttpClient client)
    {
        var payload = new
        {
            name = $"SLA Policy {Guid.NewGuid():N}"[..15],
            description = "Test SLA policy for ticket assignment",
            isActive = true,
            lowPriorityResponseMinutes = 60,
            lowPriorityResolutionMinutes = 480,
            mediumPriorityResponseMinutes = 30,
            mediumPriorityResolutionMinutes = 240,
            highPriorityResponseMinutes = 15,
            highPriorityResolutionMinutes = 120,
            urgentPriorityResponseMinutes = 5,
            urgentPriorityResolutionMinutes = 60
        };

        var response = await PostAsync(client, "/api/sla-policy", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"SLA policy creation failed: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<(TicketDto Ticket, HttpClient Client)> CreateTicketAsync(
        HttpClient client, Guid customerId, Guid? categoryId = null)
    {
        var payload = new
        {
            title = "Test Ticket Title",
            description = "Test ticket description for API integration tests",
            priority = 2,  // Medium
            type = 2,      // Incident
            customerId,
            categoryId
        };

        var response = await PostAsync(client, "/api/ticket", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Ticket creation failed: {await response.Content.ReadAsStringAsync()}");

        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.Id.Should().NotBeEmpty();
        result.Data.Title.Should().Be("Test Ticket Title");
        result.Data.Status.Should().Be(1); // Open

        return (result.Data, client);
    }

    // ── 1. CreateAsync (POST /api/ticket) ─────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldCreateTicketAndReturnDto()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);

        var payload = new
        {
            title = "Network outage in Building A",
            description = "Users in Building A report complete network outage since 9 AM",
            priority = 4,  // Urgent
            type = 2,      // Incident
            customerId
        };

        // Act
        var response = await PostAsync(client, "/api/ticket", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.Status.Should().Be(1); // Open
        result.Data.Priority.Should().Be(4);
        result.Data.Type.Should().Be(2);
        result.Data.CustomerId.Should().Be(customerId);
        result.Data.Title.Should().Be("Network outage in Building A");
    }

    // ── 2. GetByIdAsync (GET /api/ticket/{id}) ───────────────────────

    [Fact]
    public async Task GetByIdAsync_ShouldReturnTicket()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Act
        var response = await GetAsync(client, $"/api/ticket/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.Id.Should().Be(created.Id);
        result.Data.Title.Should().Be(created.Title);
    }

    // ── 3. GetListAsync (GET /api/ticket?pageIndex=0&pageSize=10) ────

    [Fact]
    public async Task GetListAsync_ShouldReturnPagedTickets()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);

        // Create multiple tickets
        await CreateTicketAsync(client, customerId);
        await CreateTicketAsync(client, customerId);

        // Act
        var response = await GetAsync(client, "/api/ticket?pageIndex=0&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<PagedResultResponse<TicketDto>>(response);
        result.Data.Items.Should().NotBeEmpty();
        result.Data.TotalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    // ── 4. UpdateAsync (PUT /api/ticket/{id}) ─────────────────────────

    [Fact]
    public async Task UpdateAsync_ShouldUpdateAllFields()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var categoryId = await CreateTestCategoryAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        var newCategoryId = await CreateTestCategoryAsync(client);

        // Act — update Title, Description, Priority, AND CategoryId
        var response = await PutAsync(client, $"/api/ticket/{created.Id}", new
        {
            id = created.Id,
            title = "Updated: Router replacement needed",
            description = "Updated description with more detail about the network issue",
            status = 1,
            priority = 3,      // High (was Medium=2)
            type = 2,
            customerId,
            categoryId = newCategoryId,
            concurrencyStamp = created.ConcurrencyStamp
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.Title.Should().Be("Updated: Router replacement needed");
        result.Data.Description.Should().Contain("Updated description");
        result.Data.Priority.Should().Be(3);
        result.Data.CategoryId.Should().Be(newCategoryId);
        result.Data.Id.Should().Be(created.Id);
    }

    // ── 5. DeleteAsync (DELETE /api/ticket/{id}) ──────────────────────

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTicket()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Act
        var deleteResponse = await DeleteAsync(client, $"/api/ticket/{created.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify ticket is gone
        var getResponse = await GetAsync(client, $"/api/ticket/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getResult = await ReadApiResponseAsync<TicketDto>(getResponse);
        getResult.Data.Should().BeNull();
    }

    // ── 6. AssignAsync (POST /api/ticket/{id}/assign?agentId={id}) ────

    [Fact]
    public async Task AssignAsync_WithActiveSlaPolicy_ShouldSetDueDateAndAssign()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        await CreateActiveSlaPolicyAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Act
        var response = await PostAsync<object>(
            client,
            $"/api/ticket/{created.Id}/assign?agentId={assigneeId}",
            null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.AssigneeId.Should().Be(assigneeId);
        result.Data.Status.Should().Be(2); // InProgress
        result.Data.DueDate.Should().NotBeNull("SLA policy should set DueDate");
        result.Data.DueDate!.Value.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task AssignAsync_WithoutSlaPolicy_ShouldAssignWithoutDueDate()
    {
        // Arrange — no active SLA policy in the seeded database
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Act
        var response = await PostAsync<object>(
            client,
            $"/api/ticket/{created.Id}/assign?agentId={assigneeId}",
            null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.AssigneeId.Should().Be(assigneeId);
        result.Data.Status.Should().Be(2); // InProgress
        result.Data.DueDate.Should().BeNull("no active SLA policy means no DueDate");
    }

    // ── 7. ResolveAsync (POST /api/ticket/{id}/resolve) ───────────────

    [Fact]
    public async Task ResolveAsync_ShouldResolveOpenTicket()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Assign first to move to InProgress, then resolve
        await PostAsync<object>(
            client,
            $"/api/ticket/{created.Id}/assign?agentId={assigneeId}",
            null!);

        // Act
        var response = await PostAsync<object>(
            client,
            $"/api/ticket/{created.Id}/resolve",
            null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.Status.Should().Be(5); // Resolved
    }

    [Fact]
    public async Task ResolveAsync_AlreadyClosed_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Assign, resolve, then close the ticket
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/assign?agentId={assigneeId}", null!);
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/resolve", null!);
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/close", null!);

        // Act — try to resolve an already closed ticket
        var response = await PostAsync<object>(client, $"/api/ticket/{created.Id}/resolve", null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Cannot resolve a ticket that is already closed");
    }

    // ── 8. CloseAsync (POST /api/ticket/{id}/close) ───────────────────

    [Fact]
    public async Task CloseAsync_ShouldCloseResolvedTicket()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Assign then resolve (must resolve before close in real workflow)
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/assign?agentId={assigneeId}", null!);
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/resolve", null!);

        // Act
        var response = await PostAsync<object>(
            client,
            $"/api/ticket/{created.Id}/close",
            null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<TicketDto>(response);
        result.Data.Status.Should().Be(6); // Closed
    }

    [Fact]
    public async Task CloseAsync_AlreadyClosed_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Assign, resolve, and close the ticket
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/assign?agentId={assigneeId}", null!);
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/resolve", null!);
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/close", null!);

        // Act — try to close an already closed ticket
        var response = await PostAsync<object>(client, $"/api/ticket/{created.Id}/close", null!);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Cannot close a ticket that is already closed");
    }

    // ── 9. GetByCustomerAsync (GET /api/ticket/customer/{customerId}) ─

    [Fact]
    public async Task GetByCustomerAsync_ShouldReturnCustomerTickets()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);

        // Create multiple tickets for this customer
        await CreateTicketAsync(client, customerId);
        await CreateTicketAsync(client, customerId);

        // Act
        var response = await GetAsync(client, $"/api/ticket/customer/{customerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<List<TicketDto>>(response);
        result.Data.Should().NotBeNull();
        result.Data.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Data.Should().AllSatisfy(t => t.CustomerId.Should().Be(customerId));
    }

    // ── 10. GetByAssigneeAsync (GET /api/ticket/assignee/{assigneeId}) ─

    [Fact]
    public async Task GetByAssigneeAsync_ShouldReturnAssigneeTickets()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        var (ticket1, _) = await CreateTicketAsync(client, customerId);
        var (ticket2, _) = await CreateTicketAsync(client, customerId);

        // Assign both tickets to the same agent
        await PostAsync<object>(client, $"/api/ticket/{ticket1.Id}/assign?agentId={assigneeId}", null!);
        await PostAsync<object>(client, $"/api/ticket/{ticket2.Id}/assign?agentId={assigneeId}", null!);

        // Act
        var response = await GetAsync(client, $"/api/ticket/assignee/{assigneeId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<List<TicketDto>>(response);
        result.Data.Should().NotBeNull();
        result.Data.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Data.Should().AllSatisfy(t => t.AssigneeId.Should().Be(assigneeId));
    }

    // ── 11. GetOverdueAsync (GET /api/ticket/overdue) ──────────────────

    [Fact]
    public async Task GetOverdueAsync_ShouldReturnOverdueTickets()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var customerId = await CreateTestCustomerAsync(client);
        var assigneeId = await CreateTestUserAsync(client);
        var (created, _) = await CreateTicketAsync(client, customerId);

        // Assign the ticket (even without SLA, the assignee is set)
        await PostAsync<object>(client, $"/api/ticket/{created.Id}/assign?agentId={assigneeId}", null!);

        // Set DueDate to the past directly via DbContext to simulate overdue
        await SetTicketDueDateToPastAsync(created.Id);

        // Act
        var response = await GetAsync(client, "/api/ticket/overdue");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadApiResponseAsync<List<TicketDto>>(response);
        result.Data.Should().NotBeNull();
        result.Data.Should().Contain(t => t.Id == created.Id,
            "the ticket with a past DueDate should appear in overdue results");
    }

    // ── DbContext helper (for GetOverdue test) ────────────────────────

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
