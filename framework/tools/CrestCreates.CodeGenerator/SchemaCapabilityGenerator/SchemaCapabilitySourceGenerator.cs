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

        var eventProviders = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetEventProviderInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var formProviders = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetFormProviderInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var humanTaskProviders = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetHumanTaskProviderInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var workflowProviders = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetWorkflowProviderInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var compilationProvider = context.CompilationProvider;

        context.RegisterSourceOutput(
            entityClasses.Combine(serviceClasses)
                .Combine(eventProviders)
                .Combine(formProviders)
                .Combine(humanTaskProviders)
                .Combine(workflowProviders)
                .Combine(compilationProvider),
            static (spc, source) =>
            {
                var compilation = source.Right;
                var workflowList = source.Left.Right;
                var humanTaskList = source.Left.Left.Right;
                var formList = source.Left.Left.Left.Right;
                var eventList = source.Left.Left.Left.Left.Right;
                var entityAndCapability = source.Left.Left.Left.Left.Left;
                GenerateRegistries(spc, entityAndCapability.Left, entityAndCapability.Right,
                    eventList, formList, humanTaskList, workflowList, compilation);
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

    private static EventDescriptorInfo? GetEventProviderInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var eventProviderType = ctx.SemanticModel.Compilation
            .GetTypeByMetadataName("CrestCreates.Event.Abstractions.IEventDescriptorProvider");
        if (eventProviderType == null) return null;

        var implements = symbol.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i, eventProviderType));
        if (!implements) return null;

        return new EventDescriptorInfo
        {
            Id = $"evt_{Guid.NewGuid():N}",
            Name = symbol.ContainingNamespace?.ToDisplayString() + "." + symbol.Name
        };
    }

    private static FormDescriptorInfo? GetFormProviderInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var formProviderType = ctx.SemanticModel.Compilation
            .GetTypeByMetadataName("CrestCreates.Form.Abstractions.IFormDescriptorProvider");
        if (formProviderType == null) return null;

        var implements = symbol.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i, formProviderType));
        if (!implements) return null;

        return new FormDescriptorInfo
        {
            Id = $"form_{Guid.NewGuid():N}",
            Name = symbol.ContainingNamespace?.ToDisplayString() + "." + symbol.Name
        };
    }

    private static HumanTaskDescriptorInfo? GetHumanTaskProviderInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var humanTaskProviderType = ctx.SemanticModel.Compilation
            .GetTypeByMetadataName("CrestCreates.HumanTask.Abstractions.IHumanTaskDescriptorProvider");
        if (humanTaskProviderType == null) return null;

        var implements = symbol.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i, humanTaskProviderType));
        if (!implements) return null;

        return new HumanTaskDescriptorInfo
        {
            Id = $"ht_{Guid.NewGuid():N}",
            Name = symbol.ContainingNamespace?.ToDisplayString() + "." + symbol.Name
        };
    }

    private static WorkflowDescriptorInfo? GetWorkflowProviderInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        var workflowProviderType = ctx.SemanticModel.Compilation
            .GetTypeByMetadataName("CrestCreates.Workflow.Abstractions.IWorkflowDescriptorProvider");
        if (workflowProviderType == null) return null;

        var implements = symbol.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i, workflowProviderType));
        if (!implements) return null;

        return new WorkflowDescriptorInfo
        {
            Id = $"wf_{Guid.NewGuid():N}",
            Name = symbol.ContainingNamespace?.ToDisplayString() + "." + symbol.Name
        };
    }

    private static void GenerateRegistries(
        SourceProductionContext spc,
        ImmutableArray<SchemaDescriptorInfo?> schemas,
        ImmutableArray<CapabilityDescriptorInfo?> capabilities,
        ImmutableArray<EventDescriptorInfo?> events,
        ImmutableArray<FormDescriptorInfo?> forms,
        ImmutableArray<HumanTaskDescriptorInfo?> humanTasks,
        ImmutableArray<WorkflowDescriptorInfo?> workflows,
        Compilation compilation)
    {
        var hasSchema = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.Schema.Abstractions");
        if (!hasSchema)
            return;

        var hasAny = schemas.Any(s => s != null)
            || capabilities.Any(c => c != null)
            || events.Any(e => e != null)
            || forms.Any(f => f != null)
            || humanTasks.Any(h => h != null)
            || workflows.Any(w => w != null);

        if (!hasAny)
            return;

        var hasEvent = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.Event.Abstractions");
        var hasCapability = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.Capability.Abstractions");
        var hasForm = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.Form.Abstractions");
        var hasHumanTask = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.HumanTask.Abstractions");
        var hasWorkflow = compilation.ReferencedAssemblyNames
            .Any(a => a.Name == "CrestCreates.Workflow.Abstractions");

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using CrestCreates.Schema.Abstractions;");
        sb.AppendLine("using CrestCreates.Schema;");
        if (hasCapability)
        {
            sb.AppendLine("using CrestCreates.Capability.Abstractions;");
            sb.AppendLine("using CrestCreates.Capability;");
        }
        if (hasEvent)
        {
            sb.AppendLine("using CrestCreates.Event.Abstractions;");
            sb.AppendLine("using CrestCreates.Event;");
        }
        if (hasForm)
        {
            sb.AppendLine("using CrestCreates.Form.Abstractions;");
            sb.AppendLine("using CrestCreates.Form;");
        }
        if (hasHumanTask)
        {
            sb.AppendLine("using CrestCreates.HumanTask.Abstractions;");
            sb.AppendLine("using CrestCreates.HumanTask;");
        }
        if (hasWorkflow)
        {
            sb.AppendLine("using CrestCreates.Workflow.Abstractions;");
            sb.AppendLine("using CrestCreates.Workflow;");
        }
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

        // Phase 2a: events are registered via IEventDescriptorProvider, not EventRegistryProvider.
        // GeneratedCapabilityEventDescriptorProvider is emitted separately below.
        // EventDescriptor model no longer has Category/Semantic fields.

        foreach (var form in forms)
        {
            if (form == null) continue;
            sb.AppendLine($"        FormRegistryProvider.Register(new FormDescriptor");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id = \"{form.Id}\",");
            sb.AppendLine($"            Name = \"{form.Name}\",");
            sb.AppendLine($"            Version = {form.Version},");
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        foreach (var ht in humanTasks)
        {
            if (ht == null) continue;
            sb.AppendLine($"        HumanTaskRegistryProvider.Register(new HumanTaskDescriptor");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id = \"{ht.Id}\",");
            sb.AppendLine($"            Name = \"{ht.Name}\",");
            sb.AppendLine($"            Version = {ht.Version},");
            sb.AppendLine($"            AssigneeStrategy = AssigneeStrategy.{ht.AssigneeStrategy},");
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        foreach (var wf in workflows)
        {
            if (wf == null) continue;
            sb.AppendLine($"        WorkflowRegistryProvider.Register(new WorkflowDescriptor");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id = \"{wf.Id}\",");
            sb.AppendLine($"            Name = \"{wf.Name}\",");
            sb.AppendLine($"            Version = {wf.Version},");
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        // Phase 2a: emit IEventDescriptorProvider for capability events
        if (events.Any(e => e != null))
        {
            sb.AppendLine();
            sb.AppendLine("public sealed class GeneratedCapabilityEventDescriptorProvider : IEventDescriptorProvider");
            sb.AppendLine("{");
            sb.AppendLine("    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() => [");
            foreach (var evt in events)
            {
                if (evt == null) continue;
                sb.AppendLine($"        new GeneratedEventDescriptor");
                sb.AppendLine("        {");
                sb.AppendLine($"            Id = \"{evt.Id}\",");
                sb.AppendLine($"            Name = \"{evt.Name}\",");
                sb.AppendLine($"            Version = {evt.Version},");
                sb.AppendLine($"            State = DescriptorState.Active,");
                sb.AppendLine($"            PayloadType = typeof(object),");
                sb.AppendLine($"            Scope = EventScope.Integration,");
                sb.AppendLine($"            Reliability = EventReliability.AtLeastOnce,");
                sb.AppendLine($"            Importance = EventImportance.{evt.Importance},");
                sb.AppendLine($"            ChangeKind = SchemaChangeKind.{evt.ChangeKind},");
                sb.AppendLine("        },");
            }
            sb.AppendLine("    ];");
            sb.AppendLine("}");
        }

        spc.AddSource("GeneratedDescriptorRegistry.g.cs", sb.ToString());
    }
}
