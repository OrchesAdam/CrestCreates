using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.JsonContracts.BuildTasks.Generation;
using Microsoft.Build.Framework;

namespace CrestCreates.JsonContracts.BuildTasks.Tasks;

/// <summary>A successful generation binds every output to the complete input fingerprint.</summary>
public sealed class CrestCrudGenerationState : ITask
{
    [Required] public string StampPath { get; set; } = string.Empty;
    [Required] public string InputManifestPath { get; set; } = string.Empty;
    [Required] public string BundlePath { get; set; } = string.Empty;
    [Required] public string OutputsManifestPath { get; set; } = string.Empty;
    [Required] public string AllowedOutputRoot { get; set; } = string.Empty;
    [Required] public string TemporaryDirectory { get; set; } = string.Empty;
    public bool Write { get; set; }
    [Output] public bool IsValid { get; set; }
    public IBuildEngine? BuildEngine { get; set; }
    public ITaskHost? HostObject { get; set; }

    public bool Execute()
    {
        var paths = new[] { InputManifestPath, BundlePath, OutputsManifestPath };
        if (paths.Append(StampPath).Append(TemporaryDirectory)
            .Any(path => !AllowedOutputPath.Contains(AllowedOutputRoot, path)))
        {
            BuildEngine?.LogErrorEvent(new BuildErrorEventArgs("CCCRUD006", null, null, 0, 0, 0, 0,
                "CRUD generation state must remain inside IntermediateOutputPath.", null, GetType().Name));
            return false;
        }

        IsValid = false;
        if (paths.Any(path => !File.Exists(path))) return !Write;
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "crud-state-v1");
            foreach (var path in paths)
            {
                using var file = File.OpenRead(path);
                writer.WriteString(Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(file)));
            }
            writer.WriteEndObject();
        }

        var state = bytes.ToArray();
        if (Write)
        {
            WriteIfChangedFile.WriteIfChanged(StampPath, state, TemporaryDirectory);
            IsValid = true;
        }
        else
        {
            IsValid = File.Exists(StampPath) && File.ReadAllBytes(StampPath).AsSpan().SequenceEqual(state);
        }
        return true;
    }
}
