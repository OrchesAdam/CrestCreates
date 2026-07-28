using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Host;

public static class ProcurementGoldenScenario
{
    public static async Task<int> RunAsync(WebApplication app)
    {
        try
        {
            var services = app.Services;
            using var client = CreateHttpClient(app);
            SetIdentity(client, "golden-tenant", "requester-1", "procurement-requester");
            var submitInput = new SubmitProcurementRequestInput
            {
                Title = "NativeAOT server rack",
                Description = "Golden scenario",
                Amount = 25_000m,
                Currency = "USD",
                Category = "Infrastructure"
            };
            using var submitContent = JsonContent.Create(
                submitInput,
                ProcurementJsonContext.Default.SubmitProcurementRequestInput);
            using var submitResponse = await client.PostAsync(
                "/api/procurement/requests",
                submitContent).ConfigureAwait(false);
            if (submitResponse.StatusCode != HttpStatusCode.Created)
                return 2;
            var submitResult = JsonSerializer.Deserialize(
                await submitResponse.Content.ReadAsStringAsync().ConfigureAwait(false),
                ProcurementJsonContext.Default.SubmitProcurementRequestResult);
            if (submitResult is null || submitResult.Status != "PendingApproval")
                return 2;

            using var nativeGetResponse = await client.GetAsync(
                $"/api/procurement/requests/{submitResult.RequestId}").ConfigureAwait(false);
            if (nativeGetResponse.StatusCode != HttpStatusCode.OK)
                return 2;
            var nativePending = JsonSerializer.Deserialize(
                await nativeGetResponse.Content.ReadAsStringAsync().ConfigureAwait(false),
                ProcurementJsonContext.Default.ProcurementRequestResult);
            if (nativePending?.RequestId != submitResult.RequestId
                || nativePending.Status != "PendingApproval")
                return 2;

            using var compatibilityResponse = await client.GetAsync(
                $"/api/procurement/query/{submitResult.RequestId}").ConfigureAwait(false);
            if (compatibilityResponse.StatusCode != HttpStatusCode.OK)
                return 2;
            using (var compatibilityJson = JsonDocument.Parse(
                       await compatibilityResponse.Content.ReadAsStringAsync().ConfigureAwait(false)))
            {
                if (compatibilityJson.RootElement.GetProperty("data")
                        .GetProperty("requestId").GetGuid() != submitResult.RequestId)
                    return 2;
            }
            Console.WriteLine("CRESTCREATES_PROCUREMENT_HTTP_OK");

            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;
            var identity = provider.GetRequiredService<SampleExecutionIdentity>();
            var store = provider.GetRequiredService<InMemoryProcurementRequestStore>();

            identity.Set("golden-tenant", "requester-1", "procurement-requester");
            var getInput = new GetProcurementRequestInput { RequestId = submitResult.RequestId };
            var mcp = provider.GetRequiredService<IMcpToolInvoker>();
            var mcpResult = await mcp.InvokeAsync(
                ProcurementContractIds.GetTool,
                JsonSerializer.SerializeToElement(getInput, ProcurementJsonContext.Default.GetProcurementRequestInput),
                new McpToolCallContext(
                    new McpToolHostContext("procurement-golden", "native-aot"),
                    "mcp-invocation-1",
                    "mcp-request-1"));
            if (mcpResult.IsError
                || mcpResult.StructuredContent?.GetProperty("requestId").GetGuid() != submitResult.RequestId)
                return 3;

            var workflowInstanceId = store.GetById("golden-tenant", submitResult.RequestId)!.WorkflowInstanceId!;
            var pending = await provider.GetRequiredService<IHumanTaskInstanceStore>()
                .GetPendingByWorkflowAsync(workflowInstanceId);
            if (pending.Count != 1)
                return 4;

            identity.Set("golden-tenant", "manager-1", "procurement-manager");
            await provider.GetRequiredService<ProcurementApprovalTaskService>()
                .CompleteAsync(pending[0].Id, "Approve", "Approved in native golden scenario");

            SetIdentity(client, "golden-tenant", "manager-1", "procurement-manager");
            using var approvedResponse = await client.GetAsync(
                $"/api/procurement/requests/{submitResult.RequestId}").ConfigureAwait(false);
            if (approvedResponse.StatusCode != HttpStatusCode.OK)
                return 5;
            var approvedRequest = JsonSerializer.Deserialize(
                await approvedResponse.Content.ReadAsStringAsync().ConfigureAwait(false),
                ProcurementJsonContext.Default.ProcurementRequestResult);
            if (approvedRequest is null
                || approvedRequest.Status != "Approved"
                || approvedRequest.ApproverId != "manager-1")
                return 5;

            identity.Set("golden-tenant", "agent-user", "procurement-requester");
            identity.SetAgent("golden-agent-execution", "golden-agent-invocation");
            var catalog = await provider.GetRequiredService<IAgentToolCatalog>().ListAsync();
            if (catalog.Select(tool => tool.ToolName).Order().ToArray() is not
                [ProcurementContractIds.GetTool, ProcurementContractIds.SubmitTool])
                return 6;

            var agentInput = new SubmitProcurementRequestInput
            {
                Title = "Agent-created request",
                Description = "Governed native invocation",
                Amount = 15_000m,
                Currency = "USD",
                Category = "Infrastructure"
            };
            using var agentArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(agentInput, ProcurementJsonContext.Default.SubmitProcurementRequestInput));
            var request = new AgentToolInvocationRequest(
                ProcurementContractIds.SubmitTool,
                agentArguments.RootElement.Clone(),
                SampleAgentToolApprovalGate.ApprovedEvidence);
            var before = store.Count;
            var agent = provider.GetRequiredService<IAgentToolInvoker>();
            var first = await agent.InvokeAsync(request);
            var replay = await agent.InvokeAsync(request);
            if (!first.IsSuccess || !replay.IsSuccess || store.Count != before + 1)
                return 7;

            var sources = services.GetRequiredService<ICapabilityAuditStore>()
                .As<InMemoryCapabilityAuditStore>()
                .GetRecords()
                .Select(record => record.Source)
                .ToHashSet();
            if (!sources.Contains(InvocationSource.Http)
                || !sources.Contains(InvocationSource.Mcp)
                || !sources.Contains(InvocationSource.Agent)
                || !sources.Contains(InvocationSource.HumanTask))
                return 8;

            Console.WriteLine("CRESTCREATES_PROCUREMENT_SAMPLE_OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static T As<T>(this object value) where T : class
        => value as T ?? throw new InvalidOperationException($"Expected service '{typeof(T).Name}'.");

    private static HttpClient CreateHttpClient(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.SingleOrDefault()
            ?? throw new InvalidOperationException("Golden HTTP listener address is unavailable.");
        return new HttpClient { BaseAddress = new Uri(address, UriKind.Absolute) };
    }

    private static void SetIdentity(HttpClient client, string tenantId, string userId, string role)
    {
        client.DefaultRequestHeaders.Remove(SampleExecutionIdentity.TenantHeader);
        client.DefaultRequestHeaders.Remove(SampleExecutionIdentity.UserHeader);
        client.DefaultRequestHeaders.Remove(SampleExecutionIdentity.RolesHeader);
        client.DefaultRequestHeaders.Add(SampleExecutionIdentity.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(SampleExecutionIdentity.UserHeader, userId);
        client.DefaultRequestHeaders.Add(SampleExecutionIdentity.RolesHeader, role);
    }
}
