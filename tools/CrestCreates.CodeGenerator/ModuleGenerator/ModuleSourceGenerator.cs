using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.ModuleGenerator
{
    [Generator]
    public class ModuleSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 创建增量数据源：查找带有ModuleAttribute的类
            var moduleClassesProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsModuleCandidate(node),
                    transform: static (ctx, _) => GetModuleClass(ctx))
                .Where(static m => m != null);

            var moduleClasses = moduleClassesProvider.Collect();

            // 注册源代码生成
            context.RegisterSourceOutput(moduleClasses, ExecuteGeneration);
        }

        private static bool IsModuleCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration &&
                   classDeclaration.AttributeLists.Count > 0;
        }

        private static INamedTypeSymbol? GetModuleClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol != null && HasModuleAttribute(symbol))
            {
                return symbol;
            }

            return null;
        }
        private static bool HasModuleAttribute(INamedTypeSymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "ModuleAttribute" ||
                attr.AttributeClass?.Name == "Module");
        }
        private static void ExecuteGeneration(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> moduleClasses)
        {
            if (moduleClasses.IsDefaultOrEmpty)
                return;

            // 筛选并去重，确保类型安全
            var uniqueModules = new List<INamedTypeSymbol>();

            foreach (var moduleClass in moduleClasses)
            {
                if (moduleClass != null && !uniqueModules.Contains(moduleClass, SymbolEqualityComparer.Default))
                {
                    uniqueModules.Add(moduleClass);
                }
            }

            // 生成模块注册代码
            if (uniqueModules.Count > 0)
            {
                GenerateModuleRegistration(context, uniqueModules);
            }
        }
        private static void GenerateModuleRegistration(SourceProductionContext context, List<INamedTypeSymbol> moduleClasses)
        {
            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            builder.AppendLine("using Microsoft.Extensions.Hosting;");
            builder.AppendLine();
            builder.AppendLine("namespace CrestCreates.Infrastructure.Modularity");
            builder.AppendLine("{");
            builder.AppendLine("    public static class AutoModuleRegistration");
            builder.AppendLine("    {");
            builder.AppendLine("        public static IHostBuilder RegisterModules(this IHostBuilder hostBuilder)");
            builder.AppendLine("        {");
            builder.AppendLine("            return hostBuilder.ConfigureServices((context, services) =>");
            builder.AppendLine("            {");

            // 创建一个表示模块依赖关系的图
            var moduleGraph = BuildModuleDependencyGraph(moduleClasses);

            // 按照拓扑排序顺序初始化模块
            var sortedModules = TopologicalSort(moduleGraph);

            foreach (var module in sortedModules)
            {
                var moduleName = module.ToDisplayString();
                builder.AppendLine($"                // 初始化 {module.Name} 模块");
                builder.AppendLine($"                services.AddSingleton<{moduleName}>(new {moduleName}());");
                builder.AppendLine($"                var module{module.Name} = services.BuildServiceProvider().GetRequiredService<{moduleName}>();");
                builder.AppendLine($"                module{module.Name}.PreInitialize();");
                builder.AppendLine($"                module{module.Name}.Initialize();");
                builder.AppendLine($"                module{module.Name}.PostInitialize();");
                builder.AppendLine();
            }

            builder.AppendLine("                // 配置服务");
            foreach (var module in sortedModules)
            {
                builder.AppendLine($"                services.BuildServiceProvider().GetRequiredService<{module.ToDisplayString()}>().ConfigureServices(services);");
            }

            builder.AppendLine("            });");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        public static IHost InitializeModules(this IHost host)");
            builder.AppendLine("        {");
            builder.AppendLine("            var serviceProvider = host.Services;");

            foreach (var module in sortedModules)
            {
                builder.AppendLine($"            serviceProvider.GetRequiredService<{module.ToDisplayString()}>().OnApplicationInitialization(host);");
            }

            builder.AppendLine("            return host;");
            builder.AppendLine("        }");

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource("AutoModuleRegistration.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }
        private static Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> BuildModuleDependencyGraph(List<INamedTypeSymbol> moduleClasses)
        {
            var comparer = SymbolEqualityComparer.Default;
            var graph = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(comparer);

            foreach (var module in moduleClasses)
            {
                if (module == null) continue;

                graph[module] = new List<INamedTypeSymbol>();

                // 获取模块依赖
                var dependsOnAttr = module.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name?.Contains("Module") == true);

                if (dependsOnAttr != null)
                {
                    var dependsOn = dependsOnAttr.NamedArguments
                        .FirstOrDefault(a => a.Key == "DependsOn").Value.Values;

                    if (dependsOn != null)
                    {
                        foreach (var dependency in dependsOn)
                        {
                            if (dependency.Value is INamedTypeSymbol dependencyType)
                            {
                                var dependencyModule = moduleClasses.FirstOrDefault(m => comparer.Equals(m, dependencyType));
                                if (dependencyModule != null)
                                {
                                    graph[module].Add(dependencyModule);
                                }
                            }
                        }
                    }
                }
            }

            return graph;
        }

        private static List<INamedTypeSymbol> TopologicalSort(Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> graph)
        {
            var comparer = SymbolEqualityComparer.Default;
            var visited = new HashSet<INamedTypeSymbol>(comparer);
            var sorted = new List<INamedTypeSymbol>();

            foreach (var module in graph.Keys)
            {
                if (!visited.Contains(module))
                {
                    TopologicalSortUtil(module, visited, sorted, graph);
                }
            }

            return sorted;
        }
        private static void TopologicalSortUtil(
            INamedTypeSymbol module,
            HashSet<INamedTypeSymbol> visited,
            List<INamedTypeSymbol> sorted,
            Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> graph)
        {
            visited.Add(module);

            if (module != null && graph.ContainsKey(module))
            {
                foreach (var dependency in graph[module])
                {
                    if (dependency != null && !visited.Contains(dependency))
                    {
                        TopologicalSortUtil(dependency, visited, sorted, graph);
                    }
                }
            }

            if (module != null)
            {
                sorted.Add(module);
            }
        }
    }    // ModuleSyntaxReceiver 类已被移除，因为我们已经使用 IIncrementalGenerator 的语法提供程序模式
}
