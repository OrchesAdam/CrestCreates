using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CrestCreates.DependencyInjection;
using CrestCreates.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.SourceGenerator;

public abstract class CommonIncrementalGenerator : IIncrementalGenerator
{
    #region 抽象方法 - 需要子类实现

    /// <summary>
    /// 获取目标Attribute的类型
    /// </summary>
    protected abstract Type GetAttributeType();
    
    protected abstract Type[] OptionalAttributeTypes { get; }

    /// <summary>
    /// 语法节点过滤逻辑
    /// </summary>
    protected abstract bool FilterSyntax(SyntaxNode node, CancellationToken token);

    /// <summary>
    /// 转换语法节点为语义模型
    /// </summary>
    protected abstract object GetSemanticModel(GeneratorAttributeSyntaxContext context, CancellationToken token);

    /// <summary>
    /// 生成源代码内容
    /// </summary>
    internal abstract void GenerateCode(SourceProductionContext context, ImmutableArray<ClassAnalysisInfo> model);

    #endregion

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 注册初始化后操作
        context.RegisterPostInitializationOutput(PostInitialization);
        // 查找所有类并进行调试
        var allClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (node, _) => node is ClassDeclarationSyntax,
                transform: (ctx, _) => AnalyzeClass(ctx))
            .Where(static info => info is not null);

        // 过滤出有服务属性的类
        var serviceClasses = allClasses.Where(static info => info!.HasServiceAttributes);

        // 收集编译信息
        var compilationAndServices = context.CompilationProvider.Combine(serviceClasses.Collect());
        
        // 生成服务注册代码
        context.RegisterSourceOutput(compilationAndServices,
            static (spc, source) => GenerateCode(spc, source.Right));

    }

    #region 虚方法 - 可选重写

    /// <summary>
    /// 初始化后操作（用于添加Attribute定义等）
    /// </summary>
    protected virtual void PostInitialization(IncrementalGeneratorPostInitializationContext context)
    {
        // 默认添加目标Attribute的定义
        context.AddSource($"{GetAttributeType().Name}.g.cs",
            $$"""
              namespace {{GetAttributeType().Namespace}}
              {
                  [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = false)]
                  internal sealed class {{GetAttributeType().Name}} : global::System.Attribute
                  {
                      public {{GetAttributeType().Name}}() { }
                  }
              }
              """);
    }

    #endregion

    private ClassAnalysisInfo? AnalyzeClass(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
        {
            return null;
        }

        var attributes = classSymbol.GetAttributes();
        var serviceAttributes = new List<string>();
        var hasServiceAttributes = false;

        foreach (var attr in attributes)
        {
            var attrName = attr.AttributeClass?.Name ?? "Unknown";
            serviceAttributes.Add(attrName);

            if (IsServiceAttributeClass(attr.AttributeClass))
            {
                hasServiceAttributes = true;
            }
        }

        return new ClassAnalysisInfo(
            classSymbol.ToDisplayString(),
            classSymbol.Name,
            hasServiceAttributes,
            serviceAttributes,
            hasServiceAttributes ? GetServiceInfo(context) : null
        );
    }

    private bool IsServiceAttributeClass(INamedTypeSymbol? attributeClass)
    {
        if (attributeClass == null)
        {
            return false;
        }

        // 直接检查已知的服务属性
        if (OptionalAttributeTypes.Select(x => x.Name).Contains(attributeClass.Name))
        {
            return true;
        }

        // 检查是否继承自ServiceAttribute
        var current = attributeClass.BaseType;
        while (current != null)
        {
            if (current.Name == nameof(ServiceAttribute))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private static ServiceDescriptorInfo? GetServiceInfo(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol is null || classSymbol.IsAbstract)
        {
            return null;
        }

        if (classSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            {
                return null;
            }
        }

        // 检查是否被标记为忽略
        if (HasIgnoreAttribute(namedTypeSymbol))
        {
            return null;
        }

        // 获取所有服务属性（包括继承的）
        var serviceAttributes = GetAllServiceAttributes(namedTypeSymbol);
        if (!serviceAttributes.Any())
        {
            return null;
        }

        var constructors = GetValidConstructors(namedTypeSymbol);
        var primaryConstructor = SelectPrimaryConstructor(constructors);

        return new ServiceDescriptorInfo(
            classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            classSymbol.Name,
            classSymbol.ContainingNamespace.ToDisplayString(),
            serviceAttributes,
            primaryConstructor,
            GetImplementedInterfaces(namedTypeSymbol)
        );
    }

    private static bool HasIgnoreAttribute(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == nameof(IgnoreServiceAttribute));
    }

    private static List<ServiceAttributeInfo> GetAllServiceAttributes(INamedTypeSymbol classSymbol)
    {
        var attributes = new List<ServiceAttributeInfo>();

        foreach (var attr in classSymbol.GetAttributes())
        {
            var serviceAttributeInfo = TryGetServiceAttributeInfo(attr, classSymbol);
            if (serviceAttributeInfo != null)
            {
                attributes.Add(serviceAttributeInfo);
            }
        }

        return attributes;
    }

    private static ServiceAttributeInfo? TryGetServiceAttributeInfo(AttributeData attr,
        INamedTypeSymbol classSymbol)
    {
        var attributeClass = attr.AttributeClass;
        if (attributeClass == null)
        {
            return null;
        }

        // 检查是否是服务属性或其派生类
        var lifetime = GetLifetimeFromAttributeClass(attributeClass);
        if (lifetime == null)
        {
            return null;
        }

        var serviceType = GetServiceTypeFromAttribute(attr, classSymbol);
        var registerAsInterfaces = GetBoolProperty(attr, nameof(ServiceAttribute.RegisterAsImplementedInterfaces));
        var replace = GetBoolProperty(attr, nameof(ServiceAttribute.Replace));

        return new ServiceAttributeInfo(
            serviceType,
            lifetime,
            registerAsInterfaces,
            replace
        );
    }

    private static string? GetLifetimeFromAttributeClass(INamedTypeSymbol attributeClass)
    {
        // 检查具体的属性类型
        switch (attributeClass.Name)
        {
            case "SingletonAttribute":
                return "Singleton";
            case "ScopedAttribute":
                return "Scoped";
            case "TransientAttribute":
                return "Transient";
            default:
                // 检查是否继承自ServiceAttribute
                var baseType = attributeClass.BaseType;
                while (baseType != null)
                {
                    if (baseType.Name == "ServiceAttribute")
                    {
                        // 这是一个自定义的服务属性，尝试获取Lifetime属性
                        return TryGetLifetimeFromCustomAttribute(attributeClass);
                    }

                    baseType = baseType.BaseType;
                }

                return null;
        }
    }

    private static string? TryGetLifetimeFromCustomAttribute(INamedTypeSymbol attributeClass)
    {
        // 查找Lifetime属性
        var lifetimeProperty = attributeClass.GetMembers("Lifetime")
            .OfType<IPropertySymbol>()
            .FirstOrDefault();

        if (lifetimeProperty?.IsOverride == true)
        {
            // 这里需要更复杂的逻辑来获取重写的值
            // 简化处理，返回默认值
            return "Transient";
        }

        return null;
    }

    private static string GetServiceTypeFromAttribute(AttributeData attr, INamedTypeSymbol implementationType)
    {
        // 检查构造函数参数
        if (attr.ConstructorArguments.Length > 0)
        {
            var firstArg = attr.ConstructorArguments[0];
            if (firstArg.Value is ITypeSymbol typeSymbol)
            {
                return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        // 检查 ServiceType 属性
        var serviceTypeProperty = attr.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == nameof(ServiceAttribute.ServiceType));

        if (serviceTypeProperty.Value.Value is ITypeSymbol serviceType)
        {
            return serviceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // 默认使用实现类型
        return implementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool GetBoolProperty(AttributeData attr, string propertyName)
    {
        var property = attr.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == propertyName);

        return property.Value.Value is true;
    }

    private static List<ConstructorInfo> GetValidConstructors(INamedTypeSymbol classSymbol)
    {
        return classSymbol.Constructors
            .Where(c => c is { DeclaredAccessibility: Accessibility.Public, IsStatic: false })
            .Select(c =>
            {
                Debug.WriteLine($"Constructor: {c.Name}");
                var paraInfo = c.Parameters.Select(p => new ParameterInfo(
                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    p.Name,
                    p.HasExplicitDefaultValue,
                    p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
                )).ToList();
                return new ConstructorInfo(paraInfo);
            })
            .ToList();
    }

    private static ConstructorInfo? SelectPrimaryConstructor(List<ConstructorInfo> constructors)
    {
        if (!constructors.Any())
        {
            return null;
        }

        // 选择参数最多的构造函数（DI 约定）
        return constructors.OrderByDescending(c => c.Parameters.Count).First();
    }

    private static List<string> GetImplementedInterfaces(INamedTypeSymbol classSymbol)
    {
        return classSymbol.AllInterfaces
            .Where(i => i.DeclaredAccessibility == Accessibility.Public)
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToList();
    }
}