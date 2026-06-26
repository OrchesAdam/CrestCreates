using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public static class MetadataContextPackDiagnosticCodes
{
    private const string FocusNotFoundValue = "CTXPACK_FOCUS_NOT_FOUND";
    public static DiagnosticCode FocusNotFound { get; } = new(FocusNotFoundValue);

    private const string TruncatedByCountValue = "CTXPACK_TRUNCATED_BY_COUNT";
    public static DiagnosticCode TruncatedByCount { get; } = new(TruncatedByCountValue);

    private const string TruncatedByDepthValue = "CTXPACK_TRUNCATED_BY_DEPTH";
    public static DiagnosticCode TruncatedByDepth { get; } = new(TruncatedByDepthValue);

    private const string RecipeMissingValue = "CTXPACK_RECIPE_MISSING";
    public static DiagnosticCode RecipeMissing { get; } = new(RecipeMissingValue);

    private const string KindExcludedValue = "CTXPACK_KIND_EXCLUDED";
    public static DiagnosticCode KindExcluded { get; } = new(KindExcludedValue);

    private const string FocusKindFilteredValue = "CTXPACK_FOCUS_KIND_FILTERED";
    public static DiagnosticCode FocusKindFiltered { get; } = new(FocusKindFilteredValue);

    private const string HashBuilderMissingValue = "CTXPACK_HASH_BUILDER_MISSING";
    public static DiagnosticCode HashBuilderMissing { get; } = new(HashBuilderMissingValue);

    private const string DescriptorMissingForTopologyRefValue = "CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF";
    public static DiagnosticCode DescriptorMissingForTopologyRef { get; } = new(DescriptorMissingForTopologyRefValue);

    private const string TopologyNodeMissingForDescriptorValue = "CTXPACK_TOPOLOGY_NODE_MISSING_FOR_DESCRIPTOR";
    public static DiagnosticCode TopologyNodeMissingForDescriptor { get; } = new(TopologyNodeMissingForDescriptorValue);

    private const string AmbiguousDescriptorRefValue = "CTXPACK_AMBIGUOUS_DESCRIPTOR_REF";
    public static DiagnosticCode AmbiguousDescriptorRef { get; } = new(AmbiguousDescriptorRefValue);
}
