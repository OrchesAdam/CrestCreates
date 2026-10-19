using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.JsonContracts.BuildTasks.Generation;

namespace CrestCreates.JsonContracts.BuildTasks.Tasks;

/// <summary>
/// Shared precompile input fingerprint writer: the full source and reference list with content hashes,
/// the compiler options, the model/tool versions and the current TFM/configuration/state partition. The
/// schema parameter keeps the two precompile front-ends distinguishable while sharing one writer.
/// </summary>
internal static class CrestInputManifestWriter
{
    public static void Write(
        string outputPath,
        string temporaryDirectory,
        string schema,
        string assemblyName,
        string targetFramework,
        string configuration,
        string runtimeIdentifier,
        bool validateOnly,
        string taskAssemblyPath,
        string langVersion,
        string nullable,
        bool allowUnsafeBlocks,
        string modelSchemaVersion,
        string toolSemanticVersion,
        string toolAssemblyPath,
        string defineConstants,
        IEnumerable<string> sources,
        IEnumerable<string> references,
        out bool outputChanged)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", schema);
            writer.WriteString("assemblyName", assemblyName);
            writer.WriteString("targetFramework", targetFramework);
            writer.WriteString("configuration", configuration);
            writer.WriteString("runtimeIdentifier", runtimeIdentifier);
            writer.WriteBoolean("validateOnly", validateOnly);
            writer.WriteString("taskAssemblyHash", HashFile(taskAssemblyPath));
            writer.WriteString("langVersion", langVersion);
            writer.WriteString("nullable", nullable);
            writer.WriteBoolean("allowUnsafeBlocks", allowUnsafeBlocks);
            writer.WriteString("modelSchemaVersion", modelSchemaVersion);
            writer.WriteString("toolSemanticVersion", toolSemanticVersion);
            writer.WriteString("toolAssemblyHash", HashFile(toolAssemblyPath));
            WriteArray(writer, "defineConstants", defineConstants.Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            WriteFiles(writer, "sources", sources);
            WriteFiles(writer, "references", references);
            writer.WriteEndObject();
            writer.Flush();
        }

        outputChanged = WriteIfChangedFile.WriteIfChanged(
            Path.GetFullPath(outputPath), stream.ToArray(), temporaryDirectory);
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
