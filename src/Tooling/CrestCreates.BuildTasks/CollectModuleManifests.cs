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
    [Required]
    public ITaskItem[] ModuleManifestFiles { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public ITaskItem[] ReferencedProjectPaths { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var allModules = new List<ModuleManifest>();

            foreach (var file in ModuleManifestFiles)
            {
                var filePath = file.ItemSpec;
                if (!File.Exists(filePath))
                {
                    Log.LogWarning($"Module manifest file not found: {filePath}");
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(filePath);
                    var modules = JsonSerializer.Deserialize<List<ModuleManifest>>(json);
                    if (modules != null)
                    {
                        allModules.AddRange(modules);
                        Log.LogMessage(MessageImportance.Low, $"Loaded {modules.Count} modules from {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Failed to parse module manifest {filePath}: {ex.Message}");
                }
            }

            foreach (var projectRef in ReferencedProjectPaths)
            {
                var projectPath = projectRef.ItemSpec;
                if (!File.Exists(projectPath)) continue;

                var projectDir = Path.GetDirectoryName(projectPath);
                if (string.IsNullOrEmpty(projectDir)) continue;

                var objDir = Path.Combine(projectDir, "obj", "Debug", "net10.0");
                var manifestPath = Path.Combine(objDir, "ModuleManifest.json");

                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var json = File.ReadAllText(manifestPath);
                        var modules = JsonSerializer.Deserialize<List<ModuleManifest>>(json);
                        if (modules != null)
                        {
                            allModules.AddRange(modules);
                            Log.LogMessage(MessageImportance.Low, $"Loaded {modules.Count} modules from referenced project {projectPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"Failed to parse module manifest {manifestPath}: {ex.Message}");
                    }
                }
            }

            if (allModules.Count == 0)
            {
                Log.LogMessage(MessageImportance.High, "No modules found in any manifest files");
                return true;
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var outputJson = JsonSerializer.Serialize(allModules, options);

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(OutputPath, outputJson);
            Log.LogMessage(MessageImportance.High, $"Collected {allModules.Count} modules to {OutputPath}");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    public class ModuleManifest
    {
        public string FullName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public List<string> DependsOn { get; set; } = new();
        public int Order { get; set; }
        public bool AutoRegisterServices { get; set; }
    }
}