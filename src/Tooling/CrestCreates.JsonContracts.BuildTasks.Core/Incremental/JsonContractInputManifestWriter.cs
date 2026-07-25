using System.Text;
using System.Text.Json;

namespace CrestCreates.JsonContracts.BuildTasks.Incremental;

internal static class JsonContractInputManifestWriter
{
    public static byte[] WriteManifest(JsonContractInputManifest manifest)
    {
        var sortedSources = manifest.SourcePaths
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var sortedRefs = manifest.ReferencePaths
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
        });

        writer.WriteStartObject();

        writer.WriteStartArray("sourcePaths");
        foreach (var s in sortedSources)
            writer.WriteStringValue(s);
        writer.WriteEndArray();

        writer.WriteStartArray("referencePaths");
        foreach (var r in sortedRefs)
            writer.WriteStringValue(r);
        writer.WriteEndArray();

        writer.WriteString("langVersion", manifest.LangVersion);
        writer.WriteString("defineConstants", manifest.DefineConstants);
        writer.WriteString("nullable", manifest.Nullable);
        writer.WriteBoolean("allowUnsafeBlocks", manifest.AllowUnsafeBlocks);
        writer.WriteString("implicitUsings", manifest.ImplicitUsings);
        writer.WriteString("allowedOutputRoot", manifest.AllowedOutputRoot.Replace('\\', '/'));
        writer.WriteString("temporaryDirectory", manifest.TemporaryDirectory.Replace('\\', '/'));
        writer.WriteString("manifestAccessibility", manifest.ManifestAccessibility);
        writer.WriteString("targetFramework", manifest.TargetFramework);
        writer.WriteString("taskSemanticVersion", manifest.TaskSemanticVersion);
        writer.WriteString("taskAssemblyIdentity", manifest.TaskAssemblyIdentity);

        writer.WriteEndObject();
        writer.Flush();

        return stream.ToArray();
    }
}
