namespace CrestCreates.JsonContracts.BuildTasks.Incremental;

internal sealed class JsonContractInputManifest
{
    public List<string> SourcePaths { get; set; } = [];
    public List<string> ReferencePaths { get; set; } = [];
    public string LangVersion { get; set; } = string.Empty;
    public string DefineConstants { get; set; } = string.Empty;
    public string Nullable { get; set; } = string.Empty;
    public bool AllowUnsafeBlocks { get; set; }
    public string ImplicitUsings { get; set; } = string.Empty;
    public string AllowedOutputRoot { get; set; } = string.Empty;
    public string TemporaryDirectory { get; set; } = string.Empty;
    public string ManifestAccessibility { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string TaskSemanticVersion { get; set; } = string.Empty;
    public string TaskAssemblyIdentity { get; set; } = string.Empty;
}
