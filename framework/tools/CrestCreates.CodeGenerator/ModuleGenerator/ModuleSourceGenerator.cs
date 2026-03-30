using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace CrestCreates.CodeGenerator.ModuleGenerator;

[Generator]
public class ModuleSourceGenerator : IIncrementalGenerator
{
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
            .FirstOrDefault(a => a.AttributeClass?.Name == "ModuleAttribute");
        if (moduleAttribute is null) return null;

        var dependencies = new List<string>();
        if (moduleAttribute.ConstructorArguments.Length > 0)
        {
            var arg = moduleAttribute.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Array)
                dependencies.AddRange(arg.Values.Select(v => v.Value is System.Type type ? type.FullName ?? v.Value.ToString() : v.Value?.ToString() ?? ""));
        }

        var namedArgs = moduleAttribute.NamedArguments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
        var dependsOn = namedArgs.TryGetValue("DependsOn", out var dv) && dv is ImmutableArray<TypedConstant> types
            ? types.Select(t => t.Value is System.Type type ? type.FullName ?? t.Value.ToString() : t.Value?.ToString() ?? "").ToList() : dependencies;
        var order = namedArgs.TryGetValue("Order", out var ov) && ov is int o ? o : 0;
        var autoRegister = namedArgs.TryGetValue("AutoRegisterServices", out var av) && av is bool b ? b : true;

        return new ModuleInfo(symbol.Name, symbol.ContainingNamespace.ToDisplayString(), dependsOn, order, autoRegister);
    }

    private static void GenerateModuleCode(SourceProductionContext context, ImmutableArray<ModuleInfo?> modules)
    {
        var validModules = modules.Where(m => m is not null).Cast<ModuleInfo>().ToList();
        if (validModules.Count == 0) return;

        var sortedModules = TopologicalSort(validModules);
        if (sortedModules is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("MODULE001", "Circular dependency", "Module dependencies contain circular reference", "ModuleGenerator", DiagnosticSeverity.Error, true), Location.None));
            return;
        }
        GenerateRegistrationCode(context, sortedModules);
    }

    private static List<ModuleInfo>? TopologicalSort(List<ModuleInfo> modules)
    {
        var sorted = new List<ModuleInfo>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        foreach (var module in modules.OrderBy(m => m.Order))
            if (!Visit(module, modules, visited, visiting, sorted)) return null;
        return sorted;
    }

    private static bool Visit(ModuleInfo module, List<ModuleInfo> all, HashSet<string> visited, HashSet<string> visiting, List<ModuleInfo> sorted)
    {
        if (visited.Contains(module.Name)) return true;
        if (visiting.Contains(module.Name)) return false;
        visiting.Add(module.Name);
        foreach (var depName in module.DependsOn)
        {
            var dep = all.FirstOrDefault(m => m.Name == depName || m.FullName == depName);
            if (dep is not null && !Visit(dep, all, visited, visiting, sorted)) return false;
        }
        visiting.Remove(module.Name);
        visited.Add(module.Name);
        sorted.Add(module);
        return true;
    }

    private static void GenerateRegistrationCode(SourceProductionContext context, List<ModuleInfo> modules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using System;");
        sb.AppendLine("using CrestCreates.Infrastructure.Modularity;");
        sb.AppendLine("namespace CrestCreates.Infrastructure.Modularity;");
        sb.AppendLine("public static class AutoModuleRegistration {");
        sb.Append("    public static readonly System.Collections.Generic.IReadOnlyList<string> RegisteredModules = new[] { ");
        sb.Append(string.Join(", ", modules.Select(m => $"\"{m.FullName}\"")));
        sb.AppendLine(" };");
        sb.AppendLine("    public static IHostBuilder RegisterModules(this IHostBuilder hostBuilder) {");
        sb.AppendLine("        return hostBuilder.ConfigureServices((context, services) => {");
        foreach (var m in modules) sb.AppendLine($"            services.AddSingleton<{m.FullName}>();");
        sb.AppendLine("        }).ConfigureServices((context, services) => {");
        sb.AppendLine("            var sp = services.BuildServiceProvider();");
        sb.AppendLine("            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();");
        sb.AppendLine("            var logger = loggerFactory.CreateLogger<IModule>();");
        sb.AppendLine("            logger?.LogInformation(\"Starting module initialization process...\");");
        foreach (var m in modules) {
            sb.AppendLine("            try {");
            sb.AppendLine($"                logger?.LogInformation(\"[PreInit] {m.Name}\");");
            sb.AppendLine($"                sp.GetRequiredService<{m.FullName}>().OnPreInitialize();");
            sb.AppendLine("            } catch (Exception ex) {");
            sb.AppendLine($"                logger?.LogError(ex, \"Error during PreInit phase of module {m.Name}\");");
            sb.AppendLine("                throw;");
            sb.AppendLine("            }");
        }
        foreach (var m in modules) {
            sb.AppendLine("            try {");
            sb.AppendLine($"                logger?.LogInformation(\"[Init] {m.Name}\");");
            sb.AppendLine($"                sp.GetRequiredService<{m.FullName}>().OnInitialize();");
            sb.AppendLine("            } catch (Exception ex) {");
            sb.AppendLine($"                logger?.LogError(ex, \"Error during Init phase of module {m.Name}\");");
            sb.AppendLine("                throw;");
            sb.AppendLine("            }");
        }
        foreach (var m in modules) {
            sb.AppendLine("            try {");
            sb.AppendLine($"                logger?.LogInformation(\"[PostInit] {m.Name}\");");
            sb.AppendLine($"                sp.GetRequiredService<{m.FullName}>().OnPostInitialize();");
            sb.AppendLine("            } catch (Exception ex) {");
            sb.AppendLine($"                logger?.LogError(ex, \"Error during PostInit phase of module {m.Name}\");");
            sb.AppendLine("                throw;");
            sb.AppendLine("            }");
        }
        foreach (var m in modules) {
            sb.AppendLine("            try {");
            sb.AppendLine($"                logger?.LogInformation(\"[ConfigureServices] {m.Name}\");");
            sb.AppendLine($"                sp.GetRequiredService<{m.FullName}>().OnConfigureServices(services);");
            sb.AppendLine("            } catch (Exception ex) {");
            sb.AppendLine($"                logger?.LogError(ex, \"Error during ConfigureServices phase of module {m.Name}\");");
            sb.AppendLine("                throw;");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            logger?.LogInformation(\"Module initialization process completed.\");");
        sb.AppendLine("        }); }");
        sb.AppendLine("    public static IHost InitializeModules(this IHost host) {");
        sb.AppendLine("        var logger = host.Services.GetService<ILogger<IModule>>();");
        sb.AppendLine("        logger?.LogInformation(\"Starting application initialization process for modules...\");");
        foreach (var m in modules) {
            sb.AppendLine("        try {");
            sb.AppendLine($"            logger?.LogInformation(\"[AppInit] {m.Name}\");");
            sb.AppendLine($"            host.Services.GetRequiredService<{m.FullName}>().OnApplicationInitialization(host);");
            sb.AppendLine("        } catch (Exception ex) {");
            sb.AppendLine($"            logger?.LogError(ex, \"Error during ApplicationInitialization phase of module {m.Name}\");");
            sb.AppendLine("            throw;");
            sb.AppendLine("        }");
        }
        sb.AppendLine("        logger?.LogInformation(\"Application initialization process for modules completed.\");");
        sb.AppendLine("        return host; } }");
        context.AddSource("AutoModuleRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
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