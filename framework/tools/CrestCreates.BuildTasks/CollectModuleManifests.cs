using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Task = Microsoft.Build.Utilities.Task;

namespace CrestCreates.BuildTasks;

public class ModuleManifest
{
    public string FullName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public List<string> DependsOn { get; set; } = new();
    public int Order { get; set; }
    public bool AutoRegisterServices { get; set; }
}

public class CollectModuleManifests : Task
{
    [Required]
    public ITaskItem[] ModuleManifestFiles { get; set; } = Array.Empty<ITaskItem>();

    [Output]
    public ITaskItem[] CollectedModuleFullNames { get; set; } = Array.Empty<ITaskItem>();

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

                var json = File.ReadAllText(filePath);
                var modules = JsonSerializer.Deserialize<List<ModuleManifest>>(json);
                if (modules != null)
                {
                    allModules.AddRange(modules);
                }
            }

            var sortedModules = TopologicalSort(allModules);

            var outputItems = new List<TaskItem>();
            foreach (var module in sortedModules)
            {
                var item = new TaskItem();
                item.ItemSpec = module.FullName;
                item.SetMetadata("Name", module.Name);
                item.SetMetadata("Namespace", module.Namespace);
                item.SetMetadata("Order", module.Order.ToString());
                item.SetMetadata("AutoRegisterServices", module.AutoRegisterServices.ToString());
                item.SetMetadata("DependsOn", string.Join(";", module.DependsOn));
                outputItems.Add(item);
            }

            CollectedModuleFullNames = outputItems.ToArray();

            Log.LogMessage(MessageImportance.High, $"Collected and sorted {sortedModules.Count} modules");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private List<ModuleManifest> TopologicalSort(List<ModuleManifest> modules)
    {
        var sorted = new List<ModuleManifest>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        foreach (var module in modules.OrderBy(m => m.Order))
        {
            Visit(module, modules, visited, visiting, sorted);
        }

        return sorted;
    }

    private void Visit(ModuleManifest module, List<ModuleManifest> all,
        HashSet<string> visited, HashSet<string> visiting, List<ModuleManifest> sorted)
    {
        if (visited.Contains(module.FullName)) return;
        if (visiting.Contains(module.FullName))
        {
            Log.LogWarning($"Circular dependency detected involving {module.FullName}");
            return;
        }

        visiting.Add(module.FullName);

        foreach (var depName in module.DependsOn)
        {
            var dep = all.FirstOrDefault(m => m.FullName == depName || m.Name == depName);
            if (dep != null)
            {
                Visit(dep, all, visited, visiting, sorted);
            }
        }

        visiting.Remove(module.FullName);
        visited.Add(module.FullName);
        sorted.Add(module);
    }
}