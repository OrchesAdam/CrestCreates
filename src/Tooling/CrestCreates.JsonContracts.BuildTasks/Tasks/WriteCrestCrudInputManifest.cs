using CrestCreates.JsonContracts.BuildTasks.Generation;
using Microsoft.Build.Framework;

namespace CrestCreates.JsonContracts.BuildTasks.Tasks;

/// <summary>
/// #128 S2: writes the CRUD precompile input fingerprint. The manifest records the full source and
/// reference list **with content hashes**, plus the compiler options, the model/emitter/tool versions
/// and the current TFM/configuration. Because the generated surface declares this file as an MSBuild
/// input, a content change re-runs the producer even when timestamps survive, and a removed or renamed
/// source changes the item list itself.
/// </summary>
public class WriteCrestCrudInputManifest : ITask
{
    public const string Schema = "crud-inputs-v1";

    [Required]
    public ITaskItem[] SourceFiles { get; set; } = [];

    [Required]
    public ITaskItem[] ReferencePaths { get; set; } = [];

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string AllowedOutputRoot { get; set; } = string.Empty;

    [Required]
    public string TemporaryDirectory { get; set; } = string.Empty;

    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    public string LangVersion { get; set; } = "latest";

    public string DefineConstants { get; set; } = string.Empty;

    public string Nullable { get; set; } = "enable";

    public bool AllowUnsafeBlocks { get; set; }

    public string TargetFramework { get; set; } = string.Empty;

    public string Configuration { get; set; } = string.Empty;

    public string RuntimeIdentifier { get; set; } = string.Empty;

    public bool ValidateOnly { get; set; }

    public string TaskAssemblyPath { get; set; } = string.Empty;

    public string ModelSchemaVersion { get; set; } = string.Empty;

    public string ToolSemanticVersion { get; set; } = string.Empty;

    public string ToolAssemblyPath { get; set; } = string.Empty;

    /// <summary>Diagnostic prefix for path-boundary failures (CCCRUD / CCHTTP).</summary>
    protected virtual string ErrorPrefix => "CCCRUD";

    protected virtual string SchemaName => Schema;

    [Output]
    public bool OutputChanged { get; set; }

    public IBuildEngine? BuildEngine { get; set; }
    public ITaskHost? HostObject { get; set; }

    public bool Execute()
    {
        var normalizedOutput = Path.GetFullPath(OutputPath);
        if (!AllowedOutputPath.Contains(AllowedOutputRoot, normalizedOutput))
        {
            BuildEngine?.LogErrorEvent(new BuildErrorEventArgs(ErrorPrefix + "006", null, null, 0, 0, 0, 0,
                $"Precompile inputs manifest '{OutputPath}' is outside AllowedOutputRoot '{AllowedOutputRoot}'.",
                null, GetType().Name));
            return false;
        }

        CrestInputManifestWriter.Write(
            normalizedOutput,
            TemporaryDirectory,
            SchemaName,
            AssemblyName,
            TargetFramework,
            Configuration,
            RuntimeIdentifier,
            ValidateOnly,
            TaskAssemblyPath,
            LangVersion,
            Nullable,
            AllowUnsafeBlocks,
            ModelSchemaVersion,
            ToolSemanticVersion,
            ToolAssemblyPath,
            DefineConstants,
            SourceFiles.Select(item => item.ItemSpec),
            ReferencePaths.Select(item => item.ItemSpec),
            out var changed);
        OutputChanged = changed;
        return true;
    }
}

/// <summary>#129 S2.2-B: input fingerprint of the hand-written AppService HTTP projection stage.</summary>
public sealed class WriteCrestAppServiceHttpInputManifest : WriteCrestCrudInputManifest
{
    protected override string ErrorPrefix => "CCHTTP";

    protected override string SchemaName => "appservice-http-inputs-v1";
}
