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
        /// 根据实体命名空间和目标类型获取目标命名空间
        /// </summary>
        private static string GetTargetNamespace(string entityNamespace, GeneratedCodeType codeType)
        {
            if (!entityNamespace.Contains(".Domain.Entities"))
            {
                return codeType switch
                {
                    GeneratedCodeType.Dto => $"{entityNamespace}.Dtos",
                    GeneratedCodeType.CreateDto => $"{entityNamespace}.Dtos",
                    GeneratedCodeType.UpdateDto => $"{entityNamespace}.Dtos",
                    GeneratedCodeType.ListRequestDto => $"{entityNamespace}.Dtos",
                    GeneratedCodeType.ServiceInterface => $"{entityNamespace}.Services",
                    GeneratedCodeType.ServiceImplementation => $"{entityNamespace}.Services",
                    GeneratedCodeType.MappingProfile => $"{entityNamespace}.Mappings",
                    GeneratedCodeType.Controller => $"{entityNamespace}.Controllers",
                    GeneratedCodeType.Repository => $"{entityNamespace}.Repositories",
                    _ => entityNamespace
                };
            }

            var baseNamespace = entityNamespace.Replace(".Domain.Entities", "");

            return codeType switch
            {
                GeneratedCodeType.Dto => $"{baseNamespace}.Application.Contracts.DTOs",
                GeneratedCodeType.CreateDto => $"{baseNamespace}.Application.Contracts.DTOs",
                GeneratedCodeType.UpdateDto => $"{baseNamespace}.Application.Contracts.DTOs",
                GeneratedCodeType.ListRequestDto => $"{baseNamespace}.Application.Contracts.DTOs",
                GeneratedCodeType.ServiceInterface => $"{baseNamespace}.Application.Contracts.Interfaces",
                GeneratedCodeType.ServiceImplementation => $"{baseNamespace}.Application.Services",
                GeneratedCodeType.MappingProfile => $"{baseNamespace}.Application",
                GeneratedCodeType.Controller => $"{baseNamespace}.Web.Controllers",
                GeneratedCodeType.Repository => $"{baseNamespace}.Domain.Repositories",
                _ => entityNamespace
            };
        }

        private enum GeneratedCodeType
        {
            Dto,
            CreateDto,
            UpdateDto,
            ListRequestDto,
            ServiceInterface,
            ServiceImplementation,
            MappingProfile,
            Controller,
            Repository
        }

        /// <summary>
        /// 获取实体属性列表
        /// </summary>
        private static List<PropertyInfo> GetEntityProperties(INamedTypeSymbol entityClass)
        {
            var properties = new List<PropertyInfo>();
            var propertyNames = new HashSet<string>();

            foreach (var member in entityClass.GetMembers())
            {
                if (member is not IPropertySymbol property || property.IsStatic)
                    continue;

                // 确保只添加唯一的属性
                if (propertyNames.Contains(property.Name))
                    continue;

                propertyNames.Add(property.Name);

                var typeName = property.Type.ToDisplayString();
                var propInfo = new PropertyInfo
                {
                    Name = property.Name,
                    TypeName = typeName,
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

            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("CCCG003", "Debug entity count",
                    $"Processing {uniqueEntities.Count} unique entities from {entities.Length} total entities",
                    "CodeGeneration", DiagnosticSeverity.Info, true),
                Location.None));

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
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var properties = GetQueryableProperties(entityInfo);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Linq;");
            builder.AppendLine("using System.Linq.Expressions;");
            builder.AppendLine($"using {namespaceName};");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.QueryExtensions");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} 查询扩展方法");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public static class {entityName}QueryExtensions");
            builder.AppendLine("    {");

            // 生成 Where 方法（等值过滤）
            foreach (var prop in properties)
            {
                var propName = prop.Name;
                var propType = prop.TypeName;

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 过滤（等值）");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> Where{propName}(this IQueryable<{entityName}> query, {propType} value)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var constant = Expression.Constant(value);");
                builder.AppendLine($"            var predicate = Expression.Equal(property, constant);");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, bool>>(predicate, parameter);");
                builder.AppendLine("            return query.Where(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            // 生成字符串属性的方法
            foreach (var prop in properties.Where(p => p.IsString))
            {
                var propName = prop.Name;

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 模糊查询（包含）");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> Where{propName}Contains(this IQueryable<{entityName}> query, string value)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var constant = Expression.Constant(value);");
                builder.AppendLine($"            var containsMethod = typeof(string).GetMethod(\"Contains\", new[] {{ typeof(string) }});");
                builder.AppendLine($"            if (containsMethod == null) return query;");
                builder.AppendLine($"            var predicate = Expression.Call(property, containsMethod, constant);");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, bool>>(predicate, parameter);");
                builder.AppendLine("            return query.Where(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 开头匹配");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> Where{propName}StartsWith(this IQueryable<{entityName}> query, string value)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var constant = Expression.Constant(value);");
                builder.AppendLine($"            var startsWithMethod = typeof(string).GetMethod(\"StartsWith\", new[] {{ typeof(string) }});");
                builder.AppendLine($"            if (startsWithMethod == null) return query;");
                builder.AppendLine($"            var predicate = Expression.Call(property, startsWithMethod, constant);");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, bool>>(predicate, parameter);");
                builder.AppendLine("            return query.Where(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 结尾匹配");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> Where{propName}EndsWith(this IQueryable<{entityName}> query, string value)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var constant = Expression.Constant(value);");
                builder.AppendLine($"            var endsWithMethod = typeof(string).GetMethod(\"EndsWith\", new[] {{ typeof(string) }});");
                builder.AppendLine($"            if (endsWithMethod == null) return query;");
                builder.AppendLine($"            var predicate = Expression.Call(property, endsWithMethod, constant);");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, bool>>(predicate, parameter);");
                builder.AppendLine("            return query.Where(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            // 生成数值/日期属性的方法
            foreach (var prop in properties.Where(p => p.IsNumeric || p.IsDateTime))
            {
                var propName = prop.Name;
                var propType = prop.TypeName;

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 大于过滤");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> Where{propName}GreaterThan(this IQueryable<{entityName}> query, {propType} value)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var constant = Expression.Constant(value);");
                builder.AppendLine($"            var predicate = Expression.GreaterThan(property, constant);");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, bool>>(predicate, parameter);");
                builder.AppendLine("            return query.Where(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 小于过滤");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> Where{propName}LessThan(this IQueryable<{entityName}> query, {propType} value)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var constant = Expression.Constant(value);");
                builder.AppendLine($"            var predicate = Expression.LessThan(property, constant);");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, bool>>(predicate, parameter);");
                builder.AppendLine("            return query.Where(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 范围过滤");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> Where{propName}Between(this IQueryable<{entityName}> query, {propType} fromValue, {propType} toValue)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var fromConstant = Expression.Constant(fromValue);");
                builder.AppendLine($"            var toConstant = Expression.Constant(toValue);");
                builder.AppendLine($"            var lowerPredicate = Expression.GreaterThanOrEqual(property, fromConstant);");
                builder.AppendLine($"            var upperPredicate = Expression.LessThanOrEqual(property, toConstant);");
                builder.AppendLine($"            var predicate = Expression.AndAlso(lowerPredicate, upperPredicate);");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, bool>>(predicate, parameter);");
                builder.AppendLine("            return query.Where(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            // 生成 OrderBy 方法
            var processedOrderByProps = new HashSet<string>();
            foreach (var prop in properties)
            {
                var propName = prop.Name;
                if (processedOrderByProps.Contains(propName))
                    continue;
                processedOrderByProps.Add(propName);
                
                var propType = prop.TypeName;

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 升序排序");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> OrderBy{propName}(this IQueryable<{entityName}> query)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, {propType}>>(property, parameter);");
                builder.AppendLine("            return query.OrderBy(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            // 生成 OrderByDescending 方法
            var processedOrderByDescProps = new HashSet<string>();
            foreach (var prop in properties)
            {
                var propName = prop.Name;
                if (processedOrderByDescProps.Contains(propName))
                    continue;
                processedOrderByDescProps.Add(propName);
                
                var propType = prop.TypeName;

                builder.AppendLine($"        /// <summary>");
                builder.AppendLine($"        /// 按 {propName} 降序排序");
                builder.AppendLine($"        /// </summary>");
                builder.AppendLine($"        public static IQueryable<{entityName}> OrderBy{propName}Descending(this IQueryable<{entityName}> query)");
                builder.AppendLine("        {");
                builder.AppendLine($"            var parameter = Expression.Parameter(typeof({entityName}), \"e\");");
                builder.AppendLine($"            var property = Expression.Property(parameter, \"{propName}\");");
                builder.AppendLine($"            var lambda = Expression.Lambda<Func<{entityName}, {propType}>>(property, parameter);");
                builder.AppendLine("            return query.OrderByDescending(lambda);");
                builder.AppendLine("        }");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}QueryExtensions.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 获取可查询的属性列表（排除导航属性和集合属性）
        /// </summary>
        private List<PropertyInfo> GetQueryableProperties(EntityInfo entityInfo)
        {
            var propertyNames = new HashSet<string>();
            return entityInfo.Properties
                .Where(p => p.Name != "Id" && !p.IsCollection && propertyNames.Add(p.Name))
                .ToList();
        }

        /// <summary>
        /// 生成 CRUD 服务
        /// </summary>
        private void GenerateCrudService(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var idType = entityInfo.IdType;
            var properties = entityInfo.Properties;
            var generateAsBaseClass = entityInfo.GenerateAsBaseClass;

            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("CCCG004", "GenerateCrudService",
                    $"GenerateCrudService called for {entityName}, GenerateAsBaseClass={generateAsBaseClass}",
                    "CodeGeneration", DiagnosticSeverity.Info, true),
                Location.None));

            GenerateEntityDto(context, entityInfo);
            GenerateCreateEntityDto(context, entityInfo);
            GenerateUpdateEntityDto(context, entityInfo);
            GenerateEntityListRequestDto(context, entityInfo);
            GenerateCrudServiceInterface(context, entityInfo);
            GenerateCrudServiceImplementation(context, entityInfo);
            GenerateMappingProfile(context, entityInfo);
        }

        /// <summary>
        /// 生成实体 DTO
        /// </summary>
        private void GenerateEntityDto(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var properties = entityInfo.Properties;
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);

            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("CCCG005", "GenerateEntityDto",
                    $"Generating {entityName}Dto with {properties.Count} properties: {string.Join(", ", properties.Select(p => $"{p.Name}:{p.TypeName}"))}",
                    "CodeGeneration", DiagnosticSeverity.Info, true),
                Location.None));

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine();
            builder.AppendLine($"namespace {dtosNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public class {entityName}Dto");
            builder.AppendLine("    {");

            builder.AppendLine($"        public {entityInfo.IdType} Id {{ get; set; }}");
            builder.AppendLine();

            foreach (var prop in properties.Where(p => p.Name != "Id"))
            {
                var typeName = prop.TypeName;
                if (prop.IsNullable && !typeName.EndsWith("?"))
                {
                    typeName += "?";
                }
                builder.AppendLine($"        public {typeName} {prop.Name} {{ get; set; }}");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成创建实体 DTO
        /// </summary>
        private void GenerateCreateEntityDto(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var properties = entityInfo.Properties;
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.CreateDto);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel.DataAnnotations;");
            builder.AppendLine();
            builder.AppendLine($"namespace {dtosNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// 创建 {entityName} DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public class Create{entityName}Dto");
            builder.AppendLine("    {");

            foreach (var prop in properties.Where(p => p.Name != "Id" && p.Name != "CreationTime" && p.Name != "LastModificationTime"))
            {
                var typeName = prop.TypeName;
                if (prop.IsNullable && !typeName.EndsWith("?"))
                {
                    typeName += "?";
                }
                builder.AppendLine($"        public {typeName} {prop.Name} {{ get; set; }}");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Create{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成更新实体 DTO
        /// </summary>
        private void GenerateUpdateEntityDto(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var properties = entityInfo.Properties;
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.UpdateDto);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel.DataAnnotations;");
            builder.AppendLine();
            builder.AppendLine($"namespace {dtosNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// 更新 {entityName} DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public class Update{entityName}Dto");
            builder.AppendLine("    {");

            builder.AppendLine($"        public {entityInfo.IdType} Id {{ get; set; }}");
            builder.AppendLine();

            foreach (var prop in properties.Where(p => p.Name != "Id" && p.Name != "CreationTime" && p.Name != "LastModificationTime"))
            {
                var typeName = prop.TypeName;
                if (prop.IsNullable && !typeName.EndsWith("?"))
                {
                    typeName += "?";
                }
                builder.AppendLine($"        public {typeName} {prop.Name} {{ get; set; }}");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Update{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成实体列表请求 DTO
        /// </summary>
        private void GenerateEntityListRequestDto(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var properties = entityInfo.Properties;
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.ListRequestDto);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine();
            builder.AppendLine($"namespace {dtosNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} 列表请求 DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public class {entityName}ListRequestDto : PagedRequestDto");
            builder.AppendLine("    {");

            var stringProperties = properties.Where(p => p.IsString && p.Name != "Id" && p.Name != "ConcurrencyStamp").Take(3).ToList();
            foreach (var prop in stringProperties)
            {
                builder.AppendLine($"        public string? {prop.Name} {{ get; set; }}");
            }

            if (properties.Any(p => p.Name == "CreationTime"))
            {
                builder.AppendLine("        public DateTime? StartTime { get; set; }");
                builder.AppendLine("        public DateTime? EndTime { get; set; }");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}ListRequestDto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成 CRUD 服务接口
        /// </summary>
        private void GenerateCrudServiceInterface(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var idType = entityInfo.IdType;

            var serviceNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.ServiceInterface);
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine($"using {dtosNamespace};");
            builder.AppendLine();
            builder.AppendLine($"namespace {serviceNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} CRUD 服务接口");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial interface I{entityName}AppService");
            builder.AppendLine("    {");

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 创建 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        Task<{entityName}Dto> CreateAsync(Create{entityName}Dto input, System.Threading.CancellationToken cancellationToken = default);");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 根据 ID 获取 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        Task<{entityName}Dto?> GetByIdAsync({idType} id, System.Threading.CancellationToken cancellationToken = default);");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 获取 {entityName} 分页列表");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        Task<PagedResult<{entityName}Dto>> GetListAsync({entityName}ListRequestDto input, System.Threading.CancellationToken cancellationToken = default);");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 更新 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        Task<{entityName}Dto> UpdateAsync(Update{entityName}Dto input, System.Threading.CancellationToken cancellationToken = default);");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 删除 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        Task DeleteAsync({idType} id, System.Threading.CancellationToken cancellationToken = default);");
            builder.AppendLine();

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"I{entityName}AppService.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成 CRUD 服务实现
        /// </summary>
        private void GenerateCrudServiceImplementation(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var idType = entityInfo.IdType;
            var properties = entityInfo.Properties;
            var generateAsBaseClass = entityInfo.GenerateAsBaseClass;

            var serviceNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.ServiceImplementation);
            var repositoriesNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Repository);
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using System.Linq;");
            builder.AppendLine("using System.Linq.Expressions;");
            builder.AppendLine("using System.Threading;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using AutoMapper;");
            builder.AppendLine("using Microsoft.EntityFrameworkCore;");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine("using CrestCreates.Domain.Exceptions;");
            builder.AppendLine($"using {namespaceName};");
            builder.AppendLine($"using {dtosNamespace};");
            builder.AppendLine($"using {repositoriesNamespace};");
            builder.AppendLine($"using {serviceNamespace};");
            builder.AppendLine();
            builder.AppendLine($"namespace {serviceNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            var className = generateAsBaseClass ? $"{entityName}CrudServiceBase" : $"{entityName}CrudService";
            var classModifier = generateAsBaseClass ? "abstract" : "";
            if (generateAsBaseClass)
            {
                builder.AppendLine($"    /// {entityName} 的 CRUD 服务基类");
                builder.AppendLine("    /// 请继承此类创建具体的服务实现");
            }
            else
            {
                builder.AppendLine($"    /// {entityName} CRUD 服务实现");
            }
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public {classModifier} class {className} : I{entityName}AppService");
            builder.AppendLine("    {");

            builder.AppendLine($"        protected readonly I{entityName}Repository _repository;");
            builder.AppendLine("        protected readonly IMapper _mapper;");
            builder.AppendLine();

            var modifier = generateAsBaseClass ? "protected" : "public";
            builder.AppendLine($"        {modifier} {className}(I{entityName}Repository repository, IMapper mapper)");
            builder.AppendLine("        {");
            builder.AppendLine("            _repository = repository ?? throw new ArgumentNullException(nameof(repository));");
            builder.AppendLine("            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));");
            builder.AppendLine("        }");
            builder.AppendLine();

            var methodModifier = generateAsBaseClass ? "virtual" : "";

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 创建 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public {methodModifier} async Task<{entityName}Dto> CreateAsync(Create{entityName}Dto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine($"            var entity = _mapper.Map<{entityName}>(input);");
            builder.AppendLine($"            await OnCreatingAsync(entity, cancellationToken);");
            builder.AppendLine("            entity = await _repository.AddAsync(entity, cancellationToken);");
            builder.AppendLine($"            await OnCreatedAsync(entity, cancellationToken);");
            builder.AppendLine($"            return _mapper.Map<{entityName}Dto>(entity);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 根据 ID 获取 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public {methodModifier} async Task<{entityName}Dto?> GetByIdAsync({idType} id, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            var entity = await _repository.GetByIdAsync(id, cancellationToken);");
            builder.AppendLine("            if (entity == null)");
            builder.AppendLine("                return null;");
            builder.AppendLine();
            builder.AppendLine($"            return _mapper.Map<{entityName}Dto>(entity);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 获取 {entityName} 分页列表");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public {methodModifier} async Task<PagedResult<{entityName}Dto>> GetListAsync({entityName}ListRequestDto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine("            Expression<Func<" + entityName + ", bool>>? predicate = null;");
            builder.AppendLine();

            var searchableProperties = properties
                .Where(p => p.IsString && p.Name != "Id" && p.Name != "ConcurrencyStamp")
                .Take(3)
                .ToList();

            if (searchableProperties.Any())
            {
                builder.AppendLine("            if (!string.IsNullOrWhiteSpace(input.Keyword))");
                builder.AppendLine("            {");
                var keywordConditions = searchableProperties
                    .Select(p => $"e.{p.Name}.Contains(input.Keyword)")
                    .ToList();
                builder.AppendLine($"                predicate = e => {string.Join(" || ", keywordConditions)};");
                builder.AppendLine("            }");
                builder.AppendLine();
            }

            foreach (var prop in searchableProperties)
            {
                builder.AppendLine($"            if (!string.IsNullOrWhiteSpace(input.{prop.Name}))");
                builder.AppendLine("            {");
                builder.AppendLine($"                predicate = predicate == null");
                builder.AppendLine($"                    ? e => e.{prop.Name} == input.{prop.Name}");
                builder.AppendLine($"                    : CombinePredicates(predicate, e => e.{prop.Name} == input.{prop.Name});");
                builder.AppendLine("            }");
                builder.AppendLine();
            }

            if (properties.Any(p => p.Name == "CreationTime"))
            {
                builder.AppendLine("            if (input.StartTime.HasValue)");
                builder.AppendLine("            {");
                builder.AppendLine("                predicate = predicate == null");
                builder.AppendLine("                    ? e => e.CreationTime >= input.StartTime.Value");
                builder.AppendLine("                    : CombinePredicates(predicate, e => e.CreationTime >= input.StartTime.Value);");
                builder.AppendLine("            }");
                builder.AppendLine();

                builder.AppendLine("            if (input.EndTime.HasValue)");
                builder.AppendLine("            {");
                builder.AppendLine("                predicate = predicate == null");
                builder.AppendLine("                    ? e => e.CreationTime <= input.EndTime.Value");
                builder.AppendLine("                    : CombinePredicates(predicate, e => e.CreationTime <= input.EndTime.Value);");
                builder.AppendLine("            }");
                builder.AppendLine();
            }

            builder.AppendLine("            var (items, totalCount) = await _repository.GetPagedListAsync(");
            builder.AppendLine("                input.PageNumber,");
            builder.AppendLine("                input.PageSize,");
            builder.AppendLine("                predicate,");
            builder.AppendLine("                e => e.Id,");
            builder.AppendLine("                ascending: false,");
            builder.AppendLine("                cancellationToken: cancellationToken);");
            builder.AppendLine();
            builder.AppendLine($"            var dtos = _mapper.Map<List<{entityName}Dto>>(items);");
            builder.AppendLine("            return new PagedResult<" + entityName + "Dto>(dtos, totalCount, input.PageNumber, input.PageSize);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 更新 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public {methodModifier} async Task<{entityName}Dto> UpdateAsync(Update{entityName}Dto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine("            var entity = await _repository.GetByIdAsync(input.Id, cancellationToken);");
            builder.AppendLine("            if (entity == null)");
            builder.AppendLine($"                throw new EntityNotFoundException(typeof({entityName}), input.Id);");
            builder.AppendLine();
            builder.AppendLine($"            await OnUpdatingAsync(entity, input, cancellationToken);");
            builder.AppendLine("            _mapper.Map(input, entity);");
            builder.AppendLine("            entity = await _repository.UpdateAsync(entity, cancellationToken);");
            builder.AppendLine($"            await OnUpdatedAsync(entity, cancellationToken);");
            builder.AppendLine($"            return _mapper.Map<{entityName}Dto>(entity);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 删除 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public {methodModifier} async Task DeleteAsync({idType} id, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            var entity = await _repository.GetByIdAsync(id, cancellationToken);");
            builder.AppendLine("            if (entity == null)");
            builder.AppendLine($"                throw new EntityNotFoundException(typeof({entityName}), id);");
            builder.AppendLine();
            builder.AppendLine($"            await OnDeletingAsync(entity, cancellationToken);");
            builder.AppendLine("            await _repository.DeleteAsync(entity, cancellationToken);");
            builder.AppendLine($"            await OnDeletedAsync(entity, cancellationToken);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine("        /// 组合两个谓词条件");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        private static Expression<Func<{entityName}, bool>> CombinePredicates(");
            builder.AppendLine($"            Expression<Func<{entityName}, bool>> expr1,");
            builder.AppendLine($"            Expression<Func<{entityName}, bool>> expr2)");
            builder.AppendLine("        {");
            builder.AppendLine("            var parameter = Expression.Parameter(typeof(" + entityName + "), \"e\");");
            builder.AppendLine("            var body = Expression.AndAlso(");
            builder.AppendLine("                Expression.Invoke(expr1, parameter),");
            builder.AppendLine("                Expression.Invoke(expr2, parameter));");
            builder.AppendLine($"            return Expression.Lambda<Func<{entityName}, bool>>(body, parameter);");
            builder.AppendLine("        }");

            if (generateAsBaseClass)
            {
                builder.AppendLine();
                builder.AppendLine("        #region 钩子方法");
                builder.AppendLine();

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// 创建实体前调用的钩子方法");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        protected virtual Task OnCreatingAsync({entityName} entity, CancellationToken cancellationToken = default)");
                builder.AppendLine("        {");
                builder.AppendLine("            return Task.CompletedTask;");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// 创建实体后调用的钩子方法");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        protected virtual Task OnCreatedAsync({entityName} entity, CancellationToken cancellationToken = default)");
                builder.AppendLine("        {");
                builder.AppendLine("            return Task.CompletedTask;");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// 更新实体前调用的钩子方法");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        protected virtual Task OnUpdatingAsync({entityName} entity, Update{entityName}Dto input, CancellationToken cancellationToken = default)");
                builder.AppendLine("        {");
                builder.AppendLine("            return Task.CompletedTask;");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// 更新实体后调用的钩子方法");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        protected virtual Task OnUpdatedAsync({entityName} entity, CancellationToken cancellationToken = default)");
                builder.AppendLine("        {");
                builder.AppendLine("            return Task.CompletedTask;");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// 删除实体前调用的钩子方法");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        protected virtual Task OnDeletingAsync({entityName} entity, CancellationToken cancellationToken = default)");
                builder.AppendLine("        {");
                builder.AppendLine("            return Task.CompletedTask;");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// 删除实体后调用的钩子方法");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        protected virtual Task OnDeletedAsync({entityName} entity, CancellationToken cancellationToken = default)");
                builder.AppendLine("        {");
                builder.AppendLine("            return Task.CompletedTask;");
                builder.AppendLine("        }");
                builder.AppendLine();

                builder.AppendLine("        #endregion");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{className}.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成 AutoMapper 映射配置
        /// </summary>
        private void GenerateMappingProfile(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);
            var mappingsNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.MappingProfile);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using AutoMapper;");
            builder.AppendLine($"using {namespaceName};");
            builder.AppendLine($"using {dtosNamespace};");
            builder.AppendLine();
            builder.AppendLine($"namespace {mappingsNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} AutoMapper 映射配置");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public class {entityName}MappingProfile : Profile");
            builder.AppendLine("    {");
            builder.AppendLine($"        public {entityName}MappingProfile()");
            builder.AppendLine("        {");
            builder.AppendLine($"            CreateMap<{entityName}, {entityName}Dto>();");
            builder.AppendLine($"            CreateMap<Create{entityName}Dto, {entityName}>();");
            builder.AppendLine($"            CreateMap<Update{entityName}Dto, {entityName}>();");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}MappingProfile.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成控制器
        /// </summary>
        private void GenerateController(SourceProductionContext context, EntityInfo entityInfo)
        {
            var entityName = entityInfo.Name;
            var namespaceName = entityInfo.Namespace;
            var idType = entityInfo.IdType;
            var controllerRoute = entityInfo.ControllerRoute ?? $"api/{entityName.ToLowerInvariant()}";
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);
            var servicesNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.ServiceInterface);
            var controllerNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Controller);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using Microsoft.AspNetCore.Mvc;");
            builder.AppendLine("using Microsoft.Extensions.Logging;");
            builder.AppendLine($"using {dtosNamespace};");
            builder.AppendLine($"using {servicesNamespace};");
            builder.AppendLine();
            builder.AppendLine($"namespace {controllerNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} API 控制器");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("    [ApiController]");
            builder.AppendLine($"    [Route(\"{controllerRoute}\")]");
            builder.AppendLine($"    public partial class {entityName}Controller : ControllerBase");
            builder.AppendLine("    {");
            builder.AppendLine($"        private readonly I{entityName}AppService _service;");
            builder.AppendLine($"        private readonly ILogger<{entityName}Controller> _logger;");
            builder.AppendLine();
            builder.AppendLine($"        public {entityName}Controller(");
            builder.AppendLine($"            I{entityName}AppService service,");
            builder.AppendLine($"            ILogger<{entityName}Controller> logger)");
            builder.AppendLine("        {");
            builder.AppendLine("            _service = service ?? throw new ArgumentNullException(nameof(service));");
            builder.AppendLine("            _logger = logger ?? throw new ArgumentNullException(nameof(logger));");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 根据 ID 获取 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        [HttpGet(\"{{id}}\")]");
            builder.AppendLine("        [ProducesResponseType(200)]");
            builder.AppendLine("        [ProducesResponseType(404)]");
            builder.AppendLine("        public async Task<IActionResult> GetById(");
            builder.AppendLine($"            {idType} id)");
            builder.AppendLine("        {");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.AppendLine($"                var result = await _service.GetByIdAsync(id);");
            builder.AppendLine("                if (result == null)");
            builder.AppendLine("                    return NotFound();");
            builder.AppendLine("                return Ok(result);");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (Exception ex)");
            builder.AppendLine("            {");
            builder.AppendLine($"                _logger.LogError(ex, \"Error getting {entityName} with id {{Id}}\", id);");
            builder.AppendLine("                return StatusCode(500, \"An error occurred while processing your request.\");");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 获取 {entityName} 分页列表");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [HttpGet(\"\")]");
            builder.AppendLine("        [ProducesResponseType(200)]");
            builder.AppendLine("        [ProducesResponseType(400)]");
            builder.AppendLine("        public async Task<IActionResult> GetList(");
            builder.AppendLine($"            [FromQuery] {entityName}ListRequestDto input)");
            builder.AppendLine("        {");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.AppendLine($"                var result = await _service.GetListAsync(input);");
            builder.AppendLine("                return Ok(result);");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (Exception ex)");
            builder.AppendLine("            {");
            builder.AppendLine($"                _logger.LogError(ex, \"Error getting {entityName} list\");");
            builder.AppendLine("                return StatusCode(500, \"An error occurred while processing your request.\");");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 创建 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [HttpPost(\"\")]");
            builder.AppendLine("        [ProducesResponseType(201)]");
            builder.AppendLine("        [ProducesResponseType(400)]");
            builder.AppendLine("        public async Task<IActionResult> Create(");
            builder.AppendLine($"            [FromBody] Create{entityName}Dto input)");
            builder.AppendLine("        {");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.AppendLine("                if (input == null)");
            builder.AppendLine("                    return BadRequest(\"Input cannot be null\");");
            builder.AppendLine();
            builder.AppendLine($"                var result = await _service.CreateAsync(input);");
            builder.AppendLine("                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (Exception ex)");
            builder.AppendLine("            {");
            builder.AppendLine($"                _logger.LogError(ex, \"Error creating {entityName}\");");
            builder.AppendLine("                return StatusCode(500, \"An error occurred while processing your request.\");");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 更新 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [HttpPut(\"{{id}}\")]");
            builder.AppendLine("        [ProducesResponseType(200)]");
            builder.AppendLine("        [ProducesResponseType(400)]");
            builder.AppendLine("        [ProducesResponseType(404)]");
            builder.AppendLine("        public async Task<IActionResult> Update(");
            builder.AppendLine($"            {idType} id,");
            builder.AppendLine($"            [FromBody] Update{entityName}Dto input)");
            builder.AppendLine("        {");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.AppendLine("                if (input == null)");
            builder.AppendLine("                    return BadRequest(\"Input cannot be null\");");
            builder.AppendLine();
            builder.AppendLine("                if (id != input.Id)");
            builder.AppendLine("                    return BadRequest(\"ID mismatch\");");
            builder.AppendLine();
            builder.AppendLine($"                var result = await _service.UpdateAsync(input);");
            builder.AppendLine("                return Ok(result);");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (Exception ex)");
            builder.AppendLine("            {");
            builder.AppendLine($"                _logger.LogError(ex, \"Error updating {entityName} with id {{Id}}\", id);");
            builder.AppendLine("                return StatusCode(500, \"An error occurred while processing your request.\");");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 删除 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [HttpDelete(\"{{id}}\")]");
            builder.AppendLine("        [ProducesResponseType(204)]");
            builder.AppendLine("        [ProducesResponseType(404)]");
            builder.AppendLine("        public async Task<IActionResult> Delete(");
            builder.AppendLine($"            {idType} id)");
            builder.AppendLine("        {");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.AppendLine($"                await _service.DeleteAsync(id);");
            builder.AppendLine("                return NoContent();");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (Exception ex)");
            builder.AppendLine("            {");
            builder.AppendLine($"                _logger.LogError(ex, \"Error deleting {entityName} with id {{Id}}\", id);");
            builder.AppendLine("                return StatusCode(500, \"An error occurred while processing your request.\");");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}Controller.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成 DTO
        /// </summary>
        private void GenerateDtos(SourceProductionContext context, EntityInfo entityInfo)
        {
            GenerateEntityDto(context, entityInfo);
            GenerateCreateEntityDto(context, entityInfo);
            GenerateUpdateEntityDto(context, entityInfo);
            GenerateEntityListRequestDto(context, entityInfo);
        }
    }
}
