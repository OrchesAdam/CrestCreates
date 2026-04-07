using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
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
                @"(?:public|internal|sealed|abstract|static|partial|\s)+\s+(?:sealed\s+)?(?:partial\s+)?class\s+(\w+Permissions)\s*(?:<[^>]+>)?\s*:\s*(?:\w+,\s*)*IEntityPermissions(?:\s*,\s*\w+)*",
                RegexOptions.Compiled);
            var constRegex = new Regex(
                @"public\s+const\s+string\s+\w+\s*=\s*""([^""]+)""",
                RegexOptions.Compiled);
            var nsRegex = new Regex(@"namespace\s+([\w\.]+)", RegexOptions.Compiled);
            var entityNameRegex = new Regex(@"EntityName\s*=>\s*""([^""]+)""", RegexOptions.Compiled);
            var entityNamePropertyRegex = new Regex(@"public\s+string\s+EntityName\s*{\s*get\s*}\s*=\s*""([^""]+)""", RegexOptions.Compiled);
            var moduleNameRegex = new Regex(@"ModuleName\s*=>\s*""([^""]+)""", RegexOptions.Compiled);
            var moduleNamePropertyRegex = new Regex(@"public\s+string\s+ModuleName\s*{\s*get\s*}\s*=\s*""([^""]+)""", RegexOptions.Compiled);
            var entityFullNameRegex = new Regex(@"EntityFullName\s*=>\s*""([^""]+)""", RegexOptions.Compiled);
            var entityFullNamePropertyRegex = new Regex(@"public\s+string\s+EntityFullName\s*{\s*get\s*}\s*=\s*""([^""]+)""", RegexOptions.Compiled);

            var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in SourceFiles)
            {
                var file = item.ItemSpec;
                if (!File.Exists(file) || !file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(file);
                if (!visitedFiles.Add(fullPath)) continue;

                var content = File.ReadAllText(fullPath);
                var classMatch = classRegex.Match(content);
                if (!classMatch.Success)
                {
                    continue;
                }

                var className = classMatch.Groups[1].Value;
                var nsMatch = nsRegex.Match(content);
                var ns = nsMatch.Success ? nsMatch.Groups[1].Value : "Unknown";
                var entityName = ExtractStringProperty(content, entityNameRegex, entityNamePropertyRegex)
                    ?? className.Replace("Permissions", "");
                var moduleName = ExtractStringProperty(content, moduleNameRegex, moduleNamePropertyRegex) ?? string.Empty;
                var entityFullName = ExtractStringProperty(content, entityFullNameRegex, entityFullNamePropertyRegex) ?? string.Empty;

                var permissionList = new List<string>();
                foreach (Match match in constRegex.Matches(content))
                {
                    permissionList.Add(match.Groups[1].Value);
                }

                if (permissionList.Count == 0)
                {
                    var entityPrefix = string.IsNullOrWhiteSpace(moduleName)
                        ? entityName
                        : $"{moduleName}.{entityName}";
                    permissionList = GenerateDefaultPermissions(entityPrefix);
                }

                permissions.Add(new EntityPermissionInfo
                {
                    ModuleName = moduleName,
                    EntityName = entityName,
                    EntityFullName = entityFullName,
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

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(OutputPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

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

    private static string? ExtractStringProperty(string content, Regex expressionRegex, Regex propertyRegex)
    {
        var propertyMatch = propertyRegex.Match(content);
        if (propertyMatch.Success)
        {
            return propertyMatch.Groups[1].Value;
        }

        var expressionMatch = expressionRegex.Match(content);
        return expressionMatch.Success ? expressionMatch.Groups[1].Value : null;
    }

    private static List<string> GenerateDefaultPermissions(string entityName)
    {
        return new List<string>
        {
            $"{entityName}.Create",
            $"{entityName}.Update",
            $"{entityName}.Delete",
            $"{entityName}.Search",
            $"{entityName}.Get",
            $"{entityName}.Export"
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
    public string ModuleName { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityFullName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}
