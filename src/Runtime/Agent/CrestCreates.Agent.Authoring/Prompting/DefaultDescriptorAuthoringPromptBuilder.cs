using System.Text;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed class DefaultDescriptorAuthoringPromptBuilder : IDescriptorAuthoringPromptBuilder
{
    public DescriptorAuthoringPromptOutput Build(DescriptorAuthoringPromptInput input)
    {
        if (input.PromptInputHash is null)
        {
            throw new InvalidOperationException("DescriptorAuthoringPromptInput.PromptInputHash is required before building a prompt.");
        }

        var systemPrompt = """
            You author CrestCreates descriptor drafts only.
            You must return JSON matching contract 7g.v1.
            You must not activate descriptors, approve changes, call Control Plane tools, mutate runtime registries, or execute runtime handlers.
            Agent memory is recalled non-authoritative context. Metadata context wins over memory.
            Use the provided visible descriptors, memory, and supported output kinds to produce accurate drafts.
            For Workflow updates, preserve existing steps and only add/modify steps relevant to the stated intent.
            """;

        var sb = new StringBuilder();
        sb.AppendLine("Intent:");
        sb.AppendLine(input.IntentText);
        sb.AppendLine();
        sb.AppendLine("Tenant:");
        sb.AppendLine(input.TenantId);
        sb.AppendLine();
        sb.AppendLine("PromptInputHash:");
        sb.AppendLine(input.PromptInputHash.Value);
        sb.AppendLine();

        // Visible descriptors from metadata
        if (input.Metadata.Descriptors.Count > 0)
        {
            sb.AppendLine("Visible Descriptors:");
            foreach (var d in input.Metadata.Descriptors)
            {
                sb.AppendLine($"  - {d.Ref.Namespace}/{d.Ref.Id} v{d.Ref.Version?.ToString() ?? "latest"} ({d.Kind}) \"{d.Name ?? "unknown"}\"");
                if (d.ContractHash is not null)
                {
                    sb.AppendLine($"    contractHash: {d.ContractHash.Value}");
                }
            }
            sb.AppendLine();
        }

        // Visible descriptor refs
        if (input.VisibleDescriptorRefs.Count > 0)
        {
            sb.AppendLine("Visible Descriptor Refs:");
            foreach (var r in input.VisibleDescriptorRefs)
            {
                sb.AppendLine($"  - {r.Namespace}/{r.Id} v{r.Version?.ToString() ?? "latest"}");
            }
            sb.AppendLine();
        }

        // Memory context
        if (input.Memory.Memories.Count > 0 || input.Memory.IsAuthoritative)
        {
            sb.AppendLine("Memory Context:");
            sb.AppendLine($"  isAuthoritative: {input.Memory.IsAuthoritative}");
            foreach (var m in input.Memory.Memories)
            {
                sb.AppendLine($"  - [{m.Kind}] {m.MemoryId}: {m.Content} (confidence: {m.Confidence})");
                if (m.DescriptorRefs.Count > 0)
                {
                    sb.AppendLine($"    refs: {string.Join(", ", m.DescriptorRefs.Select(r => $"{r.Namespace}/{r.Id}"))}");
                }
            }
            sb.AppendLine();
        }

        // Supported output kinds
        if (input.SupportedDescriptorKinds.Count > 0)
        {
            sb.AppendLine("Supported Output Kinds:");
            foreach (var k in input.SupportedDescriptorKinds)
            {
                sb.AppendLine($"  - {k}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Return a descriptor authoring plan and draft payloads matching contract 7g.v1 only.");

        var userPrompt = sb.ToString();

        return new DescriptorAuthoringPromptOutput
        {
            ContractVersion = input.ContractVersion,
            PromptTemplateVersion = "descriptor-authoring-prompt-template-v1",
            PromptInputHash = input.PromptInputHash,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt
        };
    }
}
