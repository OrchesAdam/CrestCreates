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

        var modulesAndCompilation = modulesProvider.Collect().Combine(context.CompilationProvider);

        context.RegisterSourceOutput(modulesAndCompilation, GenerateModuleCode);
    }

    private static ModuleInfo? GetModuleInfo(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
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

        // Check for parameterless constructor (AOT-friendly instantiation)
        var ctors = symbol.Constructors.Where(c => !c.IsStatic).ToArray();
        var hasParameterlessCtor = ctors.Length == 0
            || ctors.Any(c => c.Parameters.IsEmpty);

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

        return new ModuleInfo(symbol.Name, symbol.ContainingNamespace.ToDisplayString(), dependsOn, order, autoRegister, hasParameterlessCtor);
    }

    private static void GenerateModuleCode(SourceProductionContext context, (ImmutableArray<ModuleInfo?>, Compilation) combined)
    {
        var (modules, compilation) = combined;
        var validModules = modules.Where(m => m is not null).Cast<ModuleInfo>().ToList();
        if (validModules.Count == 0) return;

        GenerateModuleExtensions(context, validModules);

        // Emit app-local application initializer only when the project is a web app
        // that has the BuildTasks-generated ModuleAutoInitializer with InitializeModulesAsync.
        // We check for CrestCreates.Web.Module.WebModule as a proxy for "this is a real web app,
        // not a test/framework library project that happens to have WebApplication available."
        var webApplicationType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.WebApplication");
        var webModuleType = compilation.GetTypeByMetadataName("CrestCreates.Web.Module.WebModule");
        if (webApplicationType is not null && webModuleType is not null)
        {
            GenerateAppLocalApplicationInitializer(context);
        }
    }

    private static void GenerateModuleExtensions(SourceProductionContext context, List<ModuleInfo> modules)
    {
        var sortedModules = TopologicalSort(modules);

        foreach (var module in modules)
        {
            var extensionCode = GenerateSingleModuleExtension(module);
            context.AddSource($"{module.Name}Extensions.g.cs", SourceText.From(extensionCode, Encoding.UTF8));
        }
    }

    private static void GenerateAppLocalApplicationInitializer(SourceProductionContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using CrestCreates.Modularity;");
        sb.AppendLine();
        sb.AppendLine("namespace Microsoft.AspNetCore.Builder;");
        sb.AppendLine();
        sb.AppendLine("public static partial class CrestGeneratedApplicationInitializationExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static Task InitializeCrestApplicationAsync(this WebApplication app)");
        sb.AppendLine("    {");
        sb.AppendLine("        return app.InitializeModulesAsync();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        context.AddSource("CrestApplicationInitialization.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateAutoModuleRegistration(SourceProductionContext context, List<ModuleInfo> sortedModules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using CrestCreates.Modularity;");
        sb.AppendLine("using CrestCreates.ModuleDiagnostics.Stores;");
        sb.AppendLine("using CrestCreates.ModuleDiagnostics.Timing;");
        sb.AppendLine("using CrestCreates.ModuleDiagnostics.Modules;");
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
        sb.AppendLine();
        sb.AppendLine("        private static readonly ModuleDiagnosticsStore _diagnostics = ModuleDiagnosticsServiceCollectionExtensions.Store;");
        sb.AppendLine();

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

        // RegisterModules: register module types as singletons, then call ConfigureServices with diagnostics
        sb.AppendLine("        public static IHostBuilder RegisterModules(this IHostBuilder hostBuilder) {");
        sb.AppendLine("            return hostBuilder.ConfigureServices((context, services) => {");
        foreach (var module in sortedModules)
        {
            sb.AppendLine($"                services.AddSingleton<{module.FullName}>();");
        }
        sb.AppendLine();
        sb.AppendLine("                foreach (var descriptor in ModuleDescriptorRegistry.GetDescriptors().Where(d => d.AutoRegisterServices)) {");
        var instantiableModules = sortedModules.Where(m => m.HasParameterlessConstructor).ToList();
        if (instantiableModules.Count > 0)
        {
            sb.AppendLine("                    // ModuleDiagnostics: descriptor.ModuleType.Name → ConfigureServices");
            sb.AppendLine("                    var _csTimer = ModulePhaseTimer.StartNew(descriptor.ModuleType.Name, \"ConfigureServices\");");
            sb.AppendLine("                    try {");
            sb.AppendLine("                        IModule module;");
            for (int i = 0; i < instantiableModules.Count; i++)
            {
                var module = instantiableModules[i];
                sb.AppendLine(i == 0
                    ? $"                        if (descriptor.ModuleType == typeof({module.FullName})) module = new {module.FullName}();"
                    : $"                        else if (descriptor.ModuleType == typeof({module.FullName})) module = new {module.FullName}();");
            }
            sb.AppendLine("                        else throw new NotSupportedException($\"Unknown module type: {descriptor.ModuleType.FullName}\");");
            sb.AppendLine("                        module.OnConfigureServices(services);");
            sb.AppendLine("                        _diagnostics.Record(_csTimer.Stop(ModulePhaseStatus.Success));");
            sb.AppendLine("                    } catch (Exception ex) {");
            sb.AppendLine("                        _diagnostics.Record(_csTimer.StopFailed(ex));");
            sb.AppendLine("                        throw;");
            sb.AppendLine("                    }");
        }
        sb.AppendLine("                }");
        sb.AppendLine("            });");
        sb.AppendLine("        }");
        sb.AppendLine();

        // InitializeModules: resolve from DI and execute lifecycle hooks with diagnostics
        sb.AppendLine("        public static async Task<IHost> InitializeModulesAsync(this IHost host) {");
        sb.AppendLine("            var logger = host.Services.GetService<ILogger<IModule>>();");
        sb.AppendLine("            var descriptors = ModuleDescriptorRegistry.GetDescriptors();");
        sb.AppendLine();

        // PreInit
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                // ModuleDiagnostics: descriptor.ModuleType.Name → PreInit");
        sb.AppendLine("                var _preInitTimer = ModulePhaseTimer.StartNew(descriptor.ModuleType.Name, \"PreInit\");");
        sb.AppendLine("                try {");
        sb.AppendLine("                    await ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnPreInitializeAsync();");
        sb.AppendLine("                    _diagnostics.Record(_preInitTimer.Stop(ModulePhaseStatus.Success));");
        sb.AppendLine("                } catch (Exception ex) {");
        sb.AppendLine("                    _diagnostics.Record(_preInitTimer.StopFailed(ex));");
        sb.AppendLine("                    throw;");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();

        // Init
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                // ModuleDiagnostics: descriptor.ModuleType.Name → Init");
        sb.AppendLine("                var _initTimer = ModulePhaseTimer.StartNew(descriptor.ModuleType.Name, \"Init\");");
        sb.AppendLine("                try {");
        sb.AppendLine("                    await ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnInitializeAsync();");
        sb.AppendLine("                    _diagnostics.Record(_initTimer.Stop(ModulePhaseStatus.Success));");
        sb.AppendLine("                } catch (Exception ex) {");
        sb.AppendLine("                    _diagnostics.Record(_initTimer.StopFailed(ex));");
        sb.AppendLine("                    throw;");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();

        // PostInit
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                // ModuleDiagnostics: descriptor.ModuleType.Name → PostInit");
        sb.AppendLine("                var _postInitTimer = ModulePhaseTimer.StartNew(descriptor.ModuleType.Name, \"PostInit\");");
        sb.AppendLine("                try {");
        sb.AppendLine("                    await ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnPostInitializeAsync();");
        sb.AppendLine("                    _diagnostics.Record(_postInitTimer.Stop(ModulePhaseStatus.Success));");
        sb.AppendLine("                } catch (Exception ex) {");
        sb.AppendLine("                    _diagnostics.Record(_postInitTimer.StopFailed(ex));");
        sb.AppendLine("                    throw;");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();

        // AppInit
        sb.AppendLine("            foreach (var descriptor in descriptors) {");
        sb.AppendLine("                // ModuleDiagnostics: descriptor.ModuleType.Name → AppInit");
        sb.AppendLine("                var _appInitTimer = ModulePhaseTimer.StartNew(descriptor.ModuleType.Name, \"AppInit\");");
        sb.AppendLine("                try {");
        sb.AppendLine("                    await ((IModule)host.Services.GetRequiredService(descriptor.ModuleType)).OnApplicationInitializationAsync(host);");
        sb.AppendLine("                    _diagnostics.Record(_appInitTimer.Stop(ModulePhaseStatus.Success));");
        sb.AppendLine("                } catch (Exception ex) {");
        sb.AppendLine("                    _diagnostics.Record(_appInitTimer.StopFailed(ex));");
        sb.AppendLine("                    throw;");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();

        // Summary log output
        sb.AppendLine("            // ModuleDiagnostics Summary");
        sb.AppendLine("            if (logger != null)");
        sb.AppendLine("            {");
        sb.AppendLine("                foreach (var diag in _diagnostics.GetAll())");
        sb.AppendLine("                {");
        sb.AppendLine("                    var status = diag.Status == ModulePhaseStatus.Success ? \"OK\" : \"FAILED\";");
        sb.AppendLine("                    if (diag.Status == ModulePhaseStatus.Failed)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        logger.LogError(\"[ModuleDiagnostics] {Module} → {Phase}: {Status} ({Elapsed}ms) Error: {Error}\",");
        sb.AppendLine("                            diag.ModuleName, diag.Phase, status, diag.Elapsed.TotalMilliseconds.ToString(\"F2\"), diag.ErrorMessage);");
        sb.AppendLine("                    }");
        sb.AppendLine("                    else");
        sb.AppendLine("                    {");
        sb.AppendLine("                        logger.LogInformation(\"[ModuleDiagnostics] {Module} → {Phase}: {Status} ({Elapsed}ms)\",");
        sb.AppendLine("                            diag.ModuleName, diag.Phase, status, diag.Elapsed.TotalMilliseconds.ToString(\"F2\"));");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
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
        public ModuleInfo(string name, string ns, List<string> dependsOn, int order, bool autoRegisterServices, bool hasParameterlessConstructor)
        {
            Name = name;
            Namespace = ns;
            DependsOn = dependsOn;
            Order = order;
            AutoRegisterServices = autoRegisterServices;
            HasParameterlessConstructor = hasParameterlessConstructor;
        }

        public string Name { get; }
        public string Namespace { get; }
        public List<string> DependsOn { get; }
        public int Order { get; }
        public bool AutoRegisterServices { get; }
        public bool HasParameterlessConstructor { get; set; }
        public string FullName => $"{Namespace}.{Name}";
    }
}
