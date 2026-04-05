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

public class ScanEntityPermissions : Task
{
    [Required]
    public ITaskItem[] SourceFiles { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var permissions = new List<EntityPermissionInfo>();

            var classRegex = new Regex(
                @"(?:public|internal|sealed|abstract|static|partial|\s)+\s+(?:partial\s+)?class\s+(\w+Permissions)\s*(?:<[^>]+>)?\s*:\s*(?:\w+,\s*)*IEntityPermissions(?:\s*,\s*\w+)*",
                RegexOptions.Compiled);

            var constRegex = new Regex(
                @"public\s+const\s+string\s+(\w+)\s*=\s*""([^""]+)""",
                RegexOptions.Compiled);

            var nsRegex = new Regex(
                @"namespace\s+([\w\.]+)",
                RegexOptions.Compiled);

            var entityNameRegex = new Regex(
                @"EntityName\s*=>\s*""([^""]+)""",
                RegexOptions.Compiled);

            var entityNamePropertyRegex = new Regex(
                @"public\s+string\s+EntityName\s*{\s*get\s*}\s*=\s*""([^""]+)""",
                RegexOptions.Compiled);

            foreach (var item in SourceFiles)
            {
                var file = item.ItemSpec;
                if (!File.Exists(file)) continue;
                if (!file.EndsWith(".cs")) continue;

                var content = File.ReadAllText(file);

                var classMatch = classRegex.Match(content);
                if (!classMatch.Success) continue;

                var className = classMatch.Groups[1].Value;

                var nsMatch = nsRegex.Match(content);
                var ns = nsMatch.Success ? nsMatch.Groups[1].Value : "Unknown";

                var entityName = ExtractEntityName(content, className, entityNameRegex, entityNamePropertyRegex);

                var permissionList = new List<string>();
                var constMatches = constRegex.Matches(content);
                foreach (Match match in constMatches)
                {
                    var constName = match.Groups[1].Value;
                    var constValue = match.Groups[2].Value;
                    permissionList.Add(constValue);
                }

                if (permissionList.Count == 0)
                {
                    var entityPrefix = entityName ?? className.Replace("Permissions", "");
                    permissionList = GenerateDefaultPermissions(entityPrefix);
                }

                permissions.Add(new EntityPermissionInfo
                {
                    EntityName = entityName ?? className.Replace("Permissions", ""),
                    ClassName = className,
                    Namespace = ns,
                    Permissions = permissionList
                });
            }

            var manifest = new EntityPermissionsManifest
            {
                Version = "1.0",
                GeneratedAt = DateTime.UtcNow.ToString("O"),
                Permissions = permissions
            };

            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(manifest, options);

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(OutputPath, json);
            Log.LogMessage(MessageImportance.High, 
                $"Generated entity permissions manifest at {OutputPath} with {permissions.Count} entities");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private static string? ExtractEntityName(
        string content, 
        string className,
        Regex entityNameRegex, 
        Regex entityNamePropertyRegex)
    {
        var propertyMatch = entityNamePropertyRegex.Match(content);
        if (propertyMatch.Success)
        {
            return propertyMatch.Groups[1].Value;
        }

        var expressionMatch = entityNameRegex.Match(content);
        if (expressionMatch.Success)
        {
            return expressionMatch.Groups[1].Value;
        }

        return null;
    }

    private static List<string> GenerateDefaultPermissions(string entityName)
    {
        return new List<string>
        {
            $"{entityName}.Create",
            $"{entityName}.Update",
            $"{entityName}.Delete",
            $"{entityName}.View",
            $"{entityName}.Manage"
        };
    }
}

internal class EntityPermissionsManifest
{
    public string Version { get; set; } = "1.0";
    public string GeneratedAt { get; set; } = string.Empty;
    public List<EntityPermissionInfo> Permissions { get; set; } = new();
}

internal class EntityPermissionInfo
{
    public string EntityName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}
