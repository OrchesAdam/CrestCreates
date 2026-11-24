using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Workflow.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetApprovedReleaseAcceptanceTests
{
    private const string Tenant = "asset-release-tenant-a";
    private const string OtherTenant = "asset-release-tenant-b";
    private static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly Guid Organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ApprovedRelease_FreshHostResumesBothExactPinsAndSelectsOnlyForApprovedTenant()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant);
        var (initialTask, workflow) = await SubmitPairAsync(governance);
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");
        var receipt = await governance.IssueApprovedReleaseAsync(initialTask, workflow, SigningKey);
        var database = new HostDatabase();

        MaintenanceRequest baselineRequest;
        RuntimeDescriptorPin baselinePin;
        using (var baseline = new ReleaseHostFactory(database))
        using (var client = baseline.CreateClient())
        {
            baseline.Services.GetRequiredService<IWorkflowRegistry>().GetByVersion(AssetContractIds.MaintenanceWorkflow, 2)
                .Should().BeNull();
            baselineRequest = await StartMaintenanceAsync(client, Tenant);
            baselinePin = (await ReadWorkflowAsync(baseline, Tenant, baselineRequest)).WorkflowPin;
            baselinePin.Ref.Version.Should().Be(1);
        }

        MaintenanceRequest approvedRequest;
        HumanTaskInstance finalTask;
        RuntimeDescriptorPin approvedPin;
        using (var released = new ReleaseHostFactory(database, receipt, SigningKey))
        using (var client = released.CreateClient())
        {
            var registry = released.Services.GetRequiredService<IWorkflowRegistry>();
            registry.GetByVersion(AssetContractIds.MaintenanceWorkflow, 1).Should().NotBeNull();
            registry.GetByVersion(AssetContractIds.MaintenanceWorkflow, 2).Should().BeEquivalentTo(workflow.Draft.Payload.GetDescriptor());
            var lookup = released.Services.GetRequiredService<IDescriptorLookup>();
            lookup.Exists(new DescriptorRef("workflow", AssetContractIds.MaintenanceWorkflow, 1)).Should().BeTrue();
            lookup.Exists(new DescriptorRef("workflow", AssetContractIds.MaintenanceWorkflow, 2)).Should().BeTrue();
            lookup.Exists(new DescriptorRef("humantask", AssetContractIds.MaintenanceInitialHumanTask, 1)).Should().BeTrue();

            var otherRequest = await StartMaintenanceAsync(client, OtherTenant);
            (await ReadWorkflowAsync(released, OtherTenant, otherRequest)).WorkflowPin.Should().Be(baselinePin);
            (await ReadWorkflowAsync(released, Tenant, baselineRequest)).WorkflowPin.Should().Be(baselinePin);

            approvedRequest = await StartMaintenanceAsync(client, Tenant);
            approvedPin = (await ReadWorkflowAsync(released, Tenant, approvedRequest)).WorkflowPin;
            approvedPin.Ref.Version.Should().Be(2);
            var approvedHashes = new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer()).Build(workflow.Draft.Payload.GetDescriptor());
            approvedPin.ContractHash.Should().Be(approvedHashes.ContractHash);
            approvedPin.DefinitionHash.Should().Be(approvedHashes.DefinitionHash);

            await CompleteAsync(released, Tenant, approvedRequest.TaskId);
            (await ReadAssetAsync(client, Tenant, approvedRequest)).Status.Should().Be(nameof(AssetStatus.MaintenancePending));
            using var scope = released.Services.CreateScope();
            var tasks = await scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>()
                .GetPendingByWorkflowAsync(new RuntimeInstanceKey(Tenant, approvedRequest.WorkflowId));
            finalTask = tasks.Should().ContainSingle().Which;
            finalTask.HumanTaskId.Should().Be(AssetContractIds.MaintenanceHumanTask);
        }

        using var restarted = new ReleaseHostFactory(database, receipt, SigningKey);
        using var restartedClient = restarted.CreateClient();
        (await ReadWorkflowAsync(restarted, Tenant, baselineRequest)).WorkflowPin.Should().Be(baselinePin);
        (await ReadWorkflowAsync(restarted, Tenant, approvedRequest)).WorkflowPin.Should().Be(approvedPin);
        await CompleteAsync(restarted, Tenant, baselineRequest.TaskId);
        await WaitForCompletionAsync(restartedClient, restarted, Tenant, baselineRequest);
        await CompleteAsync(restarted, Tenant, finalTask.Id);
        await WaitForCompletionAsync(restartedClient, restarted, Tenant, approvedRequest);
    }

    [Fact]
    public async Task ApprovedRelease_RejectsAReleaseForAnotherTenantWithoutChangingOtherTenants()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant);
        var (task, workflow) = await SubmitPairAsync(governance);
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");
        var receipt = await governance.IssueApprovedReleaseAsync(task, workflow, SigningKey);
        var payload = JsonNode.Parse(Convert.FromBase64String(receipt.Split('.')[0]))!.AsObject();
        payload["tenantId"] = OtherTenant;
        AssertStartupRejected(Sign(payload), SigningKey, "activation binding");
    }

    [Fact]
    public async Task ApprovedRelease_BootRejectsTamperedBytesWrongBaselineAndPartialReceipt()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant);
        var (task, workflow) = await SubmitPairAsync(governance);
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");
        var receipt = await governance.IssueApprovedReleaseAsync(task, workflow, SigningKey);
        var parts = receipt.Split('.');
        var payload = JsonNode.Parse(Convert.FromBase64String(parts[0]))!.AsObject();

        var changedDescriptor = payload.DeepClone();
        changedDescriptor["workflow"]!["name"] = "Tampered workflow";
        AssertStartupRejected(EncodePayload(changedDescriptor) + "." + parts[1], SigningKey, "signature is invalid");
        AssertStartupRejected(receipt, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "signature is invalid");
        AssertStartupRejected(receipt, null, "SigningKey is required");

        var wrongBaseline = payload.DeepClone();
        wrongBaseline["baseline"]!.AsArray().RemoveAt(0);
        AssertStartupRejected(Sign(wrongBaseline), SigningKey, "does not match the deployed baseline");

        var partial = payload.DeepClone().AsObject();
        partial.Remove("initialTaskApproval");
        AssertStartupRejected(Sign(partial), SigningKey, "receipt is malformed");
    }

    [Fact]
    public async Task ApprovedRelease_IssuerRejectsPendingForeignAndChangedDraft()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant);
        var (task, workflow) = await SubmitPairAsync(governance);
        Func<Task> issue = () => governance.IssueApprovedReleaseAsync(task, workflow, SigningKey);
        await issue.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Activated human-reviewed request*");
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");

        await using var foreignGovernance = await AssetControlPlaneApprovalHarness.CreateAsync(OtherTenant);
        Func<Task> issueForeign = () => foreignGovernance.IssueApprovedReleaseAsync(task, workflow, SigningKey);
        await issueForeign.Should().ThrowAsync<InvalidOperationException>().WithMessage("*same tenant*");

        var descriptor = (WorkflowDescriptor)workflow.Draft.Payload.GetDescriptor();
        await governance.Services.GetRequiredService<IDescriptorDraftStore>().SaveAsync(workflow.Draft with
        {
            Payload = new WorkflowDescriptorDraftPayload(new WorkflowDescriptor
            {
                Id = descriptor.Id, Version = descriptor.Version, Steps = descriptor.Steps, Name = "Changed after approval"
            })
        });
        await issue.Should().ThrowAsync<InvalidOperationException>().WithMessage("*hashes do not match*");
    }

    private static async Task<(AssetControlPlaneApprovalHarness.ApprovalSubmission Task, AssetControlPlaneApprovalHarness.ApprovalSubmission Workflow)>
        SubmitPairAsync(AssetControlPlaneApprovalHarness governance)
    {
        const string taskIntent = "Asset maintenance initial review";
        const string taskPrompt = "asset-release-task-hash";
        var task = await governance.ParseAndSaveDraftAsync(
            AssetApprovedInventoryHandoffAcceptanceTests.CreateAssetCandidateJson(taskPrompt, taskIntent), taskIntent, taskPrompt);
        var taskSubmission = await governance.SubmitAsync(task);
        var activated = await governance.CompleteHumanAsync(taskSubmission, "task-reviewer", "Approve");
        var checkedTask = await governance.CheckApprovedInventoryAsync(activated);
        await governance.AddCheckedDescriptorsToCatalogAsync(checkedTask);

        const string workflowIntent = "Replace Asset maintenance workflow with two approved review steps";
        const string workflowPrompt = "asset-release-workflow-hash";
        var workflow = await governance.ParseAndSaveDraftAsync(
            AssetApprovedInventoryHandoffAcceptanceTests.CreateWorkflowUpdateJson(workflowPrompt, workflowIntent), workflowIntent, workflowPrompt);
        return (taskSubmission, await governance.SubmitAsync(workflow));
    }

    private static string EncodePayload(JsonNode payload) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload));

    private static string Sign(JsonNode payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToBase64String(bytes) + "." + Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(SigningKey), bytes));
    }

    private static void AssertStartupRejected(string receipt, string? key, string message)
    {
        using var host = new ReleaseHostFactory(new HostDatabase(), receipt, key, invalidRelease: true);
        var failure = Record.Exception(() => host.CreateClient());
        failure.Should().NotBeNull();
        failure!.ToString().Should().Contain(message);
    }

    private static async Task<MaintenanceRequest> StartMaintenanceAsync(HttpClient client, string tenant)
    {
        var asset = await SendAsync<AssetResult>(client, tenant, HttpMethod.Post, "/api/assets", new RegisterAssetInput
        {
            AssetTag = $"RELEASE-{Guid.NewGuid():N}", Name = "Approved release laptop", Description = "Approved release acceptance", Category = "Equipment",
            OrganizationId = Organization, Location = "Shanghai"
        }, HttpStatusCode.Created);
        var operation = await SendAsync<AssetOperationResult>(client, tenant, HttpMethod.Post, $"/api/assets/{asset.Id}/maintenance",
            new MaintenanceRequestInput { AssetId = asset.Id, Reason = "Approved release acceptance" }, HttpStatusCode.Accepted);
        return new MaintenanceRequest(asset.Id, operation.WorkflowInstanceId!, operation.HumanTaskId!);
    }

    private static async Task CompleteAsync(ReleaseHostFactory host, string tenant, string taskId)
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>().Set(tenant, "release-manager", Organization, DataScope.Tenant, "asset-manager");
        await scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>().CompleteAsync(taskId, "Approve", "Approved release review");
    }

    private static async Task<WorkflowInstance> ReadWorkflowAsync(ReleaseHostFactory host, string tenant, MaintenanceRequest request)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>().GetAsync(new RuntimeInstanceKey(tenant, request.WorkflowId))
            ?? throw new InvalidOperationException("The durable workflow was not found.");
    }

    private static Task<AssetResult> ReadAssetAsync(HttpClient client, string tenant, MaintenanceRequest request) =>
        SendAsync<AssetResult>(client, tenant, HttpMethod.Get, $"/api/assets/{request.AssetId}", null, HttpStatusCode.OK);

    private static async Task WaitForCompletionAsync(HttpClient client, ReleaseHostFactory host, string tenant, MaintenanceRequest request)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if ((await ReadAssetAsync(client, tenant, request)).Status == nameof(AssetStatus.Available) &&
                (await ReadWorkflowAsync(host, tenant, request)).Status == WorkflowInstanceStatus.Completed)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new InvalidOperationException($"Maintenance {request.WorkflowId} did not converge to an Available asset and a Completed workflow within the delivery window.");
    }

    private static async Task<T> SendAsync<T>(HttpClient client, string tenant, HttpMethod method, string uri, object? input, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(AssetExecutionIdentity.TenantHeader, tenant);
        request.Headers.Add(AssetExecutionIdentity.UserHeader, "release-manager");
        request.Headers.Add(AssetExecutionIdentity.RolesHeader, "asset-manager");
        request.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
        if (input is not null)
            request.Content = JsonContent.Create(input, options: WireOptions);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<T>(body, WireOptions) ?? throw new InvalidOperationException("Expected a response body.");
    }

    private sealed record MaintenanceRequest(Guid AssetId, string WorkflowId, string TaskId);

    private sealed class HostDatabase
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"crest-assets-release-{Guid.NewGuid():N}.db");
        public string Schema { get; } = $"crest_asset_release_{Guid.NewGuid():N}";
    }

    private sealed class ReleaseHostFactory(HostDatabase database, string? receipt = null, string? key = null, bool invalidRelease = false)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
            builder.UseSetting("AssetManagement:RuntimeConnectionString", invalidRelease
                ? "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Timeout=1"
                : Environment.GetEnvironmentVariable("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING")
                    ?? throw new InvalidOperationException("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING is required."));
            builder.UseSetting("AssetManagement:RuntimeSchema", database.Schema);
            builder.UseSetting("AssetManagement:DatabasePath", database.Path);
            if (receipt is not null)
                builder.UseSetting("AssetManagement:ApprovedRelease:Receipt", receipt);
            if (key is not null)
                builder.UseSetting("AssetManagement:ApprovedRelease:SigningKey", key);
        }
    }
}
