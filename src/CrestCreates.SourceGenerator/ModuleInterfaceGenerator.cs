using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using CrestCreates.CodeAnalyzer;
using CrestCreates.CodeAnalyzer.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.SourceGenerator;

/// <summary>
/// 模块接口源生成器，为标记了 ModuleInterfaceAttribute 的接口生成实现类。
/// </summary>
[Generator]
public class ModuleInterfaceGenerator : CommonIncrementalGenerator
{
    protected override string GeneratorName => "ModuleInterfaceGenerator";
    protected override string ClassAttributeName => "ModuleInterfaceAttribute";
    protected override string GeneratedClassName => "Generated";

    protected override string GetAttributeSource()
    {
        // ModuleInterfaceAttribute 已在 CrestCreates.Modularity 命名空间中定义
        return string.Empty;
    }

    protected override ClassAnalysisInfo? AnalyzeClass(GeneratorSyntaxContext context)
    {
        // 因为我们处理的是接口而不是类，所以需要修改逻辑
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

        // 查找接口上的 ModuleInterfaceAttribute 特性
        var moduleInterfaceAttribute = interfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == ClassAttributeName ||
                                   attr.AttributeClass?.Name == ClassAttributeName.Replace("Attribute", ""));

        // 如果没有找到特性，直接返回
        if (moduleInterfaceAttribute == null)
        {
            return null;
        }

        // 获取模块依赖项
        var dependencyTypes = new List<string>();
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

        // 获取配置类型
        string? configurationType = null;
        foreach (var namedArgument in moduleInterfaceAttribute.NamedArguments)
        {
            if (namedArgument.Key == "ConfigurationType" && namedArgument.Value.Value is ITypeSymbol configTypeSymbol)
            {
                configurationType = configTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        // 获取接口的所有成员方法
        var interfaceMethods = interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
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

        // 检查接口是否继承了特定的钩子接口
        var implementedInterfaces = interfaceSymbol.AllInterfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToList();

        // 创建分析信息
        return new ClassAnalysisInfo(
            interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            interfaceSymbol.Name,
            true, // 标记为服务
            new List<string> { "ModuleInterface" },
            new ServiceDescriptorInfo(
                interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                interfaceSymbol.Name,
                interfaceSymbol.ContainingNamespace.ToDisplayString(),
                new List<ServiceAttributeInfo>(),
                null, // 没有主构造函数
                new Dictionary<string, string>
                {
                    { "DependencyTypes", string.Join(",", dependencyTypes) },
                    { "ConfigurationType", configurationType ?? string.Empty },
                    { "Methods", JsonSerializer.Serialize(interfaceMethods) },
                    { "Interfaces", string.Join(",", implementedInterfaces) }
                },
                implementedInterfaces
            )
        );
    }

    protected override bool FilterClass(ClassAnalysisInfo? info)
    {
        if (info == null)
        {
            return false;
        }

        return info.AttributeNames.Contains("ModuleInterface");
    }

    protected override void Execute(Compilation compilation, ImmutableArray<ClassAnalysisInfo> infos, SourceProductionContext context)
    {
        if (!infos.Any())
        {
            return;
        }

        foreach (var info in infos)
        {
            var moduleInterfaceInfo = info.ServiceInfo;
            if (moduleInterfaceInfo == null)
            {
                continue;
            }

            // 获取有用的信息
            var interfaceName = info.ClassName;
            var implementationName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : $"{interfaceName}Impl";
            var namespaceName = moduleInterfaceInfo.Namespace;
            
            // 获取依赖类型和配置类型
            var dependencyTypesString = string.Empty;
            moduleInterfaceInfo.AdditionalData.TryGetValue("DependencyTypes", out var dependencyTypes);
            if (!string.IsNullOrEmpty(dependencyTypes))
            {
                var dependencyTypeList = dependencyTypes.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToArray();
                if (dependencyTypeList.Length > 0)
                {
                    dependencyTypesString = string.Join(",\n            ", dependencyTypeList.Select(t => $"typeof({t})"));
                }
            }

            // 获取配置类型
            moduleInterfaceInfo.AdditionalData.TryGetValue("ConfigurationType", out var configurationType);
            bool hasConfigurationType = !string.IsNullOrEmpty(configurationType);

            // 获取接口方法
            moduleInterfaceInfo.AdditionalData.TryGetValue("Methods", out var methodsJson);
            var methods = string.IsNullOrEmpty(methodsJson) 
                ? new List<MethodInfo>() 
                : System.Text.Json.JsonSerializer.Deserialize<List<MethodInfo>>(methodsJson) ?? new List<MethodInfo>();

            // 获取实现的接口
            moduleInterfaceInfo.AdditionalData.TryGetValue("Interfaces", out var interfacesString);
            var implementedInterfaces = string.IsNullOrEmpty(interfacesString) 
                ? new List<string>() 
                : interfacesString.Split(',').ToList();

            // 检查实现的特定接口（模块钩子）
            bool implementsPreInit = implementedInterfaces.Any(i => i.EndsWith("IOnPreApplicationInitialization"));
            bool implementsPostInit = implementedInterfaces.Any(i => i.EndsWith("IOnPostApplicationInitialization"));
            bool implementsPreShutdown = implementedInterfaces.Any(i => i.EndsWith("IOnPreApplicationShutdown"));
            bool implementsPostShutdown = implementedInterfaces.Any(i => i.EndsWith("IOnPostApplicationShutdown"));

            // 生成部分类实现代码
            var source = GeneratePartialClassImplementation(
                namespaceName,
                interfaceName,
                implementationName,
                dependencyTypesString,
                configurationType,
                hasConfigurationType,
                methods,
                implementsPreInit,
                implementsPostInit,
                implementsPreShutdown,
                implementsPostShutdown
            );

            // 添加生成的源代码
            context.AddSource($"{implementationName}.g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }

    private string GeneratePartialClassImplementation(
        string namespaceName,
        string interfaceName,
        string className,
        string dependsOnTypes,
        string? configurationType,
        bool hasConfigurationType,
        List<MethodInfo> methods,
        bool implementsPreInit,
        bool implementsPostInit,
        bool implementsPreShutdown,
        bool implementsPostShutdown)
    {
        var sb = new StringBuilder();
        
        // 添加文件头
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// 此文件由 ModuleInterfaceGenerator 自动生成，请勿直接修改");
        sb.AppendLine();

        // 添加必要的 using 引用
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using CrestCreates.Modularity;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        if (hasConfigurationType)
        {
            sb.AppendLine("using Microsoft.Extensions.Options;");
        }
        sb.AppendLine();

        // 命名空间开始
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 添加依赖特性
        sb.AppendLine("    [ModuleAttribute(");
        if (!string.IsNullOrEmpty(dependsOnTypes))
        {
            sb.AppendLine($"        {dependsOnTypes}");
        }
        sb.AppendLine("    )]");

        // 类定义开始
        sb.AppendLine($"    public partial class {className} : CrestCreatesModuleBase, {interfaceName}");
        sb.AppendLine("    {");

        // 如果有配置类型，添加配置属性
        if (hasConfigurationType)
        {
            sb.AppendLine($"        private readonly IServiceProvider _serviceProvider;");
            sb.AppendLine();
            sb.AppendLine($"        protected {configurationType}? Options => _serviceProvider.GetService<IOptions<{configurationType}>>()?.Value;");
            sb.AppendLine();
            sb.AppendLine($"        public {className}(IServiceProvider serviceProvider)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _serviceProvider = serviceProvider;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 模块配置服务方法
        sb.AppendLine("        public override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 模块基础服务配置");
        sb.AppendLine("            base.ConfigureServices(services);");
        sb.AppendLine();
        if (hasConfigurationType)
        {
            sb.AppendLine($"            // 注册配置选项");
            sb.AppendLine($"            services.AddOptions<{configurationType}>();");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // 实现钩子方法
        // 前置初始化钩子
        if (implementsPreInit)
        {
            sb.AppendLine("        public virtual void OnPreApplicationInitialization()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 前置初始化钩子默认实现");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public virtual Task OnPreApplicationInitializationAsync()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 前置初始化异步钩子默认实现");
            sb.AppendLine("            return Task.CompletedTask;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 后置初始化钩子
        if (implementsPostInit)
        {
            sb.AppendLine("        public virtual void OnPostApplicationInitialization()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 后置初始化钩子默认实现");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public virtual Task OnPostApplicationInitializationAsync()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 后置初始化异步钩子默认实现");
            sb.AppendLine("            return Task.CompletedTask;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 前置关闭钩子
        if (implementsPreShutdown)
        {
            sb.AppendLine("        public virtual void OnPreApplicationShutdown()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 前置关闭钩子默认实现");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public virtual Task OnPreApplicationShutdownAsync()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 前置关闭异步钩子默认实现");
            sb.AppendLine("            return Task.CompletedTask;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 后置关闭钩子
        if (implementsPostShutdown)
        {
            sb.AppendLine("        public virtual void OnPostApplicationShutdown()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 后置关闭钩子默认实现");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public virtual Task OnPostApplicationShutdownAsync()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 后置关闭异步钩子默认实现");
            sb.AppendLine("            return Task.CompletedTask;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 实现用户定义的接口方法
        foreach (var method in methods)
        {
            var asyncModifier = method.IsAsync ? "async " : "";
            var returnType = method.ReturnType;
            var methodName = method.Name;
            
            // 参数列表
            var parameterList = string.Join(", ", method.Parameters.Select(p => {
                var refKind = p.RefKind != "None" ? p.RefKind?.ToLowerInvariant() + " " : "";
                var defaultValue = p.HasDefaultValue ? " = " + p.DefaultValue : "";
                return $"{refKind}{p.Type} {p.Name}{defaultValue}";
            }));
            
            // 返回值 
            var returnStatement = returnType == "System.Void" || returnType == "void"
                ? ""
                : returnType.EndsWith("Task") 
                    ? "return Task.CompletedTask;" 
                    : "return default;";
                
            sb.AppendLine($"        public {asyncModifier}virtual {returnType} {methodName}({parameterList})");
            sb.AppendLine("        {");
            sb.AppendLine($"            // 自动生成的方法实现，可在部分类中重写");
            if (!string.IsNullOrEmpty(returnStatement))
            {
                sb.AppendLine($"            {returnStatement}");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 类结束
        sb.AppendLine("    }");

        // 命名空间结束
        sb.AppendLine("}");

        return sb.ToString();
    }

    protected override string GenerateDebugInfo()
    {
        return GenerateDefaultDebugInfo("CrestCreates.Generated.ModuleInterfaces");
    }

    protected override void GenerateDebugReport(Compilation compilation, ImmutableArray<ClassAnalysisInfo?> infos, SourceProductionContext context)
    {
        var validInfos = infos.Where(c => c != null).Cast<ClassAnalysisInfo>().ToList();
        var moduleInterfaceInfos = validInfos.Where(c => c.AttributeNames.Contains("ModuleInterface")).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Debug report for module interface generator");
        sb.AppendLine();
        sb.AppendLine("/*");
        sb.AppendLine($"Total interfaces analyzed: {validInfos.Count}");
        sb.AppendLine($"Module interfaces found: {moduleInterfaceInfos.Count}");
        sb.AppendLine();
        sb.AppendLine("Module interfaces:");

        foreach (var moduleInterface in moduleInterfaceInfos)
        {
            sb.AppendLine($"  - {moduleInterface.ClassName} ({moduleInterface.FullName})");
            if (moduleInterface.ServiceInfo?.AdditionalData != null)
            {
                moduleInterface.ServiceInfo.AdditionalData.TryGetValue("DependencyTypes", out var dependencyTypes);
                moduleInterface.ServiceInfo.AdditionalData.TryGetValue("ConfigurationType", out var configurationType);
                
                sb.AppendLine($"    ConfigurationType: {configurationType ?? "None"}");
                sb.AppendLine($"    DependencyTypes: {dependencyTypes ?? "None"}");
                
                if (moduleInterface.ServiceInfo.ImplementedInterfaces != null)
                {
                    sb.AppendLine($"    Implemented Interfaces: {string.Join(", ", moduleInterface.ServiceInfo.ImplementedInterfaces)}");
                }
            }
        }

        sb.AppendLine("*/");

        context.AddSource($"{GeneratorName}DebugReport.g.cs", sb.ToString());
    }
}