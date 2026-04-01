using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Task = Microsoft.Build.Utilities.Task;

namespace CrestCreates.BuildTasks;

public class GenerateModuleManifest : Task
{
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public ITaskItem[] ModuleTypes { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var modules = new List<object>();

            foreach (var item in ModuleTypes)
            {
                var fullName = item.ItemSpec;
                var name = item.GetMetadata("Name");
                var ns = item.GetMetadata("Namespace");
                var order = int.Parse(item.GetMetadata("Order") ?? "0");
                var autoRegister = bool.Parse(item.GetMetadata("AutoRegisterServices") ?? "true");
                var dependsOnStr = item.GetMetadata("DependsOn");
                var dependsOn = string.IsNullOrEmpty(dependsOnStr)
                    ? new List<string>()
                    : dependsOnStr.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

                modules.Add(new
                {
                    FullName = fullName,
                    Name = name,
                    Namespace = ns,
                    DependsOn = dependsOn,
                    Order = order,
                    AutoRegisterServices = autoRegister
                });
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(modules, options);

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(OutputPath, json);
            Log.LogMessage(MessageImportance.High, $"Generated module manifest at {OutputPath} with {modules.Count} modules");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }
}