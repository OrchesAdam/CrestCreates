using System.Text.Json;
using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed class AgentMemoryLlmModelResponseEvidenceProjector : IAgentPromptCanonicalPayloadProjector<AgentMemoryLlmModelResponseEvidenceProjection>
{
    public void Write(Utf8JsonWriter writer, AgentMemoryLlmModelResponseEvidenceProjection projection)
    {
        writer.WriteStartObject();

        // Alphabetical: failureDetail, failureKind, metadata, modelName, promptInputHash, providerName
        if (projection.FailureDetail is not null)
        {
            writer.WriteString("failureDetail", projection.FailureDetail);
        }

        if (projection.FailureKind is not null)
        {
            writer.WriteString("failureKind", projection.FailureKind);
        }

        if (projection.Metadata is { Count: > 0 })
        {
            writer.WriteStartObject("metadata");
            foreach (var kvp in projection.Metadata.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                writer.WriteString(kvp.Key, kvp.Value);
            }
            writer.WriteEndObject();
        }

        if (projection.ModelName is not null)
        {
            writer.WriteString("modelName", projection.ModelName);
        }

        if (projection.PromptInputHash is not null)
        {
            writer.WriteString("promptInputHash", projection.PromptInputHash);
        }

        if (projection.ProviderName is not null)
        {
            writer.WriteString("providerName", projection.ProviderName);
        }

        writer.WriteEndObject();
    }
}
