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

namespace CrestCreates.CodeGenerator.EntityGenerator
{
    [Generator]
    public class EntitySourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 创建增量数据源：查找带有EntityAttribute的类
            var entityClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsEntityCandidate(node),
                    transform: static (ctx, _) => GetEntityClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            // 注册源代码生成
            context.RegisterSourceOutput(entityClasses, ExecuteGeneration);
        }

        private static bool IsEntityCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration &&
                   classDeclaration.AttributeLists.Count > 0;
        }

        private static INamedTypeSymbol? GetEntityClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
            
            if (symbol != null && HasEntityAttribute(symbol))
            {
                return symbol;
            }
            
            return null;
        }

        private static bool HasEntityAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "EntityAttribute" ||
                attr.AttributeClass?.Name == "Entity");
        }

        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> entityClasses)
        {
            if (entityClasses.IsDefaultOrEmpty)
                return;

            // 去重处理
            var processedEntities = new HashSet<string>();

            foreach (var entityClass in entityClasses)
            {
                if (entityClass == null) continue;

                var entityFullName = entityClass.ToDisplayString();
                if (processedEntities.Contains(entityFullName))
                    continue;

                processedEntities.Add(entityFullName);

                try
                {
                    // 生成仓储接口和实现
                    if (HasAttribute(entityClass, "EntityAttribute") &&
                        GetAttributeProperty(entityClass, "GenerateRepository", true))
                    {
                        GenerateRepositoryInterface(context, entityClass);
                        GenerateRepositoryImplementation(context, entityClass);
                    }

                    // 生成查询扩展
                    GenerateQueryExtensions(context, entityClass);

                    // 生成ORM映射
                    GenerateOrmMappings(context, entityClass);
                }
                catch (Exception ex)
                {
                    // 记录错误但继续处理其他实体
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("CCCG001", "Code generation error",
                            $"Error generating code for {entityFullName}: {ex.Message}",
                            "CodeGeneration", DiagnosticSeverity.Warning, true),
                        Location.None));
                }
            }
        }        private void GenerateRepositoryInterface(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var idType = GetEntityIdType(entityClass);
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();

            var sourceCode = $@"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using {namespaceName};

namespace {namespaceName}.Repositories
{{
    public partial interface I{entityName}Repository : IRepository<{entityName}, {idType}>
    {{
        // 可以自定义扩展方法
    }}
}}";

            context.AddSource($"I{entityName}Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }

        private void GenerateRepositoryImplementation(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var idType = GetEntityIdType(entityClass);
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var ormProvider = GetAttributeProperty(entityClass, "OrmProvider", "EfCore");

            if (ormProvider == "EfCore")
            {
                var sourceCode = $@"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Infrastructure.EntityFrameworkCore.Repositories;
using CrestCreates.Infrastructure.EntityFrameworkCore.DbContexts;
using {namespaceName};
using {namespaceName}.Repositories;

namespace {namespaceName}.EntityFrameworkCore.Repositories
{{
    public class EfCore{entityName}Repository : EfCoreRepository<{entityName}, {idType}>, I{entityName}Repository
    {{
        public EfCore{entityName}Repository(CrestCreatesDbContext dbContext) 
            : base(dbContext)
        {{
        }}
        
        // 可以实现自定义扩展方法
    }}
}}";

                context.AddSource($"EfCore{entityName}Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "SqlSugar")
            {
                var sourceCode = $@"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using SqlSugar;
using CrestCreates.Infrastructure.SqlSugar.Repositories;
using {namespaceName};
using {namespaceName}.Repositories;

namespace {namespaceName}.SqlSugar.Repositories
{{
    public class SqlSugar{entityName}Repository : SqlSugarRepository<{entityName}, {idType}>, I{entityName}Repository
    {{
        public SqlSugar{entityName}Repository(ISqlSugarClient sqlSugarClient) 
            : base(sqlSugarClient)
        {{
        }}
        
        // 可以实现自定义扩展方法
    }}
}}";

                context.AddSource($"SqlSugar{entityName}Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "FreeSql")
            {
                var sourceCode = $@"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FreeSql;
using CrestCreates.Infrastructure.FreeSql.Repositories;
using {namespaceName};
using {namespaceName}.Repositories;

namespace {namespaceName}.FreeSql.Repositories
{{
    public class FreeSql{entityName}Repository : FreeSqlRepository<{entityName}, {idType}>, I{entityName}Repository
    {{
        public FreeSql{entityName}Repository(IFreeSql freeSql) 
            : base(freeSql)
        {{
        }}
        
        // 可以实现自定义扩展方法
    }}
}}";

                context.AddSource($"FreeSql{entityName}Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private void GenerateQueryExtensions(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();

            var sourceCode = $@"
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;
using {namespaceName};

namespace {namespaceName}.Extensions
{{
    public static class {entityName}QueryExtensions
    {{
        public static IQueryable<{entityName}> PageBy(this IQueryable<{entityName}> query, int pageNumber, int pageSize)
        {{
            return query.Skip((pageNumber - 1) * pageSize).Take(pageSize);
        }}
        
        public static IQueryable<{entityName}> WhereIf<T>(this IQueryable<{entityName}> query, bool condition, Expression<Func<{entityName}, bool>> predicate)
        {{
            return condition ? query.Where(predicate) : query;
        }}
        
        // 更多扩展方法可以在这里添加
    }}
}}";

            context.AddSource($"{entityName}QueryExtensions.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }        private void GenerateOrmMappings(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var ormProvider = GetAttributeProperty(entityClass, "OrmProvider", "EfCore");

            if (ormProvider == "EfCore")
            {
                var sourceCode = $@"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using {namespaceName};

namespace {namespaceName}.EntityFrameworkCore.Mappings
{{
    public class {entityName}Mapping : IEntityTypeConfiguration<{entityName}>
    {{
        public void Configure(EntityTypeBuilder<{entityName}> builder)
        {{
            builder.ToTable(""{entityName}s"");
            
            builder.HasKey(e => e.Id);
            
            // 自动生成审计字段映射
            {(IsAudited(entityClass) ? GenerateAuditMappings() : string.Empty)}
            
            // 这里可以添加更多属性的映射逻辑
        }}
    }}
}}";

                context.AddSource($"{entityName}Mapping.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "SqlSugar")
            {
                var sourceCode = $@"
using SqlSugar;
using {namespaceName};

namespace {namespaceName}.SqlSugar.Mappings
{{
    public static class {entityName}SugarMapping
    {{
        public static void Configure(CodeFirstProvider codeFirst)
        {{
            codeFirst.InitTables(typeof({entityName}));
        }}
        
        public static void ConfigureTable(ISqlSugarClient db)
        {{
            db.CodeFirst.InitTables<{entityName}>();
        }}
    }}
}}";

                context.AddSource($"{entityName}SugarMapping.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "FreeSql")
            {
                var sourceCode = $@"
using FreeSql;
using FreeSql.DataAnnotations;
using {namespaceName};

namespace {namespaceName}.FreeSql.Mappings
{{
    public static class {entityName}FreeSqlMapping
    {{
        public static void Configure(ICodeFirst codeFirst)
        {{
            codeFirst.Entity<{entityName}>(eb =>
            {{
                eb.ToTable(""{entityName}s"");
                eb.HasKey(e => e.Id);
                
                // 审计字段映射
                {(IsAudited(entityClass) ? GenerateFreeSqlAuditMappings() : string.Empty)}
            }});
        }}
    }}
}}";

                context.AddSource($"{entityName}FreeSqlMapping.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private string GenerateAuditMappings()
        {
            return @"
            // 审计字段
            builder.Property(e => e.CreationTime).IsRequired();
            builder.Property(e => e.CreatorId).IsRequired(false);
            builder.Property(e => e.LastModificationTime).IsRequired(false);
            builder.Property(e => e.LastModifierId).IsRequired(false);";
        }

        private string GenerateFreeSqlAuditMappings()
        {
            return @"
                // 审计字段
                eb.Property(e => e.CreationTime).IsRequired();
                eb.Property(e => e.CreatorId).IsRequired(false);
                eb.Property(e => e.LastModificationTime).IsRequired(false);
                eb.Property(e => e.LastModifierId).IsRequired(false);";
        }

        private bool IsAudited(INamedTypeSymbol entityClass)
        {
            // 检查实体是否继承了审计接口
            return entityClass.AllInterfaces.Any(i => i.Name == "IAuditedEntity" || i.Name == "IFullyAuditedEntity")
                   || GetAttributeProperty(entityClass, "GenerateAuditing", true);
        }

        private string GetEntityIdType(INamedTypeSymbol entityClass)
        {
            // 尝试从Entity<TId>或AggregateRoot<TId>获取泛型参数
            var baseType = entityClass.BaseType;
            while (baseType != null)
            {
                if ((baseType.Name == "Entity" || baseType.Name == "AggregateRoot" ||
                     baseType.Name == "AuditedEntity" || baseType.Name == "AuditedAggregateRoot" ||
                     baseType.Name == "FullyAuditedEntity" || baseType.Name == "FullyAuditedAggregateRoot")
                    && baseType.TypeArguments.Length > 0)
                {
                    return baseType.TypeArguments[0].ToDisplayString();
                }

                baseType = baseType.BaseType;
            }

            // 默认返回int
            return "int";
        }

        private bool HasAttribute(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes()
                .Any(attr => attr.AttributeClass.Name == attributeName ||
                             attr.AttributeClass.Name == attributeName + "Attribute");
        }

        private T GetAttributeProperty<T>(ISymbol symbol, string propertyName, T defaultValue)
        {
            var attr = symbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass.Name == "EntityAttribute" ||
                                        attr.AttributeClass.Name == "Entity");

            if (attr == null)
                return defaultValue;

            var namedArg = attr.NamedArguments
                .FirstOrDefault(arg => arg.Key == propertyName);

            if (namedArg.Key == propertyName && namedArg.Value.Value is T value)
                return value;

            return defaultValue;
        }    }
}