using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.SourceGenerator;

/// <summary>
/// 模块接口源生成器，为继承了 ICrestCreatesModule 的接口生成实现类。
/// </summary>
[Generator]
public class ModuleInterfaceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG
        // 添加调试信息生成
        context.RegisterPostInitializationOutput(ctx =>
            ctx.AddSource("ModuleInterfaceGeneratorDebug.g.cs", GenerateDebugInfo()));
#endif

        // 查找所有接口并进行分析
        var interfaceProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, _) => AnalyzeInterface(ctx))
            .Where(static info => info is not null);

        // 收集编译信息
        var compilationAndInterfaces = context.CompilationProvider.Combine(interfaceProvider.Collect());

#if DEBUG
        // 生成调试报告
        context.RegisterSourceOutput(compilationAndInterfaces,
            static (spc, source) => GenerateDebugReport(source.Left, source.Right, spc));
#endif

        // 生成代码
        context.RegisterSourceOutput(compilationAndInterfaces,
            static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static InterfaceInfo? AnalyzeInterface(GeneratorSyntaxContext context)
    {
        // 我们处理的是接口，检查是否为接口声明
        if (context.Node is not InterfaceDeclarationSyntax interfaceDeclaration)
        {
            return null;
        }

        // 获取语义模型和符号
        var semanticModel = context.SemanticModel;
        if (semanticModel.GetDeclaredSymbol(interfaceDeclaration) is not INamedTypeSymbol interfaceSymbol)
        {
            return null;
        }

        // 检查接口是否继承了 ICrestCreatesModule
        var inheritsICrestCreatesModule = interfaceSymbol.AllInterfaces.Any(i => i.Name == "ICrestCreatesModule");
        if (!inheritsICrestCreatesModule)
        {
            return null;
        }        // 获取 ModuleInterfaceAttribute 特性（如果存在）
        var moduleInterfaceAttribute = interfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "ModuleInterfaceAttribute" ||
                                   attr.AttributeClass?.Name == "ModuleInterface");

        // 获取 DependsOnAttribute 特性（如果存在）
        var dependsOnAttribute = interfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "DependsOnAttribute" ||
                                   attr.AttributeClass?.Name == "DependsOn");

        // 获取模块依赖项
        var dependencyTypes = new List<string>();
        string? configurationType = null;

        // 从 DependsOnAttribute 获取依赖项（优先级更高）
        if (dependsOnAttribute != null && dependsOnAttribute.ConstructorArguments.Any())
        {
            var dependencies = dependsOnAttribute.ConstructorArguments.First();
            if (dependencies.Kind == TypedConstantKind.Array && dependencies.Values.Any())
            {
                foreach (var dep in dependencies.Values)
                {
                    if (dep.Value is ITypeSymbol typeSymbol)
                    {
                        dependencyTypes.Add(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
            }
        }        // 如果没有 DependsOnAttribute，尝试从 ModuleInterfaceAttribute 获取依赖项（向后兼容）
        else if (moduleInterfaceAttribute != null)
        {
            // 从特性构造函数参数获取依赖项
            if (moduleInterfaceAttribute.ConstructorArguments.Any())
            {
                var dependencies = moduleInterfaceAttribute.ConstructorArguments.First();
                if (dependencies.Kind == TypedConstantKind.Array && dependencies.Values.Any())
                {
                    foreach (var dep in dependencies.Values)
                    {
                        if (dep.Value is ITypeSymbol typeSymbol)
                        {
                            dependencyTypes.Add(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        }
                    }
                }
            }
        }

        // 从 ModuleInterfaceAttribute 的命名参数获取配置类型
        if (moduleInterfaceAttribute != null)
        {
            foreach (var namedArgument in moduleInterfaceAttribute.NamedArguments)
            {
                if (namedArgument.Key == "ConfigurationType" && namedArgument.Value.Value is ITypeSymbol configTypeSymbol)
                {
                    configurationType = configTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        // 获取接口中定义的自定义方法（排除从 ICrestCreatesModule 继承的方法）
        var customMethods = interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => !IsICrestCreatesModuleMethod(m))
            .Select(m => new MethodInfo(
                m.Name,
                m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                m.IsAsync,
                m.Parameters.Select(p => new ParameterInfo(
                    p.Name,
                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    p.RefKind.ToString(),
                    p.HasExplicitDefaultValue,
                    p.ExplicitDefaultValue?.ToString() ?? "null"
                )).ToList()
            ))
            .ToList();

        return new InterfaceInfo(
            interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            interfaceSymbol.Name,
            interfaceSymbol.ContainingNamespace.ToDisplayString(),
            dependencyTypes,
            configurationType,
            customMethods
        );
    }

    /// <summary>
    /// 检查方法是否是从 ICrestCreatesModule 继承的方法
    /// </summary>
    private static bool IsICrestCreatesModuleMethod(IMethodSymbol method)
    {
        var crestModuleMethods = new[]
        {
            // 核心模块方法
            "ConfigureServices",
            "Initialize", 
            "Shutdown",
            // 生命周期钩子方法
            "OnPreApplicationInitialization",
            "OnPreApplicationInitializationAsync",
            "OnPostApplicationInitialization", 
            "OnPostApplicationInitializationAsync",
            "OnPreApplicationShutdown",
            "OnPreApplicationShutdownAsync",
            "OnPostApplicationShutdown",
            "OnPostApplicationShutdownAsync"
        };

        return crestModuleMethods.Contains(method.Name);
    }

    private static void Execute(Compilation compilation, ImmutableArray<InterfaceInfo?> infos, SourceProductionContext context)
    {
        var validInfos = infos.Where(i => i != null).Cast<InterfaceInfo>().ToArray();
        
        if (!validInfos.Any())
        {
            return;
        }

        foreach (var info in validInfos)
        {
            // 获取有用的信息
            var interfaceName = info.InterfaceName;
            var implementationName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : $"{interfaceName}Impl";
            var namespaceName = info.Namespace;
            
            // 获取依赖类型
            var dependencyTypesString = string.Empty;
            if (info.DependencyTypes.Count > 0)
            {
                dependencyTypesString = string.Join(",\n            ", info.DependencyTypes.Select(t => $"typeof({t})"));
            }

            // 生成部分类实现代码
            var source = GeneratePartialClassImplementation(
                namespaceName,
                interfaceName,
                implementationName,
                dependencyTypesString,
                info.ConfigurationType,
                !string.IsNullOrEmpty(info.ConfigurationType),
                info.CustomMethods
            );

            // 添加生成的源代码
            context.AddSource($"{implementationName}.g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string GeneratePartialClassImplementation(
        string namespaceName,
        string interfaceName,
        string className,
        string dependsOnTypes,
        string? configurationType,
        bool hasConfigurationType,
        List<MethodInfo> customMethods)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Options;");
        sb.AppendLine("using CrestCreates.Modularity;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// 自动生成的 {interfaceName} 实现类");
        sb.AppendLine($"    /// </summary>");
        
        // 添加 DependsOn 特性（如果有依赖）
        if (!string.IsNullOrEmpty(dependsOnTypes))
        {
            sb.AppendLine($"    [DependsOn(");
            sb.AppendLine($"        {dependsOnTypes}");
            sb.AppendLine($"    )]");
        }
        
        sb.AppendLine($"    public partial class {className} : CrestCreatesModuleBase, {interfaceName}");
        sb.AppendLine("    {");

        // 添加配置字段（如果有配置类型）
        if (hasConfigurationType && !string.IsNullOrEmpty(configurationType))
        {
            sb.AppendLine($"        private readonly {configurationType}? _configuration;");
            sb.AppendLine();
        }        // 添加构造函数
        sb.AppendLine("        public " + className + "(IServiceProvider serviceProvider)");
        sb.AppendLine("        {");
        if (hasConfigurationType && !string.IsNullOrEmpty(configurationType))
        {
            sb.AppendLine($"            _configuration = serviceProvider.GetService<IOptions<{configurationType}>>()?.Value;");
        }
        sb.AppendLine("        }");

        // 为自定义方法生成虚拟实现
        foreach (var method in customMethods)
        {
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// {method.Name} 的默认实现");
            sb.AppendLine($"        /// </summary>");
            
            var parametersString = string.Join(", ", method.Parameters.Select(p => 
                $"{(string.IsNullOrEmpty(p.RefKind) || p.RefKind == "None" ? "" : p.RefKind.ToLower() + " ")}{p.Type} {p.Name}"));
              var returnType = method.ReturnType;
            var isAsync = method.IsAsync || returnType.Contains("Task");
            var asyncKeyword = isAsync ? "async " : "";
            var virtualKeyword = "virtual";

            sb.AppendLine($"        public {virtualKeyword} {asyncKeyword}{returnType} {method.Name}({parametersString})");
            sb.AppendLine("        {");
              if (returnType == "void")
            {
                sb.AppendLine($"            // TODO: 实现 {method.Name} 方法");
            }
            else if (returnType.Contains("Task") && !returnType.Contains("Task<"))
            {
                sb.AppendLine($"            // TODO: 实现 {method.Name} 方法");
                if (isAsync)
                {
                    sb.AppendLine("            await Task.CompletedTask;");
                }
                else
                {
                    sb.AppendLine("            return Task.CompletedTask;");
                }
            }
            else if (returnType.Contains("Task<"))
            {
                var genericType = returnType.Substring(returnType.IndexOf('<') + 1, returnType.LastIndexOf('>') - returnType.IndexOf('<') - 1);
                sb.AppendLine($"            // TODO: 实现 {method.Name} 方法");
                if (isAsync)
                {
                    sb.AppendLine($"            return default({genericType})!;");
                }
                else
                {
                    sb.AppendLine($"            return Task.FromResult(default({genericType})!);");
                }
            }
            else
            {
                sb.AppendLine($"            // TODO: 实现 {method.Name} 方法");
                sb.AppendLine($"            return default({returnType})!;");
            }
            
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateDebugInfo()
    {
        return $@"// <auto-generated/>
// This file contains debug information for the ModuleInterfaceGenerator

using System;

namespace CrestCreates.Generated.ModuleInterfaces
{{
    public static class ModuleInterfaceGeneratorDebugInfo
    {{
        public static readonly string GeneratedAt = ""{DateTimeOffset.Now}"";
        public static readonly string Message = ""ModuleInterfaceGenerator is working"";
    }}
}}";
    }

    private static void GenerateDebugReport(Compilation compilation, ImmutableArray<InterfaceInfo?> infos, SourceProductionContext context)
    {
        var validInfos = infos.Where(c => c != null).Cast<InterfaceInfo>().ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Debug report for module interface generator");
        sb.AppendLine();
        sb.AppendLine("/*");
        sb.AppendLine($"Total interfaces analyzed: {validInfos.Count}");
        sb.AppendLine($"Module interfaces found: {validInfos.Count}");
        sb.AppendLine();
        sb.AppendLine("Module interfaces:");

        foreach (var moduleInterface in validInfos)
        {
            sb.AppendLine($"  - {moduleInterface.InterfaceName} ({moduleInterface.FullName})");
            sb.AppendLine($"    ConfigurationType: {moduleInterface.ConfigurationType ?? "None"}");
            sb.AppendLine($"    DependencyTypes: {string.Join(", ", moduleInterface.DependencyTypes)}");
            sb.AppendLine($"    Custom Methods: {moduleInterface.CustomMethods.Count}");
            
            foreach (var method in moduleInterface.CustomMethods)
            {
                sb.AppendLine($"      - {method.ReturnType} {method.Name}({string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"))})");
            }
        }

        sb.AppendLine("*/");

        context.AddSource("ModuleInterfaceGeneratorDebugReport.g.cs", sb.ToString());
    }
}

// 数据模型