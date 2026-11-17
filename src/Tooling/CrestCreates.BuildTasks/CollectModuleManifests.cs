using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CrestCreates.BuildTasks;

public class CollectModuleManifests : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] ModuleManifestFiles { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public ITaskItem[] ReferencedProjectPaths { get; set; } = Array.Empty<ITaskItem>();

    public ITaskItem[] ReferencedModuleManifestFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            if (ReferencedProjectPaths.Length != 0)
            {
                Log.LogError("ReferencedProjectPaths cannot resolve evaluated manifest paths; use ReferencedModuleManifestFiles target outputs.");
                return false;
            }
            var allModules = new SortedDictionary<string, ModuleManifest>(StringComparer.Ordinal);
            foreach (var file in ModuleManifestFiles.Concat(ReferencedModuleManifestFiles).Select(f => Path.GetFullPath(f.ItemSpec)).Distinct(StringComparer.Ordinal))
            {
                var modules = ReadManifest<ModuleManifest>(file);
                var identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var module in modules)
                {
                    if (!identities.Add(module.FullName)) throw new InvalidDataException($"Duplicate module identity: {module.FullName} in {file}");
                    module.DependsOn = module.DependsOn.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                    if (allModules.TryGetValue(module.FullName, out var existing))
                    {
                        if (existing.Name != module.Name || existing.Namespace != module.Namespace || existing.Order != module.Order ||
                            existing.AutoRegisterServices != module.AutoRegisterServices || !existing.DependsOn.SequenceEqual(module.DependsOn))
                            throw new InvalidDataException($"Conflicting module identity: {module.FullName} in {file}");
                    }
                    else allModules.Add(module.FullName, module);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputPath))!);
            File.WriteAllText(OutputPath, JsonSerializer.Serialize(allModules.Values, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    internal static List<T> ReadManifest<T>(string path) where T : ModuleManifest
    {
        var modules = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Module manifest must be an array: {path}");
        foreach (var module in modules)
        {
            if (module == null || string.IsNullOrWhiteSpace(module.FullName) || string.IsNullOrWhiteSpace(module.Name) ||
                module.Namespace == null || module.DependsOn == null || module.DependsOn.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Invalid module metadata in {path}");
        }
        return modules;
    }

    public class ModuleManifest
    {
        [System.Text.Json.Serialization.JsonRequired]
        public string FullName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public string Namespace { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonRequired]
        public List<string> DependsOn { get; set; } = new();
        [System.Text.Json.Serialization.JsonRequired]
        public int Order { get; set; }
        [System.Text.Json.Serialization.JsonRequired]
        public bool AutoRegisterServices { get; set; }
    }
}
