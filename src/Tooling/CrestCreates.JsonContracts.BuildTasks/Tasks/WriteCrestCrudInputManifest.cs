using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
public sealed class WriteCrestCrudInputManifest : ITask
{
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

    [Output]
    public bool OutputChanged { get; set; }

    public IBuildEngine? BuildEngine { get; set; }
    public ITaskHost? HostObject { get; set; }

    public bool Execute()
    {
        var normalizedOutput = Path.GetFullPath(OutputPath);
        if (!AllowedOutputPath.Contains(AllowedOutputRoot, normalizedOutput))
        {
            BuildEngine?.LogErrorEvent(new BuildErrorEventArgs("CCCRUD006", null, null, 0, 0, 0, 0,
                $"CRUD inputs manifest '{OutputPath}' is outside AllowedOutputRoot '{AllowedOutputRoot}'.",
                null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("assemblyName", AssemblyName);
            writer.WriteString("targetFramework", TargetFramework);
            writer.WriteString("configuration", Configuration);
            writer.WriteString("runtimeIdentifier", RuntimeIdentifier);
            writer.WriteBoolean("validateOnly", ValidateOnly);
            writer.WriteString("taskAssemblyHash", HashFile(TaskAssemblyPath));
            writer.WriteString("langVersion", LangVersion);
            writer.WriteString("nullable", Nullable);
            writer.WriteBoolean("allowUnsafeBlocks", AllowUnsafeBlocks);
            writer.WriteString("modelSchemaVersion", ModelSchemaVersion);
            writer.WriteString("toolSemanticVersion", ToolSemanticVersion);
            writer.WriteString("toolAssemblyHash", HashFile(ToolAssemblyPath));
            WriteArray(writer, "defineConstants", DefineConstants.Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            WriteFiles(writer, "sources", SourceFiles.Select(item => item.ItemSpec));
            WriteFiles(writer, "references", ReferencePaths.Select(item => item.ItemSpec));
            writer.WriteEndObject();
            writer.Flush();
        }

        OutputChanged = WriteIfChangedFile.WriteIfChanged(
            normalizedOutput, stream.ToArray(), TemporaryDirectory);
        return true;
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values.OrderBy(item => item, StringComparer.Ordinal))
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteFiles(Utf8JsonWriter writer, string name, IEnumerable<string> paths)
    {
        writer.WriteStartArray(name);
        foreach (var path in paths.OrderBy(item => item, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("path", path);
            writer.WriteString("contentHash", HashFile(path));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string HashFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return "missing";
        }

        using var file = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
}
