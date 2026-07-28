using System.Text.Json;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Host;

public static class ProcurementGoldenScenario
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        try
        {
            InitializeProjectionSnapshots(services);
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;
            var identity = provider.GetRequiredService<SampleExecutionIdentity>();
            var dispatcher = provider.GetRequiredService<ICapabilityDispatcher>();
            var store = provider.GetRequiredService<InMemoryProcurementRequestStore>();

            identity.Set("golden-tenant", "requester-1", "procurement-requester");
            var submitInput = new SubmitProcurementRequestInput
            {
                Title = "NativeAOT server rack",
                Description = "Golden scenario",
                Amount = 25_000m,
                Currency = "USD",
                Category = "Infrastructure"
            };
            var submitted = await dispatcher.DispatchAsync(
                ProcurementContractIds.SubmitCapability,
                InvocationSource.Http,
                submitInput,
                context => context.InputJson = JsonSerializer.SerializeToElement(
                    submitInput,
                    ProcurementJsonContext.Default.SubmitProcurementRequestInput));
            if (!submitted.IsSuccess || submitted.Output is not SubmitProcurementRequestResult)
            {
                Console.Error.WriteLine($"Submit failed: {submitted.ErrorCode} {submitted.ErrorMessage}");
                return 2;
            }
            var submitResult = (SubmitProcurementRequestResult)submitted.Output;
            if (submitResult.Status != "PendingApproval")
            {
                Console.Error.WriteLine($"Unexpected submit status: {submitResult.Status}");
                return 2;
            }

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

            var approved = await dispatcher.DispatchAsync(
                ProcurementContractIds.GetCapability,
                InvocationSource.Http,
                getInput,
                context => context.InputJson = JsonSerializer.SerializeToElement(
                    getInput,
                    ProcurementJsonContext.Default.GetProcurementRequestInput));
            if (!approved.IsSuccess || approved.Output is not ProcurementRequestResult)
                return 5;
            var approvedRequest = (ProcurementRequestResult)approved.Output;
            if (approvedRequest.Status != "Approved" || approvedRequest.ApproverId != "manager-1")
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

    private static void InitializeProjectionSnapshots(IServiceProvider services)
    {
        var mcpRegistry = services.GetRequiredService<McpToolRegistry>();
        mcpRegistry.Build(DescriptorProviderRegistry.GetProviders<McpToolDescriptor>());
        services.GetRequiredService<McpToolRuntimeSnapshotProvider>()
            .Publish(services.GetRequiredService<McpToolRuntimeSnapshotBuilder>().Build());
        services.GetRequiredService<AgentToolProjectionStartupBuilder>().BuildAndPublish();
    }
}
