using CrestCreates.JsonContracts.BuildTasks.Generation;
using CrestCreates.JsonContracts.BuildTasks.Incremental;
using System.Text.Json;
using System.Collections.Generic;

namespace CrestCreates.JsonContracts.Tool;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: CrestCreates.JsonContracts.Tool <command> [options]");
            Console.Error.WriteLine("Commands: generate, manifest");
            return 1;
        }

        var command = args[0];
        return command switch
        {
            "generate" => RunGenerate(args[1..]),
            "manifest" => RunManifest(args[1..]),
            _ => FailUnknownCommand(command)
        };
    }

    private static int RunGenerate(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: generate <request-json-path>");
            return 1;
        }

        var requestPath = args[0];
        if (!File.Exists(requestPath))
        {
            Console.Error.WriteLine($"Request file not found: {requestPath}");
            return 1;
        }

        var requestJson = File.ReadAllText(requestPath);
        var request = JsonSerializer.Deserialize<GenerateRequest>(requestJson);
        if (request == null)
        {
            Console.Error.WriteLine("Failed to deserialize request");
            return 1;
        }

        var engine = new JsonContractGenerationEngine();
        var result = engine.Generate(
            request.AssemblyName,
            request.Sources.Select(s => (s.Path, s.Text)).ToList(),
            request.ReferencePaths,
            request.LangVersion,
            request.DefineConstants,
            request.Nullable,
            request.AllowUnsafeBlocks,
            request.ManifestAccessibility);

        var response = new GenerateResponse
        {
            Diagnostics = result.Diagnostics.Select(d => new DiagnosticEntry
            {
                Id = d.Id,
                Severity = d.Severity.ToString(),
                Message = d.Message,
                FilePath = d.FilePath,
                Line = d.Line,
                Column = d.Column,
            }).ToList(),
            GeneratedSource = result.GeneratedSource,
            ContextCount = result.ContextCount,
            SurfaceRootCount = result.SurfaceRootCount,
            ExplicitRootCount = result.ExplicitRootCount,
        };

        var responseJson = JsonSerializer.Serialize(response);
        Console.Write(responseJson);

        return response.Diagnostics.Any(d => d.Severity == JsonContractDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static int RunManifest(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: manifest <manifest-json-path> <output-path>");
            return 1;
        }

        var manifestPath = args[0];
        var outputPath = args.Length > 1 ? args[1] : null;

        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Manifest file not found: {manifestPath}");
            return 1;
        }

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<JsonContractInputManifest>(manifestJson);
        if (manifest == null)
        {
            Console.Error.WriteLine("Failed to deserialize manifest");
            return 1;
        }

        var bytes = JsonContractInputManifestWriter.WriteManifest(manifest);

        if (outputPath != null)
        {
            File.WriteAllBytes(outputPath, bytes);
        }
        else
        {
            Console.Write(Encoding.UTF8.GetString(bytes));
        }

        return 0;
    }

    private static int FailUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return 1;
    }

    private sealed class GenerateRequest
    {
        public string AssemblyName { get; set; } = string.Empty;
        public List<SourceEntry> Sources { get; set; } = [];
        public List<string> ReferencePaths { get; set; } = [];
        public string LangVersion { get; set; } = "latest";
        public List<string> DefineConstants { get; set; } = [];
        public string Nullable { get; set; } = "enable";
        public bool AllowUnsafeBlocks { get; set; }
        public string ManifestAccessibility { get; set; } = "Internal";
    }

    private sealed class SourceEntry
    {
        public string Path { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    private sealed class GenerateResponse
    {
        public List<DiagnosticEntry> Diagnostics { get; set; } = [];
        public string GeneratedSource { get; set; } = string.Empty;
        public int ContextCount { get; set; }
        public int SurfaceRootCount { get; set; }
        public int ExplicitRootCount { get; set; }
    }

    private sealed class DiagnosticEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
