using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Mcp;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using CrestCreates.Sample.Procurement.Host;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;
using CrestCreates.Schema;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Sample.Procurement.Tests.Acceptance;

public sealed class ProjectionCompositionAcceptanceTests
{
    [Fact]
    public async Task Composition_UsesSameRequestIdAcrossAllSteps()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        SetIdentity(client, "tenant-a", "requester-a", "procurement-requester");

        var submitted = await SubmitHttpAsync(client, "Server rack", 25_000m);
        submitted.Status.Should().Be("PendingApproval");

        var pending = await GetHttpAsync(client, submitted.RequestId);
        pending.RequestId.Should().Be(submitted.RequestId);
        pending.Title.Should().Be("Server rack");
        pending.Amount.Should().Be(25_000m);
        pending.Status.Should().Be("PendingApproval");

        SetIdentity(client, "tenant-a", "manager-a", "procurement-manager");
        using var approveContent = JsonContent.Create(
            new ApproveProcurementRequestInput { Comment = "Budget confirmed" },
            ProcurementJsonContext.Default.ApproveProcurementRequestInput);
        var approveResponse = await client.PostAsync(
            $"/api/procurement/requests/{submitted.RequestId}/approve",
            approveContent);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await ReadAsync(
            approveResponse,
            ProcurementJsonContext.Default.ProcurementRequestResult);
        approved.RequestId.Should().Be(submitted.RequestId);
        approved.Status.Should().Be("Approved");
        approved.ApproverId.Should().Be("manager-a");

        var final = await GetHttpAsync(client, submitted.RequestId);
        final.RequestId.Should().Be(submitted.RequestId);
        final.Title.Should().Be("Server rack");
        final.Amount.Should().Be(25_000m);
        final.Status.Should().Be("Approved");
        final.ApproverId.Should().Be("manager-a");
    }

    [Fact]
    public async Task HttpSubmit_CompatibilityGet_ReturnsSameEntity()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        SetIdentity(client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await SubmitHttpAsync(client, "HTTP to compatibility", 1_500m);

        var response = await client.GetAsync($"/api/procurement/{submitted.RequestId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadCompatibilityDataAsync(response);
        data.GetProperty("requestId").GetGuid().Should().Be(submitted.RequestId);
        data.GetProperty("title").GetString().Should().Be("HTTP to compatibility");
    }

    [Fact]
    public async Task CompatibilitySubmit_HttpGet_ReturnsSameEntity()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        SetIdentity(client, "tenant-a", "compat-user", "procurement-requester");
        var response = await client.GetAsync(
            "/api/procurement/submit?Title=Compatibility+submit&Description=Shared+state&Amount=2500&Currency=USD&Category=General");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadCompatibilityDataAsync(response);
        var requestId = data.GetProperty("requestId").GetGuid();

        var native = await GetHttpAsync(client, requestId);
        native.RequestId.Should().Be(requestId);
        native.Title.Should().Be("Compatibility submit");
        native.RequesterId.Should().Be("compat-user");
    }

    [Fact]
    public async Task CompatibilityApprove_NativeGet_ReturnsApprovedState()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        SetIdentity(client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await SubmitHttpAsync(client, "Compatibility approval", 30_000m);

        SetIdentity(client, "tenant-a", "manager-a", "procurement-manager");
        var response = await client.GetAsync(
            $"/api/procurement/approve?RequestId={submitted.RequestId}&Comment=Approved");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var native = await GetHttpAsync(client, submitted.RequestId);
        native.Status.Should().Be("Approved");
        native.ApproverId.Should().Be("manager-a");
    }

    internal static async Task<SubmitProcurementRequestResult> SubmitHttpAsync(
        HttpClient client,
        string title,
        decimal amount)
    {
        using var content = JsonContent.Create(new SubmitProcurementRequestInput
        {
            Title = title,
            Description = "Acceptance test",
            Amount = amount,
            Currency = "USD",
            Category = "Infrastructure"
        }, ProcurementJsonContext.Default.SubmitProcurementRequestInput);
        var response = await client.PostAsync("/api/procurement/requests", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadAsync(response, ProcurementJsonContext.Default.SubmitProcurementRequestResult);
    }

    internal static async Task<ProcurementRequestResult> GetHttpAsync(HttpClient client, Guid requestId)
    {
        var response = await client.GetAsync($"/api/procurement/requests/{requestId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadAsync(response, ProcurementJsonContext.Default.ProcurementRequestResult);
    }

    internal static void SetIdentity(HttpClient client, string tenant, string user, params string[] roles)
    {
        client.DefaultRequestHeaders.Remove(SampleExecutionIdentity.TenantHeader);
        client.DefaultRequestHeaders.Remove(SampleExecutionIdentity.UserHeader);
        client.DefaultRequestHeaders.Remove(SampleExecutionIdentity.RolesHeader);
        client.DefaultRequestHeaders.Add(SampleExecutionIdentity.TenantHeader, tenant);
        client.DefaultRequestHeaders.Add(SampleExecutionIdentity.UserHeader, user);
        client.DefaultRequestHeaders.Add(SampleExecutionIdentity.RolesHeader, string.Join(',', roles));
    }

    internal static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeInfo)
            ?? throw new InvalidOperationException("Expected response body.");

    private static async Task<JsonElement> ReadCompatibilityDataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }
}

public sealed class McpProjectionAcceptanceTests
{
    [Fact]
    public async Task McpDiscovery_ExposesOnlyGet()
    {
        using var factory = new ProcurementWebApplicationFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SampleExecutionIdentity>()
            .Set("tenant-a", "user-a", "procurement-requester");

        var tools = await scope.ServiceProvider.GetRequiredService<IMcpToolDiscoveryService>()
            .ListAsync(new McpToolDiscoveryContext(new McpToolHostContext("sample", "test")));
        tools.Select(tool => tool.Name).Should().Equal(ProcurementContractIds.GetTool);
        tools.Should().NotContain(tool => tool.Name.Contains("approve", StringComparison.OrdinalIgnoreCase)
            || tool.Name.Contains("reject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task McpGet_QueriesHttpCreatedEntity_AndUsesInvocationSourceMcp()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(client, "MCP query", 2_000m);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SampleExecutionIdentity>()
            .Set("tenant-a", "requester-a", "procurement-requester");
        var outcome = await InvokeGetAsync(scope.ServiceProvider, submitted.RequestId, "mcp-1");

        outcome.IsError.Should().BeFalse();
        outcome.StructuredContent!.Value.GetProperty("requestId").GetGuid().Should().Be(submitted.RequestId);
        factory.Services.GetRequiredService<ICapabilityAuditStore>()
            .Should().BeOfType<InMemoryCapabilityAuditStore>()
            .Which.GetRecords().Should().Contain(record =>
                record.Source == InvocationSource.Mcp
                && record.CapabilityId == ProcurementContractIds.GetCapability);
    }

    [Fact]
    public async Task McpCrossTenantRequest_IsUnavailable()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(client, "Tenant private", 2_000m);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SampleExecutionIdentity>()
            .Set("tenant-b", "requester-a", "procurement-requester");
        var outcome = await InvokeGetAsync(scope.ServiceProvider, submitted.RequestId, "mcp-cross-tenant");

        outcome.IsError.Should().BeTrue();
        outcome.ErrorCode.Should().Be("CAPABILITY_RESOURCE_NOT_FOUND");
        outcome.StructuredContent.Should().BeNull();
    }

    [Fact]
    public void McpOutputSchema_MatchesSourceGeneratedContract_AndRejectsMismatch()
    {
        var schema = ProcurementDescriptorCatalog.Schemas.Single(item =>
            item.Id == ProcurementContractIds.RequestOutputSchema);
        var validator = new McpToolSchemaParityValidator();
        validator.Invoking(item => item.ValidateOutput(
                schema,
                ProcurementJsonContext.Default.ProcurementRequestResult))
            .Should().NotThrow();
        validator.Invoking(item => item.ValidateOutput(
                schema,
                ProcurementJsonContext.Default.SubmitProcurementRequestResult))
            .Should().Throw<McpToolConfigurationException>();
    }

    [Fact]
    public async Task InvalidOutputSchema_FailsProjection()
    {
        using var factory = new ProcurementWebApplicationFactory(services =>
        {
            services.RemoveAll<ICapabilityHandlerModule>();
            services.AddSingleton<ICapabilityHandlerModule>(new InvalidOutputHandlerModule());
        });
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SampleExecutionIdentity>()
            .Set("tenant-a", "requester-a", "procurement-requester");

        Func<Task> act = async () =>
            await InvokeGetAsync(scope.ServiceProvider, Guid.NewGuid(), "mcp-invalid-output");

        var exception = await act.Should().ThrowAsync<McpToolProtocolException>();
        exception.Which.FailureKind.Should().Be(McpToolProtocolFailureKind.InternalServer);
        exception.Which.InternalCode.Should().Be("MCP_TOOL_OUTPUT_SCHEMA_VIOLATION");
    }

    private static async Task<McpToolInvocationOutcome> InvokeGetAsync(
        IServiceProvider services,
        Guid requestId,
        string logicalId)
    {
        var input = new GetProcurementRequestInput { RequestId = requestId };
        return await services.GetRequiredService<IMcpToolInvoker>().InvokeAsync(
            ProcurementContractIds.GetTool,
            JsonSerializer.SerializeToElement(input, ProcurementJsonContext.Default.GetProcurementRequestInput),
            new McpToolCallContext(
                new McpToolHostContext("sample", "test"),
                logicalId,
                $"request-{logicalId}"));
    }

    private sealed class InvalidOutputHandlerModule : ICapabilityHandlerModule
    {
        public string Id => "procurement-invalid-output";

        public void Apply(CapabilityHandlerResolver resolver)
            => resolver.Register(ProcurementContractIds.GetCapability, new InvalidOutputHandler());
    }

    private sealed class InvalidOutputHandler : ICapabilityHandlerInvoker
    {
        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => Task.FromResult<object?>(new ProcurementRequestResult
            {
                Id = Guid.NewGuid(),
                RequestId = Guid.NewGuid(),
                Title = string.Empty,
                Description = string.Empty,
                Amount = 1,
                Currency = "USD",
                RequesterId = string.Empty,
                Category = string.Empty,
                Status = string.Empty
            });
    }
}

public sealed class AgentGovernanceAcceptanceTests
{
    [Fact]
    public async Task AgentDiscovery_ExposesGetAndSubmit_ButNotApproveOrReject()
    {
        using var harness = CreateHarness("agent-discovery");
        var tools = await harness.Scope.ServiceProvider.GetRequiredService<IAgentToolCatalog>().ListAsync();
        tools.Select(tool => tool.ToolName).Order().Should().Equal(
            ProcurementContractIds.GetTool,
            ProcurementContractIds.SubmitTool);
        tools.Should().NotContain(tool => tool.ToolName.Contains("approve", StringComparison.OrdinalIgnoreCase)
            || tool.ToolName.Contains("reject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AgentSubmit_WithoutApproval_DoesNotDispatch()
    {
        using var harness = CreateHarness("agent-no-approval");
        var store = harness.Scope.ServiceProvider.GetRequiredService<InMemoryProcurementRequestStore>();
        var before = store.Count;
        var outcome = await InvokeSubmitAsync(harness.Scope.ServiceProvider, approvalEvidence: null);

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.GovernanceDenied);
        store.Count.Should().Be(before);
        AgentAudit(harness.Factory).Should().BeEmpty();
    }

    [Fact]
    public async Task AgentSubmit_WithApproval_DispatchesExactlyOnce_AndUsesInvocationSourceAgent()
    {
        using var harness = CreateHarness("agent-approved");
        var store = harness.Scope.ServiceProvider.GetRequiredService<InMemoryProcurementRequestStore>();
        var before = store.Count;
        var outcome = await InvokeSubmitAsync(
            harness.Scope.ServiceProvider,
            SampleAgentToolApprovalGate.ApprovedEvidence);

        outcome.IsSuccess.Should().BeTrue();
        store.Count.Should().Be(before + 1);
        AgentAudit(harness.Factory).Should().ContainSingle(record =>
            record.CapabilityId == ProcurementContractIds.SubmitCapability);
    }

    [Fact]
    public async Task BudgetDenied_DoesNotEnterDispatcher()
    {
        using var harness = CreateHarness("agent-budget-denied");
        harness.Scope.ServiceProvider.GetRequiredService<SampleAgentToolBudgetGate>().DenyReservations = true;
        var store = harness.Scope.ServiceProvider.GetRequiredService<InMemoryProcurementRequestStore>();
        var before = store.Count;
        var outcome = await InvokeSubmitAsync(
            harness.Scope.ServiceProvider,
            SampleAgentToolApprovalGate.ApprovedEvidence);

        outcome.Kind.Should().Be(AgentToolInvocationOutcomeKind.GovernanceDenied);
        store.Count.Should().Be(before);
        AgentAudit(harness.Factory).Should().BeEmpty();
    }

    [Fact]
    public async Task CompletedInvocationReplay_DoesNotCreateSecondRequest()
    {
        using var harness = CreateHarness("agent-replay");
        var store = harness.Scope.ServiceProvider.GetRequiredService<InMemoryProcurementRequestStore>();
        var request = CreateSubmitRequest(SampleAgentToolApprovalGate.ApprovedEvidence);
        var invoker = harness.Scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>();
        var before = store.Count;

        var first = await invoker.InvokeAsync(request);
        var replay = await invoker.InvokeAsync(request);

        first.IsSuccess.Should().BeTrue();
        replay.IsSuccess.Should().BeTrue();
        store.Count.Should().Be(before + 1);
        AgentAudit(harness.Factory).Should().ContainSingle(record =>
            record.CapabilityId == ProcurementContractIds.SubmitCapability);
    }

    private static Harness CreateHarness(string invocationId)
    {
        var factory = new ProcurementWebApplicationFactory();
        _ = factory.CreateClient();
        var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<SampleExecutionIdentity>();
        identity.Set("tenant-agent", "agent-user", "procurement-requester");
        identity.SetAgent("execution-agent", invocationId);
        return new Harness(factory, scope);
    }

    private static ValueTask<AgentToolInvocationOutcome> InvokeSubmitAsync(
        IServiceProvider services,
        string? approvalEvidence)
        => services.GetRequiredService<IAgentToolInvoker>()
            .InvokeAsync(CreateSubmitRequest(approvalEvidence));

    private static AgentToolInvocationRequest CreateSubmitRequest(string? approvalEvidence)
    {
        var input = new SubmitProcurementRequestInput
        {
            Title = "Agent request",
            Description = "Governed",
            Amount = 15_000m,
            Currency = "USD",
            Category = "Infrastructure"
        };
        return new AgentToolInvocationRequest(
            ProcurementContractIds.SubmitTool,
            JsonSerializer.SerializeToElement(input, ProcurementJsonContext.Default.SubmitProcurementRequestInput),
            approvalEvidence);
    }

    private static IReadOnlyList<CapabilityExecutionRecord> AgentAudit(ProcurementWebApplicationFactory factory)
        => factory.Services.GetRequiredService<ICapabilityAuditStore>()
            .Should().BeOfType<InMemoryCapabilityAuditStore>()
            .Which.GetRecords()
            .Where(record => record.Source == InvocationSource.Agent)
            .ToArray();

    private sealed record Harness(
        ProcurementWebApplicationFactory Factory,
        IServiceScope Scope) : IDisposable
    {
        public void Dispose()
        {
            Scope.Dispose();
            Factory.Dispose();
        }
    }
}

public sealed class WorkflowTenantAndSchemaAcceptanceTests
{
    [Fact]
    public async Task HighValueSubmit_CreatesWorkflowAndHumanTask_HumanTaskApprovalDispatchesCapability()
    {
        using var factory = new ProcurementWebApplicationFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var identity = services.GetRequiredService<SampleExecutionIdentity>();
        identity.Set("tenant-a", "requester-a", "procurement-requester");
        var input = new SubmitProcurementRequestInput
        {
            Title = "Workflow request",
            Description = "Requires approval",
            Amount = 40_000m,
            Currency = "USD",
            Category = "Infrastructure"
        };
        var submit = await services.GetRequiredService<ICapabilityDispatcher>().DispatchAsync(
            ProcurementContractIds.SubmitCapability,
            InvocationSource.Http,
            input,
            context => context.InputJson = JsonSerializer.SerializeToElement(
                input,
                ProcurementJsonContext.Default.SubmitProcurementRequestInput));
        submit.IsSuccess.Should().BeTrue();
        var submitResult = submit.Output.Should().BeOfType<SubmitProcurementRequestResult>().Subject;
        var aggregate = services.GetRequiredService<InMemoryProcurementRequestStore>()
            .GetById("tenant-a", submitResult.RequestId)!;
        aggregate.WorkflowInstanceId.Should().NotBeNullOrWhiteSpace();

        var workflow = await services.GetRequiredService<IWorkflowInstanceStore>()
            .GetAsync(aggregate.WorkflowInstanceId!);
        workflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        var tasks = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetPendingByWorkflowAsync(workflow.InstanceId);
        tasks.Should().ContainSingle();

        identity.Set("tenant-a", "manager-a", "procurement-manager");
        await services.GetRequiredService<ProcurementApprovalTaskService>()
            .CompleteAsync(tasks[0].Id, "Approve", "Approved by manager");

        aggregate.Status.ToString().Should().Be("Approved");
        aggregate.ApproverId.Should().Be("manager-a");
        var completed = await services.GetRequiredService<IWorkflowInstanceStore>()
            .GetAsync(workflow.InstanceId);
        completed!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        factory.Services.GetRequiredService<ICapabilityAuditStore>()
            .Should().BeOfType<InMemoryCapabilityAuditStore>()
            .Which.GetRecords().Should().Contain(record =>
                record.Source == InvocationSource.HumanTask
                && record.CapabilityId == ProcurementContractIds.ApproveCapability);
    }

    [Fact]
    public async Task CrossTenantGet_IsDenied_AndNotFoundHasCanonicalFailure()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-a", "user-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(client, "Private", 1_000m);

        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-b", "user-b", "procurement-requester");
        var response = await client.GetAsync($"/api/procurement/requests/{submitted.RequestId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RequesterCannotApproveOwnRequest()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-a", "same-user", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(client, "Self approval", 20_000m);

        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-a", "same-user", "procurement-manager");
        using var content = JsonContent.Create(
            new ApproveProcurementRequestInput { Comment = "self" },
            ProcurementJsonContext.Default.ApproveProcurementRequestInput);
        var response = await client.PostAsync(
            $"/api/procurement/requests/{submitted.RequestId}/approve",
            content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnknownInputProperty_IsRejected_AndInvalidInputDoesNotInvokeHandler()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-a", "user-a", "procurement-requester");
        var store = factory.Services.GetRequiredService<InMemoryProcurementRequestStore>();
        var before = store.Count;
        using var content = new StringContent(
            "{\"title\":\"Invalid\",\"description\":\"x\",\"amount\":100,\"currency\":\"USD\",\"category\":\"General\",\"unknown\":true}",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/procurement/requests", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        store.Count.Should().Be(before);
    }

    [Fact]
    public void DescriptorAndJsonContracts_AreExactAndComplete()
    {
        using var factory = new ProcurementWebApplicationFactory();
        _ = factory.CreateClient();
        factory.Services.GetRequiredService<ICapabilityRegistry>()
            .GetAll()
            .Select(item => item.Id)
            .Should().BeEquivalentTo(
            ProcurementContractIds.SubmitCapability,
            ProcurementContractIds.GetCapability,
            ProcurementContractIds.ApproveCapability,
            ProcurementContractIds.RejectCapability,
            "compat.appservice.procurement.submit",
            "compat.appservice.procurement.get",
            "compat.appservice.procurement.approve",
            "compat.appservice.procurement.reject");
        ProcurementJsonContext.Default.SubmitProcurementRequestInput.Should().NotBeNull();
        ProcurementJsonContext.Default.SubmitProcurementRequestResult.Should().NotBeNull();
        ProcurementJsonContext.Default.GetProcurementRequestInput.Should().NotBeNull();
        ProcurementJsonContext.Default.ApproveProcurementRequestInput.Should().NotBeNull();
        ProcurementJsonContext.Default.RejectProcurementRequestInput.Should().NotBeNull();
        ProcurementJsonContext.Default.ProcurementRequestResult.Should().NotBeNull();
    }
}
