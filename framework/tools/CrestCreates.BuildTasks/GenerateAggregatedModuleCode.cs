using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Task = Microsoft.Build.Utilities.Task;

namespace CrestCreates.BuildTasks;

public class GenerateAggregatedModuleCode : Task
{
    [Required]
    public ITaskItem[] ModuleManifestFiles { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputPath { get; set; } = string.Empty;

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

            var code = GenerateCode(sortedModules);

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(OutputPath, code);
            Log.LogMessage(MessageImportance.High, $"Generated aggregated module code at {OutputPath} with {sortedModules.Count} modules");

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

    private string GenerateCode(List<ModuleManifest> sortedModules)
    {
        var moduleListJson = JsonSerializer.Serialize(sortedModules.Select(m => new
        {
            m.FullName,
            m.Name,
            m.Namespace,
            m.DependsOn,
            m.Order,
            m.AutoRegisterServices
        }).ToList());

        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using CrestCreates.Modularity;");
        sb.AppendLine();
        sb.AppendLine("namespace CrestCreates.Modularity {");
        sb.AppendLine();
        sb.AppendLine("    public static class CrossProjectModuleRegistry {");
        sb.AppendLine("        private static readonly List<ModuleDescriptor> _descriptors = new();");
        sb.AppendLine("        private static readonly object _lock = new();");
        sb.AppendLine("        private static bool _initialized = false;");
        sb.AppendLine();
        sb.AppendLine("        public static void Register(System.Type moduleType, int order, bool autoRegisterServices) {");
        sb.AppendLine("            lock (_lock) {");
        sb.AppendLine("                if (_descriptors.Any(d => d.ModuleType == moduleType)) return;");
        sb.AppendLine("                _descriptors.Add(new ModuleDescriptor(moduleType, order, autoRegisterServices));");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static IReadOnlyList<ModuleDescriptor> GetDescriptors() {");
        sb.AppendLine("            lock (_lock) {");
        sb.AppendLine("                if (!_initialized) {");
        sb.AppendLine("                    _initialized = true;");
        sb.AppendLine("                    var modules = System.Text.Json.JsonSerializer.Deserialize<List<ModuleInfo>>(ModuleListJson);");
        sb.AppendLine("                    if (modules != null) {");
        sb.AppendLine("                        foreach (var moduleJson in modules) {");
        sb.AppendLine("                            var moduleType = System.Type.GetType(moduleJson.FullName);");
        sb.AppendLine("                            if (moduleType != null) {");
        sb.AppendLine("                                _descriptors.Add(new ModuleDescriptor(moduleType, moduleJson.Order, moduleJson.AutoRegisterServices));");
        sb.AppendLine("                            }");
        sb.AppendLine("                        }");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
        sb.AppendLine("                return _descriptors.OrderBy(d => d.Order).ToList();");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        private static readonly string ModuleListJson = @\"{moduleListJson.Replace("\"", "\"\"")}\";");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static class CrossProjectModuleAutoInitializer {");
        sb.Append("        public static readonly IReadOnlyList<string> RegisteredModules = new[] { ");
        sb.Append(string.Join(", ", sortedModules.Select(m => $"\"{m.FullName}\"")));
        sb.AppendLine(" };");
        sb.AppendLine();
        sb.AppendLine("        public static IHostBuilder RegisterCrossProjectModules(this IHostBuilder hostBuilder) {");
        sb.AppendLine("            return hostBuilder.ConfigureServices((context, services) => {");
        sb.AppendLine("                var descriptors = CrossProjectModuleRegistry.GetDescriptors();");
        sb.AppendLine("                foreach (var descriptor in descriptors) {");
        sb.AppendLine("                    services.AddSingleton(descriptor.ModuleType);");
        sb.AppendLine("                }");
        sb.AppendLine("            }).ConfigureServices((context, services) => {");
        sb.AppendLine("                var descriptors = CrossProjectModuleRegistry.GetDescriptors();");
        sb.AppendLine("                var sp = services.BuildServiceProvider();");
        sb.AppendLine("                var loggerFactory = sp.GetService<ILoggerFactory>();");
        sb.AppendLine("                var logger = loggerFactory?.CreateLogger<IModule>();");
        sb.AppendLine();
        sb.AppendLine("                foreach (var descriptor in descriptors) {");
        sb.AppendLine("                    try { logger?.LogInformation(\"[PreInit] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                    try { ((IModule)sp.GetRequiredService(descriptor.ModuleType)).OnPreInitialize(); } catch (Exception ex) { logger?.LogError(ex, \"Error during PreInit phase\"); throw; }");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                foreach (var descriptor in descriptors) {");
        sb.AppendLine("                    try { logger?.LogInformation(\"[Init] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                    try { ((IModule)sp.GetRequiredService(descriptor.ModuleType)).OnInitialize(); } catch (Exception ex) { logger?.LogError(ex, \"Error during Init phase\"); throw; }");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                foreach (var descriptor in descriptors) {");
        sb.AppendLine("                    try { logger?.LogInformation(\"[PostInit] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                    try { ((IModule)sp.GetRequiredService(descriptor.ModuleType)).OnPostInitialize(); } catch (Exception ex) { logger?.LogError(ex, \"Error during PostInit phase\"); throw; }");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                foreach (var descriptor in descriptors.Where(d => d.AutoRegisterServices)) {");
        sb.AppendLine("                    try { logger?.LogInformation(\"[ConfigureServices] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                    try { ((IModule)sp.GetRequiredService(descriptor.ModuleType)).OnConfigureServices(services); } catch (Exception ex) { logger?.LogError(ex, \"Error during ConfigureServices phase\"); throw; }");
        sb.AppendLine("                }");
        sb.AppendLine("            });");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static IHost InitializeCrossProjectModules(this IHost host) {");
        sb.AppendLine("            var logger = host.Services.GetService<ILogger<IModule>>();");
        sb.AppendLine("            var descriptors = CrossProjectModuleRegistry.GetDescriptors();");
        sb.AppendLine();
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                try { logger?.LogInformation(\"[AppInit] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                try { ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnApplicationInitialization(host); } catch (Exception ex) { logger?.LogError(ex, \"Error during AppInit phase\"); throw; }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return host;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
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