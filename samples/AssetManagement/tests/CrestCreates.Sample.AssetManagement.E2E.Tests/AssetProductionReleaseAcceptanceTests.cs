using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Agent.Tools;
using CrestCreates.Sample.AssetManagement.MicrosoftAgent;
using Microsoft.Extensions.AI;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Contracts.Mes;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;
using CrestCreates.Workflow.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetProductionReleaseAcceptanceTests
{
    private const string Tenant = "asset-production-tenant-a";
    private static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly Guid Organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CriticalMaintenance_RequiresMesEvidenceAndIndependentQualityBeforeProductionRelease()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, additionalBaseline: ProductionBaseline());
        var workflow = await SubmitProductionWorkflowAsync(governance);
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");
        var receipt = await IssueProductionReleaseAsync(governance, workflow);
        using var mesDatabase = new MesDatabase();
        await using var mes = await mesDatabase.StartAsync();
        var database = new HostDatabase();
        using var host = new ProductionHostFactory(database, receipt, mes.BaseAddress);
        using var client = host.CreateClient();

        var asset = await RegisterCriticalAssetAsync(client);
        var otherAsset = await RegisterCriticalAssetAsync(client);
        var maintenance = await RequestMaintenanceAsync(client, Tenant, asset.Id);
        var workflowInstance = await ReadWorkflowByIdAsync(host, maintenance.WorkflowInstanceId!);
        workflowInstance.WorkflowPin.Ref.Version.Should().Be(AssetProductionDescriptors.WorkflowVersion);
        var initialTask = await PendingTaskAsync(host, workflowInstance.InstanceId, AssetContractIds.MaintenanceInitialHumanTask);
        using (var missing = await CompleteTaskAsync(client, "supervisor-1", "asset-manager", asset.Id, initialTask.Id, "Approve", "other-tenant"))
        {
            missing.StatusCode.Should().Be(HttpStatusCode.NotFound, await missing.Content.ReadAsStringAsync());
        }
        using (var unknown = await CompleteTaskAsync(client, "supervisor-1", "asset-manager", asset.Id, "missing-task", "Approve"))
        {
            unknown.StatusCode.Should().Be(HttpStatusCode.NotFound, await unknown.Content.ReadAsStringAsync());
        }
        using (var mismatch = await CompleteTaskAsync(client, "supervisor-1", "asset-manager", otherAsset.Id, initialTask.Id, "Approve"))
        {
            var failure = await mismatch.Content.ReadAsStringAsync();
            mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest, failure);
            failure.Should().Contain("ASSET_MAINTENANCE_TASK_MISMATCH");
        }
        (await PendingTaskAsync(host, workflowInstance.InstanceId, AssetContractIds.MaintenanceInitialHumanTask)).Status
            .Should().BeOneOf(HumanTaskInstanceStatus.Created, HumanTaskInstanceStatus.Assigned);
        await CompleteAsAsync(client, "supervisor-1", "asset-manager", asset.Id, initialTask.Id, "Approve");
        var trialTask = await PendingTaskAsync(host, workflowInstance.InstanceId, AssetProductionDescriptors.TrialTaskId);

        var lockPath = $"api/mes/production-locks/{asset.Id}/{workflowInstance.InstanceId}";
        using var mesClient = mes.Client();
        var lockState = await mesClient.GetFromJsonAsync(lockPath, MesJsonContext.Default.ProductionLockState);
        lockState.Should().NotBeNull();
        lockState!.IsLocked.Should().BeTrue();
        var observationId = Guid.NewGuid();
        using var trialResponse = await mesClient.PutAsJsonAsync($"{lockPath}/observations/{observationId}",
            new RecordTrialRequest(TrialOutcome.Passed, lockState.RegisteredAt), MesJsonContext.Default.RecordTrialRequest);
        trialResponse.EnsureSuccessStatusCode();
        var observation = await trialResponse.Content.ReadFromJsonAsync(MesJsonContext.Default.TrialObservation);
        observation.Should().NotBeNull();

        var qualityTask = await WaitForPendingTaskAsync(host, workflowInstance.InstanceId, AssetProductionDescriptors.QualityTaskId);
        var trialCompletion = await ReadWorkflowByIdAsync(host, workflowInstance.InstanceId);
        trialCompletion.StepResults.Should().Contain(step => step.StepId == AssetProductionDescriptors.TrialStepId &&
            step.HumanTaskOutcome == CompletionCondition.Approve);
        trialTask.HumanTaskId.Should().Be(AssetProductionDescriptors.TrialTaskId);
        using (var qualityResponse = await CompleteTaskAsync(client, "quality-1", AssetProductionDescriptors.QualityRole, asset.Id, qualityTask.Id, "Approve"))
        {
            var qualityBody = await qualityResponse.Content.ReadAsStringAsync();
            qualityResponse.StatusCode.Should().Be(HttpStatusCode.OK, qualityBody);
            var qualityResult = await qualityResponse.Content.ReadFromJsonAsync<AssetOperationResult>(WireOptions);
            qualityResult!.Status.Should().Be(nameof(AssetStatus.Available));
        }
        (await ReadWorkflowByIdAsync(host, workflowInstance.InstanceId)).Status.Should().Be(WorkflowInstanceStatus.Completed);

        var releaseInput = new AssetProductionReleaseInput
        {
            AssetId = asset.Id,
            ObservationId = observation!.ObservationId,
            SourceEventId = observation.SourceEventId,
            WorkflowInstanceId = workflowInstance.InstanceId
        };
        using var releaseRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/assets/{asset.Id}/production-release");
        releaseRequest.Headers.Add(AssetExecutionIdentity.TenantHeader, Tenant);
        releaseRequest.Headers.Add(AssetExecutionIdentity.UserHeader, "quality-1");
        releaseRequest.Headers.Add(AssetExecutionIdentity.RolesHeader, AssetProductionDescriptors.QualityRole);
        releaseRequest.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
        releaseRequest.Content = JsonContent.Create(releaseInput, options: WireOptions);
        using var releaseResponse = await client.SendAsync(releaseRequest);
        releaseResponse.StatusCode.Should().Be(HttpStatusCode.OK, await releaseResponse.Content.ReadAsStringAsync());
        var released = await releaseResponse.Content.ReadFromJsonAsync<AssetProductionReleaseFact>(WireOptions);
        released.Should().NotBeNull();
        released!.Receipt.Evidence.Should().BeEquivalentTo(observation);
        var finalLock = await mesClient.GetFromJsonAsync(lockPath, MesJsonContext.Default.ProductionLockState);
        finalLock!.IsLocked.Should().BeFalse();
        finalLock.Release!.OperationId.Should().Be(released.OperationId);
    }

    [Fact]
    public async Task FailedOrExpiredMesTrial_RequiresExplicitReworkAndRetestBeforeRelease()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, additionalBaseline: ProductionBaseline());
        var workflow = await SubmitProductionWorkflowAsync(governance);
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");
        var receipt = await IssueProductionReleaseAsync(governance, workflow);
        using var mesDatabase = new MesDatabase();
        await using var mes = await mesDatabase.StartAsync("00:00:00.0000001");
        using var host = new ProductionHostFactory(new HostDatabase(), receipt, mes.BaseAddress);
        using var client = host.CreateClient();
        using var mesClient = mes.Client();
        var asset = await RegisterCriticalAssetAsync(client);

        var failedOrder = await RequestMaintenanceAsync(client, Tenant, asset.Id);
        var failedWorkflow = await ReadWorkflowByIdAsync(host, failedOrder.WorkflowInstanceId!);
        var failedInitial = await PendingTaskAsync(host, failedWorkflow.InstanceId, AssetContractIds.MaintenanceInitialHumanTask);
        await CompleteAsAsync(client, "supervisor-1", "asset-manager", asset.Id, failedInitial.Id, "Approve");
        var failedLockPath = $"api/mes/production-locks/{asset.Id}/{failedWorkflow.InstanceId}";
        var failedLock = await mesClient.GetFromJsonAsync(failedLockPath, MesJsonContext.Default.ProductionLockState);
        using var failedTrial = await mesClient.PutAsJsonAsync($"{failedLockPath}/observations/{Guid.NewGuid()}",
            new RecordTrialRequest(TrialOutcome.Failed, failedLock!.RegisteredAt), MesJsonContext.Default.RecordTrialRequest);
        failedTrial.EnsureSuccessStatusCode();
        await WaitForWorkflowStatusAsync(host, failedWorkflow.InstanceId, WorkflowInstanceStatus.Completed);
        (await ReadWorkflowByIdAsync(host, failedWorkflow.InstanceId)).StepResults.Should().Contain(step =>
            step.StepId == AssetProductionDescriptors.TrialStepId && step.HumanTaskOutcome == CompletionCondition.Reject);
        await WaitForAssetStatusAsync(client, asset.Id, nameof(AssetStatus.Available));

        var retestOrder = await RequestMaintenanceAsync(client, Tenant, asset.Id);
        retestOrder.WorkflowInstanceId.Should().NotBe(failedOrder.WorkflowInstanceId);
        var retestWorkflow = await ReadWorkflowByIdAsync(host, retestOrder.WorkflowInstanceId!);
        var retestInitial = await PendingTaskAsync(host, retestWorkflow.InstanceId, AssetContractIds.MaintenanceInitialHumanTask);
        await CompleteAsAsync(client, "supervisor-1", "asset-manager", asset.Id, retestInitial.Id, "Approve");
        var retestLockPath = $"api/mes/production-locks/{asset.Id}/{retestWorkflow.InstanceId}";
        var retestLock = await mesClient.GetFromJsonAsync(retestLockPath, MesJsonContext.Default.ProductionLockState);
        using var passedTrial = await mesClient.PutAsJsonAsync($"{retestLockPath}/observations/{Guid.NewGuid()}",
            new RecordTrialRequest(TrialOutcome.Passed, retestLock!.RegisteredAt), MesJsonContext.Default.RecordTrialRequest);
        passedTrial.EnsureSuccessStatusCode();
        var qualityTask = await WaitForPendingTaskAsync(host, retestWorkflow.InstanceId, AssetProductionDescriptors.QualityTaskId);
        using var staleApproval = await CompleteTaskAsync(client, "quality-1", AssetProductionDescriptors.QualityRole,
            asset.Id, qualityTask.Id, "Approve");
        staleApproval.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await PendingTaskAsync(host, retestWorkflow.InstanceId, AssetProductionDescriptors.QualityTaskId)).Id.Should().Be(qualityTask.Id);
        await CompleteAsAsync(client, "quality-1", AssetProductionDescriptors.QualityRole, asset.Id, qualityTask.Id, "Reject");
        await WaitForWorkflowStatusAsync(host, retestWorkflow.InstanceId, WorkflowInstanceStatus.Completed);
        await WaitForAssetStatusAsync(client, asset.Id, nameof(AssetStatus.Available));

        var finalOrder = await RequestMaintenanceAsync(client, Tenant, asset.Id);
        finalOrder.WorkflowInstanceId.Should().NotBe(retestOrder.WorkflowInstanceId);
        (await ReadWorkflowByIdAsync(host, finalOrder.WorkflowInstanceId!)).WorkflowPin.Ref.Version.Should().Be(AssetProductionDescriptors.WorkflowVersion);
    }

    [Fact]
    public async Task MaintenanceHttp_SelectsApprovedCriticalVersionAndPreservesBaselinePins()
    {
        const string otherTenant = "asset-production-tenant-b";
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, additionalBaseline: ProductionBaseline());
        var proposal = await SubmitProductionWorkflowAsync(governance);
        await governance.CompleteHumanAsync(proposal, "workflow-reviewer", "Approve");
        var receipt = await IssueProductionReleaseAsync(governance, proposal);
        using var mesDatabase = new MesDatabase();
        await using var mes = await mesDatabase.StartAsync();
        var database = new HostDatabase();
        AssetOperationResult inFlight;
        WorkflowInstance baseline;
        using (var before = new ProductionHostFactory(database, null, mes.BaseAddress))
        using (var client = before.CreateClient())
        {
            var asset = await RegisterCriticalAssetAsync(client);
            inFlight = await RequestMaintenanceAsync(client, Tenant, asset.Id);
            baseline = await ReadWorkflowByIdAsync(before, inFlight.WorkflowInstanceId!);
            baseline.WorkflowPin.Ref.Version.Should().Be(1);
        }

        using var after = new ProductionHostFactory(database, receipt, mes.BaseAddress);
        using var afterClient = after.CreateClient();
        using var mesClient = mes.Client();
        var ordinary = await RegisterCriticalAssetAsync(afterClient, category: "Equipment");
        var other = await RegisterCriticalAssetAsync(afterClient, otherTenant);
        var ordinaryOrder = await RequestMaintenanceAsync(afterClient, Tenant, ordinary.Id);
        var otherOrder = await RequestMaintenanceAsync(afterClient, otherTenant, other.Id);
        (await ReadWorkflowByIdAsync(after, ordinaryOrder.WorkflowInstanceId!)).WorkflowPin.Should().Be(baseline.WorkflowPin);
        (await ReadWorkflowByIdAsync(after, otherOrder.WorkflowInstanceId!, otherTenant)).WorkflowPin.Should().Be(baseline.WorkflowPin);
        using var ordinaryLock = await mesClient.GetAsync($"api/mes/production-locks/{ordinary.Id}/{ordinaryOrder.WorkflowInstanceId}");
        ordinaryLock.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await CompleteAsAsync(afterClient, "supervisor-1", "asset-manager", inFlight.AssetId, inFlight.HumanTaskId!, "Approve");
        var resumed = baseline;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            resumed = await ReadWorkflowByIdAsync(after, inFlight.WorkflowInstanceId!);
            if (resumed.Status == WorkflowInstanceStatus.Completed)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        resumed.WorkflowPin.Should().Be(baseline.WorkflowPin);
        resumed.Status.Should().Be(WorkflowInstanceStatus.Completed);

        var critical = await RegisterCriticalAssetAsync(afterClient);
        var order = await RequestMaintenanceAsync(afterClient, Tenant, critical.Id);
        var retry = await RequestMaintenanceAsync(afterClient, Tenant, critical.Id);
        retry.WorkflowInstanceId.Should().Be(order.WorkflowInstanceId);
        retry.HumanTaskId.Should().Be(order.HumanTaskId);
        (await ReadWorkflowByIdAsync(after, order.WorkflowInstanceId!)).WorkflowPin.Ref.Version.Should().Be(AssetProductionDescriptors.WorkflowVersion);
        var productionLock = await mesClient.GetFromJsonAsync($"api/mes/production-locks/{critical.Id}/{order.WorkflowInstanceId}", MesJsonContext.Default.ProductionLockState);
        productionLock!.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task EvolutionOperator_SeparatesProposalFromExplicitHumanApprovalAndIssuesReceipt()
    {
        using var host = new ProductionHostFactory(new HostDatabase(), null, "http://127.0.0.1:1", evolutionEnabled: true);
        using var client = host.CreateClient();
        const string intent = "Require critical equipment MES evidence and independent quality sign-off.";
        var proposal = await AssetProductionEvolutionOperator.ProposeAsync(host.Services, Tenant, intent);
        proposal.RequestId.Should().NotBeNullOrWhiteSpace();
        proposal.ReviewTaskId.Should().NotBeNullOrWhiteSpace();

        Func<Task> selfApproval = () => AssetProductionEvolutionOperator.ApproveAsync(
            host.Services, Tenant, proposal.RequestId, proposal.ReviewTaskId, "asset-evolution-operator");
        await selfApproval.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different human approver*");

        var receipt = await AssetProductionEvolutionOperator.ApproveAsync(
            host.Services, Tenant, proposal.RequestId, proposal.ReviewTaskId, "operator-reviewer");
        var release = AssetProductionRelease.Load(receipt, SigningKey, AssetProductionEvolutionBaseline.GetDescriptors());
        release.TenantId.Should().Be(Tenant);
        release.Workflow.Version.Should().Be(AssetProductionDescriptors.WorkflowVersion);
        release.WorkflowApproval.RequestId.Should().Be(proposal.RequestId);
    }

    [Fact]
    public async Task ProductionRelease_CoexistsWithApprovedV2AndResumesItsExactWorkflowPin()
    {
        await using var v2Governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant);
        var (v2Task, v2Workflow) = await AssetApprovedReleaseAcceptanceTests.SubmitPairAsync(v2Governance);
        await v2Governance.CompleteHumanAsync(v2Workflow, "workflow-reviewer", "Approve");
        var v2Receipt = await v2Governance.IssueApprovedReleaseAsync(v2Task, v2Workflow, SigningKey);
        await using var v3Governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, additionalBaseline: ProductionBaseline());
        var v3Workflow = await SubmitProductionWorkflowAsync(v3Governance);
        await v3Governance.CompleteHumanAsync(v3Workflow, "workflow-reviewer", "Approve");
        var v3Receipt = await IssueProductionReleaseAsync(v3Governance, v3Workflow);
        using var mesDatabase = new MesDatabase();
        await using var mes = await mesDatabase.StartAsync();
        var database = new HostDatabase();
        AssetOperationResult inFlight;
        RuntimeDescriptorPin v2Pin;
        string finalTaskId;
        using (var v2Host = new ProductionHostFactory(database, null, mes.BaseAddress, v2Receipt))
        using (var v2Client = v2Host.CreateClient())
        {
            var asset = await RegisterCriticalAssetAsync(v2Client);
            inFlight = await RequestMaintenanceAsync(v2Client, Tenant, asset.Id);
            v2Pin = (await ReadWorkflowByIdAsync(v2Host, inFlight.WorkflowInstanceId!)).WorkflowPin;
            v2Pin.Ref.Version.Should().Be(2);
            var initial = await PendingTaskAsync(v2Host, inFlight.WorkflowInstanceId!, AssetContractIds.MaintenanceInitialHumanTask);
            await CompleteAsAsync(v2Client, "supervisor-1", "asset-manager", asset.Id, initial.Id, "Approve");
            finalTaskId = (await PendingTaskAsync(v2Host, inFlight.WorkflowInstanceId!, AssetContractIds.MaintenanceHumanTask)).Id;
        }

        using var host = new ProductionHostFactory(database, v3Receipt, mes.BaseAddress, v2Receipt);
        using var client = host.CreateClient();
        var registry = host.Services.GetRequiredService<IWorkflowRegistry>();
        registry.GetByVersion(AssetContractIds.MaintenanceWorkflow, 1).Should().NotBeNull();
        registry.GetByVersion(AssetContractIds.MaintenanceWorkflow, 2).Should().NotBeNull();
        registry.GetByVersion(AssetContractIds.MaintenanceWorkflow, 3).Should().NotBeNull();
        await CompleteAsAsync(client, "supervisor-1", "asset-manager", inFlight.AssetId, finalTaskId, "Approve");
        await WaitForWorkflowStatusAsync(host, inFlight.WorkflowInstanceId!, WorkflowInstanceStatus.Completed);
        (await ReadWorkflowByIdAsync(host, inFlight.WorkflowInstanceId!)).WorkflowPin.Should().Be(v2Pin);

        var newAsset = await RegisterCriticalAssetAsync(client);
        var newOrder = await RequestMaintenanceAsync(client, Tenant, newAsset.Id);
        (await ReadWorkflowByIdAsync(host, newOrder.WorkflowInstanceId!)).WorkflowPin.Ref.Version.Should().Be(3);
    }

    [Fact]
    public async Task ProductionRelease_RejectsMissingEvidenceAndSameApprover()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, additionalBaseline: ProductionBaseline());
        var workflow = await SubmitProductionWorkflowAsync(governance);
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");
        var receipt = await IssueProductionReleaseAsync(governance, workflow);
        using var mesDatabase = new MesDatabase();
        await using var mes = await mesDatabase.StartAsync();
        var database = new HostDatabase();
        using var host = new ProductionHostFactory(database, receipt, mes.BaseAddress);
        using var client = host.CreateClient();
        var asset = await RegisterCriticalAssetAsync(client);
        var maintenance = await RequestMaintenanceAsync(client, Tenant, asset.Id);
        var workflowInstance = await ReadWorkflowByIdAsync(host, maintenance.WorkflowInstanceId!);
        var initialTask = await PendingTaskAsync(host, workflowInstance.InstanceId, AssetContractIds.MaintenanceInitialHumanTask);
        await CompleteAsAsync(client, "supervisor-1", "asset-manager", asset.Id, initialTask.Id, "Approve");
        var trialTask = await PendingTaskAsync(host, workflowInstance.InstanceId, AssetProductionDescriptors.TrialTaskId);
        var lockPath = $"api/mes/production-locks/{asset.Id}/{workflowInstance.InstanceId}";
        using var mesClient = mes.Client();
        var lockState = await mesClient.GetFromJsonAsync(lockPath, MesJsonContext.Default.ProductionLockState);
        var observationId = Guid.NewGuid();
        using var trialResponse = await mesClient.PutAsJsonAsync($"{lockPath}/observations/{observationId}",
            new RecordTrialRequest(TrialOutcome.Passed, lockState!.RegisteredAt), MesJsonContext.Default.RecordTrialRequest);
        trialResponse.EnsureSuccessStatusCode();
        var observation = await trialResponse.Content.ReadFromJsonAsync(MesJsonContext.Default.TrialObservation);
        var qualityTask = await WaitForPendingTaskAsync(host, workflowInstance.InstanceId, AssetProductionDescriptors.QualityTaskId);
        var trialCompletion = await ReadWorkflowByIdAsync(host, workflowInstance.InstanceId);
        trialCompletion.StepResults.Should().Contain(step => step.StepId == AssetProductionDescriptors.TrialStepId &&
            step.HumanTaskOutcome == CompletionCondition.Approve);
        trialTask.HumanTaskId.Should().Be(AssetProductionDescriptors.TrialTaskId);
        Func<Task<HttpResponseMessage>> sameApprover = () => CompleteTaskAsync(client, "supervisor-1", AssetProductionDescriptors.QualityRole, asset.Id, qualityTask.Id, "Approve");
        (await sameApprover()).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await CompleteAsAsync(client, "quality-1", AssetProductionDescriptors.QualityRole, asset.Id, qualityTask.Id, "Approve");
        var releaseInput = new AssetProductionReleaseInput
        {
            AssetId = asset.Id,
            ObservationId = Guid.NewGuid(),
            SourceEventId = observation!.SourceEventId,
            WorkflowInstanceId = workflowInstance.InstanceId
        };
        using var releaseRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/assets/{asset.Id}/production-release");
        releaseRequest.Headers.Add(AssetExecutionIdentity.TenantHeader, Tenant);
        releaseRequest.Headers.Add(AssetExecutionIdentity.UserHeader, "quality-1");
        releaseRequest.Headers.Add(AssetExecutionIdentity.RolesHeader, AssetProductionDescriptors.QualityRole);
        releaseRequest.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
        releaseRequest.Content = JsonContent.Create(releaseInput, options: WireOptions);
        using var releaseResponse = await client.SendAsync(releaseRequest);
        releaseResponse.StatusCode.Should().NotBe(HttpStatusCode.OK);
        var finalLock = await mesClient.GetFromJsonAsync(lockPath, MesJsonContext.Default.ProductionLockState);
        finalLock!.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task MicrosoftAgentExpert_RunsAsNativeChildRunThroughGovernedToolInvoker()
    {
        await using var governance = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, additionalBaseline: ProductionBaseline());
        var workflow = await SubmitProductionWorkflowAsync(governance);
        await governance.CompleteHumanAsync(workflow, "workflow-reviewer", "Approve");
        var receipt = await IssueProductionReleaseAsync(governance, workflow);
        using var mesDatabase = new MesDatabase();
        await using var mes = await mesDatabase.StartAsync();
        using var host = new ProductionHostFactory(new HostDatabase(), receipt, mes.BaseAddress);
        using var client = host.CreateClient();
        var asset = await RegisterCriticalAssetAsync(client);

        var store = host.Services.GetRequiredService<IAgentExecutionStore>();
        var coordinator = new AgentRunCoordinator(store, TimeProvider.System);
        var key = new AgentExecutionKey(Tenant, Guid.NewGuid());
        var root = await coordinator.StartAsync(key, "evolution-author", TimeProvider.System.GetUtcNow().AddMinutes(2),
            new AgentRunAssignment(key.RootRunId, "supervisor", "Discover critical asset metadata", new AgentBudget(100, 5), [AssetContractIds.GetTool], []));
        var chat = new ExpertScriptedChatClient(asset.Id);
        var team = new AgentTeamRunner(coordinator,
            [new AgentTeamMember("maf-asset-expert", [AssetContractIds.GetTool], new MafExpertWorker(host, asset.Id, chat, coordinator))]);
        var assignment = new AgentRunAssignment(Guid.NewGuid(), "maf-asset-expert", "Look up the critical asset", new AgentBudget(40, 2), [AssetContractIds.GetTool], []);

        var result = await team.DelegateAsync(key, root, [assignment]);
        result.AllReturned.Should().BeTrue();
        chat.Answer.Should().Contain(asset.AssetTag);
        chat.Turns.Should().Be(2);
        var execution = (await store.FindAsync(key, default))!;
        execution.Runs[root.RunId].Result.Should().BeNull();
        var child = execution.Runs[assignment.RunId];
        child.ChildCall!.ParentRunId.Should().Be(root.RunId);
        child.Result!.Output.Should().NotBeNull();
        child.RemainingBudget.Should().Be(new AgentBudget(22, 1));
        child.Reservations.Values.All(receipt => receipt.Used is not null).Should().BeTrue();

        var replay = await team.DelegateAsync(key, root, [assignment]);
        replay.Steps[0].Result.Should().BeEquivalentTo(result.Steps[0].Result);
        chat.Turns.Should().Be(2);
    }

    private sealed class MafExpertWorker(ProductionHostFactory host, Guid assetId, ExpertScriptedChatClient chat, AgentRunCoordinator hostCoordinator) : IAgentRunWorker
    {
        public async ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            using var scope = host.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>();
            identity.Set(Tenant, "agent-discovery", Organization, DataScope.Tenant, "asset-manager");
            identity.SetAgent(context.Execution.RootRunId.ToString("N"), context.Owner.RunId.ToString("N"));
            var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "agent-discovery"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "asset-manager"),
                new System.Security.Claims.Claim("tenantid", Tenant)
            ], "agent"));
            using var principalScope = scope.ServiceProvider.GetRequiredService<CrestCreates.Authorization.Abstractions.ICurrentPrincipalAccessor>().Change(principal);
            var expert = new MicrosoftAgentAssetExpert(chat, scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>());
            var answer = await expert.RunAsync($"Look up asset {assetId:D} and report its asset tag.", hostCoordinator, context, cancellationToken);
            chat.Answer = answer;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(answer)));
            return new AgentRunResult(AgentResultKind.Returned, "ExpertReport", new AgentArtifactReference("maf-expert-report", hash));
        }
    }

    private sealed class ExpertScriptedChatClient(Guid assetId) : IChatClient
    {
        public int Turns { get; private set; }
        public string Answer { get; set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Turns++;
            if (Turns == 1)
            {
                var call = new FunctionCallContent("lookup-1", AssetContractIds.GetTool,
                    new Dictionary<string, object?> { ["assetId"] = assetId.ToString() });
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call]))
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                    Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2, TotalTokenCount = 12 }
                });
            }

            var result = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single();
            var json = Assert.IsType<JsonElement>(result.Result);
            var tag = json.GetProperty("assetTag").GetString();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Asset tag is {tag}."))
            {
                Usage = new UsageDetails { InputTokenCount = 4, OutputTokenCount = 2, TotalTokenCount = 6 }
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("scripted-provider") : null;

        public void Dispose() { }
    }

    private static IReadOnlyList<IDescriptor> ProductionBaseline() =>
    [
        AssetDescriptorCatalog.MaintenanceInitialHumanTask,
        AssetProductionDescriptors.TrialTask,
        AssetProductionDescriptors.QualityTask,
        AssetProductionDescriptors.ReleaseInputSchema,
        AssetProductionDescriptors.ReleaseOutputSchema,
        AssetProductionDescriptors.ReleaseCapability
    ];

    private static async Task<AssetControlPlaneApprovalHarness.ApprovalSubmission> SubmitProductionWorkflowAsync(AssetControlPlaneApprovalHarness governance)
    {
        const string intent = "Require MES trial evidence and independent quality sign-off for critical equipment";
        const string promptHash = "asset-production-workflow-hash";
        var draft = await governance.ParseAndSaveDraftAsync(CreateProductionWorkflowJson(promptHash, intent), intent, promptHash);
        return await governance.SubmitAsync(draft);
    }

    private static Task<string> IssueProductionReleaseAsync(AssetControlPlaneApprovalHarness governance, AssetControlPlaneApprovalHarness.ApprovalSubmission workflow)
    {
        var issuer = new AssetProductionReleaseIssuer(
            governance.Services.GetRequiredService<IAgentControlPlaneToolService>(),
            governance.Services.GetRequiredService<IActivationBindingArtifactResolver>(),
            governance.Services.GetRequiredService<CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts.IAgentPackageArtifactStore>(),
            governance.Services.GetRequiredService<CrestCreates.Metadata.Abstractions.DescriptorPackage.IDescriptorPackageSerializer>(),
            governance.Services.GetRequiredService<IDescriptorDraftStore>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AssetProductionRelease.SigningKeySetting] = SigningKey
            }).Build());
        return issuer.IssueAsync(new AgentToolInvocationContext
        {
            TenantId = Tenant,
            ActorId = "release-issuer",
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = "asset-production-release",
            ToolName = AgentToolName.GetActivationRequestStatus,
            InvocationSource = AgentToolInvocationSource.Direct
        }, workflow.Request.RequestId);
    }

    private static string CreateProductionWorkflowJson(string promptHash, string intent) => JsonSerializer.Serialize(new
    {
        contractVersion = "8a.v1",
        outcome = "drafts",
        promptInputHash = promptHash,
        plan = new
        {
            planId = "asset-production-workflow-v3",
            intentText = intent,
            plannedDescriptorRefs = new[] { new { @namespace = "workflow", id = AssetContractIds.MaintenanceWorkflow, version = 3 } }
        },
        items = new[]
        {
            new
            {
                descriptorKind = "Workflow",
                descriptorId = AssetContractIds.MaintenanceWorkflow,
                operation = "Update",
                baseVersion = "1",
                rationale = "Require supervisor review, MES trial evidence and independent quality sign-off.",
                payload = new
                {
                    id = AssetContractIds.MaintenanceWorkflow,
                    name = "Critical equipment maintenance release",
                    version = 3,
                    steps = new[]
                    {
                        Step(AssetProductionDescriptors.InitialStepId, AssetContractIds.MaintenanceInitialHumanTask, null),
                        Step(AssetProductionDescriptors.TrialStepId, AssetProductionDescriptors.TrialTaskId, WorkflowConditionTokens.PreviousHumanTaskApproved),
                        Step(AssetProductionDescriptors.QualityStepId, AssetProductionDescriptors.QualityTaskId, WorkflowConditionTokens.AllPriorHumanTasksApproved)
                    }
                }
            }
        }
    });

    private static object Step(string id, string taskId, string? condition) => new
    {
        id,
        name = id,
        condition,
        target = new { kind = "HumanTask", humanTask = new { @namespace = "humantask", id = taskId, version = 1 } }
    };

    private static async Task<AssetResult> RegisterCriticalAssetAsync(HttpClient client, string tenant = Tenant,
        string category = AssetProductionDescriptors.CriticalCategory)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assets");
        request.Headers.Add(AssetExecutionIdentity.TenantHeader, tenant);
        request.Headers.Add(AssetExecutionIdentity.UserHeader, "maintenance-requester");
        request.Headers.Add(AssetExecutionIdentity.RolesHeader, "asset-manager");
        request.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
        request.Content = JsonContent.Create(new RegisterAssetInput
        {
            AssetTag = $"CRITICAL-{Guid.NewGuid():N}",
            Name = "Critical packaging line",
            Description = "Critical equipment production release acceptance",
            Category = category,
            OrganizationId = Organization,
            Location = "Shanghai"
        }, options: WireOptions);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AssetResult>(WireOptions))!;
    }

    private static async Task<AssetOperationResult> RequestMaintenanceAsync(HttpClient client, string tenant, Guid assetId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/assets/{assetId}/maintenance");
        request.Headers.Add(AssetExecutionIdentity.TenantHeader, tenant);
        request.Headers.Add(AssetExecutionIdentity.UserHeader, "maintenance-requester");
        request.Headers.Add(AssetExecutionIdentity.RolesHeader, "asset-manager");
        request.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
        request.Content = JsonContent.Create(new MaintenanceRequestInput { AssetId = assetId, Reason = "Production maintenance" }, options: WireOptions);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AssetOperationResult>(WireOptions))!;
    }

    private static async Task WaitForWorkflowStatusAsync(ProductionHostFactory host, string workflowId,
        WorkflowInstanceStatus status)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await ReadWorkflowByIdAsync(host, workflowId)).Status == status)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        (await ReadWorkflowByIdAsync(host, workflowId)).Status.Should().Be(status);
    }

    private static async Task WaitForAssetStatusAsync(HttpClient client, Guid assetId, string status)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/assets/{assetId}");
            request.Headers.Add(AssetExecutionIdentity.TenantHeader, Tenant);
            request.Headers.Add(AssetExecutionIdentity.UserHeader, "asset-user");
            request.Headers.Add(AssetExecutionIdentity.RolesHeader, "asset-user");
            request.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            if ((await response.Content.ReadFromJsonAsync<AssetResult>(WireOptions))?.Status == status)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        (await ReadAssetAsync(client, assetId)).Status.Should().Be(status);
    }

    private static async Task<AssetResult> ReadAssetAsync(HttpClient client, Guid assetId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/assets/{assetId}");
        request.Headers.Add(AssetExecutionIdentity.TenantHeader, Tenant);
        request.Headers.Add(AssetExecutionIdentity.UserHeader, "asset-user");
        request.Headers.Add(AssetExecutionIdentity.RolesHeader, "asset-user");
        request.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetResult>(WireOptions))!;
    }

    private static async Task<WorkflowInstance> ReadWorkflowByIdAsync(ProductionHostFactory host, string workflowId, string tenant = Tenant)
    {
        using var scope = host.Services.CreateScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        return (await workflows.GetAsync(new RuntimeInstanceKey(tenant, workflowId)))!;
    }

    private static async Task<HumanTaskInstance> PendingTaskAsync(ProductionHostFactory host, string workflowId, string humanTaskId)
    {
        using var scope = host.Services.CreateScope();
        var tasks = await scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>()
            .GetPendingByWorkflowAsync(new RuntimeInstanceKey(Tenant, workflowId));
        return tasks.Should().ContainSingle(task => task.HumanTaskId == humanTaskId).Which;
    }

    private static async Task CompleteAsAsync(HttpClient client, string userId, string role, Guid assetId, string taskId, string outcome)
    {
        using var response = await CompleteTaskAsync(client, userId, role, assetId, taskId, outcome);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> CompleteTaskAsync(HttpClient client, string userId, string role, Guid assetId, string taskId, string outcome, string tenantId = Tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/assets/{assetId}/maintenance/tasks/{taskId}/complete");
        request.Headers.Add(AssetExecutionIdentity.TenantHeader, tenantId);
        request.Headers.Add(AssetExecutionIdentity.UserHeader, userId);
        request.Headers.Add(AssetExecutionIdentity.RolesHeader, role);
        request.Headers.Add(AssetExecutionIdentity.DataScopeHeader, "Tenant");
        request.Content = JsonContent.Create(new MaintenanceTaskCompletionInput
        {
            AssetId = assetId,
            HumanTaskId = taskId,
            Outcome = outcome,
            Note = "Production release acceptance"
        }, options: WireOptions);
        return await client.SendAsync(request);
    }

    private static async Task<HumanTaskInstance> WaitForPendingTaskAsync(ProductionHostFactory host, string workflowId, string humanTaskId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var scope = host.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
        var key = new RuntimeInstanceKey(Tenant, workflowId);
        while (!deadline.IsCancellationRequested)
        {
            var pending = await tasks.GetPendingByWorkflowAsync(key, deadline.Token);
            if (pending.SingleOrDefault(task => task.HumanTaskId == humanTaskId) is { } found)
                return found;
            await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
        }

        throw new TimeoutException($"The Workflow did not create '{humanTaskId}'.");
    }

    private sealed class HostDatabase
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"crest-assets-production-{Guid.NewGuid():N}.db");
        public string Schema { get; } = $"crest_asset_production_{Guid.NewGuid():N}";
    }

    private sealed class ProductionHostFactory(HostDatabase database, string? receipt, string mesBaseAddress,
        string? approvedReceipt = null, bool evolutionEnabled = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
            builder.UseSetting("AssetManagement:RuntimeConnectionString", Environment.GetEnvironmentVariable("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING")
                ?? throw new InvalidOperationException("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING is required."));
            builder.UseSetting("AssetManagement:RuntimeSchema", database.Schema);
            builder.UseSetting("AssetManagement:DatabasePath", database.Path);
            if (evolutionEnabled)
            {
                builder.UseSetting(AssetProductionEvolutionOperator.EnabledSetting, "true");
                builder.UseSetting(AssetProductionRelease.SigningKeySetting, SigningKey);
            }
            if (receipt is not null)
            {
                builder.UseSetting(AssetProductionRelease.ReceiptSetting, receipt);
                builder.UseSetting(AssetProductionRelease.SigningKeySetting, SigningKey);
            }
            if (approvedReceipt is not null)
            {
                builder.UseSetting("AssetManagement:ApprovedRelease:Receipt", approvedReceipt);
                builder.UseSetting("AssetManagement:ApprovedRelease:SigningKey", SigningKey);
            }
            builder.UseSetting(AssetMesClient.AddressSetting, mesBaseAddress);
            builder.UseSetting($"AssetManagement:Mes:TenantCredentials:{Tenant}", "sample-a");
            builder.UseSetting($"AssetManagement:Mes:ReleaseCredentials:{Tenant}", "sample-a-release");
        }
    }

    private sealed class MesDatabase : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("crest-asset-production-mes-");

        public async Task<RunningMes> StartAsync(string evidenceMaxAge = "00:30:00")
        {
            var app = CrestCreates.Sample.AssetManagement.Mes.Program.Build([
                "--urls=http://127.0.0.1:0",
                "--Logging:LogLevel:Default=Warning",
                $"--Mes:DatabasePath={System.IO.Path.Combine(_directory.FullName, "mes.db")}",
                $"--Mes:EvidenceMaxAge={evidenceMaxAge}",
                $"--Mes:Tenants:0:TenantId={Tenant}", "--Mes:Tenants:0:BearerToken=sample-a", "--Mes:Tenants:0:Permissions=mes.trial,mes.consume",
                $"--Mes:Tenants:1:TenantId={Tenant}", "--Mes:Tenants:1:BearerToken=sample-a-release", "--Mes:Tenants:1:Permissions=mes.release"
            ]);
            await app.StartAsync();
            return new RunningMes(app);
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class RunningMes(WebApplication app) : IAsyncDisposable
    {
        public string BaseAddress { get; } = app.Urls.Single();

        public HttpClient Client()
        {
            var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sample-a");
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
