using Microsoft.CodeAnalysis;

namespace CrestCreates.JsonContracts.BuildTasks.Model;

public sealed class JsonContractRootModel
{
    public ITypeSymbol RootType { get; set; } = null!;
    public string FullMetadataName { get; set; } = string.Empty;
    public JsonContractRootProvenance Provenance { get; set; } = new();
    public bool IsExplicitExtra { get; set; }
}
