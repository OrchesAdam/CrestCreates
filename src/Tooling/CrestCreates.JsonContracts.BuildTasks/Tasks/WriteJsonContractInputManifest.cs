using System.Text.Json;
using CrestCreates.JsonContracts.BuildTasks.Generation;
using Microsoft.Build.Framework;

namespace CrestCreates.JsonContracts.BuildTasks.Tasks;

public sealed class WriteJsonContractInputManifest : ITask
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
    public string ImplicitUsings { get; set; } = "enable";
    public string TargetFramework { get; set; } = string.Empty;
    public string ManifestAccessibility { get; set; } = "Internal";
    public string TaskSemanticVersion { get; set; } = string.Empty;
    [Required]
    public string TaskAssemblyPath { get; set; } = string.Empty;

    [Output]
    public bool OutputChanged { get; set; }

    public IBuildEngine? BuildEngine { get; set; }
    public ITaskHost? HostObject { get; set; }

    public bool Execute()
    {
        if (string.IsNullOrWhiteSpace(OutputPath)
            || string.IsNullOrWhiteSpace(AllowedOutputRoot)
            || string.IsNullOrWhiteSpace(TemporaryDirectory))
        {
            LogPathError("OutputPath, AllowedOutputRoot, and TemporaryDirectory are required.");
            return false;
        }

        try
        {
            var output = Path.GetFullPath(OutputPath);
            var allowedRoot = Path.GetFullPath(AllowedOutputRoot);
            var temporaryDirectory = Path.GetFullPath(TemporaryDirectory);

            if (!AllowedOutputPath.Contains(allowedRoot, output))
            {
                LogPathError($"OutputPath '{OutputPath}' is outside AllowedOutputRoot '{AllowedOutputRoot}'.");
                return false;
            }

            if (!AllowedOutputPath.Contains(allowedRoot, temporaryDirectory))
            {
                LogPathError($"TemporaryDirectory '{TemporaryDirectory}' is outside AllowedOutputRoot '{AllowedOutputRoot}'.");
                return false;
            }

            var bytes = WriteManifest(allowedRoot, temporaryDirectory);
            OutputChanged = WriteIfChangedFile.WriteIfChanged(output, bytes, temporaryDirectory);
            return true;
        }
        catch (Exception exception)
        {
            LogPathError($"WriteJsonContractInputManifest failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private byte[] WriteManifest(string allowedRoot, string temporaryDirectory)
    {
        var sources = SourceFiles
            .Select(item => Path.GetFullPath(item.ItemSpec).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var references = ReferencePaths
            .Select(item => Path.GetFullPath(item.ItemSpec).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteStartArray("sourcePaths");
        foreach (var source in sources)
            writer.WriteStringValue(source);
        writer.WriteEndArray();
        writer.WriteStartArray("referencePaths");
        foreach (var reference in references)
            writer.WriteStringValue(reference);
        writer.WriteEndArray();
        writer.WriteString("assemblyName", AssemblyName);
        writer.WriteString("langVersion", LangVersion);
        writer.WriteString("defineConstants", DefineConstants);
        writer.WriteString("nullable", Nullable);
        writer.WriteBoolean("allowUnsafeBlocks", AllowUnsafeBlocks);
        writer.WriteString("implicitUsings", ImplicitUsings);
        writer.WriteString("allowedOutputRoot", allowedRoot.Replace('\\', '/'));
        writer.WriteString("temporaryDirectory", temporaryDirectory.Replace('\\', '/'));
        writer.WriteString("manifestAccessibility", ManifestAccessibility);
        writer.WriteString("targetFramework", TargetFramework);
        writer.WriteString("taskSemanticVersion", TaskSemanticVersion);
        writer.WriteString("taskAssemblyIdentity", ResolveTaskAssemblyIdentity());
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    private string ResolveTaskAssemblyIdentity()
    {
        if (string.IsNullOrWhiteSpace(TaskAssemblyPath))
            return string.Empty;

        var fullPath = Path.GetFullPath(TaskAssemblyPath);
        return File.Exists(fullPath)
            ? System.Reflection.AssemblyName.GetAssemblyName(fullPath).FullName ?? Path.GetFileName(fullPath)
            : Path.GetFileName(fullPath);
    }

    private void LogPathError(string message) =>
        BuildEngine?.LogErrorEvent(new BuildErrorEventArgs(
            "CJC012", null, null, 0, 0, 0, 0, message, null,
            "CrestCreates.JsonContracts.BuildTasks"));
}
