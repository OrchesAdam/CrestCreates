using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using CrestCreates.CodeGenerator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.SchemaCapabilityGenerator;

[Generator]
public sealed class SchemaCapabilitySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entityClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds
                    && cds.AttributeLists.Count > 0,
                transform: static (ctx, ct) => GetEntityInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var serviceClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds
                    && cds.AttributeLists.Count > 0,
                transform: static (ctx, ct) => GetCapabilityInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var compilationProvider = context.CompilationProvider;

        context.RegisterSourceOutput(
            entityClasses.Combine(serviceClasses).Combine(compilationProvider),
            static (spc, source) =>
            {
                var combined = source.Left;
                var compilation = source.Right;
                GenerateRegistries(spc, combined.Left, combined.Right, compilation);
            });
    }

    private static SchemaDescriptorInfo? GetEntityInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var hasEntityAttr = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "EntityAttribute" or "Entity");

        if (!hasEntityAttr) return null;

        var fields = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p =>
            {
                var isCollection = false;
                string? collectionElementType = null;
                if (p.Type is INamedTypeSymbol nts
                    && nts.IsGenericType
                    && nts.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IList_T)
                {
                    isCollection = true;
                    collectionElementType = nts.TypeArguments[0].ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat);
                }

                return new SchemaFieldInfo
                {
                    Name = p.Name,
                    FieldType = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                    IsRequired = false,
                    IsCollection = isCollection,
                    CollectionElementType = collectionElementType
                };
            })
            .ToList();

        return new SchemaDescriptorInfo
        {
            Id = $"schema_{Guid.NewGuid():N}",
            Name = symbol.Name,
            Version = 1,
            Fields = fields
        };
    }

    private static CapabilityDescriptorInfo? GetCapabilityInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var hasServiceAttr = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "CrestServiceAttribute" or "CrestService");

        if (!hasServiceAttr) return null;

        var serviceName = symbol.Name
            .Replace("AppService", "")
            .Replace("Service", "");

        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        return new CapabilityDescriptorInfo
        {
            Id = $"cap_{Guid.NewGuid():N}",
            Name = $"{ns.ToLowerInvariant()}.{serviceName.ToLowerInvariant()}",
            Version = 1,
            SemanticTags = new List<string> { serviceName.ToLowerInvariant() }
        };
    }

    private static void GenerateRegistries(
        SourceProductionContext spc,
        ImmutableArray<SchemaDescriptorInfo?> schemas,
        ImmutableArray<CapabilityDescriptorInfo?> capabilities,
        Compilation compilation)
    {
        if (schemas.All(s => s == null) && capabilities.All(c => c == null))
            return;

        // Only generate if the project references the required assemblies
        var hasSchemaAbstractions = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.Schema.Abstractions");
        if (!hasSchemaAbstractions)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using CrestCreates.Schema.Abstractions;");
        sb.AppendLine("using CrestCreates.Schema;");
        sb.AppendLine("using CrestCreates.Capability.Abstractions;");
        sb.AppendLine("using CrestCreates.Capability;");
        sb.AppendLine("using CrestCreates.Metadata.Abstractions;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace CrestCreates.Generated;");
        sb.AppendLine();
        sb.AppendLine("internal static class GeneratedDescriptorRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    internal static void Register()");
        sb.AppendLine("    {");

        foreach (var schema in schemas)
        {
            if (schema == null) continue;
            sb.AppendLine($"        SchemaRegistryProvider.Register(new SchemaDescriptor");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id = \"{schema.Id}\",");
            sb.AppendLine($"            Name = \"{schema.Name}\",");
            sb.AppendLine($"            Version = {schema.Version},");
            sb.AppendLine($"            Fields = new List<SchemaFieldDescriptor>");
            sb.AppendLine("            {");
            foreach (var field in schema.Fields)
            {
                sb.AppendLine($"                new SchemaFieldDescriptor");
                sb.AppendLine("                {");
                sb.AppendLine($"                    Name = \"{field.Name}\",");
                sb.AppendLine($"                    FieldType = \"{field.FieldType}\",");
                sb.AppendLine($"                    IsNullable = {field.IsNullable.ToString().ToLowerInvariant()},");
                sb.AppendLine($"                    IsRequired = {field.IsRequired.ToString().ToLowerInvariant()},");
                sb.AppendLine($"                    IsCollection = {field.IsCollection.ToString().ToLowerInvariant()},");
                sb.AppendLine("                },");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        foreach (var cap in capabilities)
        {
            if (cap == null) continue;
            var tagsStr = string.Join(", ", cap.SemanticTags.Select(t => $"\"{t}\""));
            sb.AppendLine($"        CapabilityRegistryProvider.Register(new CapabilityDescriptor");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id = \"{cap.Id}\",");
            sb.AppendLine($"            Name = \"{cap.Name}\",");
            sb.AppendLine($"            Version = {cap.Version},");
            sb.AppendLine($"            CapabilityKind = CapabilityKind.{cap.CapabilityKind},");
            sb.AppendLine($"            Permission = \"{cap.Permission}\",");
            sb.AppendLine($"            SemanticTags = new List<string> {{ {tagsStr} }},");
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("GeneratedDescriptorRegistry.g.cs", sb.ToString());
    }
}
