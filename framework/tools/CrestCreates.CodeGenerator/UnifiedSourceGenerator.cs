using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using CrestCreates.CodeGenerator.Models;

namespace CrestCreates.CodeGenerator
{
    /// <summary>
    /// 统一的源代码生成器
    /// 整合所有生成逻辑，根据 [GenerateEntity] 特性统一生成相关代码
    /// </summary>
    [Generator]
    public class UnifiedSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 创建增量数据源：查找带有 GenerateEntity 属性的类
            var entityClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsEntityCandidate(node),
                    transform: static (ctx, _) => GetEntityClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            // 注册源代码生成
            context.RegisterSourceOutput(entityClasses, ExecuteGeneration);
        }

        /// <summary>
        /// 判断节点是否为候选的实体类（带有属性的类声明）
        /// </summary>
        private static bool IsEntityCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration &&
                   classDeclaration.AttributeLists.Count > 0;
        }

        /// <summary>
        /// 检查类是否具有 GenerateEntity 属性
        /// </summary>
        private static bool HasGenerateEntityAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "GenerateEntityAttribute" ||
                    attr.AttributeClass.Name == "GenerateEntity" ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateEntityAttribute") ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateEntity")
                ));
        }

        /// <summary>
        /// 获取带有 GenerateEntity 属性的类符号
        /// </summary>
        private static EntityInfo? GetEntityClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol != null && HasGenerateEntityAttribute(symbol))
            {
                return BuildEntityInfo(symbol);
            }

            return null;
        }

        /// <summary>
        /// 构建 EntityInfo 对象
        /// </summary>
        private static EntityInfo BuildEntityInfo(INamedTypeSymbol symbol)
        {
            var entityInfo = new EntityInfo
            {
                Name = symbol.Name,
                Namespace = symbol.ContainingNamespace.ToDisplayString(),
                IdType = GetEntityIdType(symbol),
                Properties = GetEntityProperties(symbol),
                BaseClass = GetBaseClassInfo(symbol),
                IsFullyAudited = IsFullyAudited(symbol)
            };

            // 读取特性配置
            ReadAttributeConfiguration(symbol, entityInfo);

            return entityInfo;
        }

        /// <summary>
        /// 读取特性配置
        /// </summary>
        private static void ReadAttributeConfiguration(INamedTypeSymbol symbol, EntityInfo entityInfo)
        {
            var attribute = symbol.GetAttributes().FirstOrDefault(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "GenerateEntityAttribute" ||
                    attr.AttributeClass.Name == "GenerateEntity"));

            if (attribute == null) return;

            // 读取各配置项
            entityInfo.GenerateRepository = GetAttributeBooleanValue(attribute, "GenerateRepository", true);
            entityInfo.GenerateRepositoryInterface = GetAttributeBooleanValue(attribute, "GenerateRepositoryInterface", true);
            entityInfo.GenerateRepositoryImplementation = GetAttributeBooleanValue(attribute, "GenerateRepositoryImplementation", true);
            entityInfo.OrmProvider = GetAttributeStringValue(attribute, "OrmProvider", "EfCore");
            entityInfo.GenerateCrudService = GetAttributeBooleanValue(attribute, "GenerateCrudService", true);
            entityInfo.GenerateDto = GetAttributeBooleanValue(attribute, "GenerateDto", true);
            entityInfo.ExcludeProperties = GetAttributeStringArrayValue(attribute, "ExcludeProperties");
            entityInfo.GenerateQueryExtensions = GetAttributeBooleanValue(attribute, "GenerateQueryExtensions", true);
            entityInfo.FilterableProperties = GetAttributeStringArrayValue(attribute, "FilterableProperties");
            entityInfo.SortableProperties = GetAttributeStringArrayValue(attribute, "SortableProperties");
            entityInfo.GenerateController = GetAttributeBooleanValue(attribute, "GenerateController", false);
            entityInfo.ControllerRoute = GetAttributeStringValue(attribute, "ControllerRoute", null);
            entityInfo.GenerateAsBaseClass = GetAttributeBooleanValue(attribute, "GenerateAsBaseClass", true);
            entityInfo.EnableTransaction = GetAttributeBooleanValue(attribute, "EnableTransaction", true);
            entityInfo.EnableLogging = GetAttributeBooleanValue(attribute, "EnableLogging", true);
            entityInfo.EnableValidation = GetAttributeBooleanValue(attribute, "EnableValidation", true);
            entityInfo.EnableCaching = GetAttributeBooleanValue(attribute, "EnableCaching", false);
            // CustomMoAttributes 需要特殊处理，这里暂不实现
        }

        /// <summary>
        /// 获取特性的布尔值
        /// </summary>
        private static bool GetAttributeBooleanValue(AttributeData attribute, string propertyName, bool defaultValue)
        {
            var namedArgument = attribute.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
            if (namedArgument.Value.Value != null && namedArgument.Value.Value is bool value)
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取特性的字符串值
        /// </summary>
        private static string? GetAttributeStringValue(AttributeData attribute, string propertyName, string? defaultValue)
        {
            var namedArgument = attribute.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
            if (namedArgument.Value.Value != null && namedArgument.Value.Value is string value)
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取特性的字符串数组值
        /// </summary>
        private static string[]? GetAttributeStringArrayValue(AttributeData attribute, string propertyName)
        {
            var namedArgument = attribute.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
            if (namedArgument.Value.Value != null && namedArgument.Value.Values.Length > 0)
            {
                return namedArgument.Value.Values
                    .Select(v => v.Value?.ToString())
                    .Where(v => v != null)
                    .Cast<string>()
                    .ToArray();
            }
            return null;
        }

        /// <summary>
        /// 获取实体 ID 类型
        /// </summary>
        private static string GetEntityIdType(INamedTypeSymbol entityClass)
        {
            // 查找 Id 属性
            var idProperty = entityClass.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.Name == "Id");

            if (idProperty != null)
            {
                return idProperty.Type.ToDisplayString();
            }

            // 如果没有找到 Id 属性，检查基类
            var baseType = entityClass.BaseType;
            while (baseType != null)
            {
                idProperty = baseType.GetMembers()
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(p => p.Name == "Id");

                if (idProperty != null)
                {
                    return idProperty.Type.ToDisplayString();
                }

                baseType = baseType.BaseType;
            }

            return "Guid"; // 默认返回 Guid
        }

        /// <summary>
        /// 获取实体属性列表
        /// </summary>
        private static List<PropertyInfo> GetEntityProperties(INamedTypeSymbol entityClass)
        {
            var properties = new List<PropertyInfo>();

            foreach (var member in entityClass.GetMembers())
            {
                if (member is not IPropertySymbol property || property.IsStatic)
                    continue;

                var propInfo = new PropertyInfo
                {
                    Name = property.Name,
                    TypeName = property.Type.ToDisplayString(),
                    IsNullable = property.NullableAnnotation == NullableAnnotation.Annotated,
                    IsString = property.Type.SpecialType == SpecialType.System_String,
                    IsNumeric = IsNumericType(property.Type),
                    IsDateTime = IsDateTimeType(property.Type),
                    IsEnum = property.Type.TypeKind == TypeKind.Enum,
                    IsCollection = IsCollectionType(property.Type)
                };

                properties.Add(propInfo);
            }

            return properties;
        }

        /// <summary>
        /// 判断是否是数值类型
        /// </summary>
        private static bool IsNumericType(ITypeSymbol type)
        {
            return type.SpecialType switch
            {
                SpecialType.System_Int16 or
                SpecialType.System_Int32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt16 or
                SpecialType.System_UInt32 or
                SpecialType.System_UInt64 or
                SpecialType.System_Decimal or
                SpecialType.System_Double or
                SpecialType.System_Single => true,
                _ => false
            };
        }

        /// <summary>
        /// 判断是否是日期时间类型
        /// </summary>
        private static bool IsDateTimeType(ITypeSymbol type)
        {
            var typeName = type.ToDisplayString();
            return typeName == "System.DateTime" || typeName == "System.DateTimeOffset";
        }

        /// <summary>
        /// 判断是否是集合类型
        /// </summary>
        private static bool IsCollectionType(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String)
                return false;

            if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                var genericType = namedType.ConstructedFrom.ToDisplayString();
                return genericType.StartsWith("System.Collections.Generic.") ||
                       genericType.StartsWith("System.Collections.");
            }

            return false;
        }

        /// <summary>
        /// 获取基类信息
        /// </summary>
        private static BaseClassInfo? GetBaseClassInfo(INamedTypeSymbol entityClass)
        {
            var baseType = entityClass.BaseType;
            if (baseType == null) return null;

            var baseClassName = baseType.Name;
            var baseClassFullName = baseType.ToDisplayString();

            return new BaseClassInfo
            {
                Name = baseClassName,
                FullName = baseClassFullName,
                IsAudited = baseClassName.Contains("Audited") || baseClassName.Contains("Audit"),
                IsAggregateRoot = baseClassName.Contains("AggregateRoot"),
                IsSoftDelete = baseClassName.Contains("SoftDelete")
            };
        }

        /// <summary>
        /// 判断是否是完全审计实体（包含软删除）
        /// </summary>
        private static bool IsFullyAudited(INamedTypeSymbol entityClass)
        {
            var baseType = entityClass.BaseType;
            while (baseType != null)
            {
                var baseClassName = baseType.Name;
                if (baseClassName.Contains("SoftDelete") || baseClassName.Contains("MultiTenantSoftDelete"))
                {
                    return true;
                }
                baseType = baseType.BaseType;
            }
            return false;
        }

        /// <summary>
        /// 执行代码生成
        /// </summary>
        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<EntityInfo?> entities)
        {
            if (entities.IsDefaultOrEmpty)
                return;

            // 去重处理
            var uniqueEntities = entities
                .Where(e => e != null)
                .Cast<EntityInfo>()
                .ToList();

            try
            {
                foreach (var entityInfo in uniqueEntities)
                {
                    // 根据配置选择性生成
                    if (entityInfo.GenerateRepository)
                    {
                        GenerateRepository(context, entityInfo);
                    }

                    if (entityInfo.GenerateQueryExtensions)
                    {
                        GenerateQueryExtensions(context, entityInfo);
                    }

                    if (entityInfo.GenerateCrudService)
                    {
                        GenerateCrudService(context, entityInfo);
                    }

                    if (entityInfo.GenerateController)
                    {
                        GenerateController(context, entityInfo);
                    }

                    if (entityInfo.GenerateDto)
                    {
                        GenerateDtos(context, entityInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("CCCG001", "Unified code generation error",
                        $"Error generating code: {ex.Message}",
                        "CodeGeneration", DiagnosticSeverity.Warning, true),
                    Location.None));
            }
        }

        /// <summary>
        /// 生成仓储
        /// </summary>
        private void GenerateRepository(SourceProductionContext context, EntityInfo entityInfo)
        {
            // TODO: 实现仓储生成逻辑
            // 可以调用现有的 RepositorySourceGenerator 逻辑
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("CCCG002", "Repository generation not implemented yet",
                    $"Repository generation for {entityInfo.Name} is not implemented yet in unified generator.",
                    "CodeGeneration", DiagnosticSeverity.Info, true),
                Location.None));
        }

        /// <summary>
        /// 生成查询扩展方法
        /// </summary>
        private void GenerateQueryExtensions(SourceProductionContext context, EntityInfo entityInfo)
        {
            // TODO: 实现查询扩展方法生成逻辑
        }

        /// <summary>
        /// 生成 CRUD 服务
        /// </summary>
        private void GenerateCrudService(SourceProductionContext context, EntityInfo entityInfo)
        {
            // TODO: 实现 CRUD 服务生成逻辑
        }

        /// <summary>
        /// 生成控制器
        /// </summary>
        private void GenerateController(SourceProductionContext context, EntityInfo entityInfo)
        {
            // TODO: 实现控制器生成逻辑
        }

        /// <summary>
        /// 生成 DTO
        /// </summary>
        private void GenerateDtos(SourceProductionContext context, EntityInfo entityInfo)
        {
            // TODO: 实现 DTO 生成逻辑
        }
    }
}
