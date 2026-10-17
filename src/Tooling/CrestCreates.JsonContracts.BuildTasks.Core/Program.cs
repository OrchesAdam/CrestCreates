using CrestCreates.JsonContracts.BuildTasks;
using CrestCreates.JsonContracts.BuildTasks.Crud;
using CrestCreates.JsonContracts.BuildTasks.Semantic;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            Console.Error.WriteLine("  prepare-crud <request-path> <response-path>");
            return 2;
        }

        var command = args[0];

        try
        {
            return command switch
            {
                "generate" => ExecuteGenerate(args),
                "prepare-crud" => ExecutePrepareCrud(args),
                _ => Fail($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal: {ex.Message}");
            return 3;
        }
    }

    /// <summary>
    /// #128 S2: materializes the CRUD surface as ordinary source before CoreCompile, together with the
    /// outputs manifest the build stage writes. The bundle is returned as bytes and the caller keeps
    /// ownership of where it lands, so the path trust boundary stays in the task.
    /// </summary>
    private static int ExecutePrepareCrud(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: prepare-crud <request-path> <response-path>");
            return 2;
        }

        var requestPath = args[1];
        var responsePath = args[2];

        var requestJson = File.ReadAllText(requestPath);
        var request = JsonSerializer.Deserialize(requestJson, ToolJsonSerializerContext.Default.CrudPrepareRequest);
        if (request is null)
        {
            Console.Error.WriteLine("Failed to deserialize CRUD precompile request.");
            return 4;
        }

        var result = !string.IsNullOrEmpty(request.ExpectedModelSchemaVersion)
            && request.ExpectedModelSchemaVersion != CrestCreates.Tooling.CrudGeneration.CrudGenerationModel.ModelSchemaVersion
            ? CrudPrecompileResult.Failed([new CrudToolDiagnostic("CCCRUD006",
                "The requested CRUD model schema version is unsupported.", null, 0, 0)])
            : new CrudPrecompileEngine().Run(
            request.AssemblyName ?? "",
            request.Sources?.Select(source => (source.Path, source.Text)).ToList() ?? [],
            request.ReferencePaths ?? [],
            request.LangVersion ?? "latest",
            request.DefineConstants ?? [],
            request.Nullable ?? "enable",
            request.AllowUnsafeBlocks,
            request.ValidateOnly);

        var diagnostics = result.Diagnostics
            .Select(diagnostic => new ToolDiagnosticDto
            {
                Id = diagnostic.Id,
                Severity = diagnostic.Severity,
                Message = diagnostic.Message,
                FilePath = diagnostic.FilePath ?? "",
                Line = diagnostic.Line,
                Column = diagnostic.Column,
            })
            .ToList();

        var response = new CrudPrepareResponse
        {
            Diagnostics = diagnostics,
            BundleBytes = result.HasErrors
                ? null
                : System.Text.Encoding.UTF8.GetBytes(result.BundleSource),
            ModelSchemaVersion = CrestCreates.Tooling.CrudGeneration.CrudGenerationModel.ModelSchemaVersion,
            DeclarationCount = result.DeclarationCount,
            Outputs = new CrudOutputManifestDocument
            {
                SchemaVersion = CrudPrecompileEngine.CrudArtifactSchemaVersion,
                ModelSchemaVersion = CrestCreates.Tooling.CrudGeneration.CrudGenerationModel.ModelSchemaVersion,
                Entries = result.Outputs
                    .Select(entry => new CrudOutputManifestEntryDto
                    {
                        OwnerAssemblyName = entry.OwnerAssemblyName,
                        OwnerServiceFullName = entry.OwnerServiceFullName,
                        ArtifactHintName = entry.ArtifactHintName,
                        DeclaredTypeFullNames = entry.DeclaredTypeFullNames.ToArray(),
                        SourceLocation = entry.SourceLocation,
                        ModelSchemaVersion = entry.ModelSchemaVersion,
                        ContentHash = entry.ContentHash,
                    })
                    .ToList(),
            },
        };

        File.WriteAllText(
            responsePath,
            JsonSerializer.Serialize(response, ToolJsonSerializerContext.Default.CrudPrepareResponse));

        return diagnostics.Count > 0 ? 1 : 0;
    }

    private static int ExecuteGenerate(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: generate <request-path> <response-path>");
            return 2;
        }

        var requestPath = args[1];
        var responsePath = args[2];

        var json = File.ReadAllText(requestPath);
        var request = JsonSerializer.Deserialize(json, ToolJsonSerializerContext.Default.GenerateRequest);
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
            request.ManifestAccessibility ?? "Internal",
            request.CrudOutputsManifestJson);

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
            generatedBytes = System.Text.Encoding.UTF8.GetBytes(result.GeneratedSource);

        var response = new GenerateResponse
        {
            Diagnostics = diagnostics,
            GeneratedSourceBytes = generatedBytes,
            ContextCount = result.ContextCount,
            SurfaceRootCount = result.SurfaceRootCount,
            ExplicitRootCount = result.ExplicitRootCount,
        };

        var responseJson = JsonSerializer.Serialize(response, ToolJsonSerializerContext.Default.GenerateResponse);
        File.WriteAllText(responsePath, responseJson);

        return diagnostics.Any(d => d.Severity == "Error") ? 1 : 0;
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

    /// <summary>CRUD precompile outputs manifest, used to reconcile the root matrix against owners.</summary>
    public string? CrudOutputsManifestJson { get; set; }
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

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
[JsonSerializable(typeof(GenerateRequest))]
[JsonSerializable(typeof(GenerateResponse))]
[JsonSerializable(typeof(CrestCreates.JsonContracts.BuildTasks.Crud.CrudPrepareRequest))]
[JsonSerializable(typeof(CrestCreates.JsonContracts.BuildTasks.Crud.CrudPrepareResponse))]
internal sealed partial class ToolJsonSerializerContext : JsonSerializerContext;
