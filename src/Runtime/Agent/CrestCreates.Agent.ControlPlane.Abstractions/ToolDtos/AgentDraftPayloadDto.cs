using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Nested one-of payload DTO replacing abstract DescriptorDraftPayload.
/// Discriminator determines which sub-record is populated.
/// Invariant: only the sub-record matching Discriminator should be non-null.
/// Projection helpers must not populate sub-records from other descriptor kinds.
/// </summary>
public sealed record AgentDraftPayloadDto
{
    public required DescriptorKind Discriminator { get; init; }
    public AgentCapabilityDraftPayloadDto? Capability { get; init; }
    public AgentWorkflowDraftPayloadDto? Workflow { get; init; }
    public AgentHumanTaskDraftPayloadDto? HumanTask { get; init; }
    public AgentFormDraftPayloadDto? Form { get; init; }
    public AgentEventDraftPayloadDto? Event { get; init; }
    public AgentSchemaDraftPayloadDto? Schema { get; init; }
}
