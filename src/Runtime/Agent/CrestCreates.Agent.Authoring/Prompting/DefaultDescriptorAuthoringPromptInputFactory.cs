using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Agent.Authoring.Prompting;

public sealed class DefaultDescriptorAuthoringPromptInputFactory : IDescriptorAuthoringPromptInputFactory
{
    private static readonly DescriptorKind[] ParserSupportedKinds =
        [DescriptorKind.HumanTask, DescriptorKind.Workflow];

    private readonly IDescriptorAuthoringPromptInputHashService _hashService;

    public DefaultDescriptorAuthoringPromptInputFactory(IDescriptorAuthoringPromptInputHashService hashService)
    {
        _hashService = hashService;
    }

    public DescriptorAuthoringPromptInput Create(AgentAuthoringContext context)
    {
        var visibleDescriptorRefs = context.MetadataContextPack.Descriptors.Select(d => d.Ref).ToArray();
        var metadata = ProjectMetadata(context.MetadataContextPack, visibleDescriptorRefs);
        var memory = ProjectMemory(context.MemoryPack);

        var input = new DescriptorAuthoringPromptInput
        {
            ContractVersion = "7g.v1",
            TenantId = context.Request.TenantId,
            IntentText = context.Request.IntentText,
            Metadata = metadata,
            Memory = memory,
            VisibleDescriptorRefs = visibleDescriptorRefs,
            SupportedDescriptorKinds = ParserSupportedKinds
        };

        return input with { PromptInputHash = _hashService.ComputeHash(input) };
    }

    private static DescriptorAuthoringMetadataContextProjection ProjectMetadata(
        MetadataContextPack pack, DescriptorRef[] visibleDescriptorRefs)
    {
        var descriptors = pack.Descriptors.Select(d => new DescriptorAuthoringDescriptorProjection
        {
            Ref = d.Ref,
            Kind = d.Kind,
            Name = d.Name,
            ContractHash = d.Hashes?.ContractHash,
            DefinitionHash = d.Hashes?.DefinitionHash
        }).ToArray();

        return new DescriptorAuthoringMetadataContextProjection
        {
            Descriptors = descriptors,
            VisibleDescriptorRefs = visibleDescriptorRefs
        };
    }

    private static DescriptorAuthoringMemoryProjection ProjectMemory(AgentMemoryPack pack)
    {
        var memories = pack.Memories.Select(m => new DescriptorAuthoringMemoryItemProjection
        {
            MemoryId = m.MemoryId,
            Kind = m.Kind,
            Content = m.Content,
            Confidence = m.Confidence,
            CanonicalContentHash = m.CanonicalContentHash,
            DescriptorRefs = m.DescriptorRefs.ToArray()
        }).ToArray();

        return new DescriptorAuthoringMemoryProjection
        {
            IsAuthoritative = pack.IsAuthoritative,
            ScopeFingerprint = pack.ScopeFingerprint,
            VisibleMemorySetHash = pack.VisibleMemorySetHash,
            CanonicalPackHash = pack.CanonicalPackHash,
            Memories = memories
        };
    }
}
