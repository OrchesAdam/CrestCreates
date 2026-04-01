using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrestCreates.BuildTasks;

public class ScanModulesFromSource : Microsoft.Build.Utilities.Task
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

            var classRegex = new Regex(@"class\s+(\w+)\s*:\s*(?:IModule|ModuleBase)", RegexOptions.Compiled);
            var orderRegex = new Regex(@"Order\s*=\s*(-?\d+)", RegexOptions.Compiled);
            var autoRegRegex = new Regex(@"AutoRegisterServices\s*=\s*(true|false)", RegexOptions.Compiled);
            var depRegex = new Regex(@"typeof\s*\(\s*(\w+)\s*\)", RegexOptions.Compiled);

            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                if (!file.EndsWith(".cs")) continue;

                var content = File.ReadAllText(file);

                var moduleAttr = ExtractModuleAttribute(content);
                if (moduleAttr == null) continue;

                var classMatch = classRegex.Match(content);
                if (!classMatch.Success) continue;

                var moduleName = classMatch.Groups[1].Value;
                var dependsOn = new List<string>();

                var depMatches = depRegex.Matches(moduleAttr);
                for (int i = 0; i < depMatches.Count; i++)
                {
                    dependsOn.Add(depMatches[i].Groups[1].Value);
                }

                var order = 0;
                var orderMatch = orderRegex.Match(moduleAttr);
                if (orderMatch.Success)
                {
                    order = int.Parse(orderMatch.Groups[1].Value);
                }

                var autoReg = true;
                var autoRegMatch = autoRegRegex.Match(moduleAttr);
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

    private string? ExtractModuleAttribute(string content)
    {
        var startIndex = content.IndexOf("[Module");
        if (startIndex < 0) return null;

        var depth = 0;
        var inBrackets = false;
        var endIndex = startIndex;

        for (int i = startIndex; i < content.Length; i++)
        {
            var c = content[i];
            
            if (c == '[')
            {
                depth++;
                inBrackets = true;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0 && inBrackets)
                {
                    endIndex = i + 1;
                    break;
                }
            }
        }

        if (endIndex <= startIndex) return null;

        return content.Substring(startIndex, endIndex - startIndex);
    }
}