using SaaSHelpdesk.Tests.Helpers;

namespace SaaSHelpdesk.Tests;

public class CustomerApiTests : BaseTest, IClassFixture<Fixtures.HelpdeskWebApplicationFactory>
{
    public CustomerApiTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    // ── Test helpers ────────────────────────────────────────────────

    private async Task<(HttpClient Client, CustomerDto Customer)> CreateTestCustomerAsync(
        string name = "Test Customer",
        string email = "test@customer.com",
        string? phone = "123-456-7890",
        string? company = "Test Company")
    {
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var payload = new
        {
            name,
            email,
            phone,
            company
        };

        var response = await PostAsync(client, "/api/customer", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<CustomerDto>(response);
        return (client, apiResponse.Data!);
    }

    private async Task<HttpResponseMessage> ActivateAsync(HttpClient client, Guid customerId)
    {
        return await GetAsync(client, $"/api/customer/activate?id={customerId}");
    }

    private async Task<HttpResponseMessage> DeactivateAsync(HttpClient client, Guid customerId)
    {
        return await GetAsync(client, $"/api/customer/deactivate?id={customerId}");
    }

    // ── CreateAsync (inherited) ─────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldCreateCustomer_ReturnsCustomerDto()
    {
        // Act
        var (_, customer) = await CreateTestCustomerAsync(
            name: "Acme Corp",
            email: "acme@example.com",
            phone: "555-0100",
            company: "Acme Inc.");

        // Assert
        customer.Id.Should().NotBe(Guid.Empty);
        customer.Name.Should().Be("Acme Corp");
        customer.Email.Should().Be("acme@example.com");
        customer.Phone.Should().Be("555-0100");
        customer.Company.Should().Be("Acme Inc.");
        customer.IsActive.Should().BeTrue("new customers are created as active by default");
    }

    [Fact]
    public async Task CreateAsync_ShouldRequireNameAndEmail()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        var payload = new { name = "", email = "" };
        var response = await PostAsync(client, "/api/customer", payload);

        // Assert — validation fails with 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GetByIdAsync (inherited) ────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ShouldReturnCustomer_WhenExists()
    {
        // Arrange
        var (client, created) = await CreateTestCustomerAsync(
            name: "GetById Customer",
            email: "getbyid@example.com",
            phone: "555-0200",
            company: "GetById Co");

        // Act
        var response = await GetAsync(client, $"/api/customer/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<CustomerDto>(response);

        // Assert
        apiResponse.Data!.Id.Should().Be(created.Id);
        apiResponse.Data.Name.Should().Be("GetById Customer");
        apiResponse.Data.Email.Should().Be("getbyid@example.com");
        apiResponse.Data.Phone.Should().Be("555-0200");
        apiResponse.Data.Company.Should().Be("GetById Co");
        apiResponse.Data.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturn404_WhenNotFound()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await GetAsync(client, $"/api/customer/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GetListAsync (inherited) ────────────────────────────────────

    [Fact]
    public async Task GetListAsync_ShouldReturnPagedCustomers()
    {
        // Arrange
        var (client, created) = await CreateTestCustomerAsync(
            name: "ListTest Customer",
            email: "listtest@example.com");

        // Act
        var response = await GetAsync(client, "/api/customer?pageIndex=0&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await ReadJsonAsync<DynamicApiResponse<PagedResultResponse<CustomerDto>>>(response);

        // Assert
        envelope.Data.Should().NotBeNull();
        envelope.Data!.TotalCount.Should().BeGreaterThan(0);
        envelope.Data.Items.Should().NotBeEmpty();
        envelope.Data.Items.Should().Contain(c => c.Id == created.Id);
    }

    // ── UpdateAsync (inherited) ─────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ShouldUpdateNamePhoneCompanyAndNotes()
    {
        // Arrange
        var (client, created) = await CreateTestCustomerAsync(
            name: "Original Name",
            email: "update@example.com",
            phone: "000-0000",
            company: "Original Co");

        var updatePayload = new
        {
            name = "Updated Name",
            phone = "999-9999",
            company = "Updated Company",
            notes = "These are updated notes for the customer."
        };

        // Act
        var response = await PutAsync(client, $"/api/customer/{created.Id}", updatePayload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<CustomerDto>(response);

        // Assert
        apiResponse.Data!.Id.Should().Be(created.Id);
        apiResponse.Data.Name.Should().Be("Updated Name");
        apiResponse.Data.Phone.Should().Be("999-9999");
        apiResponse.Data.Company.Should().Be("Updated Company");
        apiResponse.Data.Notes.Should().Be("These are updated notes for the customer.");
        apiResponse.Data.IsActive.Should().BeTrue();
    }

    // ── DeleteAsync (inherited) ─────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ShouldDeleteCustomer()
    {
        // Arrange
        var (client, created) = await CreateTestCustomerAsync(
            name: "Delete Me",
            email: "delete@example.com");

        // Act — delete
        var deleteResponse = await DeleteAsync(client, $"/api/customer/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify — get by ID should return 404
        var getResponse = await GetAsync(client, $"/api/customer/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ActivateAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ActivateAsync_ShouldActivateInactiveCustomer()
    {
        // Arrange — create customer (active by default), then deactivate
        var (client, created) = await CreateTestCustomerAsync(
            name: "Activate Test",
            email: "activate@example.com");

        var deactivateResponse = await DeactivateAsync(client, created.Id);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — reactivate
        var activateResponse = await ActivateAsync(client, created.Id);
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<CustomerDto>(activateResponse);

        // Assert
        apiResponse.Data!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ActivateAsync_ShouldReturn400_WhenAlreadyActive()
    {
        // Arrange — customer is active by default
        var (client, created) = await CreateTestCustomerAsync(
            name: "Already Active",
            email: "already_active@example.com");

        // Act — try to activate an already active customer
        var response = await ActivateAsync(client, created.Id);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await ReadJsonAsync<ErrorResponse>(response);
        error.Code.Should().Be("Crest.Operation.Invalid");
    }

    // ── DeactivateAsync ─────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_ShouldDeactivateActiveCustomer()
    {
        // Arrange — customer is active by default
        var (client, created) = await CreateTestCustomerAsync(
            name: "Deactivate Test",
            email: "deactivate@example.com");

        // Act
        var response = await DeactivateAsync(client, created.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<CustomerDto>(response);

        // Assert
        apiResponse.Data!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_ShouldReturn400_WhenAlreadyInactive()
    {
        // Arrange — create customer, deactivate once
        var (client, created) = await CreateTestCustomerAsync(
            name: "Already Inactive",
            email: "already_inactive@example.com");

        var firstDeactivate = await DeactivateAsync(client, created.Id);
        firstDeactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — try to deactivate an already inactive customer
        var response = await DeactivateAsync(client, created.Id);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await ReadJsonAsync<ErrorResponse>(response);
        error.Code.Should().Be("Crest.Operation.Invalid");
    }
}
