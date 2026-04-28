using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Task = Microsoft.Build.Utilities.Task;

namespace CrestCreates.BuildTasks;

public class ScanModulesFromSource : Task
{
    [Required]
    public string SourceFiles { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var files = SourceFiles.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var modules = new List<object>();

            var classRegex = new Regex(@"class\s+(\w+)\s*:\s*ModuleBase", RegexOptions.Compiled);
            var orderRegex = new Regex(@"Order\s*=\s*(-?\d+)", RegexOptions.Compiled);
            var autoRegRegex = new Regex(@"AutoRegisterServices\s*=\s*(true|false)", RegexOptions.Compiled);

            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                if (!file.EndsWith(".cs")) continue;

                var content = File.ReadAllText(file);

                // Match [CrestModule(...)] attribute (various forms)
                var crestModuleRegex = new Regex(@"\[CrestModule[^\]]*\]", RegexOptions.Compiled);
                var moduleMatch = crestModuleRegex.Match(content);
                if (!moduleMatch.Success) continue;

                var classMatch = classRegex.Match(content);
                if (!classMatch.Success) continue;

                var moduleName = classMatch.Groups[1].Value;
                var dependsOn = new List<string>();

                // Extract all typeof(T) from the attribute as dependencies
                var depMatches = Regex.Matches(moduleMatch.Value, @"typeof\s*\(\s*(\w+)\s*\)");
                foreach (Match match in depMatches)
                {
                    dependsOn.Add(match.Groups[1].Value);
                }

                var order = 0;
                var orderMatch = orderRegex.Match(content);
                if (orderMatch.Success)
                {
                    order = int.Parse(orderMatch.Groups[1].Value);
                }

                var autoReg = true;
                var autoRegMatch = autoRegRegex.Match(content);
                if (autoRegMatch.Success)
                {
                    autoReg = bool.Parse(autoRegMatch.Groups[1].Value);
                }

                var nsMatch = Regex.Match(content, @"namespace\s+([\w\.]+)");
                var ns = nsMatch.Success ? nsMatch.Groups[1].Value : "Unknown";

                modules.Add(new
                {
                    FullName = $"{ns}.{moduleName}",
                    Name = moduleName,
                    Namespace = ns,
                    DependsOn = dependsOn,
                    Order = order,
                    AutoRegisterServices = autoReg
                });
            }

            if (modules.Count == 0)
            {
                Log.LogMessage(MessageImportance.High, $"No modules found in {SourceFiles}");
                return true;
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