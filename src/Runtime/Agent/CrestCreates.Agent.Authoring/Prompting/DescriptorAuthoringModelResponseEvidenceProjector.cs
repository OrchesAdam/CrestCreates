using System.Text.Json;
using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed class DescriptorAuthoringModelResponseEvidenceProjector : IAgentPromptCanonicalPayloadProjector<DescriptorAuthoringModelResponseEvidenceProjection>
{
    public void Write(Utf8JsonWriter writer, DescriptorAuthoringModelResponseEvidenceProjection projection)
    {
        writer.WriteStartObject();

        writer.WriteString("providerName", projection.ProviderName);
        writer.WriteString("modelName", projection.ModelName);
        if (projection.PromptInputHash is not null)
        {
            writer.WriteString("promptInputHash", projection.PromptInputHash.Value);
        }
        writer.WriteString("failureKind", projection.FailureKind.ToString());
        if (projection.FailureDetail is not null)
        {
            writer.WriteString("failureDetail", projection.FailureDetail);
        }

        writer.WriteEndObject();
    }
}
