using CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.DescriptorDraft;

internal static class DescriptorDraftPayloadSupport
{
    public static void EnsureSupported(DescriptorDraftPayload payload)
    {
        _ = GetPayloadType(payload);
    }

    public static int GetPayloadType(DescriptorDraftPayload payload) => payload switch
    {
        SchemaDescriptorDraftPayload => 1,
        FormDescriptorDraftPayload => 2,
        CapabilityDescriptorDraftPayload => 3,
        HumanTaskDescriptorDraftPayload => 4,
        WorkflowDescriptorDraftPayload => 5,
        EventDescriptorDraftPayload => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(payload), payload.GetType(), "Unsupported DescriptorDraftPayload type.")
    };
}
