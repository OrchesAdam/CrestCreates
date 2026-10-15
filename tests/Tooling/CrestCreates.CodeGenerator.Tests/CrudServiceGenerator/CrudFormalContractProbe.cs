using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.CodeGenerator.Tests.CrudServiceGenerator;

/// <summary>
/// #128 S1 contract probe for the formal CRUD wire model. Framework contract types are resolved from
/// referenced assemblies (never from source), generated owners are resolved by full metadata name
/// inside the producer's own trees, and closed roots compare through OriginalDefinition plus type
/// arguments. Same-short-name fakes, empty shells, wrong owners and uninitialized members therefore
/// stay violations instead of passing.
/// </summary>
internal static class CrudFormalContractProbe
{
    internal const string GeneratedEnvelopeNamespace = "CrestCreates.Generated";

    internal sealed record Model(string ServiceClassName, string EntityName, string OwnerNamespace)
    {
        public string ContractsNamespace => OwnerNamespace + ".Contracts";

        public string ReadDto => ContractsNamespace + "." + EntityName + "Dto";

        public string CreateDto => ContractsNamespace + ".Create" + EntityName + "Dto";

        public string UpdateDto => ContractsNamespace + ".Update" + EntityName + "Dto";

        public string ListRequestDto => ContractsNamespace + "." + EntityName + "ListRequestDto";

        public string Envelope(string action) =>
            GeneratedEnvelopeNamespace + "." + ServiceClassName + "_" + action + "_CompatibilityInput";
    }

    private static readonly (string Label, string MetadataName)[] ContractTypes =
    [
        ("System.Text.Json.Serialization.JsonSerializerContext",
            "System.Text.Json.Serialization.JsonSerializerContext"),
        ("System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>",
            "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1"),
        ("CrestCreates.DynamicApi.DynamicApiResponse", "CrestCreates.DynamicApi.DynamicApiResponse"),
        ("CrestCreates.DynamicApi.DynamicApiResponse<T>", "CrestCreates.DynamicApi.DynamicApiResponse`1"),
        ("CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto<T>",
            "CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto`1"),
    ];

    /// <summary>
    /// Builds a compilation over the shared closure references so reverse-control mutations are
    /// judged by exactly the same rules as the real driver output.
    /// </summary>
    public static Compilation AuthoredCompilation(
        string source,
        IReadOnlyList<MetadataReference> references) =>
        CSharpCompilation.Create(
            "CrudContractProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// Reference-assembly contract types the wire model must be expressed over. A missing entry is a
    /// harness-closure defect, never a passing result, so callers surface it as a violation.
    /// </summary>
    public static IReadOnlyList<string> ClosureViolations(Compilation compilation)
    {
        List<string> violations = [];
        foreach (var (label, metadataName) in ContractTypes)
        {
            if (ResolveReferencedType(compilation, metadataName) is null)
            {
                violations.Add($"harness closure is missing the referenced contract type {label}");
            }
        }

        return violations;
    }

    /// <summary>
    /// Designed §7.1/§7.4 capability-input contract: the route/body/header composition for the write
    /// actions is a producer-declared envelope in the shared compatibility-input shape, carrying the
    /// route identity and the body/payload member at their static types.
    /// </summary>
    public static IReadOnlyList<string> InputEnvelopeViolations(
        Compilation compilation,
        IReadOnlyCollection<SyntaxTree> producerTrees,
        Model model)
    {
        List<string> violations = [];
        violations.AddRange(ClosureViolations(compilation));

        (string Action, (string Member, string TypeFqn)[] Members)[] requirements =
        [
            ("Update", [("Id", "System.Guid"), ("Input", model.UpdateDto)]),
            ("Delete", [("Id", "System.Guid"), ("IfMatch", "System.String")]),
        ];

        List<ITypeSymbol> declaredEnvelopes = [];
        foreach (var (action, members) in requirements)
        {
            var fqn = model.Envelope(action);
            var envelope = ResolveProducerType(compilation, producerTrees, fqn);
            if (envelope is null)
            {
                violations.Add($"the producer must declare the {action} capability input envelope '{fqn}'");
                continue;
            }

            declaredEnvelopes.Add(envelope);

            if (envelope.Arity > 0)
            {
                violations.Add($"{fqn} must be a closed non-generic envelope type");
            }

            foreach (var (member, typeFqn) in members)
            {
                var expected = typeFqn.StartsWith("System.", StringComparison.Ordinal)
                    ? ResolveReferencedType(compilation, typeFqn)
                    : ResolveProducerType(compilation, producerTrees, typeFqn);
                if (expected is null)
                {
                    violations.Add($"{fqn}.{member} cannot be satisfied: '{typeFqn}' is not declared by its owner");
                    continue;
                }

                var property = envelope.GetMembers(member).OfType<IPropertySymbol>().SingleOrDefault();
                if (property is null)
                {
                    violations.Add($"{fqn} must expose the input member '{member}'");
                    continue;
                }

                if (property.GetMethod is null)
                {
                    violations.Add($"{fqn}.{member} must be readable by the generated binder/invoker");
                }

                if (property.Type is IErrorTypeSymbol)
                {
                    violations.Add($"{fqn}.{member} has an error type");
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(property.Type, expected))
                {
                    violations.Add(
                        $"{fqn}.{member} must be statically typed '{typeFqn}', observed "
                        + $"'{property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'");
                }
            }
        }

        // §7.1: CapabilityEndpointMapper SerializeToElement on the actual input, so each composed
        // capability input must also be a registered JSON root, not only a declared type.
        var inputContext = FindProducerJsonSerializerContext(compilation, producerTrees);
        if (inputContext is null)
        {
            violations.Add(
                "the producer must declare a generated context derived from the referenced "
                + "System.Text.Json.Serialization.JsonSerializerContext so the capability inputs are roots");
        }
        else
        {
            var inputRoots = CollectRegisteredRoots(inputContext);
            foreach (var envelope in declaredEnvelopes)
            {
                if (!inputRoots.Contains(envelope, SymbolEqualityComparer.Default))
                {
                    violations.Add(
                        $"'{envelope.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' must be "
                        + "registered as a [JsonSerializable] root");
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Designed §7.1/§7.3 result contract: the generated bundle declares an official
    /// <c>JsonSerializerContext</c> whose <c>[JsonSerializable]</c> roots are the closed formal
    /// types. Member presence alone is not evidence — the root must be registered on the official
    /// context so the STJ toolchain, not the producer, provides the metadata.
    /// </summary>
    public static IReadOnlyList<string> ResponseRootViolations(
        Compilation compilation,
        IReadOnlyCollection<SyntaxTree> producerTrees,
        Model model)
    {
        List<string> violations = [];
        violations.AddRange(ClosureViolations(compilation));

        var readDto = ResolveProducerType(compilation, producerTrees, model.ReadDto);
        if (readDto is null)
        {
            violations.Add($"the producer must declare the entity-named CRUD DTO '{model.ReadDto}'");
        }

        var context = FindProducerJsonSerializerContext(compilation, producerTrees);
        if (context is null)
        {
            violations.Add(
                "the producer must declare a generated context derived from the referenced "
                + "System.Text.Json.Serialization.JsonSerializerContext");
            return violations;
        }

        var roots = CollectRegisteredRoots(context);
        foreach (var root in roots.Where(root => root is IErrorTypeSymbol))
        {
            violations.Add($"the context registers an error type root '{root.ToDisplayString()}'");
        }

        foreach (var required in new[] { model.CreateDto, model.UpdateDto, model.ListRequestDto })
        {
            var requiredType = ResolveProducerType(compilation, producerTrees, required);
            if (requiredType is null)
            {
                continue;
            }

            if (!roots.Contains(requiredType, SymbolEqualityComparer.Default))
            {
                violations.Add($"'{required}' must be registered as a [JsonSerializable] root");
            }
        }

        var envelopeOpen = ResolveReferencedType(compilation, "CrestCreates.DynamicApi.DynamicApiResponse`1");
        var pagedOpen = ResolveReferencedType(
            compilation, "CrestCreates.Application.Contracts.DTOs.Common.PagedResultDto`1");
        var voidEnvelope = ResolveReferencedType(compilation, "CrestCreates.DynamicApi.DynamicApiResponse");

        if (readDto is not null)
        {
            if (!roots.Contains(readDto, SymbolEqualityComparer.Default))
            {
                violations.Add($"'{model.ReadDto}' must be registered as a [JsonSerializable] root");
            }

            if (!ContainsClosedRoot(roots, pagedOpen, readDto))
            {
                violations.Add($"PagedResultDto<{model.ReadDto}> must be registered as a root");
            }

            if (!ContainsClosedRoot(roots, envelopeOpen, readDto))
            {
                violations.Add($"DynamicApiResponse<{model.ReadDto}> must be registered as a root");
            }
        }

        if (voidEnvelope is null || !roots.Contains(voidEnvelope, SymbolEqualityComparer.Default))
        {
            violations.Add(
                "the void/404 envelope root 'CrestCreates.DynamicApi.DynamicApiResponse' must be "
                + "registered on the generated context");
        }

        if (envelopeOpen is not null)
        {
            var objectRoot = roots.OfType<INamedTypeSymbol>().FirstOrDefault(root =>
                root.Arity == 1
                && SymbolEqualityComparer.Default.Equals(root.OriginalDefinition, envelopeOpen)
                && root.TypeArguments[0].SpecialType == SpecialType.System_Object);
            if (objectRoot is not null)
            {
                violations.Add("DynamicApiResponse<object> is not a formal CRUD result envelope");
            }
        }

        return violations;
    }

    private static bool ContainsClosedRoot(
        IReadOnlyCollection<ITypeSymbol> roots,
        INamedTypeSymbol? openDefinition,
        ITypeSymbol typeArgument)
    {
        if (openDefinition is null)
        {
            return false;
        }

        return roots.OfType<INamedTypeSymbol>().Any(root =>
            root.Arity == 1
            && !root.IsUnboundGenericType
            && SymbolEqualityComparer.Default.Equals(root.OriginalDefinition, openDefinition)
            && SymbolEqualityComparer.Default.Equals(root.TypeArguments[0], typeArgument));
    }

    private static IReadOnlyCollection<ITypeSymbol> CollectRegisteredRoots(INamedTypeSymbol context)
    {
        List<ITypeSymbol> roots = [];
        foreach (var attribute in context.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString()
                != "System.Text.Json.Serialization.JsonSerializableAttribute")
            {
                continue;
            }

            foreach (var argument in attribute.ConstructorArguments.Where(argument => argument.Value is ITypeSymbol))
            {
                roots.Add((ITypeSymbol)argument.Value!);
            }
        }

        return roots;
    }

    private static INamedTypeSymbol? FindProducerJsonSerializerContext(
        Compilation compilation,
        IReadOnlyCollection<SyntaxTree> producerTrees)
    {
        var officialContext = ResolveReferencedType(
            compilation, "System.Text.Json.Serialization.JsonSerializerContext");
        if (officialContext is null)
        {
            return null;
        }

        foreach (var candidate in FlattenTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!IsOwnedByProducer(candidate, producerTrees, requireAllParts: true))
            {
                continue;
            }

            for (var baseType = candidate.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(baseType, officialContext))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveProducerType(
        Compilation compilation,
        IReadOnlyCollection<SyntaxTree> producerTrees,
        string fullMetadataName)
    {
        var found = compilation.GetTypeByMetadataName(fullMetadataName);
        if (found is null || found is IErrorTypeSymbol)
        {
            return null;
        }

        return IsOwnedByProducer(found, producerTrees, requireAllParts: false) ? found : null;
    }

    private static INamedTypeSymbol? ResolveReferencedType(Compilation compilation, string fullMetadataName)
    {
        var segments = fullMetadataName.Split('.');
        var typeName = segments[^1];
        var arity = 0;
        var backtick = typeName.IndexOf('`');
        if (backtick >= 0)
        {
            arity = int.Parse(typeName[(backtick + 1)..]);
            typeName = typeName[..backtick];
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            INamespaceSymbol? current = assembly.GlobalNamespace;
            foreach (var segment in segments[..^1])
            {
                current = current.GetNamespaceMembers().FirstOrDefault(member => member.Name == segment);
                if (current is null)
                {
                    break;
                }
            }

            if (current is null)
            {
                continue;
            }

            foreach (var candidate in current.GetTypeMembers(typeName))
            {
                if (candidate.Arity == arity
                    && !SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, compilation.Assembly))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool IsOwnedByProducer(
        INamedTypeSymbol type,
        IReadOnlyCollection<SyntaxTree> producerTrees,
        bool requireAllParts)
    {
        var trees = type.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree).ToArray();
        if (trees.Length == 0)
        {
            return false;
        }

        return requireAllParts
            ? trees.All(tree => producerTrees.Contains(tree))
            : trees.Any(tree => producerTrees.Contains(tree));
    }

    private static IEnumerable<INamedTypeSymbol> FlattenTypes(INamespaceOrTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNamespace:
                    foreach (var nested in FlattenTypes(childNamespace))
                    {
                        yield return nested;
                    }

                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in FlattenTypes(type))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }
}
