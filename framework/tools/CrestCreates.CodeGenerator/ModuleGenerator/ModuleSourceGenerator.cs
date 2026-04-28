using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CrestCreates.CodeGenerator.ModuleGenerator;

[Generator]
public class ModuleSourceGenerator : IIncrementalGenerator
{
    private const string ModuleMarkerAttribute = "CrestModuleAttribute";
    private const string DependsOnAttribute = "DependsOn";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modulesProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetModuleInfo(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(modulesProvider.Collect(), GenerateModuleCode);
    }

    private static ModuleInfo? GetModuleInfo(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (symbol is null) return null;

        var moduleAttribute = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == ModuleMarkerAttribute);
        if (moduleAttribute is null) return null;

        var dependencies = new List<string>();
        if (moduleAttribute.ConstructorArguments.Length > 0)
        {
            var arg = moduleAttribute.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Array)
            {
                foreach (var value in arg.Values)
                {
                    if (value.Value is System.Type type)
                    {
                        dependencies.Add(type.FullName ?? type.Name);
                    }
                    else if (value.Value != null)
                    {
                        dependencies.Add(value.Value.ToString()!);
                    }
                }
            }
        }

        var dependsOn = dependencies;
        var order = 0;
        var autoRegister = true;

        foreach (var namedArg in moduleAttribute.NamedArguments)
        {
            if (namedArg.Key == DependsOnAttribute && namedArg.Value.Value is ImmutableArray<TypedConstant> types)
            {
                dependsOn = new List<string>();
                foreach (var type in types)
                {
                    if (type.Value is System.Type t)
                    {
                        dependsOn.Add(t.FullName ?? t.Name);
                    }
                    else if (type.Value != null)
                    {
                        dependsOn.Add(type.Value.ToString()!);
                    }
                }
            }
            else if (namedArg.Key == "Order" && namedArg.Value.Value is int o)
            {
                order = o;
            }
            else if (namedArg.Key == "AutoRegisterServices" && namedArg.Value.Value is bool b)
            {
                autoRegister = b;
            }
        }

        return new ModuleInfo(symbol.Name, symbol.ContainingNamespace.ToDisplayString(), dependsOn, order, autoRegister);
    }

    private static void GenerateModuleCode(SourceProductionContext context, ImmutableArray<ModuleInfo?> modules)
    {
        var validModules = modules.Where(m => m is not null).Cast<ModuleInfo>().ToList();
        if (validModules.Count == 0) return;

        GenerateModuleExtensions(context, validModules);
    }

    private static void GenerateModuleExtensions(SourceProductionContext context, List<ModuleInfo> modules)
    {
        var sortedModules = TopologicalSort(modules);

        GenerateAutoModuleRegistration(context, sortedModules);

        foreach (var module in modules)
        {
            var extensionCode = GenerateSingleModuleExtension(module);
            context.AddSource($"{module.Name}Extensions.g.cs", SourceText.From(extensionCode, Encoding.UTF8));
        }
    }

    private static void GenerateAutoModuleRegistration(SourceProductionContext context, List<ModuleInfo> sortedModules)
    {
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
        sb.AppendLine("    internal static class ModuleDescriptorRegistry {");
        sb.AppendLine("        private static readonly List<ModuleDescriptor> _descriptors = new();");
        sb.AppendLine("        private static readonly object _lock = new();");
        sb.AppendLine();
        sb.AppendLine("        public static void Register(System.Type moduleType, int order, bool autoRegisterServices) {");
        sb.AppendLine("            lock (_lock) {");
        sb.AppendLine("                if (_descriptors.Any(d => d.ModuleType == moduleType)) return;");
        sb.AppendLine("                _descriptors.Add(new ModuleDescriptor(moduleType, order, autoRegisterServices));");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static IReadOnlyList<ModuleDescriptor> GetDescriptors() {");
        sb.AppendLine("            lock (_lock) { return _descriptors.OrderBy(d => d.Order).ToList(); }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    internal static class ModuleAutoInitializer {");

        // Static initializer: register all module types with direct typeof() references (AOT-friendly)
        sb.AppendLine("        static ModuleAutoInitializer() {");
        foreach (var module in sortedModules)
        {
            sb.AppendLine($"            ModuleDescriptorRegistry.Register(typeof({module.FullName}), {module.Order}, {module.AutoRegisterServices.ToString().ToLower()});");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.Append("        public static readonly IReadOnlyList<string> RegisteredModules = new[] { ");
        sb.Append(string.Join(", ", sortedModules.Select(m => $"\"{m.FullName}\"")));
        sb.AppendLine(" };");
        sb.AppendLine();

        // RegisterModules: register module types and call OnConfigureServices during ConfigureServices phase
        sb.AppendLine("        public static IHostBuilder RegisterModules(this IHostBuilder hostBuilder) {");
        sb.AppendLine("            return hostBuilder.ConfigureServices((context, services) => {");
        // Register all module types as singletons
        foreach (var module in sortedModules)
        {
            sb.AppendLine($"                services.AddSingleton<{module.FullName}>();");
        }
        // Call OnConfigureServices for each module with AutoRegisterServices=true
        // We instantiate directly since we're in ConfigureServices and can't resolve from DI yet
        foreach (var module in sortedModules.Where(m => m.AutoRegisterServices))
        {
            sb.AppendLine($"                new {module.FullName}().OnConfigureServices(services);");
        }
        sb.AppendLine("            });");
        sb.AppendLine("        }");
        sb.AppendLine();

        // InitializeModules: resolve from the built host and execute lifecycle hooks
        sb.AppendLine("        public static IHost InitializeModules(this IHost host) {");
        sb.AppendLine("            var logger = host.Services.GetService<ILogger<IModule>>();");
        sb.AppendLine("            var descriptors = ModuleDescriptorRegistry.GetDescriptors();");
        sb.AppendLine();
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                try { logger?.LogInformation(\"[PreInit] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                try { ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnPreInitialize(); } catch (Exception ex) { logger?.LogError(ex, \"Error during PreInit phase\"); throw; }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                try { logger?.LogInformation(\"[Init] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                try { ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnInitialize(); } catch (Exception ex) { logger?.LogError(ex, \"Error during Init phase\"); throw; }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                try { logger?.LogInformation(\"[PostInit] {ModuleName}\", descriptor.ModuleType.Name); } catch { }");
        sb.AppendLine("                try { ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnPostInitialize(); } catch (Exception ex) { logger?.LogError(ex, \"Error during PostInit phase\"); throw; }");
        sb.AppendLine("            }");
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
        context.AddSource("AutoModuleRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static List<ModuleInfo> TopologicalSort(List<ModuleInfo> modules)
    {
        var sorted = new List<ModuleInfo>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        foreach (var module in modules.OrderBy(m => m.Order))
            Visit(module, modules, visited, visiting, sorted);
        return sorted;
    }

    private static void Visit(ModuleInfo module, List<ModuleInfo> all, HashSet<string> visited, HashSet<string> visiting, List<ModuleInfo> sorted)
    {
        if (visited.Contains(module.Name)) return;
        if (visiting.Contains(module.Name)) return;
        visiting.Add(module.Name);
        foreach (var depName in module.DependsOn)
        {
            var dep = all.FirstOrDefault(m => m.Name == depName || m.FullName == depName);
            if (dep is not null) Visit(dep, all, visited, visiting, sorted);
        }
        visiting.Remove(module.Name);
        visited.Add(module.Name);
        sorted.Add(module);
    }

    private static string GenerateSingleModuleExtension(ModuleInfo module)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using System;");
        sb.AppendLine("using CrestCreates.Modularity;");
        sb.AppendLine();
        sb.AppendLine($"namespace {module.Namespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    public static class {module.Name}Extensions");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static IServiceCollection Add{module.Name}(this IServiceCollection services)");
        sb.AppendLine("        {");
        sb.AppendLine($"            services.AddSingleton<{module.FullName}>();");
        sb.AppendLine($"            ModuleDescriptorRegistry.Register(typeof({module.FullName}), {module.Order}, {module.AutoRegisterServices.ToString().ToLower()});");
        sb.AppendLine($"            return services;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public static IHostBuilder Add{module.Name}(this IHostBuilder hostBuilder)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return hostBuilder.ConfigureServices((context, services) => services.Add{module.Name}());");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public static {module.FullName} Get{module.Name}(this IServiceProvider services) => services.GetRequiredService<{module.FullName}>();");
        sb.AppendLine($"        public static {module.FullName}? TryGet{module.Name}(this IServiceProvider services) => services.GetService<{module.FullName}>();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private class ModuleInfo
    {
        public ModuleInfo(string name, string ns, List<string> dependsOn, int order, bool autoRegisterServices)
        {
            Name = name;
            Namespace = ns;
            DependsOn = dependsOn;
            Order = order;
            AutoRegisterServices = autoRegisterServices;
        }

        public string Name { get; }
        public string Namespace { get; }
        public List<string> DependsOn { get; }
        public int Order { get; }
        public bool AutoRegisterServices { get; }
        public string FullName => $"{Namespace}.{Name}";
    }
}
