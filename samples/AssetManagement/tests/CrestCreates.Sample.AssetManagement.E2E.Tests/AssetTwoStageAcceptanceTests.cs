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
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Registry;
using CrestCreates.Metadata.Runtime;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.HumanTask;
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

    [Fact]
    public async Task CandidateV2_ChangedCompiledTaskPin_IsRejectedBeforeCompletion()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);
        using var scope = factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var task = (await tasks.GetAsync(new RuntimeInstanceKey("tenant-a", initial.HumanTaskId!)))!;
        var changedDescriptor = new HumanTaskDescriptor
        {
            Id = AssetContractIds.MaintenanceInitialHumanTask,
            Name = "Changed candidate contract",
            Version = 1,
            State = DescriptorState.Active,
            Interaction = AssetDescriptorCatalog.MaintenanceInitialHumanTask.Interaction,
            AssigneeStrategy = AssetDescriptorCatalog.MaintenanceInitialHumanTask.AssigneeStrategy,
            Outcomes = AssetDescriptorCatalog.MaintenanceInitialHumanTask.Outcomes
        };
        var changedRegistry = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
        changedRegistry.Build([new AssetDescriptorProvider<HumanTaskDescriptor>([changedDescriptor])]);
        var stableHashes = scope.ServiceProvider.GetRequiredService<IDescriptorStableHashBuilder>();
        var changedPinResolver = new RuntimeDescriptorPinResolver<HumanTaskDescriptor>(
            changedRegistry, stableHashes, "humantask", DescriptorKind.HumanTask);
        var changedPin = changedPinResolver.Capture(changedDescriptor).Pin;
        changedPinResolver
            .Resolve(changedPin).Descriptor.Name.Should().Be("Changed candidate contract");
        var resolver = new AssetMaintenanceTaskContractResolver(changedPinResolver);
        Action resolve = () => resolver.Resolve(changedPin);
        resolve.Should().Throw<InvalidOperationException>();
        (await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{asset.Id}", null, HttpStatusCode.OK))
            .Status.Should().Be(nameof(AssetStatus.MaintenancePending));
    }

    [Fact]
    public async Task CandidateV2_InitialCompletion_ReturnsWhenFinalAlreadyCompleted()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);
        var initialCompletion = CompleteAsync(factory, initial.HumanTaskId!, "Approve", "Initial review approved");
        var final = await WaitForSingleFinalTaskAsync(factory, initial.HumanTaskId!);
        await CompleteAsync(factory, final.Id, "Approve", "Final review approved");
        await initialCompletion;
        (await SendAsync<AssetResult>(client, HttpMethod.Get, $"/api/assets/{asset.Id}", null, HttpStatusCode.OK))
            .Status.Should().Be(nameof(AssetStatus.Available));
    }

    [Fact]
    public async Task CandidateV2_InconsistentCanonicalOutcomeAndFact_IsConflict()
    {
        using var factory = new AssetCandidateWebApplicationFactory();
        using var client = factory.CreateClient();
        var (asset, initial) = await StartMaintenanceAsync(client);
        using var scope = factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var task = (await tasks.GetAsync(new RuntimeInstanceKey("tenant-a", initial.HumanTaskId!)))!;
        var state = scope.ServiceProvider.GetRequiredService<IRuntimeStateContractRegistry>();
        var completed = await scope.ServiceProvider.GetRequiredService<IHumanTaskRuntime>().CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key,
            Outcome = "Approve",
            ActorId = "manager-1",
            ActorRoles = ["asset-manager"],
            Result = state.Capture(new AssetMaintenanceDecisionFact { Approved = false, AssetId = asset.Id, RequesterId = "requester", ApproverId = "manager-1" })
        });
        var consumer = scope.ServiceProvider.GetRequiredService<AssetMaintenanceDecisionConsumer>();
        var result = await consumer.ConsumeAsync(new HumanTaskCompletedEvent
        {
            EventId = completed.CompletionEventId!,
            HumanTaskKey = completed.Key,
            WorkflowKey = completed.WorkflowKey,
            HumanTaskPin = completed.HumanTaskPin,
            Outcome = "Approve",
            Result = completed.Output
        }, new OutboxDeliveryContext
        {
            Message = new OutboxMessage
            {
                Metadata = new OutboxMessageMetadata { MessageId = "asset-inconsistent-fact", ContractId = "test", RequiredConsumerIds = [], OccurredAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow },
                Payload = [],
                Integrity = new CanonicalHash { Value = "test", Algorithm = "test", AlgorithmVersion = "test", ArtifactKind = "test", Scope = "test", Purpose = "test", ContractVersion = "test", CanonicalShapeVersion = "test" }
            },
            Lease = new OutboxDeliveryLease { OwnerId = "test", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1), Attempt = 1, Fence = 1 },
            AttemptDeadline = DateTimeOffset.UtcNow.AddMinutes(1),
            Services = scope.ServiceProvider
        });
        result.Outcome.Should().Be(OutboxDeliveryOutcome.Conflict);
        result.FailureCode.Should().Be("ASSET_MAINTENANCE_FACT_INVALID");
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: true)).Should().Be(0);
        (await ReadMaintenanceRecordCountAsync(factory.DatabasePath, approved: false)).Should().Be(0);
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

    private static async Task<HumanTaskInstance> WaitForSingleFinalTaskAsync(AssetCandidateWebApplicationFactory factory, string initialTaskId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = factory.Services.CreateScope();
            var tasks = scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
            var initial = await tasks.GetAsync(new RuntimeInstanceKey("tenant-a", initialTaskId));
            if (initial?.WorkflowKey is RuntimeInstanceKey workflowKey)
            {
                var pending = await tasks.GetPendingByWorkflowAsync(workflowKey);
                if (pending.Count == 1 && pending[0].HumanTaskId == AssetContractIds.MaintenanceHumanTask)
                    return pending[0];
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("The candidate final maintenance task was not created.");
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
