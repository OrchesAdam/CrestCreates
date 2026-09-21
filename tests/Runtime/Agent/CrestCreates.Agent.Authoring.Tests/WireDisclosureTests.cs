using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class WireDisclosureTests
{
    [Fact]
    public void DefaultPrompt_DisclosesSourceGeneratedExamples_ThatRealParserMaterializes()
    {
        var output = new DefaultDescriptorAuthoringPromptBuilder().Build(CreateInput());
        var examples = ExtractJsonExamples(output.UserPrompt);
        var parser = new JsonDescriptorAuthoringOutputParser();
        var context = new DescriptorAuthoringParseContext
        {
            TenantId = "wire-disclosure-tenant",
            AuthorId = "wire-disclosure-agent",
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = DateTimeOffset.UnixEpoch,
            IntentText = "Describe the intended change here.",
            ExpectedPromptInputHash = "<prompt-input-hash-from-visible-context>"
        };

        examples.Should().HaveCount(2);

        var humanTaskResult = parser.Parse(examples[0], context);
        humanTaskResult.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        humanTaskResult.Plan.PlanId.Should().Be("example-plan");
        var plannedHumanTaskRef = humanTaskResult.Plan.PlannedDescriptorRefs.Should().ContainSingle().Which;
        plannedHumanTaskRef.Namespace.Should().Be("humantask");
        plannedHumanTaskRef.Id.Should().Be("<human-task-id>");
        plannedHumanTaskRef.Version.Should().Be(1);
        var humanTask = humanTaskResult.DraftSet.Drafts.Should().ContainSingle().Which.Payload
            .GetDescriptor().Should().BeOfType<HumanTaskDescriptor>().Subject;
        humanTask.Id.Should().Be("<human-task-id>");
        humanTask.Name.Should().Be("<human-task-name>");
        humanTask.Version.Should().Be(1);
        humanTaskResult.DraftSet.Drafts[0].Operation.Should().Be(DescriptorDraftOperation.Create);
        humanTask.Permissions.Should().Be("<permission-name>");
        humanTask.Interaction.Id.Should().Be("<interaction-id>");
        humanTask.Interaction.Version.Should().Be(1);
        humanTask.InputSchema.Should().BeNull();
        humanTask.OutputSchema.Should().BeNull();
        humanTask.AssigneeStrategy.Should().Be(AssigneeStrategy.CandidateGroup);
        humanTask.Outcomes.Select(outcome => outcome.Condition)
            .Should().Equal(CompletionCondition.Approve, CompletionCondition.Reject);

        var workflowResult = parser.Parse(examples[1], context);
        workflowResult.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        var workflow = workflowResult.DraftSet.Drafts.Should().ContainSingle().Which.Payload
            .GetDescriptor().Should().BeOfType<WorkflowDescriptor>().Subject;
        workflow.Id.Should().Be("<workflow-id>");
        workflow.Version.Should().Be(1);
        workflow.Steps.Should().HaveCount(3);
        var humanTaskTarget = workflow.Steps[0].Target.Should().BeOfType<HumanTaskTarget>().Which;
        humanTaskTarget.HumanTask.Id.Should().Be("<human-task-id>");
        humanTaskTarget.HumanTask.Version.Should().Be(1);
        workflow.Steps[0].Transitions.Should().Equal("step-capability");
        workflow.Steps[0].OnError.Should().Be(StepErrorBehavior.Fail);
        var capabilityTarget = workflow.Steps[1].Target.Should().BeOfType<CapabilityTarget>().Which;
        capabilityTarget.Capability.Id.Should().Be("<capability-id>");
        capabilityTarget.Capability.Version.Should().Be(2);
        workflow.Steps[1].Transitions.Should().Equal("step-sub-workflow");
        workflow.Steps[1].OnError.Should().Be(StepErrorBehavior.Fail);
        var subWorkflowTarget = workflow.Steps[2].Target.Should().BeOfType<SubWorkflowTarget>().Which;
        subWorkflowTarget.SubWorkflow.Id.Should().Be("<sub-workflow-id>");
        subWorkflowTarget.SubWorkflow.Version.Should().Be(1);
        workflow.Steps[2].Transitions.Should().BeEmpty();
        workflow.Steps[2].OnError.Should().Be(StepErrorBehavior.Fail);
    }

    [Fact]
    public void DefaultPrompt_StatesReferenceAndFallbackBoundaries_AndOptionsUseV2()
    {
        var output = new DefaultDescriptorAuthoringPromptBuilder().Build(CreateInput());

        output.ContractVersion.Should().Be("7g.v1");
        output.PromptTemplateVersion.Should().Be("descriptor-authoring-prompt-template-v2");
        output.SystemPrompt.Should().Contain("contract 7g.v1");
        output.UserPrompt.Should().Contain("References to existing descriptors must come from Visible Descriptors or Visible Descriptor Refs.");
        output.UserPrompt.Should().Contain("A reference to a descriptor proposed in this same response is allowed");
        output.UserPrompt.Should().Contain("IDs in the examples below are placeholders");
        output.UserPrompt.Should().Contain("The parser does not enforce every business requirement");
        output.UserPrompt.Should().Contain("does not support state");
        LlmDescriptorAuthoringAgentOptions.DefaultPromptTemplateVersion.Value
            .Should().Be("descriptor-authoring-prompt-template-v2");
    }

    private static string[] ExtractJsonExamples(string prompt)
        => prompt.Split("```json", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(block => block[..block.IndexOf("```", StringComparison.Ordinal)].Trim())
            .ToArray();

    private static DescriptorAuthoringPromptInput CreateInput() => new()
    {
        ContractVersion = "7g.v1",
        TenantId = "wire-disclosure-tenant",
        IntentText = "Describe the intended change here.",
        Metadata = new DescriptorAuthoringMetadataContextProjection(),
        Memory = new DescriptorAuthoringMemoryProjection { IsAuthoritative = false },
        PromptInputHash = new CanonicalHash
        {
            Value = "wire-disclosure-hash",
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "PromptInput",
            Scope = "InternalFull",
            Purpose = "Authoring",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "prompt-input-v1"
        }
    };
}
