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

    public string? ToolPath { get; set; }

    [Output]
    public bool OutputChanged { get; set; }

    public IBuildEngine? BuildEngine { get; set; }
    public ITaskHost? HostObject { get; set; }

    public bool Execute()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                "WriteJsonContractInputManifest: OutputPath is required.", null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(TemporaryDirectory))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                "WriteJsonContractInputManifest: TemporaryDirectory is required.", null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        var normalizedOutput = Path.GetFullPath(OutputPath);
        var normalizedAllowed = Path.GetFullPath(AllowedOutputRoot);
        var normalizedTemp = Path.GetFullPath(TemporaryDirectory);

        if (!IsPathContained(normalizedOutput, normalizedAllowed))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                $"OutputPath '{OutputPath}' is outside AllowedOutputRoot '{AllowedOutputRoot}'.",
                null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        var toolExe = ResolveToolPath();
        if (toolExe == null)
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                "Cannot find CrestCreates.JsonContracts.Tool executable.",
                null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        var manifestPath = Path.Combine(normalizedTemp, $"manifest-input-{Guid.NewGuid():N}.json");

        try
        {
            WriteManifestRequest(manifestPath);

            var exitCode = RunTool(toolExe, $"manifest \"{manifestPath}\" \"{normalizedOutput}\"");
            if (exitCode != 0)
            {
                BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                    $"Tool exited with code {exitCode}.",
                    null, "CrestCreates.JsonContracts.BuildTasks"));
                return false;
            }

            OutputChanged = true;
            return true;
        }
        finally
        {
            try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }
        }
    }

    private string? ResolveToolPath()
    {
        if (!string.IsNullOrEmpty(ToolPath) && File.Exists(ToolPath))
            return ToolPath;

        var taskAssemblyDir = Path.GetDirectoryName(typeof(WriteJsonContractInputManifest).Assembly.Location);
        if (taskAssemblyDir == null) return null;

        var candidates = new[]
        {
            Path.Combine(taskAssemblyDir, "CrestCreates.JsonContracts.Tool.dll"),
            Path.Combine(taskAssemblyDir, "..", "CrestCreates.JsonContracts.Tool", "CrestCreates.JsonContracts.Tool.dll"),
        };

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }

        return null;
    }

    private void WriteManifestRequest(string manifestPath)
    {
        var sourcePaths = SourceFiles.Select(s => s.ItemSpec).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var refPaths = ReferencePaths.Select(r => r.ItemSpec).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var defineConstantsList = DefineConstants
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var manifest = new
        {
            AssemblyName,
            SourceFiles = sourcePaths,
            ReferencePaths = refPaths,
            LangVersion,
            DefineConstants = defineConstantsList,
            Nullable,
            AllowUnsafeBlocks,
            ManifestAccessibility,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(manifest);
        File.WriteAllText(manifestPath, json);
    }

    private static int RunTool(string toolDll, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{toolDll}\" {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) return -1;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool IsPathContained(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
