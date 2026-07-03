using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

/// <summary>
/// Deterministic fake authoring agent for the Phase 7f golden scenario.
/// Produces a fixed descriptor draft set for the intent:
/// "Add second-level finance review before approving company certification."
/// Consumes only <see cref="AgentAuthoringContext"/>; does not depend on
/// raw memory stores, draft stores, activation services, or runtime services.
/// </summary>
public sealed class FakeCompanyCertificationAuthoringAgent : IDescriptorAuthoringAgent
{
    private const string Phase7fIntent = "Add second-level finance review before approving company certification.";
    private const string AuthorId = "fake-company-certification-authoring-agent";
    private const string DraftIdPrefix = "draft_company_certification";
    private const string CorrelationId = "phase7f-fake-authoring-correlation";

    public Task<CrestCreates.Agent.Authoring.Abstractions.Authoring.DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default)
    {
        var tenantId = context.Request.TenantId;
        var createdAt = DateTimeOffset.UnixEpoch;

        // Create the finance review HumanTask descriptor by copying from the existing review task
        var reviewTask = CompanyCertificationDescriptors.ReviewCompanyCertification;
        var financeTask = new HumanTaskDescriptor
        {
            Id = "ht_finance_review_company_certification",
            Name = "humantask.FinanceReviewCompanyCertification",
            Version = reviewTask.Version,
            State = reviewTask.State,
            SupersededById = reviewTask.SupersededById,
            Interaction = reviewTask.Interaction,
            InputSchema = reviewTask.InputSchema,
            OutputSchema = reviewTask.OutputSchema,
            AssigneeStrategy = reviewTask.AssigneeStrategy,
            Timeout = reviewTask.Timeout,
            Permissions = "CompanyCertification.FinanceReview",
            Outcomes = new CompletionOutcome[]
            {
                new()
                {
                    Condition = CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_approve_company_certification", 1),
                },
                new()
                {
                    Condition = CompletionCondition.Reject,
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_reject_company_certification", 1),
                },
            },
        };

        // Create the HumanTask draft
        var humanTaskDraft = new CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft
        {
            TenantId = tenantId,
            DraftId = $"{DraftIdPrefix}_finance_review_humantask",
            DescriptorKind = DescriptorKind.HumanTask,
            DescriptorId = financeTask.Id,
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            AuthorId = AuthorId,
            CreatedAt = createdAt,
            Payload = new HumanTaskDescriptorDraftPayload(financeTask),
            ProposedVersion = financeTask.Version.ToString(),
            Intent = context.Request.IntentText,
            CorrelationId = CorrelationId,
            Source = "Phase7fFakeAuthoringAgent",
        };

        // Update the workflow to insert step_finance_review between step_review and step_approve
        var originalWorkflow = CompanyCertificationDescriptors.CompanyCertificationWorkflow;

        // Deep-copy existing steps via the cloner
        var stepSubmit = CompanyCertificationDescriptorCloner.CopyWorkflowStep(originalWorkflow.Steps[0]);
        var stepReview = CompanyCertificationDescriptorCloner.CopyWorkflowStep(originalWorkflow.Steps[1]);
        var stepApprove = CompanyCertificationDescriptorCloner.CopyWorkflowStep(originalWorkflow.Steps[2]);

        // Update step_review's transitions to point to step_finance_review
        stepReview = new WorkflowStep
        {
            Id = stepReview.Id,
            Name = stepReview.Name,
            Condition = stepReview.Condition,
            InputMapping = stepReview.InputMapping,
            OutputMapping = stepReview.OutputMapping,
            OnError = stepReview.OnError,
            Target = stepReview.Target,
            Transitions = new[] { "step_finance_review" },
        };

        var stepFinanceReview = new WorkflowStep
        {
            Id = "step_finance_review",
            Name = "Finance Review Certification",
            Target = new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(
                    "ht_finance_review_company_certification", 1),
            },
            Transitions = new[] { "step_approve" },
        };

        var updatedWorkflow = new WorkflowDescriptor
        {
            Id = originalWorkflow.Id,
            Name = originalWorkflow.Name,
            Version = originalWorkflow.Version,
            State = originalWorkflow.State,
            SupersededById = originalWorkflow.SupersededById,
            VariableSchema = originalWorkflow.VariableSchema,
            DefaultVariableScope = originalWorkflow.DefaultVariableScope,
            Steps = new WorkflowStep[]
            {
                stepSubmit,
                stepReview,
                stepFinanceReview,
                stepApprove,
            },
        };

        // Create the workflow update draft
        var workflowDraft = new CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft
        {
            TenantId = tenantId,
            DraftId = $"{DraftIdPrefix}_workflow_finance_review",
            DescriptorKind = DescriptorKind.Workflow,
            DescriptorId = updatedWorkflow.Id,
            Operation = DescriptorDraftOperation.Update,
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            AuthorId = AuthorId,
            CreatedAt = createdAt,
            Payload = new WorkflowDescriptorDraftPayload(updatedWorkflow),
            BaseVersion = originalWorkflow.Version.ToString(),
            ProposedVersion = updatedWorkflow.Version.ToString(),
            Intent = context.Request.IntentText,
            CorrelationId = CorrelationId,
            Source = "Phase7fFakeAuthoringAgent",
        };

        var result = new CrestCreates.Agent.Authoring.Abstractions.Authoring.DescriptorAuthoringResult
        {
            Status = DescriptorAuthoringStatus.Succeeded,
            Plan = new DescriptorAuthoringPlan
            {
                PlanId = "plan_company_certification_finance_review",
                IntentText = Phase7fIntent,
                PlannedDescriptorRefs = new DescriptorRef[]
                {
                    new("humantask", financeTask.Id, 1),
                    new("workflow", updatedWorkflow.Id, 1),
                },
            },
            DraftSet = new DescriptorDraftSet
            {
                DraftSetId = "draftset_company_certification_finance_review",
                Drafts = new[] { humanTaskDraft, workflowDraft },
            },
            Diagnostics = Array.Empty<DescriptorAuthoringDiagnostic>(),
        };

        return Task.FromResult(result);
    }
}
