using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.TenantFilterGenerator
{
    [Generator]
    public class TenantFilterSourceGenerator : IIncrementalGenerator
    {
        private const string TenantFilterRegistryStoreFullName = "CrestCreates.OrmProviders.EFCore.MultiTenancy.TenantFilterRegistryStore";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var attributedTenantEntities = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsMultiTenantCandidate(node),
                    transform: static (ctx, _) => GetMultiTenantClass(ctx))
                .Where(static x => x is not null)
                .Collect();

            var dbContextTenantEntities = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsDbContextCandidate(node),
                    transform: static (ctx, _) => GetDbContextTenantEntities(ctx))
                .Collect();

            var hasRegistryStore = context.CompilationProvider
                .Select(static (compilation, _) =>
                    compilation.GetTypeByMetadataName(TenantFilterRegistryStoreFullName) is not null);

            var combined = attributedTenantEntities.Combine(dbContextTenantEntities).Combine(hasRegistryStore);

            context.RegisterSourceOutput(combined, ExecuteGeneration);
        }

        private static bool IsMultiTenantCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0;
        }

        private static bool IsDbContextCandidate(SyntaxNode node)
        {
            if (node is not ClassDeclarationSyntax classDecl || classDecl.BaseList == null)
            {
                return false;
            }

            foreach (var baseType in classDecl.BaseList.Types)
            {
                if (baseType.Type.ToString().Contains("DbContext"))
                {
                    return true;
                }
            }

            return false;
        }

        private static INamedTypeSymbol? GetMultiTenantClass(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null) return null;

            bool hasEntityAttr = symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "EntityAttribute" || attr.AttributeClass?.Name == "Entity");

            if (!hasEntityAttr) return null;

            if (!IsTenantEntity(symbol)) return null;

            return symbol;
        }

        private static ImmutableArray<INamedTypeSymbol> GetDbContextTenantEntities(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null || !IsDbContext(symbol))
            {
                return ImmutableArray<INamedTypeSymbol>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.Type is not INamedTypeSymbol namedType ||
                    namedType.Name != "DbSet" ||
                    !namedType.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore") ||
                    namedType.TypeArguments.Length != 1 ||
                    namedType.TypeArguments[0] is not INamedTypeSymbol entityType)
                {
                    continue;
                }

                if (IsTenantEntity(entityType))
                {
                    builder.Add(entityType);
                }
            }

            return builder.ToImmutable();
        }

        private static bool IsDbContext(INamedTypeSymbol symbol)
        {
            var baseType = symbol.BaseType;
            while (baseType != null)
            {
                if (baseType.Name == "DbContext" &&
                    baseType.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore"))
                {
                    return true;
                }

                baseType = baseType.BaseType;
            }

            return false;
        }

        private static bool IsTenantEntity(INamedTypeSymbol symbol)
        {
            foreach (var iface in symbol.AllInterfaces)
            {
                var fullName = iface.ToDisplayString();
                if (fullName == "CrestCreates.OrmProviders.EFCore.MultiTenancy.IMultiTenant" ||
                    fullName == "CrestCreates.DataFilter.Entities.IMultiTenant" ||
                    fullName == "CrestCreates.Domain.Shared.Entities.Auditing.IMustHaveTenant" ||
                    iface.Name == "IMultiTenant" ||
                    iface.Name == "IMustHaveTenant")
                {
                    return true;
                }
            }

            return false;
        }

        private void ExecuteGeneration(SourceProductionContext context, ((ImmutableArray<INamedTypeSymbol?> AttributedEntities, ImmutableArray<ImmutableArray<INamedTypeSymbol>> DbContextEntities) EntityInput, bool HasRegistryStore) input)
        {
            var ((attributedEntities, dbContextEntities), hasRegistryStore) = input;

            if (!hasRegistryStore) return;

            var processed = new HashSet<string>();
            var entityList = new List<INamedTypeSymbol>();

            foreach (var entity in attributedEntities)
            {
                if (entity == null) continue;
                var fullName = entity.ToDisplayString();
                if (processed.Contains(fullName)) continue;
                processed.Add(fullName);
                entityList.Add(entity);
            }

            foreach (var entities in dbContextEntities)
            {
                foreach (var entity in entities)
                {
                    var fullName = entity.ToDisplayString();
                    if (processed.Contains(fullName)) continue;
                    processed.Add(fullName);
                    entityList.Add(entity);
                }
            }

            if (entityList.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("using CrestCreates.MultiTenancy.Abstract;");
            sb.AppendLine("using CrestCreates.OrmProviders.EFCore.MultiTenancy;");
            sb.AppendLine();
            sb.AppendLine("namespace CrestCreates.OrmProviders.EFCore.MultiTenancy");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class GeneratedTenantFilterRegistration");
            sb.AppendLine("    {");

            sb.AppendLine("        private static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)");
            sb.AppendLine("        {");

            foreach (var entity in entityList)
            {
                sb.AppendLine($"            Configure{entity.Name}Filter(modelBuilder, currentTenant);");
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var entity in entityList)
            {
                var entityName = entity.Name;
                var entityFullName = entity.ToDisplayString();
                sb.AppendLine($"        private static void Configure{entityName}Filter(ModelBuilder modelBuilder, ICurrentTenant currentTenant)");
                sb.AppendLine("        {");
                sb.AppendLine($"            modelBuilder.Entity<{entityFullName}>().HasQueryFilter(e =>");
                sb.AppendLine("                currentTenant.Id == null || e.TenantId == currentTenant.Id);");
                sb.AppendLine($"            modelBuilder.Entity<{entityFullName}>().HasIndex(e => e.TenantId)");
                sb.AppendLine($"                .HasDatabaseName(\"IX_{entityName}_TenantId\");");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("        [System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        internal static void Register()");
            sb.AppendLine("        {");
            sb.AppendLine("            TenantFilterRegistryStore.Register(ApplyAll);");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource("TenantFilter.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }
}
