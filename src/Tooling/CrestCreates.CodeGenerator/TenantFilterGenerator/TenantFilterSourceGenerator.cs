using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.TenantFilterGenerator
{
    /// <summary>
    /// Generates strongly typed model filters for tenant and soft-delete entities declared in the
    /// current compilation. Each generated filter is guarded by the target context model so multiple
    /// DbContexts in one process can register filters without applying unknown entity types.
    /// </summary>
    [Generator]
    public class TenantFilterSourceGenerator : IIncrementalGenerator
    {
        private const string TenantFilterRegistryStoreFullName = "CrestCreates.Data.EFCore.MultiTenancy.TenantFilterRegistryStore";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var attributedTargets = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsEntityCandidate(node),
                    transform: static (ctx, _) => GetAttributedTarget(ctx))
                .Where(static x => x is not null)
                .Collect();

            var dbContextTargets = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsDbContextCandidate(node),
                    transform: static (ctx, _) => GetDbContextTargets(ctx))
                .Where(static x => !x.IsDefaultOrEmpty)
                .Collect();

            var hasRegistryStore = context.CompilationProvider
                .Select(static (compilation, _) =>
                    compilation.GetTypeByMetadataName(TenantFilterRegistryStoreFullName) is not null);

            var combined = attributedTargets.Combine(dbContextTargets).Combine(hasRegistryStore);

            context.RegisterSourceOutput(combined, ExecuteGeneration);
        }

        private sealed record EntityFilterTarget(
            string FullName,
            string Name,
            bool IsTenant,
            bool IsSoftDelete);

        private static bool IsEntityCandidate(SyntaxNode node)
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

        private static EntityFilterTarget? GetAttributedTarget(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            var hasEntityAttribute = symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "EntityAttribute" || attr.AttributeClass?.Name == "Entity");
            if (!hasEntityAttribute)
            {
                return null;
            }

            var isTenant = IsTenantEntity(symbol);
            var isSoftDelete = IsSoftDeleteEntity(symbol);
            return isTenant || isSoftDelete
                ? CreateTarget(symbol, isTenant, isSoftDelete)
                : null;
        }

        private static ImmutableArray<EntityFilterTarget> GetDbContextTargets(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol symbol ||
                !IsDbContext(symbol))
            {
                return ImmutableArray<EntityFilterTarget>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<EntityFilterTarget>();
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

                var isTenant = IsTenantEntity(entityType);
                var isSoftDelete = IsSoftDeleteEntity(entityType);
                if (isTenant || isSoftDelete)
                {
                    builder.Add(CreateTarget(entityType, isTenant, isSoftDelete));
                }
            }

            return builder.ToImmutable();
        }

        private static EntityFilterTarget CreateTarget(INamedTypeSymbol symbol, bool isTenant, bool isSoftDelete)
        {
            return new EntityFilterTarget(
                symbol.ToDisplayString(),
                symbol.Name,
                isTenant,
                isSoftDelete);
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
                if (fullName == "CrestCreates.Data.EFCore.MultiTenancy.IMultiTenant" ||
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

        private static bool IsSoftDeleteEntity(INamedTypeSymbol symbol)
        {
            return symbol.AllInterfaces.Any(iface =>
                iface.ToDisplayString() == "CrestCreates.Domain.Shared.Entities.Auditing.ISoftDelete" ||
                iface.Name == "ISoftDelete");
        }

        private void ExecuteGeneration(
            SourceProductionContext context,
            ((ImmutableArray<EntityFilterTarget?> AttributedTargets, ImmutableArray<ImmutableArray<EntityFilterTarget>> DbContextTargets) Entities, bool HasRegistryStore) input)
        {
            var ((attributedTargets, dbContextTargets), hasRegistryStore) = input;

            if (!hasRegistryStore)
            {
                return;
            }

            var targets = new Dictionary<string, EntityFilterTarget>(StringComparer.Ordinal);
            foreach (var target in attributedTargets)
            {
                Merge(targets, target);
            }

            foreach (var contextTargets in dbContextTargets)
            {
                foreach (var target in contextTargets)
                {
                    Merge(targets, target);
                }
            }

            var tenantTargets = targets.Values
                .Where(target => target.IsTenant)
                .OrderBy(target => target.FullName, StringComparer.Ordinal)
                .ToList();
            var softDeleteTargets = targets.Values
                .Where(target => target.IsSoftDelete)
                .OrderBy(target => target.FullName, StringComparer.Ordinal)
                .ToList();

            if (tenantTargets.Count == 0 && softDeleteTargets.Count == 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine("using System;");
            builder.AppendLine("using Microsoft.EntityFrameworkCore;");
            builder.AppendLine("using CrestCreates.MultiTenancy.Abstract;");
            builder.AppendLine("using CrestCreates.Data.EFCore.MultiTenancy;");
            builder.AppendLine();
            builder.AppendLine("namespace CrestCreates.Data.EFCore.MultiTenancy");
            builder.AppendLine("{");
            builder.AppendLine("    internal static class GeneratedTenantFilterRegistration");
            builder.AppendLine("    {");

            if (tenantTargets.Count > 0)
            {
                builder.AppendLine("        private static void ApplyAll(ModelBuilder modelBuilder, ICurrentTenant currentTenant)");
                builder.AppendLine("        {");
                foreach (var target in tenantTargets)
                {
                    builder.AppendLine($"            Configure{MethodSuffix(target)}Filter(modelBuilder, currentTenant);");
                }
                builder.AppendLine("        }");
                builder.AppendLine();

                foreach (var target in tenantTargets)
                {
                    AppendTenantFilter(builder, target);
                }
            }

            if (softDeleteTargets.Count > 0)
            {
                builder.AppendLine("        private static void ApplySoftDeleteAll(ModelBuilder modelBuilder)");
                builder.AppendLine("        {");
                foreach (var target in softDeleteTargets)
                {
                    builder.AppendLine($"            Configure{MethodSuffix(target)}SoftDeleteFilter(modelBuilder);");
                }
                builder.AppendLine("        }");
                builder.AppendLine();

                foreach (var target in softDeleteTargets)
                {
                    AppendSoftDeleteFilter(builder, target);
                }
            }

            builder.AppendLine("        [System.Runtime.CompilerServices.ModuleInitializer]");
            builder.AppendLine("        internal static void Register()");
            builder.AppendLine("        {");
            if (tenantTargets.Count > 0)
            {
                builder.AppendLine("            TenantFilterRegistryStore.Register(ApplyAll);");
            }
            if (softDeleteTargets.Count > 0)
            {
                builder.AppendLine("            SoftDeleteFilterRegistryStore.Register(ApplySoftDeleteAll);");
            }
            builder.AppendLine("        }");

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource("TenantFilter.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private static string MethodSuffix(EntityFilterTarget target)
        {
            return target.FullName.Replace('.', '_').Replace('+', '_');
        }

        private static void Merge(
            Dictionary<string, EntityFilterTarget> targets,
            EntityFilterTarget? target)
        {
            if (target is null)
            {
                return;
            }

            if (targets.TryGetValue(target.FullName, out var existing))
            {
                targets[target.FullName] = existing with
                {
                    IsTenant = existing.IsTenant || target.IsTenant,
                    IsSoftDelete = existing.IsSoftDelete || target.IsSoftDelete
                };
                return;
            }

            targets[target.FullName] = target;
        }

        private static void AppendTenantFilter(StringBuilder builder, EntityFilterTarget target)
        {
            var filter = "currentTenant.Id == null || e.TenantId == currentTenant.Id";
            if (target.IsSoftDelete)
            {
                filter = "(" + filter + ") && !e.IsDeleted";
            }

            builder.AppendLine($"        private static void Configure{MethodSuffix(target)}Filter(ModelBuilder modelBuilder, ICurrentTenant currentTenant)");
            builder.AppendLine("        {");
            builder.AppendLine($"            if (modelBuilder.Model.FindEntityType(typeof(global::{target.FullName})) is null) return;");
            builder.AppendLine($"            modelBuilder.Entity<global::{target.FullName}>().HasQueryFilter(e => {filter});");
            builder.AppendLine($"            modelBuilder.Entity<global::{target.FullName}>().HasIndex(e => e.TenantId)");
            builder.AppendLine($"                .HasDatabaseName(\"IX_{MethodSuffix(target)}_TenantId\");");
            builder.AppendLine("        }");
            builder.AppendLine();
        }

        private static void AppendSoftDeleteFilter(StringBuilder builder, EntityFilterTarget target)
        {
            builder.AppendLine($"        private static void Configure{MethodSuffix(target)}SoftDeleteFilter(ModelBuilder modelBuilder)");
            builder.AppendLine("        {");
            builder.AppendLine($"            if (modelBuilder.Model.FindEntityType(typeof(global::{target.FullName})) is null) return;");
            builder.AppendLine($"            modelBuilder.Entity<global::{target.FullName}>().HasQueryFilter(e => !e.IsDeleted);");
            builder.AppendLine("        }");
            builder.AppendLine();
        }
    }
}
