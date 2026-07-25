using CrestCreates.JsonContracts.BuildTasks;
using CrestCreates.JsonContracts.BuildTasks.Generation;
using CrestCreates.JsonContracts.BuildTasks.Incremental;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using System.Text.Json;

namespace CrestCreates.JsonContracts.BuildTasks.Tool;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: CrestCreates.JsonContracts.Tool <command> <args>");
            Console.Error.WriteLine("Commands:");
            Console.Error.WriteLine("  generate <request-path> <response-path>");
            Console.Error.WriteLine("  manifest <request-path> <output-path>");
            return 2;
        }

        var command = args[0];

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        try
        {
            return command switch
            {
                "generate" => ExecuteGenerate(args, jsonOptions),
                "manifest" => ExecuteManifest(args, jsonOptions),
                _ => Fail($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal: {ex.Message}");
            return 3;
        }
    }

    private static int ExecuteGenerate(string[] args, JsonSerializerOptions jsonOptions)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: generate <request-path> <response-path>");
            return 2;
        }

        var requestPath = args[1];
        var responsePath = args[2];

        var json = File.ReadAllText(requestPath);
        var request = JsonSerializer.Deserialize<GenerateRequest>(json, jsonOptions);
        if (request == null)
        {
            Console.Error.WriteLine("Failed to deserialize request.");
            return 4;
        }

        var sources = request.Sources?.Select(s => (s.Path, s.Text)).ToList() ?? [];
        var refPaths = request.ReferencePaths ?? [];
        var defineConstants = request.DefineConstants ?? [];

        var engine = new JsonContractGenerationEngine();
        var result = engine.Generate(
            request.AssemblyName ?? "",
            sources,
            refPaths,
            request.LangVersion ?? "latest",
            defineConstants,
            request.Nullable ?? "enable",
            request.AllowUnsafeBlocks,
            request.ManifestAccessibility ?? "Internal");

        var diagnostics = result.Diagnostics.Select(d => new ToolDiagnosticDto
        {
            Id = d.Id,
            Severity = d.Severity.ToString(),
            Message = d.Message,
            FilePath = d.FilePath,
            Line = d.Line,
            Column = d.Column,
        }).ToList();

        byte[]? generatedBytes = null;
        if (!diagnostics.Any(d => d.Severity == "Error") && result.GeneratedSource != null)
        {
            generatedBytes = System.Text.Encoding.UTF8.GetBytes(result.GeneratedSource);

            if (!string.IsNullOrEmpty(request.OutputPath) && !string.IsNullOrEmpty(request.TemporaryDirectory))
            {
                var normalizedOutput = Path.GetFullPath(request.OutputPath);
                var normalizedTemp = Path.GetFullPath(request.TemporaryDirectory);
                WriteIfChangedFile.WriteIfChanged(normalizedOutput, generatedBytes, normalizedTemp);
            }
        }

        var response = new GenerateResponse
        {
            Diagnostics = diagnostics,
            GeneratedSourceBytes = generatedBytes,
            ContextCount = result.ContextCount,
            SurfaceRootCount = result.SurfaceRootCount,
            ExplicitRootCount = result.ExplicitRootCount,
        };

        var responseJson = JsonSerializer.Serialize(response, jsonOptions);
        File.WriteAllText(responsePath, responseJson);

        return diagnostics.Any(d => d.Severity == "Error") ? 1 : 0;
    }

    private static int ExecuteManifest(string[] args, JsonSerializerOptions jsonOptions)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: manifest <request-path> <output-path>");
            return 2;
        }

        var requestPath = args[1];
        var outputPath = args[2];

        var json = File.ReadAllText(requestPath);
        var request = JsonSerializer.Deserialize<ManifestRequest>(json, jsonOptions);
        if (request == null)
        {
            Console.Error.WriteLine("Failed to deserialize manifest request.");
            return 4;
        }

        var sourcePaths = request.SourceFiles ?? [];
        var refPaths = request.ReferencePaths ?? [];
        var defineConstants = request.DefineConstants ?? [];

        var sources = new List<(string Path, string Text)>();
        foreach (var p in sourcePaths)
        {
            try
            {
                sources.Add((p, File.ReadAllText(p)));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Cannot read source file '{p}': {ex.Message}");
                return 4;
            }
        }

        var compilation = JsonContractCompilationFactory.Create(
            request.AssemblyName ?? "",
            sources,
            refPaths,
            request.LangVersion ?? "latest",
            defineConstants,
            request.Nullable ?? "enable",
            request.AllowUnsafeBlocks);

        var modelBuilder = new JsonContractSurfaceModelBuilder();
        var model = modelBuilder.Build(compilation);

        var manifest = new JsonContractInputManifest
        {
            SourcePaths = sourcePaths,
            ReferencePaths = refPaths,
            LangVersion = request.LangVersion ?? "latest",
            DefineConstants = string.Join(";", defineConstants),
            Nullable = request.Nullable ?? "enable",
            AllowUnsafeBlocks = request.AllowUnsafeBlocks,
            ManifestAccessibility = request.ManifestAccessibility ?? "Internal",
        };

        var manifestBytes = JsonContractInputManifestWriter.WriteManifest(manifest);
        var manifestJson = System.Text.Encoding.UTF8.GetString(manifestBytes);

        var normalizedOutput = Path.GetFullPath(outputPath);
        var dir = Path.GetDirectoryName(normalizedOutput);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(normalizedOutput, manifestJson);

        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}

internal sealed class GenerateRequest
{
    public string? AssemblyName { get; set; }
    public List<SourceDto>? Sources { get; set; }
    public List<string>? ReferencePaths { get; set; }
    public string? LangVersion { get; set; }
    public List<string>? DefineConstants { get; set; }
    public string? Nullable { get; set; }
    public bool AllowUnsafeBlocks { get; set; }
    public string? ManifestAccessibility { get; set; }
    public string? OutputPath { get; set; }
    public string? AllowedOutputRoot { get; set; }
    public string? TemporaryDirectory { get; set; }
}

internal sealed class SourceDto
{
    public string Path { get; set; } = "";
    public string Text { get; set; } = "";
}

internal sealed class GenerateResponse
{
    public List<ToolDiagnosticDto>? Diagnostics { get; set; }
    public byte[]? GeneratedSourceBytes { get; set; }
    public int ContextCount { get; set; }
    public int SurfaceRootCount { get; set; }
    public int ExplicitRootCount { get; set; }
}

internal sealed class ToolDiagnosticDto
{
    public string Id { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

internal sealed class ManifestRequest
{
    public string? AssemblyName { get; set; }
    public List<string>? SourceFiles { get; set; }
    public List<string>? ReferencePaths { get; set; }
    public string? LangVersion { get; set; }
    public List<string>? DefineConstants { get; set; }
    public string? Nullable { get; set; }
    public bool AllowUnsafeBlocks { get; set; }
    public string? ManifestAccessibility { get; set; }
}
