using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Strongly-typed descriptor catalog for the Company Certification domain.
/// All descriptors (schemas, forms, capabilities, events, human tasks, workflow)
/// are defined as static properties composing a coherent control plane.
/// </summary>
public static class CompanyCertificationDescriptors
{
    //  Schema descriptors

    public static SchemaDescriptor CompanyCertificationSubmitInput { get; } = new()
    {
        Id = "schema_company_certification_submit_input",
        Name = "schema.CompanyCertificationSubmitInput",
        Version = 1,
        State = DescriptorState.Active,
        ChangeKind = SchemaChangeKind.Additive,
        Fields = new SchemaFieldDescriptor[]
        {
            new() { Name = "CompanyName",              FieldType = "string", IsRequired = true },
            new() { Name = "UnifiedSocialCreditCode", FieldType = "string", IsRequired = true },
            new() { Name = "CertificationType",        FieldType = "string", IsRequired = true },
            new() { Name = "ApplicationDate",   FieldType = "string", IsRequired = false, IsNullable = true },
            new() { Name = "Notes",             FieldType = "string", IsRequired = false, IsNullable = true, MaxLength = 2000 },
        },
    };

    public static SchemaDescriptor CompanyCertificationReviewInput { get; } = new()
    {
        Id = "schema_company_certification_review_input",
        Name = "schema.CompanyCertificationReviewInput",
        Version = 1,
        State = DescriptorState.Active,
        ChangeKind = SchemaChangeKind.Additive,
        Fields = new SchemaFieldDescriptor[]
        {
            new() { Name = "ReviewerNotes", FieldType = "string", IsRequired = true },
            new() { Name = "Decision",      FieldType = "string", IsRequired = true },
        },
    };

    public static SchemaDescriptor CompanyCertificationResult { get; } = new()
    {
        Id = "schema_company_certification_result",
        Name = "schema.CompanyCertificationResult",
        Version = 1,
        State = DescriptorState.Active,
        ChangeKind = SchemaChangeKind.Additive,
        Fields = new SchemaFieldDescriptor[]
        {
            new() { Name = "CertificationId", FieldType = "string", IsRequired = true },
            new() { Name = "Status",          FieldType = "string", IsRequired = true },
            new() { Name = "Message",         FieldType = "string", IsRequired = false, IsNullable = true },
        },
    };

    public static SchemaDescriptor CompanyCertificationApprovedPayload { get; } = new()
    {
        Id = "schema_company_certification_approved_payload",
        Name = "schema.CompanyCertificationApprovedPayload",
        Version = 1,
        State = DescriptorState.Active,
        ChangeKind = SchemaChangeKind.Additive,
        Fields = new SchemaFieldDescriptor[]
        {
            new() { Name = "CertificationId", FieldType = "string", IsRequired = true },
            new() { Name = "ApprovedBy",      FieldType = "string", IsRequired = true },
            new() { Name = "ApprovedAt",      FieldType = "string", IsRequired = false, IsNullable = true },
        },
    };

    public static SchemaDescriptor CompanyCertificationRejectedPayload { get; } = new()
    {
        Id = "schema_company_certification_rejected_payload",
        Name = "schema.CompanyCertificationRejectedPayload",
        Version = 1,
        State = DescriptorState.Active,
        ChangeKind = SchemaChangeKind.Additive,
        Fields = new SchemaFieldDescriptor[]
        {
            new() { Name = "CertificationId", FieldType = "string", IsRequired = true },
            new() { Name = "RejectedBy",      FieldType = "string", IsRequired = true },
            new() { Name = "Reason",          FieldType = "string", IsRequired = false, IsNullable = true },
        },
    };

    //  Form descriptors

    public static FormDescriptor CompanyCertificationSubmitForm { get; } = new()
    {
        Id = "form_company_certification_submit",
        Name = "form.CompanyCertificationSubmitForm",
        Version = 1,
        State = DescriptorState.Active,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_submit_input", 1),
        Fields = new FormFieldDescriptor[]
        {
            new() { SchemaFieldName = "CompanyName",              Label = "Company Name",               Order = 1, ControlType = "text" },
            new() { SchemaFieldName = "UnifiedSocialCreditCode", Label = "Unified Social Credit Code", Order = 2, ControlType = "text" },
            new() { SchemaFieldName = "CertificationType",        Label = "Certification Type",         Order = 3, ControlType = "select" },
            new() { SchemaFieldName = "ApplicationDate",          Label = "Application Date",           Order = 4, ControlType = "date" },
            new() { SchemaFieldName = "Notes",                    Label = "Notes",                      Order = 5, ControlType = "textarea" },
        },
    };

    public static FormDescriptor CompanyCertificationReviewForm { get; } = new()
    {
        Id = "form_company_certification_review",
        Name = "form.CompanyCertificationReviewForm",
        Version = 1,
        State = DescriptorState.Active,
        Schema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_review_input", 1),
        Fields = new FormFieldDescriptor[]
        {
            new() { SchemaFieldName = "ReviewerNotes", Label = "Reviewer Notes", Order = 1, ControlType = "textarea" },
            new() { SchemaFieldName = "Decision",      Label = "Decision",       Order = 2, ControlType = "select" },
        },
    };

    //  Capability descriptors

    public static CapabilityDescriptor SubmitCompanyCertification { get; } = new()
    {
        Id = "cap_submit_company_certification",
        Name = "capability.SubmitCompanyCertification",
        Version = 1,
        State = DescriptorState.Active,
        CapabilityKind = CapabilityKind.Command,
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_submit_input", 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_result", 1),
        Permissions = new[] { "CompanyCertification.Submit" },
        Produces = new EventRef[] { new("event", "evt_company_certification_submitted") },
        Categories = new[] { "Certification" },
    };

    public static CapabilityDescriptor ApproveCompanyCertification { get; } = new()
    {
        Id = "cap_approve_company_certification",
        Name = "capability.ApproveCompanyCertification",
        Version = 1,
        State = DescriptorState.Active,
        CapabilityKind = CapabilityKind.Command,
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_review_input", 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_approved_payload", 1),
        Permissions = new[] { "CompanyCertification.Approve" },
        Produces = new EventRef[] { new("event", "evt_company_certification_approved") },
        Categories = new[] { "Certification" },
    };

    public static CapabilityDescriptor RejectCompanyCertification { get; } = new()
    {
        Id = "cap_reject_company_certification",
        Name = "capability.RejectCompanyCertification",
        Version = 1,
        State = DescriptorState.Active,
        CapabilityKind = CapabilityKind.Command,
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_review_input", 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_rejected_payload", 1),
        Permissions = new[] { "CompanyCertification.Reject" },
        Produces = new EventRef[] { new("event", "evt_company_certification_rejected") },
        Categories = new[] { "Certification" },
    };

    //  HumanTask descriptor

    public static HumanTaskDescriptor ReviewCompanyCertification { get; } = new()
    {
        Id = "ht_review_company_certification",
        Name = "humantask.ReviewCompanyCertification",
        Version = 1,
        State = DescriptorState.Active,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_company_certification_review", 1),
        InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_review_input", 1),
        OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_result", 1),
        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
        Permissions = "CompanyCertification.Review",
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

    //  Workflow descriptor

    public static WorkflowDescriptor CompanyCertificationWorkflow { get; } = new()
    {
        Id = "wf_company_certification",
        Name = "workflow.CompanyCertificationWorkflow",
        Version = 1,
        State = DescriptorState.Active,
        Steps = new WorkflowStep[]
        {
            new()
            {
                Id = "step_submit",
                Name = "Submit Certification",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_submit_company_certification", 1),
                },
                Transitions = new[] { "step_review" },
            },
            new()
            {
                Id = "step_review",
                Name = "Review Certification",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_review_company_certification", 1),
                },
                Transitions = new[] { "step_approve" },
            },
            new()
            {
                Id = "step_approve",
                Name = "Finalize Approval",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_approve_company_certification", 1),
                },
                Transitions = Array.Empty<string>(),
            },
        },
    };

    //  Event descriptors

    public static EventDescriptor CompanyCertificationSubmitted { get; } = new()
    {
        Id = "evt_company_certification_submitted",
        Name = "event.CompanyCertificationSubmitted",
        Version = 1,
        State = DescriptorState.Active,
        PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_submit_input", 1),
        Category = EventCategory.Capability,
        Semantic = EventSemantic.StateTransition,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive,
    };

    public static EventDescriptor CompanyCertificationApproved { get; } = new()
    {
        Id = "evt_company_certification_approved",
        Name = "event.CompanyCertificationApproved",
        Version = 1,
        State = DescriptorState.Active,
        PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_approved_payload", 1),
        Category = EventCategory.Capability,
        Semantic = EventSemantic.StateTransition,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive,
    };

    public static EventDescriptor CompanyCertificationRejected { get; } = new()
    {
        Id = "evt_company_certification_rejected",
        Name = "event.CompanyCertificationRejected",
        Version = 1,
        State = DescriptorState.Active,
        PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_company_certification_rejected_payload", 1),
        Category = EventCategory.Capability,
        Semantic = EventSemantic.StateTransition,
        Importance = EventImportance.Business,
        ChangeKind = SchemaChangeKind.Additive,
    };
}
