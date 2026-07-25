using CrestCreates.JsonContracts.BuildTasks.Generation;
using Microsoft.Build.Framework;

namespace CrestCreates.JsonContracts.BuildTasks.Tasks;

public sealed class GenerateJsonContracts : ITask
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

    public string ManifestAccessibility { get; set; } = "Internal";

    public string TaskSemanticVersion { get; set; } = string.Empty;

    public string? ToolPath { get; set; }

    [Output]
    public int GeneratedContextCount { get; set; }

    [Output]
    public int GeneratedSurfaceRootCount { get; set; }

    [Output]
    public int GeneratedExplicitRootCount { get; set; }

    [Output]
    public bool OutputChanged { get; set; }

    public IBuildEngine? BuildEngine { get; set; }
    public ITaskHost? HostObject { get; set; }

    public bool Execute()
    {
        if (!ValidatePaths())
            return false;

        var normalizedOutput = Path.GetFullPath(OutputPath);
        var normalizedAllowed = Path.GetFullPath(AllowedOutputRoot);
        var normalizedTemp = Path.GetFullPath(TemporaryDirectory);

        if (!IsPathContained(normalizedOutput, normalizedAllowed))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs(
                "CJC012", null, null, 0, 0, 0, 0,
                $"OutputPath '{OutputPath}' is outside AllowedOutputRoot '{AllowedOutputRoot}'.",
                null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        if (!IsPathContained(normalizedTemp, normalizedAllowed))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs(
                "CJC012", null, null, 0, 0, 0, 0,
                $"TemporaryDirectory '{TemporaryDirectory}' is outside AllowedOutputRoot '{AllowedOutputRoot}'.",
                null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        var toolExe = ResolveToolPath();
        if (toolExe == null)
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs(
                "CJC012", null, null, 0, 0, 0, 0,
                "Cannot find CrestCreates.JsonContracts.Tool executable.",
                null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }

        var requestPath = Path.Combine(normalizedTemp, $"generate-request-{Guid.NewGuid():N}.json");
        var responsePath = Path.Combine(normalizedTemp, $"generate-response-{Guid.NewGuid():N}.json");

        try
        {
            WriteRequest(requestPath, normalizedOutput, normalizedAllowed, normalizedTemp);

            var exitCode = RunTool(toolExe, $"generate \"{requestPath}\" \"{responsePath}\"");
            
            if (File.Exists(responsePath))
            {
                var result = ReadResponse(responsePath, normalizedOutput, normalizedTemp);
                if (!result) return false;
            }

            if (exitCode != 0)
            {
                BuildEngine.LogErrorEvent(new BuildErrorEventArgs(
                    "CJC012", null, null, 0, 0, 0, 0,
                    $"Tool exited with code {exitCode}.",
                    null, "CrestCreates.JsonContracts.BuildTasks"));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                $"GenerateJsonContracts failed: {ex.GetType().Name}: {ex.Message}", null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }
        finally
        {
            try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { }
            try { if (File.Exists(responsePath)) File.Delete(responsePath); } catch { }
        }
    }

    private bool ValidatePaths()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                "GenerateJsonContracts: OutputPath is required.", null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }
        if (string.IsNullOrWhiteSpace(AllowedOutputRoot))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                "GenerateJsonContracts: AllowedOutputRoot is required.", null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }
        if (string.IsNullOrWhiteSpace(TemporaryDirectory))
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                "GenerateJsonContracts: TemporaryDirectory is required.", null, "CrestCreates.JsonContracts.BuildTasks"));
            return false;
        }
        return true;
    }

    private string? ResolveToolPath()
    {
        if (!string.IsNullOrEmpty(ToolPath) && File.Exists(ToolPath))
            return ToolPath;

        var taskAssemblyDir = Path.GetDirectoryName(typeof(GenerateJsonContracts).Assembly.Location);
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

    private void WriteRequest(string requestPath, string normalizedOutput, string normalizedAllowed, string normalizedTemp)
    {
        var sourcePaths = SourceFiles
            .Select(s => s.ItemSpec)
            .Where(p => !p.Contains("AssemblyInfo") && !p.Contains("AssemblyAttributes") && !p.Contains(".NETCoreApp,Version="))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var refPaths = ReferencePaths.Select(r => r.ItemSpec).OrderBy(p => p, StringComparer.Ordinal).ToList();

        var defineConstantsList = DefineConstants
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        using var stream = File.Create(requestPath);
        using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("AssemblyName", AssemblyName);
        writer.WriteStartArray("Sources");
        foreach (var p in sourcePaths)
        {
            writer.WriteStartObject();
            writer.WriteString("Path", p);
            writer.WriteString("Text", File.ReadAllText(p));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("ReferencePaths");
        foreach (var r in refPaths)
            writer.WriteStringValue(r);
        writer.WriteEndArray();
        writer.WriteString("LangVersion", LangVersion);
        writer.WriteStartArray("DefineConstants");
        foreach (var d in defineConstantsList)
            writer.WriteStringValue(d);
        writer.WriteEndArray();
        writer.WriteString("Nullable", Nullable);
        writer.WriteBoolean("AllowUnsafeBlocks", AllowUnsafeBlocks);
        writer.WriteString("ManifestAccessibility", ManifestAccessibility);
        writer.WriteString("OutputPath", normalizedOutput);
        writer.WriteString("AllowedOutputRoot", normalizedAllowed);
        writer.WriteString("TemporaryDirectory", normalizedTemp);
        writer.WriteEndObject();
        writer.Flush();
    }

    private bool ReadResponse(string responsePath, string normalizedOutput, string normalizedTemp)
    {
        var bytes = File.ReadAllBytes(responsePath);
        var reader = new System.Text.Json.Utf8JsonReader(bytes);

        var diagnostics = new List<ToolDiagnostic>();
        byte[]? generatedSourceBytes = null;
        int contextCount = 0, surfaceRootCount = 0, explicitRootCount = 0;

        while (reader.Read())
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                reader.Read();
                if (reader.TokenType == System.Text.Json.JsonTokenType.Null) continue;
                switch (propName)
                {
                    case "Diagnostics":
                        if (reader.TokenType == System.Text.Json.JsonTokenType.StartArray)
                        {
                            while (reader.Read() && reader.TokenType != System.Text.Json.JsonTokenType.EndArray)
                            {
                                if (reader.TokenType == System.Text.Json.JsonTokenType.StartObject)
                                {
                                    var d = ReadDiagnostic(ref reader);
                                    diagnostics.Add(d);
                                }
                            }
                        }
                        break;
                    case "GeneratedSourceBytes":
                        generatedSourceBytes = reader.GetBytesFromBase64();
                        break;
                    case "ContextCount":
                        contextCount = reader.GetInt32();
                        break;
                    case "SurfaceRootCount":
                        surfaceRootCount = reader.GetInt32();
                        break;
                    case "ExplicitRootCount":
                        explicitRootCount = reader.GetInt32();
                        break;
                }
            }
        }

        foreach (var d in diagnostics)
        {
            if (d.Severity == "Error")
                BuildEngine.LogErrorEvent(new BuildErrorEventArgs(d.Id, null, d.FilePath, d.Line, d.Column, 0, 0, d.Message, null, "CrestCreates.JsonContracts.BuildTasks"));
            else if (d.Severity == "Warning")
                BuildEngine.LogWarningEvent(new BuildWarningEventArgs(d.Id, null, d.FilePath, d.Line, d.Column, 0, 0, d.Message, null, "CrestCreates.JsonContracts.BuildTasks"));
        }

        if (diagnostics.Any(d => d.Severity == "Error"))
            return false;

        if (generatedSourceBytes != null && generatedSourceBytes.Length > 0)
        {
            try
            {
                OutputChanged = WriteIfChangedFile.WriteIfChanged(normalizedOutput, generatedSourceBytes, normalizedTemp);
            }
            catch (Exception ex)
            {
                BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC012", null, null, 0, 0, 0, 0,
                    $"Failed to write generated source: {ex.Message}", null, "CrestCreates.JsonContracts.BuildTasks"));
                return false;
            }
        }

        GeneratedContextCount = contextCount;
        GeneratedSurfaceRootCount = surfaceRootCount;
        GeneratedExplicitRootCount = explicitRootCount;

        return true;
    }

    private static ToolDiagnostic ReadDiagnostic(ref System.Text.Json.Utf8JsonReader reader)
    {
        var d = new ToolDiagnostic();
        while (reader.Read() && reader.TokenType != System.Text.Json.JsonTokenType.EndObject)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (reader.TokenType == System.Text.Json.JsonTokenType.Null) continue;
                switch (name)
                {
                    case "Id": d.Id = reader.GetString() ?? ""; break;
                    case "Severity": d.Severity = reader.GetString() ?? ""; break;
                    case "Message": d.Message = reader.GetString() ?? ""; break;
                    case "FilePath": d.FilePath = reader.GetString() ?? ""; break;
                    case "Line": d.Line = reader.GetInt32(); break;
                    case "Column": d.Column = reader.GetInt32(); break;
                }
            }
        }
        return d;
    }

    private int RunTool(string toolDll, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{toolDll}\" {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            return -1;

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var msg = string.Join(" | ", new[] { stdout, stderr }.Where(s => !string.IsNullOrEmpty(s)));
            if (string.IsNullOrEmpty(msg)) msg = "(no output)";
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("CJC013", null, null, 0, 0, 0, 0,
                $"Tool error (exit={process.ExitCode}): {msg}", null, "GenerateJsonContracts"));
        }
        return process.ExitCode;
    }

    private static bool IsPathContained(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private sealed class ToolDiagnostic
    {
        public string Id { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
