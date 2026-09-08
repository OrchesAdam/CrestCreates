using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Contracts.Json;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetTwoStageAcceptanceTests
{
    [Fact]
    public async Task CandidateV2_InitialApproval_MustLeaveAssetPendingAndCreateFinalTask()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        SetIdentity(client, "tenant-a", "manager-1", "asset-manager", "Tenant");

        var registered = await SendAsync<AssetResult>(client, HttpMethod.Post, "/api/assets", new RegisterAssetInput
        {
            AssetTag = "LAPTOP-TWO-STAGE",
            Name = "Two-stage laptop",
            Description = "Business acceptance fixture",
            Category = "Equipment",
            OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Location = "Shanghai"
        }, HttpStatusCode.Created);
        var request = await SendAsync<AssetOperationResult>(client, HttpMethod.Post,
            $"/api/assets/{registered.Id}/maintenance",
            new MaintenanceRequestInput { AssetId = registered.Id, Reason = "Two-stage review" },
            HttpStatusCode.Accepted);

        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>();
            identity.Set("tenant-a", "manager-1", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), DataScope.Tenant, "asset-manager");
            await scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>()
                .CompleteAsync(request.HumanTaskId!, "Approve", "Initial review approved",
                    new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        var pending = await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{registered.Id}", null, HttpStatusCode.OK);
        pending.Status.Should().Be(nameof(AssetStatus.MaintenancePending));

        using var inspection = factory.Services.CreateScope();
        var workflowStore = inspection.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        var taskStore = inspection.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var initial = await taskStore.GetAsync(new RuntimeInstanceKey("tenant-a", request.HumanTaskId!));
        initial.Should().NotBeNull();
        initial!.WorkflowKey.Should().NotBeNull();
        var finalTasks = await taskStore.GetPendingByWorkflowAsync(initial.WorkflowKey!.Value);
        finalTasks.Should().ContainSingle(task => task.HumanTaskId == AssetContractIds.MaintenanceHumanTask && task.HumanTaskVersion == 1);
        var workflow = await workflowStore.GetAsync(initial.WorkflowKey.Value);
        workflow.Should().NotBeNull();
        workflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    }

    [Fact]
    public async Task CandidateV2_FinalApproval_RestoresPriorStateAndWritesOneApprovedRecord()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);
        await CompleteAsync(factory, initial.HumanTaskId!, "Approve", "Initial review approved");

        using var inspection = factory.Services.CreateScope();
        var taskStore = inspection.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var initialTask = await taskStore.GetAsync(new RuntimeInstanceKey("tenant-a", initial.HumanTaskId!));
        initialTask.Should().NotBeNull();
        var finalTasks = await taskStore.GetPendingByWorkflowAsync(initialTask!.WorkflowKey!.Value);
        finalTasks.Should().ContainSingle();

        await CompleteAsync(factory, finalTasks[0].Id, "Approve", "Final review approved");
        var completed = await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{asset.Id}", null, HttpStatusCode.OK);
        completed.Status.Should().Be(nameof(AssetStatus.Available));
        completed.MaintenanceWorkflowInstanceId.Should().BeNull();
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: true)).Should().Be(1);
    }

    [Fact]
    public async Task CandidateV2_InitialRejection_RestoresPriorStateWithoutFinalTask()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);
        await CompleteAsync(factory, initial.HumanTaskId!, "Reject", "Initial review rejected");

        var completed = await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{asset.Id}", null, HttpStatusCode.OK);
        completed.Status.Should().Be(nameof(AssetStatus.Available));
        using var inspection = factory.Services.CreateScope();
        var taskStore = inspection.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var initialTask = await taskStore.GetAsync(new RuntimeInstanceKey("tenant-a", initial.HumanTaskId!));
        initialTask.Should().NotBeNull();
        (await taskStore.GetPendingByWorkflowAsync(initialTask!.WorkflowKey!.Value)).Should().BeEmpty();
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: true)).Should().Be(0);
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: false)).Should().Be(1);
    }

    [Fact]
    public async Task CandidateV2_FinalRejection_WritesRejectedRecordWithoutApprovedRecord()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);
        await CompleteAsync(factory, initial.HumanTaskId!, "Approve", "Initial review approved");

        using var inspection = factory.Services.CreateScope();
        var taskStore = inspection.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var initialTask = await taskStore.GetAsync(new RuntimeInstanceKey("tenant-a", initial.HumanTaskId!));
        var finalTasks = await taskStore.GetPendingByWorkflowAsync(initialTask!.WorkflowKey!.Value);
        await CompleteAsync(factory, finalTasks.Single().Id, "Reject", "Final review rejected");

        var completed = await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{asset.Id}", null, HttpStatusCode.OK);
        completed.Status.Should().Be(nameof(AssetStatus.Available));
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: true)).Should().Be(0);
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: false)).Should().Be(1);
    }

    [Fact]
    public async Task CandidateV2_RepeatedInitialApproval_CannotCreateTerminalDecisionOrDuplicateFinalTask()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);
        await CompleteAsync(factory, initial.HumanTaskId!, "Approve", "Initial review approved");

        Func<Task> replay = () => CompleteAsync(factory, initial.HumanTaskId!, "Approve", "Replay");
        await replay.Should().ThrowAsync<InvalidOperationException>();

        var pending = await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{asset.Id}", null, HttpStatusCode.OK);
        pending.Status.Should().Be(nameof(AssetStatus.MaintenancePending));
        using var inspection = factory.Services.CreateScope();
        var taskStore = inspection.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var initialTask = await taskStore.GetAsync(new RuntimeInstanceKey("tenant-a", initial.HumanTaskId!));
        var finalTasks = await taskStore.GetPendingByWorkflowAsync(initialTask!.WorkflowKey!.Value);
        finalTasks.Should().ContainSingle();
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: true)).Should().Be(0);
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: false)).Should().Be(0);
    }

    [Fact]
    public async Task CandidateV2_WrongTenantAndUnauthorizedCompletion_CannotMutateTaskOrAsset()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);

        using (var wrongTenant = factory.Services.CreateScope())
        {
            var identity = wrongTenant.ServiceProvider.GetRequiredService<AssetExecutionIdentity>();
            identity.Set("tenant-b", "manager-2", null, DataScope.Tenant, "asset-manager");
            Func<Task> complete = () => wrongTenant.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>()
                .CompleteAsync(initial.HumanTaskId!, "Approve", "Wrong tenant");
            await complete.Should().ThrowAsync<InvalidOperationException>();
        }
        using (var unauthorized = factory.Services.CreateScope())
        {
            var identity = unauthorized.ServiceProvider.GetRequiredService<AssetExecutionIdentity>();
            identity.Set("tenant-a", "user-1", null, DataScope.Tenant, "asset-user");
            Func<Task> complete = () => unauthorized.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>()
                .CompleteAsync(initial.HumanTaskId!, "Approve", "Unauthorized");
            await complete.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        var pending = await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{asset.Id}", null, HttpStatusCode.OK);
        pending.Status.Should().Be(nameof(AssetStatus.MaintenancePending));
        await CompleteAsync(factory, initial.HumanTaskId!, "Approve", "Initial review approved");
    }

    private static async Task<(AssetResult Asset, AssetOperationResult Maintenance)> StartMaintenanceAsync(HttpClient client)
    {
        SetIdentity(client, "tenant-a", "manager-1", "asset-manager", "Tenant");
        var asset = await SendAsync<AssetResult>(client, HttpMethod.Post, "/api/assets", new RegisterAssetInput
        {
            AssetTag = $"LAPTOP-{Guid.NewGuid():N}",
            Name = "Two-stage laptop",
            Description = "Business acceptance fixture",
            Category = "Equipment",
            OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Location = "Shanghai"
        }, HttpStatusCode.Created);
        var maintenance = await SendAsync<AssetOperationResult>(client, HttpMethod.Post,
            $"/api/assets/{asset.Id}/maintenance",
            new MaintenanceRequestInput { AssetId = asset.Id, Reason = "Two-stage review" },
            HttpStatusCode.Accepted);
        return (asset, maintenance);
    }

    private static async Task CompleteAsync(AssetCandidateWebApplicationFactory factory, string taskId, string outcome, string note)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>();
        identity.Set("tenant-a", "manager-1", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), DataScope.Tenant, "asset-manager");
        await scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>()
            .CompleteAsync(taskId, outcome, note);
    }

    private static async Task<int> ReadMaintenanceRecordCountAsync(string path, bool approved)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MaintenanceRecords WHERE Approved=$approved";
        command.Parameters.AddWithValue("$approved", approved ? 1 : 0);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string uri, object? input, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (input is not null)
            request.Content = JsonContent.Create(input, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Expected a response body.");
    }

    private static void SetIdentity(HttpClient client, string tenant, string user, string role, string scope)
    {
        foreach (var name in new[] { AssetExecutionIdentity.TenantHeader, AssetExecutionIdentity.UserHeader, AssetExecutionIdentity.RolesHeader, AssetExecutionIdentity.DataScopeHeader, AssetExecutionIdentity.OrganizationHeader })
            client.DefaultRequestHeaders.Remove(name);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.TenantHeader, tenant);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.UserHeader, user);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.RolesHeader, role);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.DataScopeHeader, scope);
    }
}
