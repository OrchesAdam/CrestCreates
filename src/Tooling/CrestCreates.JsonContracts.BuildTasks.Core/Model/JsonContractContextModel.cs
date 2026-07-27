using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Model;

public sealed class JsonContractContextModel
{
    public INamedTypeSymbol ContextSymbol { get; init; } = null!;
    public string FullMetadataName { get; init; } = string.Empty;
    public string ContainingNamespace { get; init; } = string.Empty;
    public string ContextSimpleName { get; init; } = string.Empty;
    public string DeclaredAccessibility { get; init; } = "internal";
    public List<JsonContractRootModel> SurfaceRoots { get; init; } = [];
    public List<JsonContractRootModel> BindingRoots { get; init; } = [];
    public List<JsonContractRootModel> ExplicitRoots { get; init; } = [];
    public List<JsonContractRootModel> AllDirectRoots { get; init; } = [];
    public JsonContractManifestAccessibility ManifestAccessibility { get; set; }
    public string ManifestClassName { get; init; } = string.Empty;
}
