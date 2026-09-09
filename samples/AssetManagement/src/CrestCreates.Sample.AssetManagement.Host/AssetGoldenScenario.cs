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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.Host;

public static class AssetGoldenScenario
{
    public static async Task<int> RunCandidateV2Async(WebApplication app)
    {
        try
        {
            EnsureDurableRuntime(app);
            using var client = CreateClient(app);

            // Each case uses a distinct asset so the SQLite terminal records
            // and the durable Workflow/HumanTask observations are independent.
            var approvedCase = await StartCandidateMaintenanceAsync(client, "candidate-approve");
            await CompleteCandidateAsync(app, approvedCase.HumanTaskId!, "Approve", "Initial review approved");
            var approvedInitial = await GetCandidateTaskAsync(app, approvedCase.HumanTaskId!);
            if (approvedInitial is null)
                throw new InvalidOperationException("Candidate initial approval task was not persisted.");
            EnsureInitialCandidateContract(app, approvedInitial);
            var approvedFinalTasks = await WaitForCandidateFinalTasksAsync(app, approvedInitial!.WorkflowKey!.Value);
            Ensure(approvedFinalTasks.Count == 1 && approvedFinalTasks[0].HumanTaskId == AssetContractIds.MaintenanceHumanTask,
                "Initial approval must leave the asset pending with exactly one final HumanTask.");
            var approvedWorkflowBeforeFinal = await GetCandidateWorkflowAsync(app, approvedInitial.WorkflowKey.Value);
            Ensure(approvedWorkflowBeforeFinal is not null
                && approvedWorkflowBeforeFinal.WorkflowPin.Ref.Id == AssetContractIds.MaintenanceWorkflow
                && approvedWorkflowBeforeFinal.WorkflowPin.Ref.Version == 2,
                "Candidate initial approval must use the compiled v2 Workflow pin.");
            var pending = await GetAssetAsync(client, approvedCase.AssetId);
            Ensure(pending.Status == "MaintenancePending", "Initial approval must leave the asset pending.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, approvedCase.AssetId, approved: true) == 0
                && await ReadMaintenanceRecordCountAsync(app, approvedCase.AssetId, approved: false) == 0,
                "Initial approval must not create a terminal maintenance record.");

            await CompleteCandidateAsync(app, approvedFinalTasks[0].Id, "Approve", "Final review approved");
            var approvedWorkflow = await WaitForCandidateWorkflowCompletedAsync(app, approvedInitial.WorkflowKey.Value);
            var approvedTerminal = await GetAssetAsync(client, approvedCase.AssetId);
            Ensure(approvedTerminal.Status == "Available" && approvedTerminal.MaintenanceWorkflowInstanceId is null,
                "Final approval must restore the asset to Available and clear its workflow.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, approvedCase.AssetId, approved: true) == 1,
                "Final approval must create exactly one approved maintenance record.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, approvedCase.AssetId, approved: false) == 0,
                "Final approval must not create a rejected maintenance record.");
            Ensure(approvedWorkflow.Status == WorkflowInstanceStatus.Completed,
                "Final approval must complete the candidate Workflow.");

            var assignedCase = await StartCandidateMaintenanceAsync(client, "candidate-assigned-approve", assigned: true);
            await CompleteCandidateAsync(app, assignedCase.HumanTaskId!, "Approve", "Assigned initial review approved");
            var assignedInitial = await GetCandidateTaskAsync(app, assignedCase.HumanTaskId!);
            if (assignedInitial is null || assignedInitial.WorkflowKey is not { } assignedWorkflowKey)
                throw new InvalidOperationException("Assigned candidate initial task was not correlated to a Workflow.");
            EnsureInitialCandidateContract(app, assignedInitial);
            var assignedFinalTasks = await WaitForCandidateFinalTasksAsync(app, assignedWorkflowKey);
            Ensure(assignedFinalTasks.Count == 1, "Assigned initial approval must create exactly one final HumanTask.");
            var assignedPending = await GetAssetAsync(client, assignedCase.AssetId);
            Ensure(assignedPending.Status == "MaintenancePending",
                "Assigned initial approval must leave the asset pending.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, assignedCase.AssetId, approved: true) == 0
                && await ReadMaintenanceRecordCountAsync(app, assignedCase.AssetId, approved: false) == 0,
                "Assigned initial approval must not create a terminal maintenance record.");
            await CompleteCandidateAsync(app, assignedFinalTasks[0].Id, "Approve", "Assigned final review approved");
            var assignedWorkflow = await WaitForCandidateWorkflowCompletedAsync(app, assignedWorkflowKey);
            var assignedTerminal = await GetAssetAsync(client, assignedCase.AssetId);
            Ensure(assignedTerminal.Status == "Assigned"
                && assignedTerminal.AssignedUserId == "assigned-user"
                && assignedTerminal.ActiveAssignmentId == assignedCase.ActiveAssignmentId
                && assignedTerminal.MaintenanceWorkflowInstanceId is null,
                "Assigned final approval must restore the prior assignment without losing its identity.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, assignedCase.AssetId, approved: true) == 1
                && await ReadMaintenanceRecordCountAsync(app, assignedCase.AssetId, approved: false) == 0,
                "Assigned final approval must create exactly one approved maintenance record.");
            Ensure(assignedWorkflow.Status == WorkflowInstanceStatus.Completed,
                "Assigned final approval must complete the candidate Workflow.");

            var initialRejectedCase = await StartCandidateMaintenanceAsync(client, "candidate-initial-reject");
            await CompleteCandidateAsync(app, initialRejectedCase.HumanTaskId!, "Reject", "Initial review rejected");
            var initialRejected = await GetCandidateTaskAsync(app, initialRejectedCase.HumanTaskId!);
            if (initialRejected is null || initialRejected.WorkflowKey is not { } initialRejectedWorkflow)
                throw new InvalidOperationException("Initial rejection task was not correlated to a Workflow.");
            EnsureInitialCandidateContract(app, initialRejected);
            var initialRejectedWorkflowState = await WaitForCandidateWorkflowCompletedAsync(app, initialRejectedWorkflow);
            var rejectedPending = await GetCandidatePendingTasksAsync(app, initialRejectedWorkflow);
            Ensure(rejectedPending.Count == 0, "Initial rejection must not create a final HumanTask.");
            Ensure(initialRejectedWorkflowState.Status == WorkflowInstanceStatus.Completed,
                "Initial rejection must complete the candidate Workflow before terminal assertions.");
            var rejectedFinalStep = initialRejectedWorkflowState.StepResults.SingleOrDefault(step => step.StepId == "maintenance-final-review");
            Ensure(rejectedFinalStep?.Status == StepExecutionStatus.Skipped,
                "Initial rejection must record the candidate final review step as Skipped.");
            var rejectedAsset = await GetAssetAsync(client, initialRejectedCase.AssetId);
            Ensure(rejectedAsset.Status == "Available", "Initial rejection must restore the asset to Available.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, initialRejectedCase.AssetId, approved: false) == 1,
                "Initial rejection must create one rejected maintenance record.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, initialRejectedCase.AssetId, approved: true) == 0,
                "Initial rejection must not create an approved maintenance record.");

            var finalRejectedCase = await StartCandidateMaintenanceAsync(client, "candidate-final-reject");
            await CompleteCandidateAsync(app, finalRejectedCase.HumanTaskId!, "Approve", "Initial review approved");
            var finalRejectedInitial = await GetCandidateTaskAsync(app, finalRejectedCase.HumanTaskId!);
            if (finalRejectedInitial is null || finalRejectedInitial.WorkflowKey is not { } finalRejectedWorkflow)
                throw new InvalidOperationException("Final rejection case initial task was not correlated to a Workflow.");
            EnsureInitialCandidateContract(app, finalRejectedInitial);
            var finalRejectedTasks = await WaitForCandidateFinalTasksAsync(app, finalRejectedWorkflow);
            Ensure(finalRejectedTasks.Count == 1, "Final rejection case must create exactly one final HumanTask.");
            await CompleteCandidateAsync(app, finalRejectedTasks[0].Id, "Reject", "Final review rejected");
            var finalRejectedWorkflowState = await WaitForCandidateWorkflowCompletedAsync(app, finalRejectedWorkflow);
            var finalRejectedAsset = await GetAssetAsync(client, finalRejectedCase.AssetId);
            Ensure(finalRejectedAsset.Status == "Available" && finalRejectedAsset.MaintenanceWorkflowInstanceId is null,
                "Final rejection must restore the asset to Available and clear its workflow.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, finalRejectedCase.AssetId, approved: true) == 0,
                "Final rejection must not create an approved maintenance record.");
            Ensure(await ReadMaintenanceRecordCountAsync(app, finalRejectedCase.AssetId, approved: false) == 1,
                "Final rejection must create exactly one rejected maintenance record.");
            Ensure(finalRejectedWorkflowState.Status == WorkflowInstanceStatus.Completed,
                "Final rejection must complete the candidate Workflow.");

            Console.WriteLine("CRESTCREATES_ASSET_MANAGEMENT_CANDIDATE_V2_GOLDEN_OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

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

    private static void EnsureDurableRuntime(WebApplication app)
    {
        var runtimeCapabilities = app.Services.GetRequiredService<IRuntimePersistenceProviderCapabilities>();
        if (runtimeCapabilities is not PostgreSqlRuntimeProviderCapabilities
            || runtimeCapabilities.Tier != RuntimePersistenceProviderTier.FullDurable
            || !runtimeCapabilities.SupportsAtomicMultiStoreTransactions
            || !runtimeCapabilities.SupportsRestartRecovery
            || app.Services.GetRequiredService<IWorkflowInstanceStore>().GetType().Name.Contains("InMemory", StringComparison.Ordinal)
            || app.Services.GetRequiredService<IHumanTaskInstanceStore>().GetType().Name.Contains("InMemory", StringComparison.Ordinal)
            || app.Services.GetRequiredService<IOutboxDispatchStore>().GetType().Name.Contains("InMemory", StringComparison.Ordinal))
            throw new InvalidOperationException("Candidate verification requires the durable PostgreSQL runtime stores.");
    }

    private static async Task<(Guid AssetId, string? HumanTaskId, Guid? ActiveAssignmentId)> StartCandidateMaintenanceAsync(HttpClient client, string suffix, bool assigned = false)
    {
        SetIdentity(client, "candidate-tenant", "manager-1", "asset-manager", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Tenant");
        var asset = await SendAsync<AssetResult, RegisterAssetInput>(client, HttpMethod.Post, "/api/assets", new RegisterAssetInput
        {
            AssetTag = $"CANDIDATE-{suffix}-{Guid.NewGuid():N}",
            Name = "Candidate verification asset",
            Description = "Candidate native verification",
            Category = "Equipment",
            OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Location = "Shanghai"
        }, AssetJsonContext.Default.RegisterAssetInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.Created);
        Guid? activeAssignmentId = null;
        if (assigned)
        {
            var assignment = await SendAsync<AssetResult, AssignAssetInput>(client, HttpMethod.Post,
                $"/api/assets/{asset.Id}/assign", new AssignAssetInput
                {
                    AssetId = asset.Id,
                    UserId = "assigned-user",
                    OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                }, AssetJsonContext.Default.AssignAssetInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
            Ensure(assignment.Status == "Assigned" && assignment.AssignedUserId == "assigned-user" && assignment.ActiveAssignmentId is not null,
                "Candidate Assigned setup must persist the original assignment.");
            activeAssignmentId = assignment.ActiveAssignmentId;
        }
        var operation = await SendAsync<AssetOperationResult, MaintenanceRequestInput>(client, HttpMethod.Post,
            $"/api/assets/{asset.Id}/maintenance", new MaintenanceRequestInput { AssetId = asset.Id, Reason = suffix },
            AssetJsonContext.Default.MaintenanceRequestInput, AssetJsonContext.Default.AssetOperationResult, HttpStatusCode.Accepted);
        Ensure(operation.Status == "MaintenancePending" && !string.IsNullOrWhiteSpace(operation.HumanTaskId),
            "Candidate maintenance request must suspend on an initial HumanTask.");
        return (asset.Id, operation.HumanTaskId, activeAssignmentId);
    }

    private static async Task CompleteCandidateAsync(WebApplication app, string taskId, string outcome, string note)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>().Set(
            "candidate-tenant", "manager-1", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CrestCreates.Domain.Shared.Enums.DataScope.Tenant, "asset-manager");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>()
            .CompleteAsync(taskId, outcome, note, timeout.Token);
    }

    private static async Task<AssetResult> GetAssetAsync(HttpClient client, Guid assetId)
    {
        SetIdentity(client, "candidate-tenant", "manager-1", "asset-manager", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Tenant");
        return await SendAsync<AssetResult, AssetQueryInput>(client, HttpMethod.Get, $"/api/assets/{assetId}", null,
            AssetJsonContext.Default.AssetQueryInput, AssetJsonContext.Default.AssetResult, HttpStatusCode.OK);
    }

    private static async Task<HumanTaskInstance?> GetCandidateTaskAsync(WebApplication app, string taskId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>()
            .GetAsync(new RuntimeInstanceKey("candidate-tenant", taskId));
    }

    private static async Task<IReadOnlyList<HumanTaskInstance>> GetCandidatePendingTasksAsync(WebApplication app, RuntimeInstanceKey workflowKey)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>().GetPendingByWorkflowAsync(workflowKey);
    }

    private static async Task<IReadOnlyList<HumanTaskInstance>> WaitForCandidateFinalTasksAsync(WebApplication app, RuntimeInstanceKey workflowKey)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var pending = await GetCandidatePendingTasksAsync(app, workflowKey);
            if (pending.Count == 1 && pending[0].HumanTaskId == AssetContractIds.MaintenanceHumanTask)
                return pending;
            await Task.Delay(20);
        }
        throw new TimeoutException("Candidate final maintenance HumanTask was not created.");
    }

    private static async Task<WorkflowInstance?> GetCandidateWorkflowAsync(WebApplication app, RuntimeInstanceKey workflowKey)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>().GetAsync(workflowKey);
    }

    private static async Task<WorkflowInstance> WaitForCandidateWorkflowCompletedAsync(WebApplication app, RuntimeInstanceKey workflowKey)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var workflow = await GetCandidateWorkflowAsync(app, workflowKey);
            if (workflow?.Status == WorkflowInstanceStatus.Completed)
                return workflow;
            await Task.Delay(20);
        }
        throw new TimeoutException("Candidate Workflow did not reach Completed before the assertion deadline.");
    }

    private static void EnsureInitialCandidateContract(WebApplication app, HumanTaskInstance task)
    {
        if (task.HumanTaskId != AssetContractIds.MaintenanceInitialHumanTask || task.HumanTaskVersion != 1)
            throw new InvalidOperationException("Candidate maintenance must begin with the compiled initial HumanTask contract.");
        using var scope = app.Services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<AssetMaintenanceTaskContractResolver>().Resolve(task.HumanTaskPin) != AssetMaintenanceTaskRole.Initial)
            throw new InvalidOperationException("Candidate maintenance initial HumanTask role could not be resolved.");
    }

    private static async Task<int> ReadMaintenanceRecordCountAsync(WebApplication app, Guid assetId, bool approved)
    {
        var path = app.Configuration["AssetManagement:DatabasePath"]
            ?? throw new InvalidOperationException("Candidate verification database path is unavailable.");
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MaintenanceRecords WHERE AssetId=$asset AND Approved=$approved";
        command.Parameters.AddWithValue("$asset", assetId.ToString("D"));
        command.Parameters.AddWithValue("$approved", approved ? 1 : 0);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
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
