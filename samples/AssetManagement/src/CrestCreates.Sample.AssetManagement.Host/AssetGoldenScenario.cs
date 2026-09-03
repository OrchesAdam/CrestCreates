using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using CrestCreates.Agent.Tools;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Contracts.Json;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.Host;

public static class AssetGoldenScenario
{
    public static async Task<int> RunAsync(WebApplication app)
    {
        try
        {
            var runtimeCapabilities = app.Services.GetRequiredService<IRuntimePersistenceProviderCapabilities>();
            if (runtimeCapabilities is not PostgreSqlRuntimeProviderCapabilities
                || runtimeCapabilities.Tier != RuntimePersistenceProviderTier.FullDurable
                || !runtimeCapabilities.SupportsAtomicMultiStoreTransactions
                || !runtimeCapabilities.SupportsRestartRecovery
                || app.Services.GetRequiredService<IWorkflowInstanceStore>().GetType().Name.Contains("InMemory", StringComparison.Ordinal)
                || app.Services.GetRequiredService<IHumanTaskInstanceStore>().GetType().Name.Contains("InMemory", StringComparison.Ordinal)
                || app.Services.GetRequiredService<IOutboxDispatchStore>().GetType().Name.Contains("InMemory", StringComparison.Ordinal))
                return 18;
            if (app.Services.GetRequiredService<IPermissionChecker>() is not PermissionChecker
                || app.Services.GetRequiredService<IPermissionGrantStore>() is not PermissionGrantStore
                || app.Services.GetRequiredService<IPermissionGrantManager>() is not PermissionGrantManager)
                return 22;
            using var client = CreateClient(app);
            SetIdentity(client, "golden-tenant", "manager-1", "asset-manager", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Tenant");
            var registered = await SendAsync<AssetResult, RegisterAssetInput>(client, HttpMethod.Post, "/api/assets", new RegisterAssetInput
            {
                AssetTag = "LAPTOP-001", Name = "Engineering laptop", Description = "Golden scenario asset", Category = "Equipment", OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Location = "Shanghai"
            }, AssetJsonContext.Default.RegisterAssetInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.Created);
            var assetId = registered.Id;

            SetIdentity(client, "golden-tenant", "user-1", "asset-user", null, "Organization");
            var missingOrganization = await client.GetAsync($"/api/assets/{assetId}");
            if (missingOrganization.StatusCode != HttpStatusCode.NotFound)
                return 19;
            SetIdentity(client, "golden-tenant", "user-1", "asset-user", registered.OrganizationId, "Organization");
            var read = await SendAsync<AssetResult, AssetQueryInput>(client, HttpMethod.Get, $"/api/assets/{assetId}", null, AssetJsonContext.Default.AssetQueryInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
            if (read.AssetTag != "LAPTOP-001") return 2;
            var list = await SendAsync<List<AssetResult>, AssetQueryInput>(client, HttpMethod.Post, "/api/assets/query", new AssetQueryInput(), AssetJsonContext.Default.AssetQueryInput, AssetJsonContext.Default.ListAssetResult, HttpStatusCode.OK);
            if (list.Count != 1 || list[0].Id != assetId) return 3;

            SetIdentity(client, "other-tenant", "user-2", "asset-user", registered.OrganizationId, "Organization");
            var isolated = await client.GetAsync($"/api/assets/{assetId}");
            if (isolated.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)) return 4;

            SetIdentity(client, "golden-tenant", "manager-1", "asset-manager", null, "Tenant");
            await SendAsync<AssetResult, RegisterAssetInput>(client, HttpMethod.Post, "/api/assets", new RegisterAssetInput
            {
                AssetTag = "MONITOR-001", Name = "Engineering monitor", Description = "Organization scope fixture", Category = "Equipment", OrganizationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Location = "Shanghai"
            }, AssetJsonContext.Default.RegisterAssetInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.Created);

            SetIdentity(client, "golden-tenant", "user-1", "asset-user", registered.OrganizationId, "Organization");
            var unauthorizedAssign = await SendStatusAsync(client, HttpMethod.Post, $"/api/assets/{assetId}/assign", new AssignAssetInput { UserId = "user-1", OrganizationId = registered.OrganizationId!.Value }, AssetJsonContext.Default.AssignAssetInput);
            if (unauthorizedAssign != HttpStatusCode.Forbidden) return 5;
            var unauthorizedTransfer = await SendStatusAsync(client, HttpMethod.Post, $"/api/assets/{assetId}/transfer", new TransferAssetInput { OrganizationId = registered.OrganizationId!.Value, Location = "Shanghai" }, AssetJsonContext.Default.TransferAssetInput);
            if (unauthorizedTransfer != HttpStatusCode.Forbidden) return 6;
            var scopedList = await SendAsync<List<AssetResult>, AssetQueryInput>(client, HttpMethod.Post, "/api/assets/query", new AssetQueryInput(), AssetJsonContext.Default.AssetQueryInput, AssetJsonContext.Default.ListAssetResult, HttpStatusCode.OK);
            if (scopedList.Count != 1 || scopedList[0].Id != assetId) return 7;

            SetIdentity(client, "golden-tenant", "manager-1", "asset-manager", registered.OrganizationId, "Tenant");
            var assigned = await SendAsync<AssetResult, AssignAssetInput>(client, HttpMethod.Post, $"/api/assets/{assetId}/assign", new AssignAssetInput { UserId = "user-1", OrganizationId = registered.OrganizationId!.Value }, AssetJsonContext.Default.AssignAssetInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
            if (assigned.Status != "Assigned" || assigned.AssignedUserId != "user-1") return 8;
            var persistedAssigned = await SendAsync<AssetResult, AssetQueryInput>(client, HttpMethod.Get, $"/api/assets/{assetId}", null, AssetJsonContext.Default.AssetQueryInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
            if (persistedAssigned.Status != "Assigned" || persistedAssigned.ActiveAssignmentId is null) return 23;
            var assignedTransfer = await SendStatusAsync(client, HttpMethod.Post, $"/api/assets/{assetId}/transfer", new TransferAssetInput { AssetId = assetId, OrganizationId = registered.OrganizationId!.Value, Location = "Shanghai" }, AssetJsonContext.Default.TransferAssetInput);
            if (assignedTransfer != HttpStatusCode.Conflict) return 20;
            var assignedMaintenance = await SendAsync<AssetOperationResult, MaintenanceRequestInput>(client, HttpMethod.Post, $"/api/assets/{assetId}/maintenance", new MaintenanceRequestInput { AssetId = assetId, Reason = "Assigned battery replacement" }, AssetJsonContext.Default.MaintenanceRequestInput, AssetJsonContext.Default.AssetOperationResult, HttpStatusCode.Accepted);
            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>().Set("golden-tenant", "manager-1", registered.OrganizationId, CrestCreates.Domain.Shared.Enums.DataScope.Tenant, "asset-manager");
                await scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>().CompleteAsync(assignedMaintenance.HumanTaskId!, "Approve", "Assigned maintenance approved");
            }
            var assignedAfterMaintenance = await SendAsync<AssetResult, AssetQueryInput>(client, HttpMethod.Get, $"/api/assets/{assetId}", null, AssetJsonContext.Default.AssetQueryInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
            if (assignedAfterMaintenance.Status != "Assigned" || assignedAfterMaintenance.AssignedUserId != "user-1" || assignedAfterMaintenance.ActiveAssignmentId is null) return 21;
            var returned = await SendAsync<AssetResult, AssetIdInput>(client, HttpMethod.Post, $"/api/assets/{assetId}/return", null, AssetJsonContext.Default.AssetIdInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
            if (returned.Status != "Available") return 9;

            var maintenance = await SendAsync<AssetOperationResult, MaintenanceRequestInput>(client, HttpMethod.Post, $"/api/assets/{assetId}/maintenance", new MaintenanceRequestInput { Reason = "Battery replacement" }, AssetJsonContext.Default.MaintenanceRequestInput, AssetJsonContext.Default.AssetOperationResult, HttpStatusCode.Accepted);
            if (maintenance.Status != "MaintenancePending" || string.IsNullOrWhiteSpace(maintenance.HumanTaskId)) return 10;
            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>().Set("golden-tenant", "manager-1", registered.OrganizationId, CrestCreates.Domain.Shared.Enums.DataScope.Tenant, "asset-manager");
                await scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>().CompleteAsync(maintenance.HumanTaskId!, "Approve", "Battery replacement approved");
            }
            var final = await SendAsync<AssetResult, AssetQueryInput>(client, HttpMethod.Get, $"/api/assets/{assetId}", null, AssetJsonContext.Default.AssetQueryInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
            if (final.Status != "Available" || final.MaintenanceWorkflowInstanceId is not null) return 11;

            using (var scope = app.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>();
                identity.Set("golden-tenant", "user-1", registered.OrganizationId, CrestCreates.Domain.Shared.Enums.DataScope.Organization, "asset-user");
                var principalAccessor = scope.ServiceProvider.GetRequiredService<ICurrentPrincipalAccessor>();
                using var principalScope = principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Role, "asset-user")], "golden-mcp")));
                var mcp = await scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>().InvokeAsync(AssetContractIds.GetTool, JsonSerializer.SerializeToElement(new AssetQueryInput { AssetId = assetId }, AssetJsonContext.Default.AssetQueryInput), new McpToolCallContext(new McpToolHostContext("asset-golden", "native-aot"), "mcp-1", "mcp-request-1"));
                if (mcp.IsError)
                    throw new InvalidOperationException($"MCP get failed ({mcp.ErrorCode}): {string.Join(" | ", mcp.Content.OfType<McpToolTextContent>().Select(content => content.Text))}");
                if (mcp.StructuredContent is null || !mcp.StructuredContent.Value.TryGetProperty("id", out var mcpId) || mcpId.GetGuid() != assetId)
                    throw new InvalidOperationException($"MCP get returned an unexpected structured result: {mcp.StructuredContent}");
                identity.SetAgent("asset-agent-execution", "asset-agent-invocation");
                var tools = await scope.ServiceProvider.GetRequiredService<IAgentToolCatalog>().ListAsync();
                if (!tools.Any(tool => tool.ToolName == AssetContractIds.GetTool)) return 13;
                using var agentArguments = JsonDocument.Parse(JsonSerializer.Serialize(new AssetQueryInput { AssetId = assetId }, AssetJsonContext.Default.AssetQueryInput));
                var agent = await scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>().InvokeAsync(new AgentToolInvocationRequest(AssetContractIds.GetTool, agentArguments.RootElement.Clone()));
                if (!agent.IsSuccess)
                    throw new InvalidOperationException($"Agent get failed ({agent.Code}): {agent.Message}");
                if (agent.StructuredOutput is null || !agent.StructuredOutput.Value.TryGetProperty("id", out var agentId) || agentId.GetGuid() != assetId)
                    throw new InvalidOperationException($"Agent get returned an unexpected structured result: {agent.StructuredOutput}");
            }

            var accountabilityRecords = app.Services.GetRequiredService<InMemoryAuditSink>().GetRecords();
            var sources = accountabilityRecords.Select(record => record.Runtime.InvocationSource).ToHashSet(StringComparer.Ordinal);
            if (!sources.Contains("http") || !sources.Contains("mcp") || !sources.Contains("agent") || !sources.Contains("human-task"))
                return 15;
            if (!accountabilityRecords.Any(record => record.Action.Kind == "capability.execute" && record.Action.Name == AssetContractIds.RegisterCapability))
                return 16;
            if (!accountabilityRecords.Any(record => record.Action.Kind == "capability.execute" && record.Action.Name == AssetContractIds.ApplyMaintenanceCapability))
                return 17;

            Console.WriteLine("CRESTCREATES_ASSET_MANAGEMENT_GOLDEN_OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<HttpStatusCode> SendStatusAsync<TInput>(HttpClient client, HttpMethod method, string uri, TInput input, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TInput> inputTypeInfo)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(input, inputTypeInfo), Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault()
            ?? throw new InvalidOperationException("Asset golden scenario listener is unavailable.");
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static async Task<TResponse> SendAsync<TResponse, TInput>(HttpClient client, HttpMethod method, string uri, TInput? input, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TInput> inputTypeInfo, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResponse> responseTypeInfo, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (input is not null)
        {
            var json = JsonSerializer.Serialize(input, inputTypeInfo);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        using var response = await client.SendAsync(request);
        if (response.StatusCode != expected)
            throw new InvalidOperationException($"Expected {expected} from {method} {uri}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), responseTypeInfo) ?? throw new InvalidOperationException("Expected response body.");
    }

    private static void SetIdentity(HttpClient client, string tenant, string user, string role, Guid? organization, string dataScope)
    {
        foreach (var name in new[] { AssetExecutionIdentity.TenantHeader, AssetExecutionIdentity.UserHeader, AssetExecutionIdentity.RolesHeader, AssetExecutionIdentity.OrganizationHeader, AssetExecutionIdentity.DataScopeHeader }) client.DefaultRequestHeaders.Remove(name);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.TenantHeader, tenant);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.UserHeader, user);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.RolesHeader, role);
        if (organization is Guid id) client.DefaultRequestHeaders.Add(AssetExecutionIdentity.OrganizationHeader, id.ToString());
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.DataScopeHeader, dataScope);
    }
}
