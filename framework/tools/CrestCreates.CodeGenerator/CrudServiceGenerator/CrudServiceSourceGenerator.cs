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
        private static readonly SymbolDisplayFormat FullyQualifiedFormat =
            SymbolDisplayFormat.FullyQualifiedFormat;

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
                    GetAttributeBooleanValue(symbol, "GenerateAsBaseClass", false) :
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
                if (namedArgument.Value.Kind == TypedConstantKind.Array && namedArgument.Value.Values.Length > 0)
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

                processedEntities.Add(entityFullName);

                try
                {
                    var entityName = entityClass.Name;
                    var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
                    var idType = GetEntityIdType(entityClass);
                    var properties = GetEntityProperties(entityClass);

                    // Generate DTOs
                    GenerateEntityDto(context, entityClass, entityName, namespaceName, properties);
                    GenerateCreateEntityDto(context, entityClass, entityName, namespaceName, properties);
                    GenerateUpdateEntityDto(context, entityClass, entityName, namespaceName, properties);
                    GenerateEntityListRequestDto(context, entityClass, entityName, namespaceName);

                    // Generate contract
                    GenerateCrudServiceInterface(context, entityName, namespaceName, idType);

                    // Generate permissions
                    GenerateCrudPermissions(context, entityName, namespaceName);

                    // Generate object mapping declarations
                    GenerateObjectMappingDeclarations(context, entityClass, entityName, namespaceName);

                    // Generate mainline app service implementation
                    GenerateCrudServiceImplementation(context, entityClass, entityName, namespaceName, idType, properties, generateAsBaseClass);
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

        private static string GetPropertyTypeDeclaration(IPropertySymbol prop)
        {
            var propType = prop.Type.ToDisplayString();
            // If the type already has a nullable suffix (e.g., "System.Guid?"), don't add another
            if (propType.EndsWith("?"))
                return propType;

            if (prop.NullableAnnotation == NullableAnnotation.Annotated)
                return propType + "?";

            return propType;
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
            builder.AppendLine($"    public partial class {entityName}Dto");
            builder.AppendLine("    {");

            foreach (var prop in properties)
            {
                if (excludedProperties.Contains(prop.Name))
                    continue;
                if (prop.Name == "IsDeleted" || prop.Name == "DeletionTime" || prop.Name == "DeleterId")
                    continue;

                var propType = GetPropertyTypeDeclaration(prop);
                builder.AppendLine($"        public {propType} {prop.Name} {{ get; set; }}");
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
            builder.AppendLine($"    public partial class Create{entityName}Dto");
            builder.AppendLine("    {");

            foreach (var prop in properties)
            {
                if (allExcludedProperties.Contains(prop.Name))
                    continue;

                var propType = GetPropertyTypeDeclaration(prop);

                if (prop.Type.SpecialType == SpecialType.System_String && prop.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    builder.AppendLine("        [Required]");
                    builder.AppendLine("        [StringLength(255)]");
                }

                builder.AppendLine($"        public {propType} {prop.Name} {{ get; set; }}");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Create{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateUpdateEntityDto(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName, List<IPropertySymbol> properties)
        {
            var excludedFromAttribute = GetAttributeStringArrayValue(entityClass, "ExcludeProperties", Array.Empty<string>());
            var defaultExcludedProperties = new[] { "Id", "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId", "IsDeleted", "DeletionTime", "DeleterId" };
            var allExcludedProperties = defaultExcludedProperties.Concat(excludedFromAttribute).ToArray();

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel.DataAnnotations;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Dtos");
            builder.AppendLine("{");
            builder.AppendLine($"    public partial class Update{entityName}Dto");
            builder.AppendLine("    {");

            foreach (var prop in properties)
            {
                if (allExcludedProperties.Contains(prop.Name))
                    continue;

                var propType = GetPropertyTypeDeclaration(prop);

                if (prop.Type.SpecialType == SpecialType.System_String && prop.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    builder.AppendLine("        [Required]");
                    builder.AppendLine("        [StringLength(255)]");
                }

                builder.AppendLine($"        public {propType} {prop.Name} {{ get; set; }}");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Update{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateEntityListRequestDto(SourceProductionContext context, INamedTypeSymbol entityClass, string entityName, string namespaceName)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Dtos");
            builder.AppendLine("{");
            builder.AppendLine($"    public partial class {entityName}ListRequestDto : PagedRequestDto");
            builder.AppendLine("    {");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}ListRequestDto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateCrudServiceInterface(
            SourceProductionContext context,
            string entityName,
            string namespaceName,
            string idType)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using CrestCreates.Application.Contracts.Interfaces;");
            builder.AppendLine($"using {namespaceName}.Dtos;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Services");
            builder.AppendLine("{");
            builder.AppendLine($"    public partial interface I{entityName}AppService : ICrudAppService<{idType}, {entityName}Dto, Create{entityName}Dto, Update{entityName}Dto, {entityName}ListRequestDto>");
            builder.AppendLine("    {");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"I{entityName}AppService.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateCrudPermissions(SourceProductionContext context, string entityName, string namespaceName)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Permissions");
            builder.AppendLine("{");
            builder.AppendLine($"    public static partial class {entityName}CrudPermissions");
            builder.AppendLine("    {");
            builder.AppendLine($"        public const string Create = \"{entityName}.Create\";");
            builder.AppendLine($"        public const string Get = \"{entityName}.Get\";");
            builder.AppendLine($"        public const string Search = \"{entityName}.Search\";");
            builder.AppendLine($"        public const string Update = \"{entityName}.Update\";");
            builder.AppendLine($"        public const string Delete = \"{entityName}.Delete\";");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}CrudPermissions.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateObjectMappingDeclarations(
            SourceProductionContext context,
            INamedTypeSymbol entityClass,
            string entityName,
            string namespaceName)
        {
            var entityFullName = entityClass.ToDisplayString(FullyQualifiedFormat);
            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using CrestCreates.Domain.Shared.ObjectMapping;");
            builder.AppendLine($"using {namespaceName}.Dtos;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Mappings");
            builder.AppendLine("{");
            builder.AppendLine($"    [GenerateObjectMapping(typeof({entityFullName}), typeof({entityName}Dto))]");
            builder.AppendLine($"    [GenerateObjectMapping(typeof(Create{entityName}Dto), typeof({entityFullName}), Direction = MapDirection.Create)]");
            builder.AppendLine($"    [GenerateObjectMapping(typeof(Update{entityName}Dto), typeof({entityFullName}), Direction = MapDirection.Apply)]");
            builder.AppendLine($"    public static partial class {entityName}ObjectMappings");
            builder.AppendLine("    {");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}ObjectMappings.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateCrudServiceImplementation(
            SourceProductionContext context,
            INamedTypeSymbol entityClass,
            string entityName,
            string namespaceName,
            string idType,
            List<IPropertySymbol> properties,
            bool generateAsBaseClass)
        {
            var entityFullName = entityClass.ToDisplayString(FullyQualifiedFormat);
            var hasConcurrencyStamp = entityClass.AllInterfaces.Any(i =>
                i.Name == "IHasConcurrencyStamp" && i.ContainingNamespace.ToDisplayString() == "CrestCreates.Domain.Shared.Entities");
            // Also check for test stubs where IHasConcurrencyStamp is in the same namespace
            if (!hasConcurrencyStamp)
            {
                hasConcurrencyStamp = entityClass.AllInterfaces.Any(i => i.Name == "IHasConcurrencyStamp");
            }

            var queryableProperties = GetQueryableProperties(properties);

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using System.Linq;");
            builder.AppendLine("using System.Threading;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using CrestCreates.Aop.Interceptors;");
            builder.AppendLine("using CrestCreates.Authorization.Abstractions;");
            builder.AppendLine("using CrestCreates.Application.Contracts.DTOs.Common;");
            builder.AppendLine("using CrestCreates.Application.Contracts.Query;");
            builder.AppendLine("using CrestCreates.Domain.Exceptions;");
            builder.AppendLine("using CrestCreates.Domain.Repositories;");
            builder.AppendLine("using CrestCreates.Domain.Shared.DataFilter;");
            builder.AppendLine("using CrestCreates.Domain.Shared.Entities;");
            builder.AppendLine("using CrestCreates.Domain.Shared.Entities.Auditing;");
            builder.AppendLine("using CrestCreates.Domain.Shared.Exceptions;");
            builder.AppendLine($"using {namespaceName};");
            builder.AppendLine($"using {namespaceName}.Dtos;");
            builder.AppendLine($"using {namespaceName}.Mappings;");
            builder.AppendLine($"using {namespaceName}.Permissions;");
            builder.AppendLine($"using {namespaceName}.Services;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Services");
            builder.AppendLine("{");

            if (generateAsBaseClass)
            {
                builder.AppendLine($"    public abstract class {entityName}CrudServiceBase : I{entityName}AppService");
                builder.AppendLine("    {");
                builder.AppendLine($"        protected readonly ICrestRepositoryBase<{entityName}, {idType}> Repository;");
                builder.AppendLine("        protected readonly IPermissionChecker PermissionChecker;");
                builder.AppendLine("        protected readonly ICurrentUser CurrentUser;");
                builder.AppendLine("        protected readonly IDataPermissionFilter DataPermissionFilter;");
                builder.AppendLine();
                builder.AppendLine($"        protected {entityName}CrudServiceBase(");
                builder.AppendLine($"            ICrestRepositoryBase<{entityName}, {idType}> repository,");
                builder.AppendLine("            IPermissionChecker permissionChecker,");
                builder.AppendLine("            ICurrentUser currentUser,");
                builder.AppendLine("            IDataPermissionFilter dataPermissionFilter)");
                builder.AppendLine("        {");
                builder.AppendLine("            Repository = repository ?? throw new ArgumentNullException(nameof(repository));");
                builder.AppendLine("            PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));");
                builder.AppendLine("            CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));");
                builder.AppendLine("            DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));");
                builder.AppendLine("        }");
            }
            else
            {
                builder.AppendLine($"    public partial class {entityName}AppService : I{entityName}AppService");
                builder.AppendLine("    {");
                builder.AppendLine($"        protected readonly ICrestRepositoryBase<{entityName}, {idType}> Repository;");
                builder.AppendLine("        protected readonly IPermissionChecker PermissionChecker;");
                builder.AppendLine("        protected readonly ICurrentUser CurrentUser;");
                builder.AppendLine("        protected readonly IDataPermissionFilter DataPermissionFilter;");
                builder.AppendLine();
                builder.AppendLine($"        public {entityName}AppService(");
                builder.AppendLine($"            ICrestRepositoryBase<{entityName}, {idType}> repository,");
                builder.AppendLine("            IPermissionChecker permissionChecker,");
                builder.AppendLine("            ICurrentUser currentUser,");
                builder.AppendLine("            IDataPermissionFilter dataPermissionFilter)");
                builder.AppendLine("        {");
                builder.AppendLine("            Repository = repository ?? throw new ArgumentNullException(nameof(repository));");
                builder.AppendLine("            PermissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));");
                builder.AppendLine("            CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));");
                builder.AppendLine("            DataPermissionFilter = dataPermissionFilter ?? throw new ArgumentNullException(nameof(dataPermissionFilter));");
                builder.AppendLine("        }");
            }

            // Allowed query fields
            builder.AppendLine();
            builder.AppendLine($"        private static readonly HashSet<string> AllowedQueryFields = new(StringComparer.OrdinalIgnoreCase)");
            builder.AppendLine("        {");
            foreach (var prop in queryableProperties)
            {
                builder.AppendLine($"            \"{prop.Name}\",");
            }
            builder.AppendLine("        };");

            // Permission check helper
            builder.AppendLine();
            builder.AppendLine("        protected virtual Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            return PermissionChecker.CheckAsync(permissionName);");
            builder.AppendLine("        }");

            // Data permission filter helper
            builder.AppendLine();
            builder.AppendLine($"        protected virtual async Task<IQueryable<{entityName}>> ApplyDataPermissionFilterAsync(IQueryable<{entityName}> query)");
            builder.AppendLine("        {");
            builder.AppendLine("            return await DataPermissionFilter.ApplyFilterAsync(query);");
            builder.AppendLine("        }");

            // Audit helpers
            builder.AppendLine();
            builder.AppendLine($"        protected virtual Task SetCreationAuditPropertiesAsync({entityName} entity)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (entity is IMustHaveTenant mustHaveTenant)");
            builder.AppendLine("                mustHaveTenant.TenantId = CurrentUser.TenantId ?? throw new InvalidOperationException(\"当前用户没有关联租户\");");
            builder.AppendLine("            var creatorId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;");
            builder.AppendLine("            if (entity is IHasCreator hasCreator)");
            builder.AppendLine("                hasCreator.CreatorId = creatorId;");
            builder.AppendLine("            if (entity is IAuditedEntity audited)");
            builder.AppendLine("            {");
            builder.AppendLine("                audited.CreationTime = DateTime.UtcNow;");
            builder.AppendLine("                audited.CreatorId = creatorId;");
            builder.AppendLine("            }");
            builder.AppendLine("            return Task.CompletedTask;");
            builder.AppendLine("        }");

            builder.AppendLine();
            builder.AppendLine($"        protected virtual Task SetModificationAuditPropertiesAsync({entityName} entity)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (entity is IAuditedEntity audited)");
            builder.AppendLine("            {");
            builder.AppendLine("                audited.LastModificationTime = DateTime.UtcNow;");
            builder.AppendLine("                audited.LastModifierId = Guid.TryParse(CurrentUser.Id, out var userId) ? userId : (Guid?)null;");
            builder.AppendLine("            }");
            builder.AppendLine("            return Task.CompletedTask;");
            builder.AppendLine("        }");

            builder.AppendLine();
            builder.AppendLine($"        protected virtual Task ValidateDataOwnershipAsync({entityName} entity)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (entity is IMustHaveTenant mustHaveTenant && mustHaveTenant.TenantId != CurrentUser.TenantId)");
            builder.AppendLine("                throw new UnauthorizedAccessException(\"您没有权限访问此数据：租户不匹配\");");
            builder.AppendLine("            return Task.CompletedTask;");
            builder.AppendLine("        }");

            // Field validation guards
            builder.AppendLine();
            builder.AppendLine("        private static void EnsureAllowedFilterFields(IEnumerable<FilterDescriptor>? filters)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (filters == null)");
            builder.AppendLine("                return;");
            builder.AppendLine();
            builder.AppendLine("            foreach (var filter in filters)");
            builder.AppendLine("            {");
            builder.AppendLine("                if (!AllowedQueryFields.Contains(filter.Field))");
            builder.AppendLine($"                    throw new CrestBusinessException(\"Crest.Crud.InvalidFilterField\", typeof({entityName}).Name, filter.Field);");
            builder.AppendLine("            }");
            builder.AppendLine("        }");

            builder.AppendLine();
            builder.AppendLine("        private static void EnsureAllowedSortFields(IEnumerable<SortDescriptor>? sorts)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (sorts == null)");
            builder.AppendLine("                return;");
            builder.AppendLine();
            builder.AppendLine("            foreach (var sort in sorts)");
            builder.AppendLine("            {");
            builder.AppendLine("                if (!AllowedQueryFields.Contains(sort.Field))");
            builder.AppendLine($"                    throw new CrestBusinessException(\"Crest.Crud.InvalidSortField\", typeof({entityName}).Name, sort.Field);");
            builder.AppendLine("            }");
            builder.AppendLine("        }");

            // Create method
            builder.AppendLine();
            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 创建 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [UnitOfWorkMo]");
            builder.AppendLine($"        public virtual async Task<{entityName}Dto> CreateAsync(Create{entityName}Dto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine($"            await CheckPermissionAsync({entityName}CrudPermissions.Create, cancellationToken);");
            builder.AppendLine("            await ValidateCreateAsync(input, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine($"            var entity = {entityName}ObjectMappings.To{entityName}(input);");
            builder.AppendLine($"            await SetCreationAuditPropertiesAsync(entity);");
            builder.AppendLine($"            await OnCreatingAsync(entity, input, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine("            var created = await Repository.InsertAsync(entity, cancellationToken);");
            builder.AppendLine("            await OnCreatedAsync(created, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine($"            return {entityName}ObjectMappings.To{entityName}Dto(created);");
            builder.AppendLine("        }");

            // GetById method
            builder.AppendLine();
            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 根据 ID 获取 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public virtual async Task<{entityName}Dto?> GetByIdAsync({idType} id, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine($"            await CheckPermissionAsync({entityName}CrudPermissions.Get, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine("            var query = Repository.GetQueryable();");
            builder.AppendLine("            query = await ApplyDataPermissionFilterAsync(query);");
            builder.AppendLine("            var entity = query.FirstOrDefault(x => x.Id.Equals(id));");
            builder.AppendLine("            if (entity == null)");
            builder.AppendLine($"                throw new CrestEntityNotFoundException(typeof({entityName}).Name, id);");
            builder.AppendLine();
            builder.AppendLine("            await ValidateDataOwnershipAsync(entity);");
            builder.AppendLine($"            return {entityName}ObjectMappings.To{entityName}Dto(entity);");
            builder.AppendLine("        }");

            // GetList method
            builder.AppendLine();
            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 获取 {entityName} 分页列表");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        public virtual async Task<PagedResultDto<{entityName}Dto>> GetListAsync({entityName}ListRequestDto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine($"            await CheckPermissionAsync({entityName}CrudPermissions.Search, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine("            EnsureAllowedFilterFields(input.Filters);");
            builder.AppendLine("            EnsureAllowedSortFields(input.Sorts);");
            builder.AppendLine();
            builder.AppendLine("            var query = Repository.GetQueryable();");
            builder.AppendLine("            query = await ApplyDataPermissionFilterAsync(query);");
            builder.AppendLine("            query = await ConfigureListQueryAsync(query, input, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine($"            query = QueryExecutor<{entityName}>.ApplyFilters(query, input.Filters ?? new List<FilterDescriptor>());");
            builder.AppendLine($"            query = QueryExecutor<{entityName}>.ApplySorts(query, input.Sorts ?? new List<SortDescriptor>());");
            builder.AppendLine();
            builder.AppendLine("            var totalCount = query.Count();");
            builder.AppendLine($"            query = QueryExecutor<{entityName}>.ApplyPaging(query, input.GetSkipCount(), input.PageSize);");
            builder.AppendLine();
            builder.AppendLine("            var entities = query.ToList();");
            builder.AppendLine($"            var dtos = entities.Select({entityName}ObjectMappings.To{entityName}Dto).ToList();");
            builder.AppendLine();
            builder.AppendLine($"            return new PagedResultDto<{entityName}Dto>(dtos, totalCount, input.PageIndex, input.PageSize);");
            builder.AppendLine("        }");

            // Update method
            builder.AppendLine();
            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 更新 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [UnitOfWorkMo]");
            builder.AppendLine($"        public virtual async Task<{entityName}Dto> UpdateAsync({idType} id, Update{entityName}Dto input, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (input == null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(input));");
            builder.AppendLine();
            builder.AppendLine($"            await CheckPermissionAsync({entityName}CrudPermissions.Update, cancellationToken);");
            builder.AppendLine("            await ValidateUpdateAsync(id, input, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine("            var entity = await Repository.GetAsync(id, cancellationToken);");
            builder.AppendLine("            if (entity == null)");
            builder.AppendLine($"                throw new CrestEntityNotFoundException(typeof({entityName}).Name, id);");
            builder.AppendLine();
            builder.AppendLine("            await ValidateDataOwnershipAsync(entity);");
            builder.AppendLine("            await OnUpdatingAsync(entity, input, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine($"            {entityName}ObjectMappings.ApplyTo(input, entity);");
            builder.AppendLine("            await SetModificationAuditPropertiesAsync(entity);");
            builder.AppendLine();
            builder.AppendLine("            var updated = await Repository.UpdateAsync(entity, cancellationToken);");
            builder.AppendLine("            await OnUpdatedAsync(updated, cancellationToken);");
            builder.AppendLine();
            builder.AppendLine($"            return {entityName}ObjectMappings.To{entityName}Dto(updated);");
            builder.AppendLine("        }");

            // Delete method
            builder.AppendLine();
            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// 删除 {entityName}");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [UnitOfWorkMo]");
            builder.AppendLine($"        public virtual async Task DeleteAsync({idType} id, string? expectedStamp = null, CancellationToken cancellationToken = default)");
            builder.AppendLine("        {");
            builder.AppendLine($"            await CheckPermissionAsync({entityName}CrudPermissions.Delete, cancellationToken);");
            builder.AppendLine();

            if (hasConcurrencyStamp)
            {
                builder.AppendLine("            if (string.IsNullOrWhiteSpace(expectedStamp))");
                builder.AppendLine($"                throw new CrestPreconditionRequiredException(typeof({entityName}).Name, id);");
                builder.AppendLine();
                builder.AppendLine("            await Repository.DeleteAsync(id, expectedStamp!, cancellationToken);");
                builder.AppendLine("            await OnDeletedAsync(id, cancellationToken);");
                builder.AppendLine("            return;");
            }
            else
            {
                builder.AppendLine("            var entity = await Repository.GetAsync(id, cancellationToken);");
                builder.AppendLine("            if (entity == null)");
                builder.AppendLine($"                throw new CrestEntityNotFoundException(typeof({entityName}).Name, id);");
                builder.AppendLine();
                builder.AppendLine("            await ValidateDataOwnershipAsync(entity);");
                builder.AppendLine("            await OnDeletingAsync(entity, cancellationToken);");
                builder.AppendLine("            await Repository.DeleteAsync(entity, cancellationToken);");
                builder.AppendLine("            await OnDeletedAsync(id, cancellationToken);");
            }

            builder.AppendLine("        }");

            // Extension hooks
            builder.AppendLine();
            builder.AppendLine("        #region Extension Hooks");
            builder.AppendLine();
            builder.AppendLine($"        protected virtual Task ValidateCreateAsync(Create{entityName}Dto input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task ValidateUpdateAsync({idType} id, Update{entityName}Dto input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task OnCreatingAsync({entityName} entity, Create{entityName}Dto input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task OnCreatedAsync({entityName} entity, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task OnUpdatingAsync({entityName} entity, Update{entityName}Dto input, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task OnUpdatedAsync({entityName} entity, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task OnDeletingAsync({entityName} entity, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task OnDeletedAsync({idType} id, CancellationToken cancellationToken = default) => Task.CompletedTask;");
            builder.AppendLine($"        protected virtual Task<IQueryable<{entityName}>> ConfigureListQueryAsync(IQueryable<{entityName}> query, {entityName}ListRequestDto input, CancellationToken cancellationToken = default) => Task.FromResult(query);");
            builder.AppendLine();
            builder.AppendLine("        #endregion");

            builder.AppendLine("    }");
            builder.AppendLine("}");

            var fileName = generateAsBaseClass ? $"{entityName}CrudServiceBase.g.cs" : $"{entityName}AppService.g.cs";
            context.AddSource(fileName, SourceText.From(builder.ToString(), Encoding.UTF8));
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

        private static readonly HashSet<string> s_platformExclusions = new(StringComparer.Ordinal)
        {
            "DomainEvents",
        };

        private List<IPropertySymbol> GetEntityProperties(INamedTypeSymbol entityClass)
        {
            var properties = new List<IPropertySymbol>();
            var seen = new HashSet<string>();

            for (var current = entityClass; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (member.DeclaredAccessibility == Accessibility.Public &&
                        !member.IsStatic &&
                        member.CanBeReferencedByName &&
                        !s_platformExclusions.Contains(member.Name) &&
                        seen.Add(member.Name))
                    {
                        properties.Add(member);
                    }
                }
            }

            return properties;
        }

        private static List<IPropertySymbol> GetQueryableProperties(List<IPropertySymbol> properties)
        {
            var excludedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "DomainEvents", "IsDeleted", "DeletionTime", "DeleterId"
            };

            return properties
                .Where(p => !excludedNames.Contains(p.Name))
                .Where(p => p.Type.TypeKind != TypeKind.Array)
                .Where(p => !IsNavigationProperty(p))
                .ToList();
        }

        private static bool IsNavigationProperty(IPropertySymbol property)
        {
            if (property.Type.TypeKind == TypeKind.Class &&
                property.Type.SpecialType == SpecialType.None &&
                property.Type.ToDisplayString() != "System.String" &&
                property.Type.ToDisplayString() != "System.DateTime" &&
                property.Type.ToDisplayString() != "System.DateTimeOffset" &&
                property.Type.ToDisplayString() != "System.Guid" &&
                property.Type.TypeKind != TypeKind.Enum)
            {
                return true;
            }

            return false;
        }
    }
}
