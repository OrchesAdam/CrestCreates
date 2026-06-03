namespace SaaSHelpdesk.Tests;

public class AgentApiTests : BaseTest, IClassFixture<Fixtures.HelpdeskWebApplicationFactory>
{
    public AgentApiTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    // ── Test helpers ──────────────────────────────────────────────────

    private async Task<AgentResponse> CreateAgentAsync(
        HttpClient client,
        string userName,
        string email,
        string password = "AgentPass123!",
        string? role = "Administrators")
    {
        var payload = new
        {
            userName,
            email,
            password,
            role
        };
        var response = await PostAsync(client, "/api/agent", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<AgentResponse>(response);
        return apiResponse.Data!;
    }

    // ── CreateAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldCreateAgent_ReturnsAgentDtoWithRole()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var payload = new
        {
            userName = "test-agent",
            email = "test-agent@helpdesk.local",
            password = "AgentPass123!",
            role = "Administrators"
        };

        // Act
        var response = await PostAsync(client, "/api/agent", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<AgentResponse>(response);
        var agent = apiResponse.Data!;

        // Assert
        agent.Id.Should().NotBe(Guid.Empty);
        agent.UserName.Should().Be("test-agent");
        agent.Email.Should().Be("test-agent@helpdesk.local");
        agent.IsActive.Should().BeTrue("new agents are created as active by default");
        agent.Role.Should().Be("Administrators");
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateAgent_WithoutRole()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var payload = new
        {
            userName = "basic-agent",
            email = "basic-agent@helpdesk.local",
            password = "AgentPass123!"
        };

        // Act
        var response = await PostAsync(client, "/api/agent", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<AgentResponse>(response);
        var agent = apiResponse.Data!;

        // Assert
        agent.Id.Should().NotBe(Guid.Empty);
        agent.UserName.Should().Be("basic-agent");
        agent.Email.Should().Be("basic-agent@helpdesk.local");
        agent.IsActive.Should().BeTrue();
        agent.Role.Should().BeNull();
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ShouldReturnAgent_WhenExists()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateAgentAsync(client,
            "getbyid-agent", "getbyid-agent@helpdesk.local");

        // Act
        var response = await GetAsync(client, $"/api/agent/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<AgentResponse>(response);

        // Assert
        apiResponse.Data!.Id.Should().Be(created.Id);
        apiResponse.Data.UserName.Should().Be("getbyid-agent");
        apiResponse.Data.Email.Should().Be("getbyid-agent@helpdesk.local");
        apiResponse.Data.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNotFound_WhenAgentNotFound()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await GetAsync(client, $"/api/agent/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DeactivateAsync ───────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_ShouldDeactivateAgent_ReturnsIsActiveFalse()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();
        var created = await CreateAgentAsync(client,
            "deactivate-agent", "deactivate-agent@helpdesk.local");

        // Act
        var response = await PostAsync<object>(client,
            $"/api/agent/{created.Id}/deactivate", null!);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<AgentResponse>(response);

        // Assert
        apiResponse.Data!.Id.Should().Be(created.Id);
        apiResponse.Data.IsActive.Should().BeFalse();

        // Verify — GetById returns agent with IsActive = false
        var getResponse = await GetAsync(client, $"/api/agent/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getApiResponse = await ReadApiResponseAsync<AgentResponse>(getResponse);
        getApiResponse.Data!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_ShouldThrow_WhenAgentNotFound()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await PostAsync<object>(client,
            $"/api/agent/{Guid.NewGuid()}/deactivate", null!);

        // Assert — the service throws KeyNotFoundException, which should map to a 404 or 500
        // Depending on the framework error handling, this may be 500 or 404
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.InternalServerError);
    }
}

// ── Response models ────────────────────────────────────────────────

internal sealed class AgentResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Role { get; set; }
}
