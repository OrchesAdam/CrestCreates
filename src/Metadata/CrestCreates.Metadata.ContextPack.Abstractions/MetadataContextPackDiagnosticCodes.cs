namespace CrestCreates.Metadata.ContextPack.Abstractions;

public static class MetadataContextPackDiagnosticCodes
{
    public const string FocusNotFound = "CTXPACK_FOCUS_NOT_FOUND";
    public const string TruncatedByCount = "CTXPACK_TRUNCATED_BY_COUNT";
    public const string TruncatedByDepth = "CTXPACK_TRUNCATED_BY_DEPTH";
    public const string RecipeMissing = "CTXPACK_RECIPE_MISSING";
    public const string KindExcluded = "CTXPACK_KIND_EXCLUDED";
    public const string FocusKindFiltered = "CTXPACK_FOCUS_KIND_FILTERED";
    public const string HashBuilderMissing = "CTXPACK_HASH_BUILDER_MISSING";
    public const string DescriptorMissingForTopologyRef = "CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF";
    public const string TopologyNodeMissingForDescriptor = "CTXPACK_TOPOLOGY_NODE_MISSING_FOR_DESCRIPTOR";
    public const string AmbiguousDescriptorRef = "CTXPACK_AMBIGUOUS_DESCRIPTOR_REF";
}
