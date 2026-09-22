using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Domain.Shared.Enums;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Registry;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using DescriptorDraftModel = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetApprovedInventoryHandoffAcceptanceTests
{
    [Fact]
    public async Task ActivatedRequest_LoadsCheckedParsedDescriptorIntoCatalog()
    {
        const string tenantId = "asset-approved-host-smoke-tenant";
        const string authorId = "asset-approved-host-smoke-author";
        const string intent = "Asset maintenance initial review";
        const string promptHash = "asset-authoring-prompt-hash";

        await using var harness = await AssetControlPlaneApprovalHarness.CreateAsync(tenantId, authorId);
        var draft = await harness.ParseAndSaveDraftAsync(
            CreateAssetCandidateJson(promptHash, intent),
            intent,
            promptHash,
            DateTimeOffset.UnixEpoch);
        var parsed = draft.Payload.GetDescriptor();
        var submission = await harness.SubmitAsync(draft);
        submission.Request.Status.Should().Be(ActivationRequestStatus.UnderReview);

        Func<Task> checkBeforeApproval = () => harness.CheckApprovedInventoryAsync(submission.Request);
        await checkBeforeApproval.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Only Activated requests may load approved content*");

        var activated = await harness.CompleteHumanAsync(
            submission,
            "asset-human-reviewer",
            "Approve");
        activated.Status.Should().Be(ActivationRequestStatus.Activated);

        var checkedInventory = await harness.CheckApprovedInventoryAsync(activated);
        var checkedParsed = checkedInventory.Descriptors
            .Should().ContainSingle(descriptor => descriptor.Id == parsed.Id)
            .Which;
        checkedParsed.Should().BeEquivalentTo(parsed);
        checkedInventory.Descriptors.Should().BeEquivalentTo(
            AssetDescriptorCatalog.Schemas.Cast<IDescriptor>()
                .Concat(AssetDescriptorCatalog.Capabilities)
                .Append(AssetDescriptorCatalog.MaintenanceForm)
                .Append(AssetDescriptorCatalog.MaintenanceHumanTask)
                .Append(AssetDescriptorCatalog.MaintenanceWorkflow)
                .Append(parsed)
                .ToArray());

        await harness.AddCheckedDescriptorsToCatalogAsync(checkedInventory);
    }

    [Fact]
    public async Task SequentialApprovedReceipts_ReplaceWorkflowVersionAndRejectForeignOrTamperedReceipt()
    {
        const string tenantId = "tenant-a";
        const string authorId = "asset-approved-host-sequence-author";
        const string initialIntent = "Asset maintenance initial review";
        const string initialPromptHash = "asset-authoring-prompt-hash-sequence";

        await using var harness = await AssetControlPlaneApprovalHarness.CreateAsync(tenantId, authorId);
        var initialDraft = await harness.ParseAndSaveDraftAsync(
            CreateAssetCandidateJson(initialPromptHash, initialIntent),
            initialIntent,
            initialPromptHash,
            DateTimeOffset.UnixEpoch);
        var parsedInitial = initialDraft.Payload.GetDescriptor();
        var initialSubmission = await harness.SubmitAsync(initialDraft);
        var initialActivated = await harness.CompleteHumanAsync(
            initialSubmission,
            "asset-human-reviewer",
            "Approve");
        var initialReceipt = await harness.CheckApprovedInventoryAsync(initialActivated);

        var receiptDescriptors = initialReceipt.Descriptors as IDescriptor[]
            ?? throw new InvalidOperationException("The checked inventory receipt must expose its array snapshot for this tamper test.");
        var parsedInitialIndex = Array.FindIndex(receiptDescriptors, descriptor => descriptor.Id == parsedInitial.Id);
        parsedInitialIndex.Should().BeGreaterThanOrEqualTo(0);
        var parsedInitialHumanTask = parsedInitial.Should().BeOfType<HumanTaskDescriptor>().Subject;
        receiptDescriptors[parsedInitialIndex] = new HumanTaskDescriptor
        {
            Id = parsedInitialHumanTask.Id,
            Name = parsedInitialHumanTask.Name + " tampered",
            Version = parsedInitialHumanTask.Version,
            State = parsedInitialHumanTask.State,
            Interaction = parsedInitialHumanTask.Interaction,
            AssigneeStrategy = parsedInitialHumanTask.AssigneeStrategy,
            Outcomes = parsedInitialHumanTask.Outcomes
        };
        Func<Task> addTampered = () => harness.AddCheckedDescriptorsToCatalogAsync(initialReceipt);
        await addTampered.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*receipt no longer matches the authoritative retained package content*");
        receiptDescriptors[parsedInitialIndex] = parsedInitial;

        await using (var foreignHarness = await AssetControlPlaneApprovalHarness.CreateAsync(tenantId, "foreign-author"))
        {
            Func<Task> addForeign = () => foreignHarness.AddCheckedDescriptorsToCatalogAsync(initialReceipt);
            await addForeign.Should().ThrowAsync<InvalidOperationException>();
        }

        await harness.AddCheckedDescriptorsToCatalogAsync(initialReceipt);

        const string workflowIntent = "Replace Asset maintenance workflow with two approved review steps";
        const string workflowPromptHash = "asset-authoring-workflow-prompt-hash";
        var workflowDraft = await ParseAndSaveWorkflowUpdateAsync(
            harness,
            workflowPromptHash,
            workflowIntent);
        var parsedWorkflow = workflowDraft.Payload.GetDescriptor()
            .Should().BeOfType<WorkflowDescriptor>().Subject;
        parsedWorkflow.Version.Should().Be(2);
        parsedWorkflow.Steps.Should().HaveCount(2);
        parsedWorkflow.Steps[0].Target.Should().BeOfType<HumanTaskTarget>()
            .Which.HumanTask.Id.Should().Be(AssetContractIds.MaintenanceInitialHumanTask);
        parsedWorkflow.Steps[1].Target.Should().BeOfType<HumanTaskTarget>()
            .Which.HumanTask.Id.Should().Be(AssetContractIds.MaintenanceHumanTask);
        parsedWorkflow.Steps[1].Condition.Should().Be(WorkflowConditionTokens.PreviousHumanTaskApproved);

        var workflowSubmission = await harness.SubmitAsync(workflowDraft);
        workflowSubmission.Request.Status.Should().Be(ActivationRequestStatus.UnderReview);
        Func<Task> checkWorkflowBeforeApproval = () => harness.CheckApprovedInventoryAsync(workflowSubmission.Request);
        await checkWorkflowBeforeApproval.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Only Activated requests may load approved content*");

        var workflowActivated = await harness.CompleteHumanAsync(
            workflowSubmission,
            "asset-workflow-reviewer",
            "Approve");
        var workflowReceipt = await harness.CheckApprovedInventoryAsync(workflowActivated);
        workflowReceipt.Descriptors.Should().BeEquivalentTo(
            AssetDescriptorCatalog.Schemas.Cast<IDescriptor>()
                .Concat(AssetDescriptorCatalog.Capabilities)
                .Append(AssetDescriptorCatalog.MaintenanceForm)
                .Append(AssetDescriptorCatalog.MaintenanceHumanTask)
                .Append(parsedInitial)
                .Append(parsedWorkflow)
                .ToArray());
        workflowReceipt.Descriptors.OfType<WorkflowDescriptor>()
            .Should().NotContain(workflow => workflow.Version == 1);
        workflowReceipt.Descriptors.OfType<WorkflowDescriptor>()
            .Should().ContainSingle()
            .Which.Should().BeEquivalentTo(parsedWorkflow);

        var firstApprovedReceipt = await harness.CheckApprovedInventoryAsync(initialActivated);
        var secondApprovedReceipt = await harness.CheckApprovedInventoryAsync(workflowActivated);
        firstApprovedReceipt.Descriptors.OfType<WorkflowDescriptor>()
            .Should().ContainSingle()
            .Which.Version.Should().Be(1);
        secondApprovedReceipt.Descriptors.OfType<WorkflowDescriptor>()
            .Should().ContainSingle()
            .Which.Version.Should().Be(2);

        var stableHashBuilder = new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer());
        var approvedUnion = BuildApprovedDescriptorUnion(
            stableHashBuilder,
            firstApprovedReceipt.Descriptors,
            secondApprovedReceipt.Descriptors);
        approvedUnion.OfType<WorkflowDescriptor>()
            .Select(workflow => workflow.Version)
            .Should().Contain(1).And.Contain(2);

        var workflowV2 = secondApprovedReceipt.Descriptors.OfType<WorkflowDescriptor>()
            .Single(workflow => workflow.Version == 2);
        using var factory = new AssetCandidateWebApplicationFactory(approvedUnion);
        using (var inspection = factory.Services.CreateScope())
        {
            var workflows = inspection.ServiceProvider.GetRequiredService<IWorkflowRegistry>();
            var loadedWorkflowV2 = workflows.GetByVersion(AssetContractIds.MaintenanceWorkflow, 2);
            loadedWorkflowV2.Should().BeSameAs(workflowV2);
            var loadedHashes = stableHashBuilder.Build(loadedWorkflowV2!);
            var receiptHashes = stableHashBuilder.Build(workflowV2);
            loadedHashes.ContractHash.Should().BeEquivalentTo(receiptHashes.ContractHash);
            loadedHashes.DefinitionHash.Should().BeEquivalentTo(receiptHashes.DefinitionHash);
        }

        using var client = factory.CreateClient();
        var (asset, initialMaintenance) = await StartMaintenanceAsync(client);
        await CompleteAsync(factory, initialMaintenance.HumanTaskId!, "Approve", "Initial review approved");

        using (var inspection = factory.Services.CreateScope())
        {
            var taskStore = inspection.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();
            var initialTask = await taskStore.GetAsync(
                new RuntimeInstanceKey(tenantId, initialMaintenance.HumanTaskId!));
            initialTask.Should().NotBeNull();
            var finalTasks = await taskStore.GetPendingByWorkflowAsync(initialTask!.WorkflowKey!.Value);
            finalTasks.Should().ContainSingle(task =>
                task.HumanTaskId == AssetContractIds.MaintenanceHumanTask
                && task.HumanTaskVersion == 1);
            await CompleteAsync(factory, finalTasks.Single().Id, "Approve", "Final review approved");
        }

        var completed = await SendAsync<AssetResult>(
            client,
            HttpMethod.Get,
            $"/api/assets/{asset.Id}",
            null,
            HttpStatusCode.OK);
        completed.Status.Should().Be(nameof(AssetStatus.Available));

        await harness.AddCheckedDescriptorsToCatalogAsync(secondApprovedReceipt);
    }

    private static IReadOnlyList<IDescriptor> BuildApprovedDescriptorUnion(
        IDescriptorStableHashBuilder hashBuilder,
        params IReadOnlyList<IDescriptor>[] receipts)
    {
        var union = new Dictionary<ApprovedDescriptorKey, IDescriptor>();
        foreach (var receipt in receipts)
        {
            foreach (var descriptor in receipt)
            {
                var key = ApprovedDescriptorKey.Create(descriptor);
                if (!union.TryGetValue(key, out var existing))
                {
                    union.Add(key, descriptor);
                    continue;
                }

                var existingHashes = hashBuilder.Build(existing);
                var candidateHashes = hashBuilder.Build(descriptor);
                if (!existingHashes.ContractHash.Equals(candidateHashes.ContractHash)
                    || !existingHashes.DefinitionHash.Equals(candidateHashes.DefinitionHash))
                {
                    throw new InvalidOperationException(
                        $"Approved descriptor union contains conflicting canonical hashes for {descriptor.FullId} v{key.Version} ({key.Kind}).");
                }
            }
        }

        return union.Values.ToArray();
    }

    private static async Task<DescriptorDraftModel> ParseAndSaveWorkflowUpdateAsync(
        AssetControlPlaneApprovalHarness harness,
        string promptHash,
        string intent)
    {
        var result = new JsonDescriptorAuthoringOutputParser().Parse(
            CreateWorkflowUpdateJson(promptHash, intent),
            new DescriptorAuthoringParseContext
            {
                TenantId = harness.TenantId,
                AuthorId = harness.AuthorId,
                AuthorKind = DescriptorDraftAuthorKind.Agent,
                CreatedAt = DateTimeOffset.UnixEpoch,
                IntentText = intent,
                ExpectedPromptInputHash = promptHash
            });
        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        var parsed = result.DraftSet.Drafts.Should().ContainSingle().Which;
        parsed.Operation.Should().Be(DescriptorDraftOperation.Update);
        parsed.ProposedVersion.Should().Be("2");
        parsed.BaseVersion.Should().Be("1");
        await harness.Services.GetRequiredService<IDescriptorDraftStore>().SaveAsync(parsed);
        return parsed;
    }

    internal static string CreateAssetCandidateJson(string promptHash, string intent)
        => JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1",
            promptInputHash = promptHash,
            plan = new
            {
                planId = "asset-maintenance-initial-review",
                intentText = intent,
                plannedDescriptorRefs = new[]
                {
                    new { @namespace = "humantask", id = AssetContractIds.MaintenanceInitialHumanTask, version = 1 }
                }
            },
            items = new[]
            {
                new
                {
                    descriptorKind = "HumanTask",
                    descriptorId = AssetContractIds.MaintenanceInitialHumanTask,
                    operation = "Create",
                    rationale = "Require a human reviewer before selecting the candidate Asset contract.",
                    payload = new
                    {
                        id = AssetContractIds.MaintenanceInitialHumanTask,
                        name = "Asset maintenance initial review",
                        state = "Active",
                        version = 1,
                        assigneeStrategy = "CandidateGroup",
                        interaction = new { @namespace = "form", id = AssetContractIds.MaintenanceForm, version = 1 },
                        outcomes = new[]
                        {
                            new { condition = "Approve" },
                            new { condition = "Reject" }
                        }
                    }
                }
            }
        });

    private static string CreateWorkflowUpdateJson(string promptHash, string intent)
        => JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1",
            promptInputHash = promptHash,
            plan = new
            {
                planId = "asset-maintenance-workflow-v2",
                intentText = intent,
                plannedDescriptorRefs = new[]
                {
                    new { @namespace = "workflow", id = AssetContractIds.MaintenanceWorkflow, version = 2 }
                }
            },
            items = new[]
            {
                new
                {
                    descriptorKind = "Workflow",
                    descriptorId = AssetContractIds.MaintenanceWorkflow,
                    operation = "Update",
                    baseVersion = "1",
                    rationale = "Replace the Asset maintenance workflow with initial and final human review steps.",
                    payload = new
                    {
                        id = AssetContractIds.MaintenanceWorkflow,
                        name = "Supplied Asset maintenance workflow",
                        version = 2,
                        steps = new[]
                        {
                            new
                                {
                                    id = "maintenance-initial-review",
                                    name = "Supplied initial maintenance review",
                                    condition = (string?)null,
                                    target = new
                                {
                                    kind = "HumanTask",
                                    humanTask = new
                                    {
                                        @namespace = "humantask",
                                        id = AssetContractIds.MaintenanceInitialHumanTask,
                                        version = 1
                                    }
                                }
                            },
                            new
                            {
                                id = "maintenance-final-review",
                                name = "Supplied final maintenance review",
                                condition = (string?)WorkflowConditionTokens.PreviousHumanTaskApproved,
                                target = new
                                {
                                    kind = "HumanTask",
                                    humanTask = new
                                    {
                                        @namespace = "humantask",
                                        id = AssetContractIds.MaintenanceHumanTask,
                                        version = 1
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

    private static async Task<(AssetResult Asset, AssetOperationResult Maintenance)> StartMaintenanceAsync(
        HttpClient client)
    {
        SetIdentity(client, "tenant-a", "manager-1", "asset-manager", "Tenant");
        var asset = await SendAsync<AssetResult>(
            client,
            HttpMethod.Post,
            "/api/assets",
            new RegisterAssetInput
            {
                AssetTag = $"LAPTOP-{Guid.NewGuid():N}",
                Name = "Two-stage laptop",
                Description = "Approved inventory host fixture",
                Category = "Equipment",
                OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Location = "Shanghai"
            },
            HttpStatusCode.Created);
        var maintenance = await SendAsync<AssetOperationResult>(
            client,
            HttpMethod.Post,
            $"/api/assets/{asset.Id}/maintenance",
            new MaintenanceRequestInput { AssetId = asset.Id, Reason = "Two-stage review" },
            HttpStatusCode.Accepted);
        return (asset, maintenance);
    }

    private static async Task CompleteAsync(
        AssetCandidateWebApplicationFactory factory,
        string taskId,
        string outcome,
        string note,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<AssetExecutionIdentity>();
        identity.Set(
            "tenant-a",
            "manager-1",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DataScope.Tenant,
            "asset-manager");
        await scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>()
            .CompleteAsync(taskId, outcome, note, ct);
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client,
        HttpMethod method,
        string uri,
        object? input,
        HttpStatusCode expected)
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

    private static void SetIdentity(
        HttpClient client,
        string tenant,
        string user,
        string role,
        string scope)
    {
        foreach (var name in new[]
        {
            AssetExecutionIdentity.TenantHeader,
            AssetExecutionIdentity.UserHeader,
            AssetExecutionIdentity.RolesHeader,
            AssetExecutionIdentity.DataScopeHeader,
            AssetExecutionIdentity.OrganizationHeader
        })
        {
            client.DefaultRequestHeaders.Remove(name);
        }

        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.TenantHeader, tenant);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.UserHeader, user);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.RolesHeader, role);
        client.DefaultRequestHeaders.Add(AssetExecutionIdentity.DataScopeHeader, scope);
    }

    private readonly record struct ApprovedDescriptorKey(
        string Namespace,
        string Id,
        int? Version,
        DescriptorKind Kind)
    {
        public static ApprovedDescriptorKey Create(IDescriptor descriptor)
            => new(
                descriptor.Namespace,
                descriptor.Id,
                (descriptor as IVersionedDescriptor)?.Version,
                descriptor.Kind);
    }
}
