using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.CrudServiceGenerator
{
    [Generator]
    public class CrudServiceSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var entityClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsEntityCandidate(node),
                    transform: static (ctx, _) => GetEntityClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(entityClasses, ExecuteGeneration);
        }

        private static bool IsEntityCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0;
        }

        private static (INamedTypeSymbol Symbol, bool GenerateAsBaseClass, bool IsUsingNewAttribute)? GetEntityClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol != null && HasGenerateCrudServiceAttribute(symbol))
            {
                var isUsingNewAttribute = IsUsingGenerateEntityAttribute(symbol);
                var generateAsBaseClass = isUsingNewAttribute ? 
                    GetAttributeBooleanValue(symbol, "GenerateAsBaseClass", true) : 
                    false;
                return (symbol, generateAsBaseClass, isUsingNewAttribute);
            }

            return null;
        }

        private static bool IsUsingGenerateEntityAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "GenerateEntityAttribute" ||
                    attr.AttributeClass.Name == "GenerateEntity" ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateEntityAttribute") ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateEntity")
                ));
        }

        private static bool HasGenerateCrudServiceAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass != null && (
                    attr.AttributeClass.Name == "GenerateCrudServiceAttribute" ||
                    attr.AttributeClass.Name == "GenerateCrudService" ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateCrudServiceAttribute") ||
                    attr.AttributeClass.ToDisplayString().EndsWith(".GenerateCrudService")
                ));
        }

        /// <summary>
        /// 获取特性的布尔值
        /// </summary>
        private static bool GetAttributeBooleanValue(INamedTypeSymbol symbol, string propertyName, bool defaultValue)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass == null) continue;
                var name = attr.AttributeClass.Name;
                var fullName = attr.AttributeClass.ToDisplayString();
                if (!(name == "GenerateCrudServiceAttribute" || name == "GenerateCrudService" ||
                      name == "GenerateEntityAttribute" || name == "GenerateEntity" ||
                      fullName.EndsWith(".GenerateCrudServiceAttribute") || fullName.EndsWith(".GenerateCrudService") ||
                      fullName.EndsWith(".GenerateEntityAttribute") || fullName.EndsWith(".GenerateEntity")))
                    continue;

                var namedArgument = attr.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
                if (namedArgument.Value.Value != null && namedArgument.Value.Value is bool value)
                {
                    return value;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取特性的字符串数组值
        /// </summary>
        private static string[] GetAttributeStringArrayValue(INamedTypeSymbol symbol, string propertyName, string[] defaultValue)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass == null) continue;
                var name = attr.AttributeClass.Name;
                var fullName = attr.AttributeClass.ToDisplayString();
                if (!(name == "GenerateCrudServiceAttribute" || name == "GenerateCrudService" ||
                      name == "GenerateEntityAttribute" || name == "GenerateEntity" ||
                      fullName.EndsWith(".GenerateCrudServiceAttribute") || fullName.EndsWith(".GenerateCrudService") ||
                      fullName.EndsWith(".GenerateEntityAttribute") || fullName.EndsWith(".GenerateEntity")))
                    continue;

                var namedArgument = attr.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
                if (namedArgument.Value.Values != null && namedArgument.Value.Values.Length > 0)
                {
                    var result = new List<string>();
                    foreach (var value in namedArgument.Value.Values)
                    {
                        if (value.Value is string stringValue)
                        {
                            result.Add(stringValue);
                        }
                    }
                    return result.ToArray();
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取特性的字符串值
        /// </summary>
        private static string? GetAttributeStringValue(INamedTypeSymbol symbol, string propertyName, string? defaultValue)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass == null) continue;
                var name = attr.AttributeClass.Name;
                var fullName = attr.AttributeClass.ToDisplayString();
                if (!(name == "GenerateCrudServiceAttribute" || name == "GenerateCrudService" ||
                      name == "GenerateEntityAttribute" || name == "GenerateEntity" ||
                      fullName.EndsWith(".GenerateCrudServiceAttribute") || fullName.EndsWith(".GenerateCrudService") ||
                      fullName.EndsWith(".GenerateEntityAttribute") || fullName.EndsWith(".GenerateEntity")))
                    continue;

                var namedArgument = attr.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
                if (namedArgument.Value.Value is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return defaultValue;
        }

        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<(INamedTypeSymbol Symbol, bool GenerateAsBaseClass, bool IsUsingNewAttribute)?> entityClasses)
        {
            if (entityClasses.IsDefaultOrEmpty) return;

            var processedEntities = new HashSet<string>();

            foreach (var entityInfo in entityClasses)
            {
                if (!entityInfo.HasValue) continue;

                var (entityClass, generateAsBaseClass, isUsingNewAttribute) = entityInfo.Value;
                var entityFullName = entityClass.ToDisplayString();
                if (processedEntities.Contains(entityFullName)) continue;

                // 注意：现在同时支持 GenerateCrudServiceAttribute 和 GenerateEntityAttribute
                processedEntities.Add(entityFullName);

                try
                {
                    var entityName = entityClass.Name;
                    var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
                    var idType = GetEntityIdType(entityClass);
                    var properties = GetEntityProperties(entityClass);
                    var generateController = GetAttributeBooleanValue(entityClass, "GenerateController", false);
                    var controllerRoute = GetCrudControllerRoute(entityClass, entityName);

                    var dtosNamespace = $"{namespaceName}.Dtos";
                    GenerateEntityDto(context, entityClass, entityName, namespaceName, properties);
                    GenerateCreateEntityDto(context, entityClass, entityName, namespaceName, properties);
                    GenerateUpdateEntityDto(context, entityClass, entityName, namespaceName, properties);
                    GenerateEntityListRequestDto(context, entityClass, entityName, namespaceName, properties);
                    GenerateCrudServiceInterface(context, entityClass, entityName, namespaceName, idType, generateController, controllerRoute);
                    GenerateCrudServiceImplementation(context, entityClass, entityName, namespaceName, idType, properties, generateAsBaseClass);
                    GenerateObjectMappingDeclarations(context, entityName, namespaceName, dtosNamespace);
                }
                catch (Exception ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("CCCG003", "CRUD Service generation error",
                            $"Error generating CRUD service code for {entityFullName}: {ex.Message}",
                            "CodeGeneration", DiagnosticSeverity.Warning, true),
                        Location.None));
                }
            }
        }

        private static string GetCrudControllerRoute(INamedTypeSymbol entityClass, string entityName)
        {
            var controllerRoute = GetAttributeStringValue(entityClass, "ControllerRoute", null);
            if (!string.IsNullOrWhiteSpace(controllerRoute))
            {
                return controllerRoute!;
            }

            var serviceRoute = GetAttributeStringValue(entityClass, "ServiceRoute", null);
            if (!string.IsNullOrWhiteSpace(serviceRoute))
            {
                return serviceRoute!;
            }

            return $"api/{entityName.ToLowerInvariant()}";
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void GenerateEntityDto(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName, List<IPropertySymbol> properties)
        {
            var excludedProperties = GetAttributeStringArrayValue(entityClass, "ExcludeProperties", Array.Empty<string>());
            
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Dtos");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} 输出 DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial class {entityName}Dto");
            builder.AppendLine("    {");

            foreach (var prop in properties)
            {
                if (excludedProperties.Contains(prop.Name))
                    continue;

                var propType = prop.Type.ToDisplayString();
                var nullableAnnotation = prop.NullableAnnotation == NullableAnnotation.Annotated ? "?" : "";
                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// {prop.Name}");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        public {propType}{nullableAnnotation} {prop.Name} {{ get; set; }}");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateCreateEntityDto(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName, List<IPropertySymbol> properties)
        {
            var excludedFromAttribute = GetAttributeStringArrayValue(entityClass, "ExcludeProperties", Array.Empty<string>());
            var defaultExcludedProperties = new[] { "Id", "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId", "IsDeleted", "DeletionTime", "DeleterId", "ConcurrencyStamp" };
            var allExcludedProperties = defaultExcludedProperties.Concat(excludedFromAttribute).ToArray();

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel.DataAnnotations;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Dtos");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// 创建 {entityName} 输入 DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial class Create{entityName}Dto");
            builder.AppendLine("    {");

            foreach (var prop in properties)
            {
                if (allExcludedProperties.Contains(prop.Name))
                    continue;

                var propType = prop.Type.ToDisplayString();
                var nullableAnnotation = prop.NullableAnnotation == NullableAnnotation.Annotated ? "?" : "";

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// {prop.Name}");
                builder.AppendLine("        /// </summary>");

                if (prop.Type.SpecialType == SpecialType.System_String && prop.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    builder.AppendLine("        [Required]");
                    builder.AppendLine("        [StringLength(255)]");
                }

                builder.AppendLine($"        public {propType}{nullableAnnotation} {prop.Name} {{ get; set; }}");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Create{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateUpdateEntityDto(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName, List<IPropertySymbol> properties)
        {
            var excludedFromAttribute = GetAttributeStringArrayValue(entityClass, "ExcludeProperties", Array.Empty<string>());
            var defaultExcludedProperties = new[] { "Id", "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId", "IsDeleted", "DeletionTime", "DeleterId", "ConcurrencyStamp" };
            var allExcludedProperties = defaultExcludedProperties.Concat(excludedFromAttribute).ToArray();

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel.DataAnnotations;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Dtos");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// 更新 {entityName} 输入 DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial class Update{entityName}Dto");
            builder.AppendLine("    {");

            var idType = GetEntityIdType(entityClass);
            builder.AppendLine("        /// <summary>");
            builder.AppendLine("        /// 实体 ID");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [Required]");
            builder.AppendLine($"        public {idType} Id {{ get; set; }}");
            builder.AppendLine();

            foreach (var prop in properties)
            {
                if (allExcludedProperties.Contains(prop.Name))
                    continue;

                var propType = prop.Type.ToDisplayString();
                var nullableAnnotation = prop.NullableAnnotation == NullableAnnotation.Annotated ? "?" : "";

                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// {prop.Name}");
                builder.AppendLine("        /// </summary>");

                if (prop.Type.SpecialType == SpecialType.System_String && prop.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    builder.AppendLine("        [Required]");
                    builder.AppendLine("        [StringLength(255)]");
                }

                builder.AppendLine($"        public {propType}{nullableAnnotation} {prop.Name} {{ get; set; }}");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Update{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateEntityListRequestDto(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName, List<IPropertySymbol> properties)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel;");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Dtos");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} 列表查询请求 DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial class {entityName}ListRequestDto : PagedRequestDto");
            builder.AppendLine("    {");

            var searchableProperties = properties
                .Where(p => p.Type.SpecialType == SpecialType.System_String && p.Name != "Id" && p.Name != "ConcurrencyStamp")
                .Take(3)
                .ToList();

            if (searchableProperties.Any())
            {
                builder.AppendLine("        /// <summary>");
                builder.AppendLine("        /// 关键字搜索");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine("        public string? Keyword { get; set; }");
                builder.AppendLine();
            }

            foreach (var prop in searchableProperties)
            {
                builder.AppendLine("        /// <summary>");
                builder.AppendLine($"        /// {prop.Name} 过滤");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine($"        public {prop.Type.ToDisplayString()}? {prop.Name} {{ get; set; }}");
                builder.AppendLine();
            }

            if (properties.Any(p => p.Name == "CreationTime"))
            {
                builder.AppendLine("        /// <summary>");
                builder.AppendLine("        /// 开始时间");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine("        public DateTime? StartTime { get; set; }");
                builder.AppendLine();

                builder.AppendLine("        /// <summary>");
                builder.AppendLine("        /// 结束时间");
                builder.AppendLine("        /// </summary>");
                builder.AppendLine("        public DateTime? EndTime { get; set; }");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}ListRequestDto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateCrudServiceInterface(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName, string idType, bool generateController, string controllerRoute)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Threading.Tasks;");
            if (generateController)
            {
                builder.AppendLine("using CrestCreates.Domain.Shared.Attributes;");
            }
            builder.AppendLine("using CrestCreates.Application.Contracts.Interfaces;");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine($"using {namespaceName}.Dtos;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Services");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} CRUD 服务接口");
            builder.AppendLine("    /// </summary>");
            if (generateController)
            {
                builder.AppendLine($"    [CrestCrudApiController(\"{EscapeStringLiteral(entityName)}\", \"{EscapeStringLiteral(controllerRoute)}\")]");
            }
            builder.AppendLine($"    public partial interface I{entityName}CrudService : ICrudAppService<{idType}, {entityName}Dto, Create{entityName}Dto, Update{entityName}Dto, {entityName}ListRequestDto>");
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
            builder.AppendLine($"        Task<PagedResultDto<{entityName}Dto>> GetListAsync({entityName}ListRequestDto input, System.Threading.CancellationToken cancellationToken = default);");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 更新 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        Task<{entityName}Dto> UpdateAsync({idType} id, Update{entityName}Dto input, System.Threading.CancellationToken cancellationToken = default);");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 删除 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        Task DeleteAsync({idType} id, System.Threading.CancellationToken cancellationToken = default);");

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"I{entityName}CrudService.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateCrudServiceImplementation(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName, string idType, List<IPropertySymbol> properties, bool generateAsBaseClass)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using System.Linq;");
            builder.AppendLine("using System.Linq.Expressions;");
            builder.AppendLine("using System.Threading;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using Microsoft.EntityFrameworkCore;");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine("using CrestCreates.Domain.Exceptions;");
            builder.AppendLine($"using {namespaceName};");
            builder.AppendLine($"using {namespaceName}.Dtos;");
            builder.AppendLine($"using {namespaceName}.Repositories;");
            builder.AppendLine($"using {namespaceName}.Services;");
            builder.AppendLine($"using {namespaceName}.Mappings;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Services");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            var className = generateAsBaseClass ? $"{entityName}CrudServiceBase" : $"{entityName}CrudService";
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
            builder.AppendLine($"    public abstract class {className} : I{entityName}CrudService");
            builder.AppendLine("    {");

            builder.AppendLine($"        protected readonly I{entityName}Repository _repository;");
            builder.AppendLine();

            builder.AppendLine($"        protected {className}(I{entityName}Repository repository)");
            builder.AppendLine("        {");
            builder.AppendLine("            _repository = repository ?? throw new ArgumentNullException(nameof(repository));");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 将创建 DTO 映射为实体");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        protected abstract {entityName} MapToEntity(Create{entityName}Dto dto);");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 创建 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public virtual async Task<{entityName}Dto> CreateAsync(Create{entityName}Dto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine($"            var entity = MapToEntity(input);");
            builder.AppendLine($"            await OnCreatingAsync(entity, cancellationToken);");
            builder.AppendLine("            entity = await _repository.AddAsync(entity, cancellationToken);");
            builder.AppendLine($"            await OnCreatedAsync(entity, cancellationToken);");
            builder.AppendLine($"            return {entityName}To{entityName}DtoMapper.ToTarget(entity);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 根据 ID 获取 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public virtual async Task<{entityName}Dto?> GetByIdAsync({idType} id, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            var entity = await _repository.GetByIdAsync(id, cancellationToken);");
            builder.AppendLine("            if (entity == null)");
            builder.AppendLine("                return null;");
            builder.AppendLine();
            builder.AppendLine($"            return {entityName}To{entityName}DtoMapper.ToTarget(entity);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 获取 {entityName} 分页列表");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public virtual async Task<PagedResultDto<{entityName}Dto>> GetListAsync({entityName}ListRequestDto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine("            Expression<Func<" + entityName + ", bool>>? predicate = null;");
            builder.AppendLine();

            var searchableProperties = properties
                .Where(p => p.Type.SpecialType == SpecialType.System_String && p.Name != "Id" && p.Name != "ConcurrencyStamp")
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
            builder.AppendLine($"            var dtos = items.Select({entityName}To{entityName}DtoMapper.ToTarget).ToList();");
            builder.AppendLine("            return new PagedResultDto<" + entityName + "Dto>(dtos, totalCount, input.PageNumber, input.PageSize);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 更新 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public virtual async Task<{entityName}Dto> UpdateAsync({idType} id, Update{entityName}Dto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine("            var entity = await _repository.GetByIdAsync(id, cancellationToken);");
            builder.AppendLine("            if (entity == null)");
            builder.AppendLine($"                throw new EntityNotFoundException(typeof({entityName}), id);");
            builder.AppendLine();
            builder.AppendLine($"            await OnUpdatingAsync(entity, input, cancellationToken);");
            builder.AppendLine($"            Update{entityName}DtoTo{entityName}Mapper.Apply(input, entity);");
            builder.AppendLine("            entity = await _repository.UpdateAsync(entity, cancellationToken);");
            builder.AppendLine($"            await OnUpdatedAsync(entity, cancellationToken);");
            builder.AppendLine($"            return {entityName}To{entityName}DtoMapper.ToTarget(entity);");
            builder.AppendLine("        }");
            builder.AppendLine();

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 删除 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public virtual async Task DeleteAsync({idType} id, CancellationToken cancellationToken = default)");
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

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{className}.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateObjectMappingDeclarations(SourceProductionContext context, string entityName, string namespaceName, string dtosNamespace)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using CrestCreates.Domain.Shared.ObjectMapping;");
            builder.AppendLine($"using {namespaceName};");
            builder.AppendLine($"using {dtosNamespace};");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Mappings");
            builder.AppendLine("{");
            builder.AppendLine($"    [GenerateObjectMapping(typeof({entityName}), typeof({entityName}Dto))]");
            builder.AppendLine($"    public static partial class {entityName}To{entityName}DtoMapper {{ }}");
            builder.AppendLine();
            builder.AppendLine($"    [GenerateObjectMapping(typeof(Update{entityName}Dto), typeof({entityName}), Direction = MapDirection.Apply)]");
            builder.AppendLine($"    public static partial class Update{entityName}DtoTo{entityName}Mapper {{ }}");
            builder.AppendLine("}");

            context.AddSource($"{entityName}ObjectMappings.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private string GetEntityIdType(INamedTypeSymbol entityClass)
        {
            var baseType = entityClass.BaseType;
            while (baseType != null)
            {
                if ((baseType.Name == "Entity" || baseType.Name == "AggregateRoot" ||
                     baseType.Name == "AuditedEntity" || baseType.Name == "AuditedAggregateRoot" ||
                     baseType.Name == "FullyAuditedEntity" || baseType.Name == "FullyAuditedAggregateRoot") &&
                    baseType.TypeArguments.Length > 0)
                {
                    return baseType.TypeArguments[0].ToDisplayString();
                }
                baseType = baseType.BaseType;
            }
            return "int";
        }

        private List<IPropertySymbol> GetEntityProperties(INamedTypeSymbol entityClass)
        {
            var properties = new List<IPropertySymbol>();
            var allMembers = entityClass.GetMembers();

            foreach (var member in allMembers.OfType<IPropertySymbol>())
            {
                if (member.DeclaredAccessibility == Accessibility.Public &&
                    !member.IsStatic &&
                    member.CanBeReferencedByName)
                {
                    properties.Add(member);
                }
            }

            return properties;
        }
    }
}
