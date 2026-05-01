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
using CrestCreates.CodeGenerator.ObjectMappingGenerator;

namespace CrestCreates.CodeGenerator.EntityGenerator
{
    [Generator]
    public class EntitySourceGenerator : IIncrementalGenerator
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
                attr.AttributeClass?.Name == "EntityAttribute" || attr.AttributeClass?.Name == "Entity");
        }

        private void ExecuteGeneration(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> entityClasses)
        {
            if (entityClasses.IsDefaultOrEmpty) return;

            var processedEntities = new HashSet<string>();

            foreach (var entityClass in entityClasses)
            {
                if (entityClass == null) continue;

                var entityFullName = entityClass.ToDisplayString();
                if (processedEntities.Contains(entityFullName)) continue;

                processedEntities.Add(entityFullName);

                try
                {
                    GenerateRepositoryBase(context, entityClass);

                    if (HasAttribute(entityClass, "EntityAttribute") && GetAttributeProperty(entityClass, "GenerateRepository", true))
                    {
                        GenerateRepositoryInterface(context, entityClass);
                        GenerateRepositoryImplementation(context, entityClass);
                    }

                    GenerateOrmMappings(context, entityClass);
                    GenerateEntityDto(context, entityClass);
                    GenerateCreateEntityDto(context, entityClass);
                    GenerateUpdateEntityDto(context, entityClass);
                    GenerateMappingExtensions(context, entityClass);
                    GenerateEntityExtensions(context, entityClass);
                    GenerateValidationRules(context, entityClass);

                    if (GetAttributeProperty(entityClass, "GeneratePermissions", true))
                    {
                        GenerateEntityPermissions(context, entityClass);
                    }
                }
                catch (Exception ex)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("CCCG001", "Code generation error",
                            $"Error generating code for {entityFullName}: {ex.Message}",
                            "CodeGeneration", DiagnosticSeverity.Warning, true),
                        Location.None));
                }
            }
        }

        private string GetTargetNamespace(string entityNamespace, GeneratedCodeType codeType)
        {
            if (!entityNamespace.Contains(".Domain.Entities"))
            {
                return codeType switch
                {
                    GeneratedCodeType.Dto => $"{entityNamespace}.Dtos",
                    GeneratedCodeType.CreateDto => $"{entityNamespace}.Dtos",
                    GeneratedCodeType.UpdateDto => $"{entityNamespace}.Dtos",
                    GeneratedCodeType.MappingProfile => $"{entityNamespace}.Mappings",
                    _ => entityNamespace
                };
            }

            var baseNamespace = entityNamespace.Replace(".Domain.Entities", "");

            return codeType switch
            {
                GeneratedCodeType.Dto => $"{baseNamespace}.Application.Contracts.DTOs",
                GeneratedCodeType.CreateDto => $"{baseNamespace}.Application.Contracts.DTOs",
                GeneratedCodeType.UpdateDto => $"{baseNamespace}.Application.Contracts.DTOs",
                GeneratedCodeType.MappingProfile => $"{baseNamespace}.Application",
                _ => entityNamespace
            };
        }

        private enum GeneratedCodeType
        {
            Dto,
            CreateDto,
            UpdateDto,
            MappingProfile
        }

        /// <summary>
        /// Collects all properties of the entity, including inherited members from base classes
        /// (e.g. ConcurrencyStamp declared in AuditedEntity{TId}), via GetMembers() which traverses
        /// the base-type chain.  No explicit base-type walk is needed.
        /// </summary>
        private List<IPropertySymbol> GetAllEntityProperties(INamedTypeSymbol entityClass)
        {
            var properties = new List<IPropertySymbol>();
            var propertyNames = new HashSet<string>();

            foreach (var member in entityClass.GetMembers())
            {
                if (member is not IPropertySymbol property || property.IsStatic)
                    continue;

                if (propertyNames.Contains(property.Name))
                    continue;

                propertyNames.Add(property.Name);
                properties.Add(property);
            }

            return properties;
        }

        private void GenerateRepositoryInterface(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var idType = GetEntityIdType(entityClass);
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();

            var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
            sourceCode += "using System;\n";
            sourceCode += "using System.Collections.Generic;\n";
            sourceCode += "using System.Linq;\n";
            sourceCode += "using System.Linq.Expressions;\n";
            sourceCode += "using System.Threading;\n";
            sourceCode += "using System.Threading.Tasks;\n";
            sourceCode += "using " + namespaceName + ";\n";
            sourceCode += "\n";
            sourceCode += "namespace " + namespaceName + ".Repositories\n";
            sourceCode += "{\n";
            sourceCode += "    /// <summary>\n";
            sourceCode += "    /// " + entityName + " 实体的仓储接口\n";
            sourceCode += "    /// </summary>\n";
            sourceCode += "    partial interface I" + entityName + "Repository\n";
            sourceCode += "    {\n";
            sourceCode += "        // 基础仓库方法\n";
            sourceCode += "        Task<" + entityName + "> GetByIdAsync(" + idType + " id, CancellationToken cancellationToken = default);\n";
            sourceCode += "        Task<List<" + entityName + ">> GetAllAsync(CancellationToken cancellationToken = default);\n";
            sourceCode += "        Task<" + entityName + "> AddAsync(" + entityName + " entity, CancellationToken cancellationToken = default);\n";
            sourceCode += "        Task<" + entityName + "> UpdateAsync(" + entityName + " entity, CancellationToken cancellationToken = default);\n";
            sourceCode += "        Task DeleteAsync(" + entityName + " entity, CancellationToken cancellationToken = default);\n";
            sourceCode += "        Task<List<" + entityName + ">> FindAsync(Expression<Func<" + entityName + ", bool>> predicate, CancellationToken cancellationToken = default);\n";
            sourceCode += "\n";
            sourceCode += "        // 基于实体属性的查询方法\n";
            sourceCode += GenerateRepositoryQueryMethods(entityClass);
            sourceCode += "\n";
            sourceCode += "        // 分页查询方法\n";
            sourceCode += "        Task<(List<" + entityName + "> Items, int TotalCount)> GetPagedListAsync(\n";
            sourceCode += "            int pageNumber, \n";
            sourceCode += "            int pageSize, \n";
            sourceCode += "            Expression<Func<" + entityName + ", bool>>? predicate = null,\n";
            sourceCode += "            Expression<Func<" + entityName + ", object>>? orderBy = null,\n";
            sourceCode += "            bool ascending = true,\n";
            sourceCode += "            CancellationToken cancellationToken = default);\n";
            sourceCode += "\n";
            sourceCode += "        // 软删除相关方法（如果支持）\n";
            sourceCode += IsFullyAudited(entityClass) ? GenerateSoftDeleteMethods(entityName, idType) : string.Empty;
            sourceCode += "    }\n";
            sourceCode += "}";

            context.AddSource("I" + entityName + "Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }

        private void GenerateRepositoryImplementation(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var idType = GetEntityIdType(entityClass);
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var ormProvider = GetAttributeProperty(entityClass, "OrmProvider", "EfCore");

            if (ormProvider == "EfCore")
            {
                var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
                sourceCode += "using System;\n";
                sourceCode += "using System.Collections.Generic;\n";
                sourceCode += "using System.Linq;\n";
                sourceCode += "using System.Linq.Expressions;\n";
                sourceCode += "using System.Threading;\n";
                sourceCode += "using System.Threading.Tasks;\n";
                sourceCode += "using Microsoft.EntityFrameworkCore;\n";
                sourceCode += "using CrestCreates.Domain.Repositories;\n";
                sourceCode += "using " + namespaceName + ";\n";
                sourceCode += "using " + namespaceName + ".Repositories;\n";
                sourceCode += "\n";
                sourceCode += "namespace " + namespaceName + ".EntityFrameworkCore.Repositories\n";
                sourceCode += "{\n";
                sourceCode += "    public class EfCore" + entityName + "Repository : I" + entityName + "Repository\n";
                sourceCode += "    {\n";
                sourceCode += "        protected readonly DbContext DbContext;\n";
                sourceCode += "\n";
                sourceCode += "        public EfCore" + entityName + "Repository(DbContext dbContext) \n";
                sourceCode += "        {\n";
                sourceCode += "            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 基础仓库方法\n";
                sourceCode += "        public async Task<" + entityName + "> GetByIdAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await DbContext.Set<" + entityName + ">().FindAsync(new object[] { id }, cancellationToken);\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<List<" + entityName + ">> GetAllAsync(CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await DbContext.Set<" + entityName + ">().ToListAsync(cancellationToken);\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<" + entityName + "> AddAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            DbContext.Set<" + entityName + ">().Add(entity);\n";
                sourceCode += "            if (DbContext.Database.CurrentTransaction == null)\n";
                sourceCode += "            {\n";
                sourceCode += "                await DbContext.SaveChangesAsync(cancellationToken);\n";
                sourceCode += "            }\n";
                sourceCode += "            return entity;\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<" + entityName + "> UpdateAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            DbContext.Set<" + entityName + ">().Update(entity);\n";
                sourceCode += "            if (DbContext.Database.CurrentTransaction == null)\n";
                sourceCode += "            {\n";
                sourceCode += "                await DbContext.SaveChangesAsync(cancellationToken);\n";
                sourceCode += "            }\n";
                sourceCode += "            return entity;\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task DeleteAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            DbContext.Set<" + entityName + ">().Remove(entity);\n";
                sourceCode += "            if (DbContext.Database.CurrentTransaction == null)\n";
                sourceCode += "            {\n";
                sourceCode += "                await DbContext.SaveChangesAsync(cancellationToken);\n";
                sourceCode += "            }\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<List<" + entityName + ">> FindAsync(Expression<Func<" + entityName + ", bool>> predicate, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await DbContext.Set<" + entityName + ">().Where(predicate).ToListAsync(cancellationToken);\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 分页查询方法\n";
                sourceCode += "        public async Task<(List<" + entityName + "> Items, int TotalCount)> GetPagedListAsync(\n";
                sourceCode += "            int pageNumber, \n";
                sourceCode += "            int pageSize, \n";
                sourceCode += "            Expression<Func<" + entityName + ", bool>>? predicate = null,\n";
                sourceCode += "            Expression<Func<" + entityName + ", object>>? orderBy = null,\n";
                sourceCode += "            bool ascending = true,\n";
                sourceCode += "            CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            var query = predicate != null ? DbContext.Set<" + entityName + ">().Where(predicate) : DbContext.Set<" + entityName + ">();\n";
                sourceCode += "            \n";
                sourceCode += "            if (orderBy != null)\n";
                sourceCode += "            {\n";
                sourceCode += "                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);\n";
                sourceCode += "            }\n";
                sourceCode += "            \n";
                sourceCode += "            var totalCount = await query.CountAsync(cancellationToken);\n";
                sourceCode += "            var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);\n";
                sourceCode += "            \n";
                sourceCode += "            return (items, totalCount);\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 基于属性的查询方法\n";
                sourceCode += GenerateRepositoryPropertyMethods(entityClass, idType, "EfCore");
                sourceCode += "        \n";
                sourceCode += "        // 软删除相关方法\n";
                if (IsFullyAudited(entityClass))
                {
                    sourceCode += "        public async Task SoftDeleteAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            var entity = await GetByIdAsync(id);\n";
                    sourceCode += "            if (entity != null)\n";
                    sourceCode += "            {\n";
                    sourceCode += "                await DeleteAsync(entity);\n";
                    sourceCode += "            }\n";
                    sourceCode += "        }\n";
                    sourceCode += "\n";
                    sourceCode += "        public async Task RestoreAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            // 实现恢复逻辑\n";
                    sourceCode += "            await Task.CompletedTask;\n";
                    sourceCode += "        }\n";
                    sourceCode += "\n";
                    sourceCode += "        public async Task<List<" + entityName + ">> GetNotDeletedAsync(CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            return await DbContext.Set<" + entityName + ">().ToListAsync(cancellationToken);\n";
                    sourceCode += "        }\n";
                }
                sourceCode += "    }\n";
                sourceCode += "}";

                context.AddSource("EfCore" + entityName + "Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "SqlSugar")
            {
                var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
                sourceCode += "using System;\n";
                sourceCode += "using System.Collections.Generic;\n";
                sourceCode += "using System.Linq;\n";
                sourceCode += "using System.Linq.Expressions;\n";
                sourceCode += "using System.Threading;\n";
                sourceCode += "using System.Threading.Tasks;\n";
                sourceCode += "using SqlSugar;\n";
                sourceCode += "using CrestCreates.Domain.Repositories;\n";
                sourceCode += "using " + namespaceName + ";\n";
                sourceCode += "using " + namespaceName + ".Repositories;\n";
                sourceCode += "\n";
                sourceCode += "namespace " + namespaceName + ".SqlSugar.Repositories\n";
                sourceCode += "{\n";
                sourceCode += "    public class SqlSugar" + entityName + "Repository : I" + entityName + "Repository\n";
                sourceCode += "    {\n";
                sourceCode += "        protected readonly ISqlSugarClient Db;\n";
                sourceCode += "\n";
                sourceCode += "        public SqlSugar" + entityName + "Repository(ISqlSugarClient sqlSugarClient) \n";
                sourceCode += "        {\n";
                sourceCode += "            Db = sqlSugarClient ?? throw new ArgumentNullException(nameof(sqlSugarClient));\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 基础仓库方法\n";
                sourceCode += "        public async Task<" + entityName + "> GetByIdAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await Db.Queryable<" + entityName + ">().InSingleAsync(id);\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<List<" + entityName + ">> GetAllAsync(CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await Db.Queryable<" + entityName + ">().ToListAsync();\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<" + entityName + "> AddAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            await Db.Insertable(entity).ExecuteCommandAsync();\n";
                sourceCode += "            return entity;\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<" + entityName + "> UpdateAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            await Db.Updateable(entity).ExecuteCommandAsync();\n";
                sourceCode += "            return entity;\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task DeleteAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            await Db.Deleteable(entity).ExecuteCommandAsync();\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<List<" + entityName + ">> FindAsync(Expression<Func<" + entityName + ", bool>> predicate, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await Db.Queryable<" + entityName + ">().Where(predicate).ToListAsync();\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 分页查询方法\n";
                sourceCode += "        public async Task<(List<" + entityName + "> Items, int TotalCount)> GetPagedListAsync(\n";
                sourceCode += "            int pageNumber, \n";
                sourceCode += "            int pageSize, \n";
                sourceCode += "            Expression<Func<" + entityName + ", bool>>? predicate = null,\n";
                sourceCode += "            Expression<Func<" + entityName + ", object>>? orderBy = null,\n";
                sourceCode += "            bool ascending = true,\n";
                sourceCode += "            CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            var query = predicate != null ? Db.Queryable<" + entityName + ">().Where(predicate) : Db.Queryable<" + entityName + ">();\n";
                sourceCode += "            \n";
                sourceCode += "            if (orderBy != null)\n";
                sourceCode += "            {\n";
                sourceCode += "                query = ascending ? query.OrderBy(orderBy) : query.OrderBy(orderBy, OrderByType.Desc);\n";
                sourceCode += "            }\n";
                sourceCode += "            \n";
                sourceCode += "            var totalCount = await query.CountAsync();\n";
                sourceCode += "            var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();\n";
                sourceCode += "            \n";
                sourceCode += "            return (items, totalCount);\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 基于属性的查询方法\n";
                sourceCode += GenerateRepositoryPropertyMethods(entityClass, idType, "SqlSugar");
                sourceCode += "        \n";
                sourceCode += "        // 软删除相关方法\n";
                if (IsFullyAudited(entityClass))
                {
                    sourceCode += "        public async Task SoftDeleteAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            var entity = await GetByIdAsync(id);\n";
                    sourceCode += "            if (entity != null)\n";
                    sourceCode += "            {\n";
                    sourceCode += "                await DeleteAsync(entity);\n";
                    sourceCode += "            }\n";
                    sourceCode += "        }\n";
                    sourceCode += "\n";
                    sourceCode += "        public async Task RestoreAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            // 实现恢复逻辑\n";
                    sourceCode += "            await Task.CompletedTask;\n";
                    sourceCode += "        }\n";
                    sourceCode += "\n";
                    sourceCode += "        public async Task<List<" + entityName + ">> GetNotDeletedAsync(CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            return await Db.Queryable<" + entityName + ">().ToListAsync();\n";
                    sourceCode += "        }\n";
                }
                sourceCode += "    }\n";
                sourceCode += "}";

                context.AddSource("SqlSugar" + entityName + "Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "FreeSql")
            {
                var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
                sourceCode += "using System;\n";
                sourceCode += "using System.Collections.Generic;\n";
                sourceCode += "using System.Linq;\n";
                sourceCode += "using System.Linq.Expressions;\n";
                sourceCode += "using System.Threading;\n";
                sourceCode += "using System.Threading.Tasks;\n";
                sourceCode += "using FreeSql;\n";
                sourceCode += "using CrestCreates.Domain.Repositories;\n";
                sourceCode += "using " + namespaceName + ";\n";
                sourceCode += "using " + namespaceName + ".Repositories;\n";
                sourceCode += "\n";
                sourceCode += "namespace " + namespaceName + ".FreeSql.Repositories\n";
                sourceCode += "{\n";
                sourceCode += "    public class FreeSql" + entityName + "Repository : I" + entityName + "Repository\n";
                sourceCode += "    {\n";
                sourceCode += "        protected readonly IFreeSql Db;\n";
                sourceCode += "\n";
                sourceCode += "        public FreeSql" + entityName + "Repository(IFreeSql freeSql) \n";
                sourceCode += "        {\n";
                sourceCode += "            Db = freeSql ?? throw new ArgumentNullException(nameof(freeSql));\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 基础仓库方法\n";
                sourceCode += "        public async Task<" + entityName + "> GetByIdAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await Db.Select<" + entityName + ">().Where(e => e.Id.Equals(id)).FirstAsync();\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<List<" + entityName + ">> GetAllAsync(CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await Db.Select<" + entityName + ">().ToListAsync();\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<" + entityName + "> AddAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            await Db.Insert<" + entityName + ">().AppendData(entity).ExecuteAffrowsAsync();\n";
                sourceCode += "            return entity;\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<" + entityName + "> UpdateAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            await Db.Update<" + entityName + ">().SetSource(entity).ExecuteAffrowsAsync();\n";
                sourceCode += "            return entity;\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task DeleteAsync(" + entityName + " entity, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            await Db.Delete<" + entityName + ">().Where(e => e.Id.Equals(entity.Id)).ExecuteAffrowsAsync();\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public async Task<List<" + entityName + ">> FindAsync(Expression<Func<" + entityName + ", bool>> predicate, CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            return await Db.Select<" + entityName + ">().Where(predicate).ToListAsync();\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 分页查询方法\n";
                sourceCode += "        public async Task<(List<" + entityName + "> Items, int TotalCount)> GetPagedListAsync(\n";
                sourceCode += "            int pageNumber, \n";
                sourceCode += "            int pageSize, \n";
                sourceCode += "            Expression<Func<" + entityName + ", bool>>? predicate = null,\n";
                sourceCode += "            Expression<Func<" + entityName + ", object>>? orderBy = null,\n";
                sourceCode += "            bool ascending = true,\n";
                sourceCode += "            CancellationToken cancellationToken = default)\n";
                sourceCode += "        {\n";
                sourceCode += "            var query = predicate != null ? Db.Select<" + entityName + ">().Where(predicate) : Db.Select<" + entityName + ">();\n";
                sourceCode += "            \n";
                sourceCode += "            if (orderBy != null)\n";
                sourceCode += "            {\n";
                sourceCode += "                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);\n";
                sourceCode += "            }\n";
                sourceCode += "            \n";
                sourceCode += "            var totalCount = await query.CountAsync();\n";
                sourceCode += "            var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();\n";
                sourceCode += "            \n";
                sourceCode += "            return (items, (int)totalCount);\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        // 基于属性的查询方法\n";
                sourceCode += GenerateRepositoryPropertyMethods(entityClass, idType, "FreeSql");
                sourceCode += "        \n";
                sourceCode += "        // 软删除相关方法\n";
                if (IsFullyAudited(entityClass))
                {
                    sourceCode += "        public async Task SoftDeleteAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            var entity = await GetByIdAsync(id);\n";
                    sourceCode += "            if (entity != null)\n";
                    sourceCode += "            {\n";
                    sourceCode += "                await DeleteAsync(entity);\n";
                    sourceCode += "            }\n";
                    sourceCode += "        }\n";
                    sourceCode += "\n";
                    sourceCode += "        public async Task RestoreAsync(" + idType + " id, CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            // 实现恢复逻辑\n";
                    sourceCode += "            await Task.CompletedTask;\n";
                    sourceCode += "        }\n";
                    sourceCode += "\n";
                    sourceCode += "        public async Task<List<" + entityName + ">> GetNotDeletedAsync(CancellationToken cancellationToken = default)\n";
                    sourceCode += "        {\n";
                    sourceCode += "            return await Db.Select<" + entityName + ">().ToListAsync();\n";
                    sourceCode += "        }\n";
                }
                sourceCode += "    }\n";
                sourceCode += "}";

                context.AddSource("FreeSql" + entityName + "Repository.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private string GenerateRepositoryPropertyMethods(INamedTypeSymbol entityClass, string idType, string ormProvider)
        {
            var properties = GetEntityProperties(entityClass);
            var methods = new StringBuilder();

            foreach (var prop in properties)
            {
                var propName = prop.Name;
                var propType = prop.Type.ToDisplayString();
                var propLower = propName.ToLowerInvariant();

                methods.AppendLine("        public async Task<List<" + entityClass.Name + ">> FindBy" + propName + "Async(" + propType + " " + propLower + ", CancellationToken cancellationToken = default)\n");
                methods.AppendLine("        {\n");

                if (ormProvider == "EfCore")
                {
                    methods.AppendLine("            return await DbContext.Set<" + entityClass.Name + ">().Where(e => e." + propName + " == " + propLower + ").ToListAsync(cancellationToken);\n");
                }
                else if (ormProvider == "SqlSugar")
                {
                    methods.AppendLine("            return await Db.Queryable<" + entityClass.Name + ">().Where(e => e." + propName + " == " + propLower + ").ToListAsync();\n");
                }
                else if (ormProvider == "FreeSql")
                {
                    methods.AppendLine("            return await Db.Select<" + entityClass.Name + ">().Where(e => e." + propName + " == " + propLower + ").ToListAsync();\n");
                }

                methods.AppendLine("        }\n");
                methods.AppendLine();
            }

            return methods.ToString();
        }

        private void GenerateOrmMappings(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var ormProvider = GetAttributeProperty(entityClass, "OrmProvider", "EfCore");
            var tableName = GetAttributeProperty(entityClass, "TableName", entityName + "s");

            if (ormProvider == "EfCore")
            {
                var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
                sourceCode += "using Microsoft.EntityFrameworkCore;\n";
                sourceCode += "using Microsoft.EntityFrameworkCore.Metadata.Builders;\n";
                sourceCode += "using " + namespaceName + ";\n";
                sourceCode += "\n";
                sourceCode += "namespace " + namespaceName + ".EntityFrameworkCore.Mappings\n";
                sourceCode += "{\n";
                sourceCode += "    /// <summary>\n";
                sourceCode += "    /// " + entityName + " 实体的 EF Core 映射配置\n";
                sourceCode += "    /// </summary>\n";
                sourceCode += "    public class " + entityName + "Mapping : IEntityTypeConfiguration<" + entityName + ">\n";
                sourceCode += "    {\n";
                sourceCode += "        public void Configure(EntityTypeBuilder<" + entityName + "> builder)\n";
                sourceCode += "        {\n";
                sourceCode += "            builder.ToTable(\"" + tableName + "\");\n";
                sourceCode += "            \n";
                sourceCode += "            builder.HasKey(e => e.Id);\n";
                sourceCode += "            \n";
                sourceCode += "            // 属性映射配置\n";
                sourceCode += GeneratePropertyMappings(entityClass, "EfCore");
                sourceCode += "            \n";
                sourceCode += "            // 审计字段映射\n";
                sourceCode += IsAudited(entityClass) ? GenerateEfCoreAuditMappings() : string.Empty;
                sourceCode += "            \n";
                sourceCode += "            // 索引配置\n";
                sourceCode += GenerateIndexMappings(entityClass, "EfCore");
                sourceCode += "        }\n";
                sourceCode += "    }\n";
                sourceCode += "}";

                context.AddSource(entityName + "Mapping.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "SqlSugar")
            {
                var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
                sourceCode += "using SqlSugar;\n";
                sourceCode += "using " + namespaceName + ";\n";
                sourceCode += "\n";
                sourceCode += "namespace " + namespaceName + ".SqlSugar.Mappings\n";
                sourceCode += "{\n";
                sourceCode += "    /// <summary>\n";
                sourceCode += "    /// " + entityName + " 实体的 SqlSugar 映射配置\n";
                sourceCode += "    /// </summary>\n";
                sourceCode += "    public static class " + entityName + "SugarMapping\n";
                sourceCode += "    {\n";
                sourceCode += "        public static void Configure(CodeFirstProvider codeFirst)\n";
                sourceCode += "        {\n";
                sourceCode += "            codeFirst.InitTables(typeof(" + entityName + "));\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        public static void ConfigureTable(ISqlSugarClient db)\n";
                sourceCode += "        {\n";
                sourceCode += "            db.CodeFirst.InitTables<" + entityName + ">();\n";
                sourceCode += "        }\n";
                sourceCode += "\n";
                sourceCode += "        public static void ConfigureEntity(EntityInfo entityInfo)\n";
                sourceCode += "        {\n";
                sourceCode += "            if (entityInfo.EntityName == typeof(" + entityName + ").Name)\n";
                sourceCode += "            {\n";
                sourceCode += "                entityInfo.DbTableName = \"" + tableName + "\";\n";
                sourceCode += "                \n";
                sourceCode += "                // 属性映射配置\n";
                sourceCode += GeneratePropertyMappings(entityClass, "SqlSugar");
                sourceCode += "            }\n";
                sourceCode += "        }\n";
                sourceCode += "    }\n";
                sourceCode += "}";

                context.AddSource(entityName + "SugarMapping.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
            else if (ormProvider == "FreeSql")
            {
                var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
                sourceCode += "using FreeSql;\n";
                sourceCode += "using FreeSql.DataAnnotations;\n";
                sourceCode += "using " + namespaceName + ";\n";
                sourceCode += "\n";
                sourceCode += "namespace " + namespaceName + ".FreeSql.Mappings\n";
                sourceCode += "{\n";
                sourceCode += "    /// <summary>\n";
                sourceCode += "    /// " + entityName + " 实体的 FreeSql 映射配置\n";
                sourceCode += "    /// </summary>\n";
                sourceCode += "    public static class " + entityName + "FreeSqlMapping\n";
                sourceCode += "    {\n";
                sourceCode += "        public static void Configure(IFreeSql freeSql)\n";
                sourceCode += "        {\n";
                sourceCode += "            // 使用 ConfigEntity 配置实体映射\n";
                sourceCode += "            freeSql.CodeFirst.ConfigEntity<" + entityName + ">(eb =>\n";
                sourceCode += "            {\n";
                sourceCode += "                eb.Name(\"" + tableName + "\");\n";
                sourceCode += GenerateFreeSqlPropertyMappings(entityClass);
                sourceCode += "            });\n";
                sourceCode += "        }\n";
                sourceCode += "        \n";
                sourceCode += "        public static void SyncStructure(IFreeSql freeSql)\n";
                sourceCode += "        {\n";
                sourceCode += "            // 同步数据库结构\n";
                sourceCode += "            freeSql.CodeFirst.SyncStructure<" + entityName + ">();\n";
                sourceCode += "        }\n";
                sourceCode += "    }\n";
                sourceCode += "}";

                context.AddSource(entityName + "FreeSqlMapping.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private string GenerateAuditMappings()
        {
            return "// <auto-generated /\n" +
                   "            // 审计字段\n" +
                   "            builder.Property(e => e.CreationTime).IsRequired();\n" +
                   "            builder.Property(e => e.CreatorId).IsRequired(false);\n" +
                   "            builder.Property(e => e.LastModificationTime).IsRequired(false);\n" +
                   "            builder.Property(e => e.LastModifierId).IsRequired(false);";
        }

        private string GenerateEfCoreAuditMappings()
        {
            return "            // 审计字段映射\n" +
                   "            builder.Property(e => e.CreationTime)\n" +
                   "                .IsRequired();\n" +
                   "                \n" +
                   "            builder.Property(e => e.CreatorId)\n" +
                   "                .IsRequired(false);\n" +
                   "                \n" +
                   "            builder.Property(e => e.LastModificationTime)\n" +
                   "                .IsRequired(false);\n" +
                   "                \n" +
                   "            builder.Property(e => e.LastModifierId)\n" +
                   "                .IsRequired(false);";
        }

        private string GenerateFreeSqlAuditMappings()
        {
            return "                // 审计字段映射\n" +
                   "                eb.Property(e => e.CreationTime).IsRequired().HasDefaultValue(DateTime.UtcNow);\n" +
                   "                eb.Property(e => e.CreatorId).IsRequired(false);\n" +
                   "                eb.Property(e => e.LastModificationTime).IsRequired(false);\n" +
                   "                eb.Property(e => e.LastModifierId).IsRequired(false);";
        }

        private bool IsAudited(INamedTypeSymbol entityClass)
        {
            return entityClass.AllInterfaces.Any(i => i.Name == "IAuditedEntity" || i.Name == "IFullyAuditedEntity")
                   || GetAttributeProperty(entityClass, "GenerateAuditing", true);
        }

        private bool IsFullyAudited(INamedTypeSymbol entityClass)
        {
            return entityClass.AllInterfaces.Any(i => i.Name == "IFullyAuditedEntity")
                   || entityClass.BaseType?.Name == "FullyAuditedEntity"
                   || entityClass.BaseType?.Name == "FullyAuditedAggregateRoot";
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

        private bool HasAttribute(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == attributeName || attr.AttributeClass?.Name == attributeName + "Attribute");
        }

        private T GetAttributeProperty<T>(ISymbol symbol, string propertyName, T defaultValue)
        {
            var attr = symbol.GetAttributes().FirstOrDefault(attr =>
                attr.AttributeClass?.Name == "EntityAttribute" || attr.AttributeClass?.Name == "Entity");

            if (attr == null) return defaultValue;

            var namedArg = attr.NamedArguments.FirstOrDefault(arg => arg.Key == propertyName);
            if (namedArg.Key != propertyName) return defaultValue;

            var typedConstant = namedArg.Value;

            if (typeof(T) == typeof(string[]) && typedConstant.Kind == TypedConstantKind.Array)
            {
                var values = typedConstant.Values
                    .Select(v => v.Value?.ToString())
                    .Where(v => v != null)
                    .Cast<string>()
                    .ToArray();
                return (T)(object)values;
            }

            if (typedConstant.Value is T value) return value;

            return defaultValue;
        }

        private void GenerateRepositoryBase(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var idType = GetEntityIdType(entityClass);
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();

            var sourceCode = new StringBuilder();
            sourceCode.AppendLine("#nullable enable");
            sourceCode.AppendLine("// <auto-generated />");
            sourceCode.AppendLine("using System;");
            sourceCode.AppendLine("using System.Collections.Generic;");
            sourceCode.AppendLine("using System.Linq;");
            sourceCode.AppendLine("using System.Linq.Expressions;");
            sourceCode.AppendLine("using System.Threading;");
            sourceCode.AppendLine("using System.Threading.Tasks;");
            sourceCode.AppendLine("using CrestCreates.Domain.Repositories;");
            sourceCode.AppendLine($"using {namespaceName};");
            sourceCode.AppendLine();

            var targetNamespace = namespaceName.Contains(".Domain.Entities")
                ? namespaceName.Replace(".Domain.Entities", ".Domain.Repositories")
                : $"{namespaceName}.Repositories";

            sourceCode.AppendLine($"namespace {targetNamespace}");
            sourceCode.AppendLine("{");
            sourceCode.AppendLine($"    /// <summary>");
            sourceCode.AppendLine($"    /// {entityName} 仓储基类");
            sourceCode.AppendLine($"    /// </summary>");
            sourceCode.AppendLine($"    public abstract partial class {entityName}RepositoryBase : CrestRepositoryBase<{entityName}, {idType}>");
            sourceCode.AppendLine("    {");
            sourceCode.AppendLine();
            sourceCode.AppendLine(GenerateRepositoryBaseQueryMethods(entityClass));
            sourceCode.AppendLine("    }");
            sourceCode.AppendLine("}");

            context.AddSource($"{entityName}RepositoryBase.g.cs", SourceText.From(sourceCode.ToString(), Encoding.UTF8));
        }

        private string GenerateRepositoryBaseQueryMethods(INamedTypeSymbol entityClass)
        {
            var properties = GetEntityProperties(entityClass);
            var methods = new StringBuilder();

            foreach (var prop in properties)
            {
                var propName = prop.Name;
                var propType = prop.Type.ToDisplayString();
                var propLower = propName.ToLowerInvariant();
                var entityName = entityClass.Name;

                methods.AppendLine($"        /// <summary>");
                methods.AppendLine($"        /// 根据 {propName} 查找单个实体");
                methods.AppendLine($"        /// </summary>");
                methods.AppendLine($"        public virtual async Task<{entityName}?> GetBy{propName}Async({propType} {propLower}, CancellationToken cancellationToken = default)");
                methods.AppendLine("        {");
                methods.AppendLine($"            return await GetAsync(e => e.{propName}!.Equals({propLower}), cancellationToken);");
                methods.AppendLine("        }");
                methods.AppendLine();

                methods.AppendLine($"        /// <summary>");
                methods.AppendLine($"        /// 根据 {propName} 查找多个实体");
                methods.AppendLine($"        /// </summary>");
                methods.AppendLine($"        public virtual async Task<List<{entityName}>> FindBy{propName}Async({propType} {propLower}, CancellationToken cancellationToken = default)");
                methods.AppendLine("        {");
                methods.AppendLine($"            return await GetListAsync(e => e.{propName}!.Equals({propLower}), cancellationToken);");
                methods.AppendLine("        }");
                methods.AppendLine();

                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    methods.AppendLine($"        /// <summary>");
                    methods.AppendLine($"        /// 根据 {propName} 查找包含指定内容的实体");
                    methods.AppendLine($"        /// </summary>");
                    methods.AppendLine($"        public virtual async Task<List<{entityName}>> FindBy{propName}ContainsAsync(string {propLower}, CancellationToken cancellationToken = default)");
                    methods.AppendLine("        {");
                    methods.AppendLine($"            return await GetListAsync(e => e.{propName} != null && e.{propName}.Contains({propLower}), cancellationToken);");
                    methods.AppendLine("        }");
                    methods.AppendLine();
                }
            }

            return methods.ToString();
        }

        private string GenerateRepositoryQueryMethods(INamedTypeSymbol entityClass)
        {
            var properties = GetEntityProperties(entityClass);
            var methods = new StringBuilder();

            foreach (var prop in properties)
            {
                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    methods.AppendLine("        /// <summary>");
                    methods.AppendLine("        /// 根据 " + prop.Name + " 查找实体");
                    methods.AppendLine("        /// </summary>");
                    methods.AppendLine("        Task<List<" + entityClass.Name + ">> FindBy" + prop.Name + "Async(string " + prop.Name.ToLowerInvariant() + ", CancellationToken cancellationToken = default);");
                }
                else if (prop.Type.SpecialType != SpecialType.None)
                {
                    methods.AppendLine("        /// <summary>");
                    methods.AppendLine("        /// 根据 " + prop.Name + " 查找实体");
                    methods.AppendLine("        /// </summary>");
                    methods.AppendLine("        Task<List<" + entityClass.Name + ">> FindBy" + prop.Name + "Async(" + prop.Type.ToDisplayString() + " " + prop.Name.ToLowerInvariant() + ", CancellationToken cancellationToken = default);");
                }
            }

            return methods.ToString();
        }

        private string GenerateSoftDeleteMethods(string entityName, string idType)
        {
            return "        /// <summary>\n" +
                   "        /// 软删除实体\n" +
                   "        /// </summary>\n" +
                   "        Task SoftDeleteAsync(" + idType + " id, CancellationToken cancellationToken = default);\n" +
                   "\n" +
                   "        /// <summary>\n" +
                   "        /// 恢复软删除的实体\n" +
                   "        /// </summary>\n" +
                   "        Task RestoreAsync(" + idType + " id, CancellationToken cancellationToken = default);\n" +
                   "\n" +
                   "        /// <summary>\n" +
                   "        /// 获取未删除的实体列表\n" +
                   "        /// </summary>\n" +
                   "        Task<List<" + entityName + ">> GetNotDeletedAsync(CancellationToken cancellationToken = default);";
        }

        private List<IPropertySymbol> GetEntityProperties(INamedTypeSymbol entityClass)
        {
            return entityClass.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public &&
                           !p.IsStatic &&
                           p.CanBeReferencedByName &&
                           p.Name != "Id")
                .ToList();
        }

        private string GeneratePropertyMappings(INamedTypeSymbol entityClass, string ormProvider)
        {
            var properties = GetEntityProperties(entityClass);
            var mappings = new StringBuilder();

            foreach (var prop in properties)
            {
                if (ormProvider == "EfCore")
                {
                    if (prop.Type.SpecialType == SpecialType.System_String)
                    {
                        mappings.AppendLine("            builder.Property(e => e." + prop.Name + ")");
                        mappings.AppendLine("                .HasMaxLength(255)");
                        mappings.AppendLine("                .IsRequired(" + (prop.NullableAnnotation != NullableAnnotation.Annotated ? "true" : "false") + ");");
                    }
                }
            }

            return mappings.ToString();
        }

        private string GenerateFreeSqlPropertyMappings(INamedTypeSymbol entityClass)
        {
            var properties = GetEntityProperties(entityClass);
            var mappings = new StringBuilder();

            // 配置主键
            mappings.AppendLine("                eb.Property(a => a.Id).IsPrimary(true);");

            foreach (var prop in properties)
            {
                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    mappings.AppendLine("                eb.Property(a => a." + prop.Name + ").StringLength(255);");
                }
                else if (prop.Type.SpecialType == SpecialType.System_Decimal)
                {
                    mappings.AppendLine("                eb.Property(a => a." + prop.Name + ").Precision(18, 2);");
                }
                else if (prop.Type.SpecialType == SpecialType.System_DateTime)
                {
                    mappings.AppendLine("                eb.Property(a => a." + prop.Name + ").DbType(\"datetime\");");
                }
            }

            return mappings.ToString();
        }

        private string GenerateIndexMappings(INamedTypeSymbol entityClass, string ormProvider)
        {
            var properties = GetEntityProperties(entityClass);
            var indexes = new StringBuilder();

            if (ormProvider == "EfCore")
            {
                foreach (var prop in properties.Take(3))
                {
                    if (prop.Type.SpecialType == SpecialType.System_String)
                    {
                        indexes.AppendLine("            builder.HasIndex(e => e." + prop.Name + ");");
                    }
                }
            }

            return indexes.ToString();
        }

        private void GenerateEntityDto(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var properties = GetAllEntityProperties(entityClass);
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);
            var idType = GetEntityIdType(entityClass);

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine();
            builder.AppendLine($"namespace {dtosNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {entityName} DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial class {entityName}Dto");
            builder.AppendLine("    {");

            builder.AppendLine($"        public {idType} Id {{ get; set; }}");
            builder.AppendLine();

            foreach (var prop in properties.Where(p => p.Name != "Id"))
            {
                var typeName = prop.Type.ToDisplayString();
                if (prop.NullableAnnotation == NullableAnnotation.Annotated && !typeName.EndsWith("?"))
                {
                    typeName += "?";
                }
                builder.AppendLine($"        public {typeName} {prop.Name} {{ get; set; }}");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateCreateEntityDto(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var properties = GetAllEntityProperties(entityClass);
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.CreateDto);

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel.DataAnnotations;");
            builder.AppendLine();
            builder.AppendLine($"namespace {dtosNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// 创建 {entityName} DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial class Create{entityName}Dto");
            builder.AppendLine("    {");

            foreach (var prop in properties.Where(p => p.Name != "Id" && p.Name != "CreationTime" && p.Name != "LastModificationTime" && p.Name != "CreatorId" && p.Name != "LastModifierId" && p.Name != "ConcurrencyStamp"))
            {
                var typeName = prop.Type.ToDisplayString();
                if (prop.NullableAnnotation == NullableAnnotation.Annotated && !typeName.EndsWith("?"))
                {
                    typeName += "?";
                }
                builder.AppendLine($"        public {typeName} {prop.Name} {{ get; set; }}");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Create{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateUpdateEntityDto(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var properties = GetAllEntityProperties(entityClass);
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.UpdateDto);
            var idType = GetEntityIdType(entityClass);

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.ComponentModel.DataAnnotations;");
            builder.AppendLine();
            builder.AppendLine($"namespace {dtosNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// 更新 {entityName} DTO");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public partial class Update{entityName}Dto");
            builder.AppendLine("    {");

            builder.AppendLine($"        public {idType} Id {{ get; set; }}");
            builder.AppendLine();

            foreach (var prop in properties.Where(p => p.Name != "Id" && p.Name != "CreationTime" && p.Name != "LastModificationTime" && p.Name != "CreatorId" && p.Name != "LastModifierId"))
            {
                var typeName = prop.Type.ToDisplayString();
                if (prop.NullableAnnotation == NullableAnnotation.Annotated && !typeName.EndsWith("?"))
                {
                    typeName += "?";
                }
                builder.AppendLine($"        public {typeName} {prop.Name} {{ get; set; }}");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"Update{entityName}Dto.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private void GenerateMappingExtensions(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var properties = GetAllEntityProperties(entityClass);
            var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);
            var idType = GetEntityIdType(entityClass);

            // Derive DTO property names (same logic as DTO generation: exclude audit properties)
            var dtoPropertyNames = new HashSet<string>(properties
                .Where(p => p.Name != "CreationTime" && p.Name != "LastModificationTime"
                    && p.Name != "CreatorId" && p.Name != "LastModifierId")
                .Select(p => p.Name));

            // Resolve mappings from source-side attributes
            var resolver = new ObjectMappingRuleResolver();
            var mappings = resolver.ResolvePropertyMappingsFromSource(entityClass, dtoPropertyNames, context);

            // Filter ignored
            var activeMappings = mappings
                .Where(m => !m.IsIgnored)
                .ToList();

            // Separate Id from other properties (Id may not be resolved from base class via GetMembers)
            var idMapping = activeMappings.FirstOrDefault(m => m.TargetPropertyName == "Id");
            var nonIdMappings = activeMappings
                .Where(m => m.TargetPropertyName != "Id")
                .OrderBy(m => m.TargetPropertyName)
                .ToList();

            // Check if entity has writable Id
            var hasWritableId = entityClass.GetMembers().OfType<IPropertySymbol>()
                .Any(p => p.Name == "Id" && p.SetMethod != null
                    && p.SetMethod.DeclaredAccessibility == Accessibility.Public);

            // Writable non-Id properties for ApplyTo (must have public setter, exclude audit)
            var writableNonIdMappings = nonIdMappings
                .Where(m => m.SourceProperty.SetMethod != null
                    && m.SourceProperty.SetMethod.DeclaredAccessibility == Accessibility.Public
                    && m.TargetPropertyName != "CreationTime"
                    && m.TargetPropertyName != "LastModificationTime"
                    && m.TargetPropertyName != "CreatorId"
                    && m.TargetPropertyName != "LastModifierId")
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("#nullable enable");
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Linq.Expressions;");
            builder.AppendLine($"using {namespaceName};");
            builder.AppendLine($"using {dtosNamespace};");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}.Extensions");
            builder.AppendLine("{");
            builder.AppendLine($"    public static partial class {entityName}MappingExtensions");
            builder.AppendLine("    {");

            // --- ToDto() ---
            builder.AppendLine($"        public static {entityName}Dto ToDto(this {entityName} source)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (source is null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(source));");
            builder.AppendLine();
            builder.AppendLine($"            var result = new {entityName}Dto");
            builder.AppendLine("            {");

            // Id always first (hardcoded — may not be resolved from base class via GetMembers)
            builder.AppendLine($"                Id = source.Id,");

            for (int i = 0; i < nonIdMappings.Count; i++)
            {
                var mapping = nonIdMappings[i];
                var comma = i < nonIdMappings.Count - 1 ? "," : "";
                var expr = GetEntityMappingExpression(mapping);
                builder.AppendLine($"                {mapping.TargetPropertyName} = {expr}{comma}");
            }

            builder.AppendLine("            };");
            builder.AppendLine();
            builder.AppendLine("            AfterToDto(source, result);");
            builder.AppendLine("            return result;");
            builder.AppendLine("        }");
            builder.AppendLine();

            // --- CreateXxxDto.ApplyTo() ---
            builder.AppendLine($"        public static void ApplyTo(this Create{entityName}Dto source, {entityName} destination)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (source is null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(source));");
            builder.AppendLine("            if (destination is null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(destination));");
            builder.AppendLine();
            builder.AppendLine("            BeforeApplyTo(source, destination);");
            builder.AppendLine();

            foreach (var mapping in writableNonIdMappings)
            {
                builder.AppendLine($"            destination.{mapping.TargetPropertyName} = source.{mapping.TargetPropertyName};");
            }

            builder.AppendLine();
            builder.AppendLine("            AfterApplyTo(source, destination);");
            builder.AppendLine("        }");
            builder.AppendLine();

            // --- UpdateXxxDto.ApplyTo() ---
            builder.AppendLine($"        public static void ApplyTo(this Update{entityName}Dto source, {entityName} destination)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (source is null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(source));");
            builder.AppendLine("            if (destination is null)");
            builder.AppendLine("                throw new ArgumentNullException(nameof(destination));");
            builder.AppendLine();
            builder.AppendLine("            BeforeApplyTo(source, destination);");
            builder.AppendLine();

            if (hasWritableId)
            {
                builder.AppendLine("            destination.Id = source.Id;");
            }

            foreach (var mapping in writableNonIdMappings)
            {
                builder.AppendLine($"            destination.{mapping.TargetPropertyName} = source.{mapping.TargetPropertyName};");
            }

            builder.AppendLine();
            builder.AppendLine("            AfterApplyTo(source, destination);");
            builder.AppendLine("        }");
            builder.AppendLine();

            // --- ToDtoExpression (skip navigation/converter properties) ---
            var exprMappings = nonIdMappings
                .Where(m => m.NavigationSegments == null && m.ConverterTypeFullName == null)
                .ToList();

            builder.AppendLine($"        public static Expression<Func<{entityName}, {entityName}Dto>> ToDtoExpression =>");
            builder.AppendLine($"            source => new {entityName}Dto");
            builder.AppendLine("            {");

            builder.AppendLine($"                Id = source.Id,");

            for (int i = 0; i < exprMappings.Count; i++)
            {
                var mapping = exprMappings[i];
                var comma = i < exprMappings.Count - 1 ? "," : "";
                builder.AppendLine($"                {mapping.TargetPropertyName} = source.{mapping.SourceProperty.Name}{comma}");
            }

            builder.AppendLine("            };");
            builder.AppendLine();

            // --- Partial hook declarations ---
            builder.AppendLine($"        static partial void AfterToDto({entityName} source, {entityName}Dto destination);");
            builder.AppendLine($"        static partial void BeforeApplyTo(Create{entityName}Dto source, {entityName} destination);");
            builder.AppendLine($"        static partial void AfterApplyTo(Create{entityName}Dto source, {entityName} destination);");
            builder.AppendLine($"        static partial void BeforeApplyTo(Update{entityName}Dto source, {entityName} destination);");
            builder.AppendLine($"        static partial void AfterApplyTo(Update{entityName}Dto source, {entityName} destination);");

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{entityName}MappingExtensions.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private static string GetEntityMappingExpression(PropertyMapping mapping)
        {
            if (mapping.NavigationSegments != null && mapping.NavigationSegments.Count > 1)
            {
                var segments = mapping.NavigationSegments;
                var firstProp = mapping.SourceProperty;

                var parts = new List<string> { $"source.{segments[0]}" };

                INamedTypeSymbol? currentType = firstProp.Type as INamedTypeSymbol;
                for (int i = 1; i < segments.Count; i++)
                {
                    if (currentType == null) break;
                    bool isRefType = currentType.IsReferenceType
                        || (currentType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
                    string op = isRefType ? "?." : ".";
                    parts.Add($"{op}{segments[i]}");

                    var segmentProp = currentType.GetMembers(segments[i])
                        .OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic);
                    currentType = segmentProp?.Type as INamedTypeSymbol;
                }

                var expr = string.Concat(parts);
                if (mapping.ConverterTypeFullName != null)
                    return $"{mapping.ConverterTypeFullName}.Convert({expr})";
                return expr;
            }

            var baseExpr = $"source.{mapping.SourceProperty.Name}";
            if (mapping.ConverterTypeFullName != null)
                return $"{mapping.ConverterTypeFullName}.Convert({baseExpr})";
            return baseExpr;
        }

        private void GenerateEntityExtensions(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();

            var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
            sourceCode += "using System;\n";
            sourceCode += "using " + namespaceName + ";\n";
            sourceCode += "\n";
            sourceCode += "namespace " + namespaceName + ".Extensions\n";
            sourceCode += "{\n";
            sourceCode += "    /// <summary>\n";
            sourceCode += "    /// " + entityName + " 实体的扩展方法\n";
            sourceCode += "    /// </summary>\n";
            sourceCode += "    public static class " + entityName + "Extensions\n";
            sourceCode += "    {\n";
            sourceCode += "        /// <summary>\n";
            sourceCode += "        /// 转换为摘要信息\n";
            sourceCode += "        /// </summary>\n";
            sourceCode += "        public static string ToSummary(this " + entityName + " entity)\n";
            sourceCode += "        {\n";
            sourceCode += "            if (entity == null) return string.Empty;\n";
            sourceCode += "            return \"" + entityName + " - \" + entity.GetType().Name;\n";
            sourceCode += "        }\n";
            sourceCode += "\n";
            sourceCode += "        /// <summary>\n";
            sourceCode += "        /// 克隆实体（不包含ID）\n";
            sourceCode += "        /// </summary>\n";
            sourceCode += "        public static " + entityName + " CloneWithoutId(this " + entityName + " entity)\n";
            sourceCode += "        {\n";
            sourceCode += "            if (entity == null) return null;\n";
            sourceCode += "            \n";
            sourceCode += "            // 这里可以根据实际需要实现深拷贝逻辑\n";
            sourceCode += "            throw new NotImplementedException(\"Clone method needs to be implemented manually\");\n";
            sourceCode += "        }\n";
            sourceCode += "\n";
            sourceCode += IsAudited(entityClass) ? GenerateAuditExtensions(entityName) : string.Empty;
            sourceCode += "    }\n";
            sourceCode += "}";

            context.AddSource(entityName + "Extensions.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }

        private string GenerateAuditExtensions(string entityName)
        {
            return "        /// <summary>\n" +
                   "        /// 设置创建审计信息\n" +
                   "        /// </summary>\n" +
                   "        public static void SetCreationAudit(this " + entityName + " entity, Guid? userId = null)\n" +
                   "        {\n" +
                   "            entity.CreationTime = DateTime.UtcNow;\n" +
                   "            entity.CreatorId = userId;\n" +
                   "        }\n" +
                   "\n" +
                   "        /// <summary>\n" +
                   "        /// 设置修改审计信息\n" +
                   "        /// </summary>\n" +
                   "        public static void SetModificationAudit(this " + entityName + " entity, Guid? userId = null)\n" +
                   "        {\n" +
                   "            entity.LastModificationTime = DateTime.UtcNow;\n" +
                   "            entity.LastModifierId = userId;\n" +
                   "        }";
        }

        private void GenerateValidationRules(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            var properties = GetEntityProperties(entityClass);

            var sourceCode = "#nullable enable\n";
            sourceCode += "// <auto-generated /\n";
            sourceCode += "using FluentValidation;\n";
            sourceCode += "using " + namespaceName + ";\n";
            sourceCode += "\n";
            sourceCode += "namespace " + namespaceName + ".Validations\n";
            sourceCode += "{\n";
            sourceCode += "    /// <summary>\n";
            sourceCode += "    /// " + entityName + " 实体的验证规则\n";
            sourceCode += "    /// </summary>\n";
            sourceCode += "    public class " + entityName + "Validator : AbstractValidator<" + entityName + ">\n";
            sourceCode += "    {\n";
            sourceCode += "        public " + entityName + "Validator()\n";
            sourceCode += "        {\n";
            
            foreach (var prop in properties)
            {
                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    sourceCode += "            RuleFor(x => x." + prop.Name + ")\n";
                    sourceCode += "                .NotEmpty().WithMessage(\"" + prop.Name + " 不能为空\");\n";
                }
            }
            
            sourceCode += "        }\n";
            sourceCode += "    }\n";
            sourceCode += "}";

            context.AddSource(entityName + "Validator.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        }

        private void GenerateEntityPermissions(SourceProductionContext context, INamedTypeSymbol entityClass)
        {
            var entityName = entityClass.Name;
            var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
            
            var baseNamespace = namespaceName.Contains(".Domain.Entities") 
                ? namespaceName.Replace(".Domain.Entities", "") 
                : namespaceName;
            
            var targetNamespace = $"{baseNamespace}.Domain.Permissions";
            var moduleName = string.IsNullOrWhiteSpace(baseNamespace)
                ? entityClass.ContainingAssembly.Name
                : baseNamespace;
            var permissionPrefix = $"{moduleName}.{entityName}";

            var defaultPermissions = new List<string> { "Create", "Update", "Delete", "Search", "Get", "Export" };
            
            var customPermissions = GetAttributeProperty<string[]?>(entityClass, "CustomPermissions", default);
            var permissions = customPermissions != null && customPermissions.Length > 0 
                ? customPermissions.ToList() 
                : defaultPermissions;

            var sourceCode = new StringBuilder();
            sourceCode.AppendLine("#nullable enable");
            sourceCode.AppendLine("// <auto-generated />");
            sourceCode.AppendLine("using System.Collections.Generic;");
            sourceCode.AppendLine("using CrestCreates.Domain.Shared.Permissions;");
            sourceCode.AppendLine();
            sourceCode.AppendLine($"namespace {targetNamespace}");
            sourceCode.AppendLine("{");
            sourceCode.AppendLine($"    /// <summary>");
            sourceCode.AppendLine($"    /// {entityName} 实体的权限定义");
            sourceCode.AppendLine($"    /// </summary>");
            sourceCode.AppendLine($"    public sealed partial class {entityName}Permissions : IEntityPermissions");
            sourceCode.AppendLine("    {");

            foreach (var permission in permissions)
            {
                var actionName = ToPermissionConstantName(permission);
                var permissionValue = permission.Contains(".")
                    ? permission
                    : $"{permissionPrefix}.{permission}";
                sourceCode.AppendLine($"        public const string {actionName} = \"{EscapeStringLiteral(permissionValue)}\";");
            }

            sourceCode.AppendLine();
            sourceCode.AppendLine($"        public string ModuleName => \"{EscapeStringLiteral(moduleName)}\";");
            sourceCode.AppendLine();
            sourceCode.AppendLine($"        public string EntityName => \"{EscapeStringLiteral(entityName)}\";");
            sourceCode.AppendLine();
            sourceCode.AppendLine($"        public string EntityFullName => \"{EscapeStringLiteral(entityClass.ToDisplayString())}\";");
            sourceCode.AppendLine();
            sourceCode.AppendLine("        public IEnumerable<string> GetAllPermissions()");
            sourceCode.AppendLine("        {");

            foreach (var permission in permissions)
            {
                var actionName = ToPermissionConstantName(permission);
                sourceCode.AppendLine($"            yield return {actionName};");
            }

            sourceCode.AppendLine("        }");
            sourceCode.AppendLine();
            sourceCode.AppendLine($"        public static {entityName}Permissions Instance {{ get; }} = new {entityName}Permissions();");
            sourceCode.AppendLine("    }");
            sourceCode.AppendLine("}");

            context.AddSource($"{entityName}Permissions.g.cs", SourceText.From(sourceCode.ToString(), Encoding.UTF8));
        }

        private static string ToPermissionConstantName(string permission)
        {
            var actionName = permission.Contains(".")
                ? permission.Split('.').Last()
                : permission;

            var builder = new StringBuilder(actionName.Length + 1);
            foreach (var character in actionName)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            }

            if (builder.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(builder[0]))
            {
                builder.Insert(0, '_');
            }

            var identifier = builder.ToString();
            return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None
                ? identifier
                : $"@{identifier}";
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
