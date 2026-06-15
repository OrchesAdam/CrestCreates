using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Strongly-typed descriptor inventory for the Company Certification control plane.
/// Collects all descriptors defined in <see cref="CompanyCertificationDescriptors"/>
/// and exposes them grouped by descriptor kind for consumption by registries, validators,
/// and catalogues.
/// </summary>
public static class CompanyCertificationDescriptorInventory
{
    public static IReadOnlyList<SchemaDescriptor> AllSchemas { get; } =
    [
        CompanyCertificationDescriptors.CompanyCertificationSubmitInput,
        CompanyCertificationDescriptors.CompanyCertificationReviewInput,
        CompanyCertificationDescriptors.CompanyCertificationResult,
        CompanyCertificationDescriptors.CompanyCertificationApprovedPayload,
        CompanyCertificationDescriptors.CompanyCertificationRejectedPayload,
    ];

    public static IReadOnlyList<FormDescriptor> AllForms { get; } =
    [
        CompanyCertificationDescriptors.CompanyCertificationSubmitForm,
        CompanyCertificationDescriptors.CompanyCertificationReviewForm,
    ];

    public static IReadOnlyList<CapabilityDescriptor> AllCapabilities { get; } =
    [
        CompanyCertificationDescriptors.SubmitCompanyCertification,
        CompanyCertificationDescriptors.ApproveCompanyCertification,
        CompanyCertificationDescriptors.RejectCompanyCertification,
    ];

    public static IReadOnlyList<HumanTaskDescriptor> AllHumanTasks { get; } =
    [
        CompanyCertificationDescriptors.ReviewCompanyCertification,
    ];

    public static IReadOnlyList<WorkflowDescriptor> AllWorkflows { get; } =
    [
        CompanyCertificationDescriptors.CompanyCertificationWorkflow,
    ];

    public static IReadOnlyList<EventDescriptor> AllEvents { get; } =
    [
        CompanyCertificationDescriptors.CompanyCertificationSubmitted,
        CompanyCertificationDescriptors.CompanyCertificationApproved,
        CompanyCertificationDescriptors.CompanyCertificationRejected,
    ];

    /// <summary>
    /// All descriptors across all kinds, ordered for deterministic consumption.
    /// </summary>
    public static IReadOnlyList<IDescriptor> AllDescriptors()
    {
        var result = new List<IDescriptor>();
        result.AddRange(AllSchemas);
        result.AddRange(AllForms);
        result.AddRange(AllCapabilities);
        result.AddRange(AllHumanTasks);
        result.AddRange(AllWorkflows);
        result.AddRange(AllEvents);
        return result;
    }
}
