using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CrestCreates.JsonContracts.BuildTasks.Generation;
using Microsoft.Build.Framework;

namespace CrestCreates.JsonContracts.BuildTasks.Tasks;

/// <summary>
/// Runs an isolated precompile producer for the current inner build and writes the materialized bundle
/// plus its outputs manifest inside the SDK intermediate path. The bundle becomes ordinary Compile
/// input, so the official STJ generator and the remaining generators see the materialized surface
/// inside one real CoreCompile instead of depending on each other's same-round output.
/// </summary>
public abstract class GeneratePrecompiledArtifacts : ITask
{
    /// <summary>Tool command name, for example <c>prepare-crud</c>.</summary>
    protected abstract string CommandName { get; }

    /// <summary>Outputs manifest schema the producer must return.</summary>
    protected abstract string ExpectedArtifactSchemaVersion { get; }

    /// <summary>Fallback diagnostic prefix for transport/state failures (CCCRUD / CCHTTP).</summary>
    protected abstract string ErrorPrefix { get; }

    [Required]
    public ITaskItem[] SourceFiles { get; set; } = [];

    [Required]
    public ITaskItem[] ReferencePaths { get; set; } = [];

    [Required]
    public string BundlePath { get; set; } = string.Empty;

    [Required]
    public string OutputsManifestPath { get; set; } = string.Empty;

    [Required]
    public string AllowedOutputRoot { get; set; } = string.Empty;

    [Required]
    public string TemporaryDirectory { get; set; } = string.Empty;

    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    [Required]
    public string ToolPath { get; set; } = string.Empty;

    public string LangVersion { get; set; } = "latest";

    public string DefineConstants { get; set; } = string.Empty;

    public string Nullable { get; set; } = "enable";

    public bool AllowUnsafeBlocks { get; set; }

    public string TargetFramework { get; set; } = string.Empty;

    public string ExpectedModelSchemaVersion { get; set; } = string.Empty;

    /// <summary>True when CrestCreatesCodeGeneration is off: formal declarations must then fail the build.</summary>
    public bool ValidateOnly { get; set; }

    /// <summary>Bounded wait so a stuck isolated producer cannot hang the build node.</summary>
    public int TimeoutMilliseconds { get; set; } = 300_000;

    [Output]
    public int DeclarationCount { get; set; }

    [Output]
    public int ArtifactCount { get; set; }

    [Output]
    public bool OutputChanged { get; set; }

    public IBuildEngine? BuildEngine { get; set; }
    public ITaskHost? HostObject { get; set; }

    public bool Execute()
    {
        var normalizedBundle = Path.GetFullPath(BundlePath);
        var normalizedManifest = Path.GetFullPath(OutputsManifestPath);
        var normalizedRoot = Path.GetFullPath(AllowedOutputRoot);
        var normalizedTemp = Path.GetFullPath(TemporaryDirectory);

        foreach (var candidate in new[] { normalizedBundle, normalizedManifest, normalizedTemp })
        {
            if (!AllowedOutputPath.Contains(normalizedRoot, candidate))
            {
                Error(ErrorPrefix + "006",
                    $"Precompile output '{candidate}' is outside the intermediate root "
                    + $"'{AllowedOutputRoot}'.");
                return false;
            }
        }

        if (!File.Exists(ToolPath))
        {
            Error(ErrorPrefix + "002",
                $"The CrestCreates precompile tool is not assembled: '{ToolPath}' does not exist. "
                + "Import exactly one transport (Build.props or Build.Repository.props).");
            return false;
        }

        var requestPath = Path.Combine(normalizedTemp, $"prepare-request-{Guid.NewGuid():N}.json");
        var responsePath = Path.Combine(normalizedTemp, $"prepare-response-{Guid.NewGuid():N}.json");

        try
        {
            Directory.CreateDirectory(normalizedTemp);
            WriteRequest(requestPath);

            var exitCode = RunTool(requestPath, responsePath);
            if (exitCode != 0 && !File.Exists(responsePath))
            {
                Error(ErrorPrefix + "002", $"The precompile tool failed with exit code {exitCode}.");
                return false;
            }

            if (!TryReadResponse(responsePath, normalizedBundle, normalizedManifest, out var responseErrors))
            {
                foreach (var responseError in responseErrors)
                {
                    Error(responseError.Code, responseError.Message,
                        responseError.File, responseError.Line, responseError.Column);
                }

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Error(ErrorPrefix + "006", $"{GetType().Name} failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
        }
    }

    private void WriteRequest(string requestPath)
    {
        var sourcePaths = SourceFiles
            .Select(item => item.ItemSpec)
            .Where(path => !path.Contains("AssemblyInfo", StringComparison.Ordinal)
                           && !path.Contains("AssemblyAttributes", StringComparison.Ordinal)
                           && !path.Contains(".NETCoreApp,Version=", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var referencePaths = ReferencePaths
            .Select(item => item.ItemSpec)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        using var stream = File.Create(requestPath);
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("AssemblyName", AssemblyName);
        writer.WriteStartArray("Sources");
        foreach (var path in sourcePaths)
        {
            writer.WriteStartObject();
            writer.WriteString("Path", path);
            writer.WriteString("Text", File.ReadAllText(path));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("ReferencePaths");
        foreach (var path in referencePaths)
        {
            writer.WriteStringValue(path);
        }

        writer.WriteEndArray();
        writer.WriteString("LangVersion", LangVersion);
        writer.WriteStartArray("DefineConstants");
        foreach (var constant in DefineConstants.Split(';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            writer.WriteStringValue(constant);
        }

        writer.WriteEndArray();
        writer.WriteString("Nullable", Nullable);
        writer.WriteBoolean("AllowUnsafeBlocks", AllowUnsafeBlocks);
        writer.WriteString("TargetFramework", TargetFramework);
        writer.WriteString("ExpectedModelSchemaVersion", ExpectedModelSchemaVersion);
        writer.WriteBoolean("ValidateOnly", ValidateOnly);
        writer.WriteEndObject();
        writer.Flush();
    }

    private int RunTool(string requestPath, string responsePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{ToolPath}\" {CommandName} \"{requestPath}\" \"{responsePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The precompile tool could not be started.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeoutMilliseconds);
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"The precompile tool exceeded the bounded {TimeoutMilliseconds} ms wait.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0 && stderr.Length > 0)
        {
            Error(ErrorPrefix + "002", $"Precompile tool output: {stderr.Trim()}");
        }
        else if (process.ExitCode != 0 && stdout.Length > 0)
        {
            Error(ErrorPrefix + "002", $"Precompile tool output: {stdout.Trim()}");
        }

        return process.ExitCode;
    }

    private bool TryReadResponse(
        string responsePath,
        string normalizedBundle,
        string normalizedManifest,
        out IReadOnlyList<(string Code, string Message, string? File, int Line, int Column)> errors)
    {
        List<(string, string, string?, int, int)> failures = [];
        errors = failures;

        if (!File.Exists(responsePath))
        {
            failures.Add((ErrorPrefix + "006", "The precompile tool produced no response document.", null, 0, 0));
            return false;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(responsePath));
        var root = document.RootElement;

        var diagnostics = new List<(string Id, string Severity, string Message, string? File, int Line, int Column)>();
        if (root.TryGetProperty("Diagnostics", out var diagnosticsElement)
            && diagnosticsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var diagnostic in diagnosticsElement.EnumerateArray())
            {
                diagnostics.Add((
                    GetString(diagnostic, "Id"),
                    GetString(diagnostic, "Severity"),
                    GetString(diagnostic, "Message"),
                    NullIfEmpty(GetString(diagnostic, "FilePath")),
                    GetInt(diagnostic, "Line"),
                    GetInt(diagnostic, "Column")));
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            if (string.Equals(diagnostic.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                Warn(diagnostic.Id, diagnostic.Message, diagnostic.File, diagnostic.Line, diagnostic.Column);
            }
            else if (!string.IsNullOrEmpty(diagnostic.Id))
            {
                failures.Add((diagnostic.Id, diagnostic.Message, diagnostic.File, diagnostic.Line, diagnostic.Column));
            }
        }

        if (failures.Count > 0)
        {
            return false;
        }

        var bundleBytes = root.TryGetProperty("BundleBytes", out var bundle)
            && bundle.ValueKind == JsonValueKind.String
                ? Convert.FromBase64String(bundle.GetString()!)
                : Encoding.UTF8.GetBytes(string.Empty);

        if (!root.TryGetProperty("Outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Object)
        {
            failures.Add((ErrorPrefix + "006", "The precompile response carries no outputs manifest.", null, 0, 0));
            return false;
        }

        if (GetString(outputs, "SchemaVersion") != ExpectedArtifactSchemaVersion)
        {
            failures.Add((ErrorPrefix + "006",
                $"Unsupported precompile outputs schema '{GetString(outputs, "SchemaVersion")}'; "
                + $"expected '{ExpectedArtifactSchemaVersion}'.", null, 0, 0));
            return false;
        }

        if (!string.IsNullOrEmpty(ExpectedModelSchemaVersion)
            && GetString(outputs, "ModelSchemaVersion") != ExpectedModelSchemaVersion)
        {
            failures.Add((ErrorPrefix + "006",
                $"Unsupported precompile model schema '{GetString(outputs, "ModelSchemaVersion")}'; "
                + $"expected '{ExpectedModelSchemaVersion}'.", null, 0, 0));
            return false;
        }

        var manifestBytes = Encoding.UTF8.GetBytes(outputs.GetRawText());
        var bundleChanged = WriteIfChangedFile.WriteIfChanged(normalizedBundle, bundleBytes, TemporaryDirectory);
        var manifestChanged = WriteIfChangedFile.WriteIfChanged(normalizedManifest, manifestBytes, TemporaryDirectory);
        OutputChanged = bundleChanged || manifestChanged;
        DeclarationCount = GetInt(root, "DeclarationCount");

        if (outputs.TryGetProperty("Entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            ArtifactCount = entries.GetArrayLength();
        }

        // A declared surface must actually land: a boolean marker alone is never completion evidence.
        if (DeclarationCount > 0 && bundleBytes.Length == 0)
        {
            failures.Add((ErrorPrefix + "002",
                $"The precompile reported {DeclarationCount} declaration(s) but produced an empty bundle.",
                null, 0, 0));
            return false;
        }

        errors = failures;
        return true;
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Error(string code, string message, string? file = null, int line = 0, int column = 0) =>
        BuildEngine?.LogErrorEvent(new BuildErrorEventArgs(
            code, null, file, line, column, 0, 0, message, null, "CrestCreates.JsonContracts.BuildTasks"));

    private void Warn(string code, string message, string? file, int line, int column) =>
        BuildEngine?.LogWarningEvent(new BuildWarningEventArgs(
            code, null, file, line, column, 0, 0, message, null, "CrestCreates.JsonContracts.BuildTasks"));
}

/// <summary>#128 CRUD precompile front-end.</summary>
public sealed class GenerateCrestCrudArtifacts : GeneratePrecompiledArtifacts
{
    protected override string CommandName => "prepare-crud";

    protected override string ExpectedArtifactSchemaVersion => "crud-artifacts-v1";

    protected override string ErrorPrefix => "CCCRUD";
}

/// <summary>#129 hand-written AppService HTTP projection precompile front-end.</summary>
public sealed class GenerateCrestAppServiceHttpArtifacts : GeneratePrecompiledArtifacts
{
    protected override string CommandName => "prepare-appservice-http";

    protected override string ExpectedArtifactSchemaVersion => "appservice-http-artifacts-v1";

    protected override string ErrorPrefix => "CCHTTP";
}
