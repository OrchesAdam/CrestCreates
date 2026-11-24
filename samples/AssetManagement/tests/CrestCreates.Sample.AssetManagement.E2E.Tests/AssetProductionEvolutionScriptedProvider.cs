using System.Text.Json;
using CrestCreates.Agent.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

internal sealed class AssetProductionEvolutionScriptedProvider(
    Func<string, string>? writeResponse = null) : IDescriptorAuthoringModelClient
{
    internal const string Intent = "Require MES trial evidence and independent quality sign-off for critical equipment.";
    private const string ProviderName = "asset-production-scripted";
    private const string ModelName = "asset-production-workflow";

    public DescriptorAuthoringModelRequest? Request { get; private set; }
    public int CallCount { get; private set; }

    public void Register(IServiceCollection services)
    {
        services.AddAgentPrompting();
        services.AddAgentMemoryReadRuntime();
        services.AddDescriptorAuthoring();
        services.AddSingleton<IDescriptorAuthoringModelClient>(this);
        services.Configure<LlmDescriptorAuthoringAgentOptions>(options =>
        {
            options.AuthorId = "asset-author-agent";
            options.ProviderProfileRef = new AgentPromptProviderProfileRef(ProviderName);
            options.ModelProfile = new DescriptorAuthoringModelProfile
            {
                ProfileName = ProviderName,
                ProviderName = ProviderName,
                ModelName = ModelName,
                SupportsJsonMode = true
            };
        });
        services.AddSingleton<IDescriptorAuthoringAgent, LlmDescriptorAuthoringAgent>();
    }

    public Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Request = request;
        CallCount++;
        return Task.FromResult(new DescriptorAuthoringModelResponse
        {
            ResponseText = writeResponse?.Invoke(request.Prompt.PromptInputHash.Value) ??
                WriteProposal(request.Prompt.PromptInputHash.Value),
            ProviderName = ProviderName,
            ModelName = ModelName,
            PromptInputHash = request.Prompt.PromptInputHash
        });
    }

    internal static string WriteProposal(
        string promptHash, string? qualityCondition = WorkflowConditionTokens.AllPriorHumanTasksApproved, string baseVersion = "1") =>
        JsonSerializer.Serialize(new
        {
            contractVersion = "8a.v1",
            outcome = "drafts",
            promptInputHash = promptHash,
            plan = new
            {
                planId = "asset-production-workflow-v3",
                plannedDescriptorRefs = new[]
                {
                    new { @namespace = "workflow", id = AssetContractIds.MaintenanceWorkflow, version = 3 }
                }
            },
            items = new[]
            {
                new
                {
                    descriptorKind = "Workflow",
                    descriptorId = AssetContractIds.MaintenanceWorkflow,
                    operation = "Update",
                    baseVersion,
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
                            Step(AssetProductionDescriptors.QualityStepId, AssetProductionDescriptors.QualityTaskId, qualityCondition)
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
}
