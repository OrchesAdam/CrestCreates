using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.CapabilityEndpointGenerator;

[Generator]
public sealed class CapabilityEndpointGenerator : IIncrementalGenerator
{
    private const string SpecAttributeMetadataName =
        "CrestCreates.DynamicApi.CapabilityEndpointSpecAttribute";

    private const string InputAttributeMetadataName =
        "CrestCreates.DynamicApi.CapabilityEndpointInputAttribute";

    // --- Level 2 HTTP method attribute metadata names ---
    private const string PostAttributeMetadataName =
        "CrestCreates.DynamicApi.PostAttribute";

    private const string GetAttributeMetadataName =
        "CrestCreates.DynamicApi.GetAttribute";

    private const string PutAttributeMetadataName =
        "CrestCreates.DynamicApi.PutAttribute";

    private const string DeleteAttributeMetadataName =
        "CrestCreates.DynamicApi.DeleteAttribute";

    private const string PatchAttributeMetadataName =
        "CrestCreates.DynamicApi.PatchAttribute";

    // --- Level 2 container attribute ---
    private const string SetAttributeMetadataName =
        "CrestCreates.DynamicApi.CapabilityEndpointSetAttribute";

    // --- Diagnostic-only attribute names ---
    private const string CrestServiceMetadataName =
        "CrestCreates.Domain.Shared.Attributes.CrestServiceAttribute";

    private const string DynamicApiRouteMetadataName =
        "CrestCreates.Domain.Shared.Attributes.DynamicApiRouteAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ================================================================
        // Level 1: [CapabilityEndpointSpec] provider
        // ================================================================
        var level1SpecProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            SpecAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ExtractSpecRecord(ctx));

        var level1Specs = level1SpecProvider
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .Collect();

        // ================================================================
        // Level 2: [Post], [Get], [Put], [Delete], [Patch] providers
        // ================================================================
        var level2PostProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            PostAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) =>
                CapabilityEndpointSpecNormalizer.Normalize(ctx, httpMethodValue: 2)); // Post

        var level2GetProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            GetAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) =>
                CapabilityEndpointSpecNormalizer.Normalize(ctx, httpMethodValue: 1)); // Get

        var level2PutProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            PutAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) =>
                CapabilityEndpointSpecNormalizer.Normalize(ctx, httpMethodValue: 3)); // Put

        var level2DeleteProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            DeleteAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) =>
                CapabilityEndpointSpecNormalizer.Normalize(ctx, httpMethodValue: 5)); // Delete

        var level2PatchProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            PatchAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) =>
                CapabilityEndpointSpecNormalizer.Normalize(ctx, httpMethodValue: 4)); // Patch

        var level2PostSpecs = level2PostProvider
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .Collect();

        var level2GetSpecs = level2GetProvider
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .Collect();

        var level2PutSpecs = level2PutProvider
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .Collect();

        var level2DeleteSpecs = level2DeleteProvider
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .Collect();

        var level2PatchSpecs = level2PatchProvider
            .Where(static spec => spec is not null)
            .Select(static (spec, _) => spec!)
            .Collect();

        // ================================================================
        // Generation: merge Level 1 + Level 2 specs, de-duplicate, emit
        // ================================================================
        context.RegisterSourceOutput(
            level1Specs
                .Combine(level2PostSpecs)
                .Combine(level2GetSpecs)
                .Combine(level2PutSpecs)
                .Combine(level2DeleteSpecs)
                .Combine(level2PatchSpecs),
            static (spc, combined) =>
            {
                // Unwrap nested tuples:
                // (((((l1, l2Post), l2Get), l2Put), l2Delete), l2Patch)
                var l1 = combined.Left.Left.Left.Left.Left;
                var l2Post = combined.Left.Left.Left.Left.Right;
                var l2Get = combined.Left.Left.Left.Right;
                var l2Put = combined.Left.Left.Right;
                var l2Delete = combined.Left.Right;
                var l2Patch = combined.Right;

                // Merge all specs. Level 2 (child HTTP method attributes) are preferred
                // as primary emission source; Level 1 is fallback. Precedence is ensured
                // by adding Level 2 first so HashSet de-duplication keeps them.
                var allSpecs = new List<CapabilityEndpointSpecRecord>();
                AddRangeIfNotEmpty(allSpecs, l2Post);
                AddRangeIfNotEmpty(allSpecs, l2Get);
                AddRangeIfNotEmpty(allSpecs, l2Put);
                AddRangeIfNotEmpty(allSpecs, l2Delete);
                AddRangeIfNotEmpty(allSpecs, l2Patch);
                AddRangeIfNotEmpty(allSpecs, l1);

                if (allSpecs.Count == 0)
                    return;

                var groups = GroupAndDeduplicate(allSpecs.ToImmutableArray());

                foreach (var group in groups)
                {
                    var providerSource = CapabilityEndpointProviderEmitter.EmitProviderSource(group);
                    spc.AddSource(
                        $"{group.ContainerClassName}_Provider.g.cs",
                        Microsoft.CodeAnalysis.Text.SourceText.From(providerSource, System.Text.Encoding.UTF8));

                    var bindingSource = CapabilityEndpointBindingEmitter.EmitBindingSource(group);
                    spc.AddSource(
                        $"{group.ContainerClassName}_Bindings.g.cs",
                        Microsoft.CodeAnalysis.Text.SourceText.From(bindingSource, System.Text.Encoding.UTF8));
                }
            });

        // ================================================================
        // Diagnostics: CEP001-CEP005 (Level 1 [CapabilityEndpointSpec])
        // ================================================================
        var cep001to005Provider = context.SyntaxProvider.ForAttributeWithMetadataName(
            SpecAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ValidateLevel1SpecDiagnostics(ctx));

        context.RegisterSourceOutput(
            cep001to005Provider.Collect(),
            static (spc, diagnosticsList) =>
            {
                foreach (var diags in diagnosticsList)
                {
                    if (diags.IsDefaultOrEmpty)
                        continue;
                    foreach (var diag in diags)
                    {
                        spc.ReportDiagnostic(diag);
                    }
                }
            });

        // ================================================================
        // Diagnostics: CEP008-011 (Level 2 HTTP method attributes)
        // ================================================================
        var cep010PostProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            PostAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ValidateLevel2Diagnostics(ctx, httpMethodValue: 2));

        var cep010GetProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            GetAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ValidateLevel2Diagnostics(ctx, httpMethodValue: 1));

        var cep010PutProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            PutAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ValidateLevel2Diagnostics(ctx, httpMethodValue: 3));

        var cep010DeleteProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            DeleteAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ValidateLevel2Diagnostics(ctx, httpMethodValue: 5));

        var cep010PatchProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            PatchAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ValidateLevel2Diagnostics(ctx, httpMethodValue: 4));

        RegisterDiagnosticOutput(context, cep010PostProvider);
        RegisterDiagnosticOutput(context, cep010GetProvider);
        RegisterDiagnosticOutput(context, cep010PutProvider);
        RegisterDiagnosticOutput(context, cep010DeleteProvider);
        RegisterDiagnosticOutput(context, cep010PatchProvider);

        // ================================================================
        // Diagnostics: CEP009 ([CapabilityEndpointSet])
        // ================================================================
        var cep009Provider = context.SyntaxProvider.ForAttributeWithMetadataName(
            SetAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ValidateSetDiagnostic(ctx));

        RegisterDiagnosticOutput(context, cep009Provider);
    }

    // ================================================================
    // Diagnostic validation helpers
    // ================================================================

    /// <summary>
    /// Validates CEP001-CEP005 diagnostics for [CapabilityEndpointSpec] classes.
    /// </summary>
    private static ImmutableArray<Diagnostic> ValidateLevel1SpecDiagnostics(
        GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return ImmutableArray<Diagnostic>.Empty;

        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        var location = classSymbol.Locations.FirstOrDefault();

        // CEP001: Must be sealed AND nested
        var isSealed = classSymbol.IsSealed;
        var isNested = classSymbol.ContainingType is not null;
        if (!isSealed || !isNested)
        {
            builder.Add(Diagnostic.Create(
                CapabilityEndpointDiagnostics.SpecMustBeSealedNested,
                location,
                classSymbol.Name));
        }

        // CEP002: Container class must have [CapabilityEndpointSpecs]
        if (isNested && classSymbol.ContainingType is not null)
        {
            var container = classSymbol.ContainingType;
            var hasSpecsMarker = container.GetAttributes().Any(attr =>
                attr.AttributeClass?.ToDisplayString() == "CrestCreates.DynamicApi.CapabilityEndpointSpecsAttribute");

            if (!hasSpecsMarker)
            {
                builder.Add(Diagnostic.Create(
                    CapabilityEndpointDiagnostics.ContainerMustHaveSpecsMarker,
                    container.Locations.FirstOrDefault() ?? location,
                    container.Name));
            }
        }

        // CEP003: No methods or constructors with parameters
        var hasMethods = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Any(m => m.MethodKind == MethodKind.Ordinary ||
                       m.MethodKind == MethodKind.Constructor);
        if (hasMethods)
        {
            builder.Add(Diagnostic.Create(
                CapabilityEndpointDiagnostics.SpecNoMethodsOrCtorParams,
                location,
                classSymbol.Name));
        }

        // CEP004: Not inside a [CrestService] type
        if (isNested)
        {
            var containingType = classSymbol.ContainingType;
            while (containingType is not null)
            {
                var hasCrestService = containingType.GetAttributes().Any(attr =>
                    attr.AttributeClass?.ToDisplayString() == CrestServiceMetadataName);

                if (hasCrestService)
                {
                    builder.Add(Diagnostic.Create(
                        CapabilityEndpointDiagnostics.SpecNotInsideCrestService,
                        location,
                        classSymbol.Name,
                        containingType.Name));
                    break;
                }

                containingType = containingType.ContainingType;
            }
        }

        // CEP005: Cannot coexist with [DynamicApiRoute]
        var hasDynamicApiRoute = classSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.ToDisplayString() == DynamicApiRouteMetadataName);
        if (hasDynamicApiRoute)
        {
            builder.Add(Diagnostic.Create(
                CapabilityEndpointDiagnostics.SpecNoDynamicApiRoute,
                location,
                classSymbol.Name));
        }

        // CEP014: Scalar input names must be valid C# identifiers for body+scalar binding.
        // For Level 1 specs, the Body is defined by a [CapabilityEndpointInput] with Source=Body.
        var hasBody = false;
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass is null)
                continue;
            if (attr.AttributeClass.ToDisplayString() != InputAttributeMetadataName)
                continue;

            var sourceValue = 0;
            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == "Source" && !kvp.Value.IsNull && kvp.Value.Value is int sv)
                    sourceValue = sv;
            }

            if (sourceValue == 3) // Body
            {
                hasBody = true;
                break;
            }
        }

        if (hasBody)
        {
            foreach (var inputAttr in classSymbol.GetAttributes())
            {
                if (inputAttr.AttributeClass is null)
                    continue;
                if (inputAttr.AttributeClass.ToDisplayString() != InputAttributeMetadataName)
                    continue;

                var sourceValue = 0;
                var name = string.Empty;
                var capabilityInputPath = (string?)null;

                foreach (var kvp in inputAttr.NamedArguments)
                {
                    if (kvp.Key == "Name" && !kvp.Value.IsNull)
                        name = kvp.Value.Value as string ?? string.Empty;
                    if (kvp.Key == "Source" && !kvp.Value.IsNull && kvp.Value.Value is int sv)
                        sourceValue = sv;
                    if (kvp.Key == "CapabilityInputPath" && !kvp.Value.IsNull)
                        capabilityInputPath = kvp.Value.Value as string;
                }

                // Body inputs are not assigned via EmitScalarPropertyAssignment
                if (sourceValue == 3)
                    continue;

                if (string.IsNullOrEmpty(capabilityInputPath) && !string.IsNullOrEmpty(name)
                    && !CapabilityEndpointBindingEmitter.IsValidCSharpIdentifier(name))
                {
                    builder.Add(Diagnostic.Create(
                        CapabilityEndpointDiagnostics.InvalidScalarPropertyName,
                        location,
                        name,
                        classSymbol.Name,
                        name));
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Validates CEP010 and CEP011 (and CEP008) diagnostics for Level 2 HTTP method attributes.
    /// </summary>
    private static ImmutableArray<Diagnostic> ValidateLevel2Diagnostics(
        GeneratorAttributeSyntaxContext ctx,
        int httpMethodValue)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return ImmutableArray<Diagnostic>.Empty;

        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        var location = classSymbol.Locations.FirstOrDefault();

        // CEP010: Must be sealed, partial, and nested
        var isSealed = classSymbol.IsSealed;
        var isNested = classSymbol.ContainingType is not null;
        var isPartial = IsPartialDeclaration(ctx.TargetNode as ClassDeclarationSyntax);

        if (!isSealed || !isNested || !isPartial)
        {
            builder.Add(Diagnostic.Create(
                CapabilityEndpointDiagnostics.HttpMethodAttributeMustBeSealedPartialNested,
                location,
                classSymbol.Name));
        }

        // CEP011: [Post]/[Put]/[Patch] without Body (warning)
        if (httpMethodValue == 2 || httpMethodValue == 3 || httpMethodValue == 4)
        {
            if (!ctx.Attributes.IsDefaultOrEmpty && ctx.Attributes.Length > 0)
            {
                var attr = ctx.Attributes[0];
                var hasBody = false;
                foreach (var kvp in attr.NamedArguments)
                {
                    if (kvp.Key == "Body" && !kvp.Value.IsNull)
                    {
                        hasBody = true;
                        break;
                    }
                }

                if (!hasBody)
                {
                    builder.Add(Diagnostic.Create(
                        CapabilityEndpointDiagnostics.PostPutPatchMissingBody,
                        location,
                        classSymbol.Name));
                }
            }
        }

        // CEP008: Route+Body DTO must have settable property matching each route token
        if (!ctx.Attributes.IsDefaultOrEmpty && ctx.Attributes.Length > 0)
        {
            var attr = ctx.Attributes[0];
            var dtoDiagnostic = ValidateRouteBodyDtoDiagnostic(classSymbol, attr, location);
            if (dtoDiagnostic is not null)
            {
                builder.Add(dtoDiagnostic);
            }

            // CEP012: Route parameter type must be scalar or enum
            // CEP013: Multiple route params without body (warning)
            var bindingTypeDiagnostics = ValidateRouteBindingTypes(classSymbol, attr, location);
            if (bindingTypeDiagnostics.Length > 0)
                builder.AddRange(bindingTypeDiagnostics);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Validates CEP009: [CapabilityEndpointSet] must be on a static partial class.
    /// </summary>
    private static ImmutableArray<Diagnostic> ValidateSetDiagnostic(
        GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return ImmutableArray<Diagnostic>.Empty;

        var location = classSymbol.Locations.FirstOrDefault();
        var isStatic = classSymbol.IsStatic;
        var isPartial = IsPartialDeclaration(ctx.TargetNode as ClassDeclarationSyntax);

        if (!isStatic || !isPartial)
        {
            return ImmutableArray.Create(Diagnostic.Create(
                CapabilityEndpointDiagnostics.SetMustBeStaticPartial,
                location,
                classSymbol.Name));
        }

        return ImmutableArray<Diagnostic>.Empty;
    }

    /// <summary>
    /// CEP008: Checks that the Body DTO has settable properties for each route token in the route template.
    /// </summary>
    private static Diagnostic? ValidateRouteBodyDtoDiagnostic(
        INamedTypeSymbol classSymbol,
        AttributeData attr,
        Location? location)
    {
        // Get the Body type
        INamedTypeSymbol? bodyType = null;
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == "Body" && !kvp.Value.IsNull &&
                kvp.Value.Kind == TypedConstantKind.Type)
            {
                bodyType = kvp.Value.Value as INamedTypeSymbol;
                break;
            }
        }

        if (bodyType is null)
            return null; // No body — CEP011 handles this separately

        // Get the route string
        var route = string.Empty;
        var ctorArgs = attr.ConstructorArguments;
        if (ctorArgs.Length > 1)
        {
            route = (ctorArgs[1].Value as string) ?? string.Empty;
        }

        // Extract route tokens
        var routeTokens = CapabilityEndpointSpecNormalizer.ExtractAllRouteTokenNames(route);
        if (routeTokens.IsDefaultOrEmpty)
            return null; // No route tokens — nothing to check

        // Get settable property names from body type
        var settableProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectSettableProperties(bodyType, settableProps);

        // Find missing route tokens
        var missing = new List<string>();
        foreach (var token in routeTokens)
        {
            if (!settableProps.Contains(token))
                missing.Add(token);
        }

        if (missing.Count > 0)
        {
            return Diagnostic.Create(
                CapabilityEndpointDiagnostics.RouteBodyDtoMissingProperty,
                location,
                bodyType.Name,
                string.Join(", ", missing));
        }

        return null;
    }

    /// <summary>
    /// Recursively collects all public settable (non-readonly) property names from a type
    /// and its base types.
    /// </summary>
    private static void CollectSettableProperties(INamedTypeSymbol type, HashSet<string> result)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IPropertySymbol prop &&
                prop.DeclaredAccessibility == Accessibility.Public &&
                !prop.IsStatic &&
                !prop.IsReadOnly &&
                prop.SetMethod is not null)
            {
                result.Add(prop.Name);
            }
        }

        // Walk base type
        if (type.BaseType is not null &&
            type.BaseType.SpecialType != SpecialType.System_Object)
        {
            CollectSettableProperties(type.BaseType, result);
        }
    }

    /// <summary>
    /// CEP012 + CEP013: Checks whether route parameter types are scalar or enum (CEP012, error)
    /// and whether multiple route tokens exist without a body/input type (CEP013, warning).
    /// </summary>
    private static ImmutableArray<Diagnostic> ValidateRouteBindingTypes(
        INamedTypeSymbol classSymbol,
        AttributeData attr,
        Location? location)
    {
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();

        // Get Body type from named args
        INamedTypeSymbol? bodyType = null;
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == "Body" && !kvp.Value.IsNull &&
                kvp.Value.Kind == TypedConstantKind.Type)
            {
                bodyType = kvp.Value.Value as INamedTypeSymbol;
                break;
            }
        }

        // Get Input type and name from named args
        INamedTypeSymbol? inputType = null;
        string? inputName = null;
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == "Input" && !kvp.Value.IsNull &&
                kvp.Value.Kind == TypedConstantKind.Type)
                inputType = kvp.Value.Value as INamedTypeSymbol;
            if (kvp.Key == "InputName" && !kvp.Value.IsNull)
                inputName = kvp.Value.Value as string;
        }

        // Get route string
        var route = string.Empty;
        var ctorArgs = attr.ConstructorArguments;
        if (ctorArgs.Length > 1)
            route = (ctorArgs[1].Value as string) ?? string.Empty;

        var routeTokens = CapabilityEndpointSpecNormalizer.ExtractAllRouteTokenNames(route);

        // CEP012: Check explicit Input type
        if (inputType is not null &&
            !CapabilityEndpointSpecNormalizer.IsSupportedRouteBindingType(inputType))
        {
            builder.Add(Diagnostic.Create(
                CapabilityEndpointDiagnostics.UnsupportedRouteParamType,
                location,
                inputName ?? "input",
                classSymbol.Name,
                inputType.ToDisplayString()));
        }

        // CEP012: Check Body-type route token matching properties
        if (bodyType is not null && routeTokens.Length > 0)
        {
            foreach (var token in routeTokens)
            {
                var pascalName = token.Length > 0
                    ? char.ToUpperInvariant(token[0]) + token.Substring(1)
                    : token;

                var propType = CapabilityEndpointSpecNormalizer.FindPropertyTypeOnType(
                    bodyType, pascalName);
                if (propType is not null &&
                    !CapabilityEndpointSpecNormalizer.IsSupportedRouteBindingType(propType))
                {
                    builder.Add(Diagnostic.Create(
                        CapabilityEndpointDiagnostics.UnsupportedRouteParamType,
                        location,
                        token,
                        classSymbol.Name,
                        propType.ToDisplayString()));
                }
            }
        }

        // CEP013: Multiple route tokens without body or explicit input
        if (bodyType is null && inputType is null && routeTokens.Length > 1)
        {
            builder.Add(Diagnostic.Create(
                CapabilityEndpointDiagnostics.MultipleRouteParamsWithoutBody,
                location,
                classSymbol.Name,
                routeTokens.Length));
        }

        // CEP014: Scalar input names must be valid C# identifiers for body+scalar binding.
        // When a Body type exists, scalar inputs (Route/Query/Header) get assigned to model
        // properties — if the input Name is not a valid C# identifier (e.g. "X-Request-Id"),
        // the generated C# code will be invalid. CapabilityInputPath provides a workaround.
        if (bodyType is not null)
        {
            // Check route tokens
            foreach (var token in routeTokens)
            {
                if (!CapabilityEndpointBindingEmitter.IsValidCSharpIdentifier(token))
                {
                    builder.Add(Diagnostic.Create(
                        CapabilityEndpointDiagnostics.InvalidScalarPropertyName,
                        location,
                        token,
                        classSymbol.Name,
                        token));
                }
            }

            // Check [CapabilityEndpointInput] attributes on the class
            foreach (var inputAttr in classSymbol.GetAttributes())
            {
                if (inputAttr.AttributeClass is null)
                    continue;

                var attrDisplayName = inputAttr.AttributeClass.ToDisplayString();
                if (attrDisplayName != InputAttributeMetadataName)
                    continue;

                var inputNamedArgs = inputAttr.NamedArguments;
                var sourceValue = 0;
                var name = string.Empty;
                var capabilityInputPath = (string?)null;

                foreach (var kvp in inputNamedArgs)
                {
                    if (kvp.Key == "Name" && !kvp.Value.IsNull)
                        name = kvp.Value.Value as string ?? string.Empty;
                    if (kvp.Key == "Source" && !kvp.Value.IsNull && kvp.Value.Value is int sv)
                        sourceValue = sv;
                    if (kvp.Key == "CapabilityInputPath" && !kvp.Value.IsNull)
                        capabilityInputPath = kvp.Value.Value as string;
                }

                // Body inputs (sourceValue==3) are not assigned as properties via EmitScalarPropertyAssignment
                if (sourceValue == 3)
                    continue;

                if (string.IsNullOrEmpty(capabilityInputPath) && !string.IsNullOrEmpty(name)
                    && !CapabilityEndpointBindingEmitter.IsValidCSharpIdentifier(name))
                {
                    builder.Add(Diagnostic.Create(
                        CapabilityEndpointDiagnostics.InvalidScalarPropertyName,
                        location,
                        name,
                        classSymbol.Name,
                        name));
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Checks whether a ClassDeclarationSyntax has the 'partial' modifier.
    /// </summary>
    private static bool IsPartialDeclaration(ClassDeclarationSyntax? classDecl)
    {
        if (classDecl is null)
            return false;

        return classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    }

    /// <summary>
    /// Registers a diagnostic-only provider that produces <see cref="ImmutableArray{Diagnostic}"/>.
    /// </summary>
    private static void RegisterDiagnosticOutput(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<ImmutableArray<Diagnostic>> provider)
    {
        context.RegisterSourceOutput(
            provider.Collect(),
            static (spc, diagnosticsList) =>
            {
                foreach (var diags in diagnosticsList)
                {
                    if (diags.IsDefaultOrEmpty)
                        continue;
                    foreach (var diag in diags)
                    {
                        spc.ReportDiagnostic(diag);
                    }
                }
            });
    }

    // ================================================================
    // Level 1: Extract [CapabilityEndpointSpec] records (existing logic)
    // ================================================================

    private static List<ContainerEndpointGroup> GroupAndDeduplicate(
        ImmutableArray<CapabilityEndpointSpecRecord> specs)
    {
        var groupedByContainer = new Dictionary<string, List<CapabilityEndpointSpecRecord>>(
            StringComparer.Ordinal);

        foreach (var spec in specs)
        {
            var key = spec.ContainerClassName;
            if (!groupedByContainer.TryGetValue(key, out var list))
            {
                list = new List<CapabilityEndpointSpecRecord>();
                groupedByContainer[key] = list;
            }

            list.Add(spec);
        }

        var result = new List<ContainerEndpointGroup>(groupedByContainer.Count);
        foreach (var kvp in groupedByContainer)
        {
            var firstSpec = kvp.Value[0];
            var deduped = DeDuplicateSpecs(kvp.Value);
            result.Add(new ContainerEndpointGroup
            {
                ContainerClassName = firstSpec.ContainerClassName,
                ContainerNamespace = firstSpec.ContainerNamespace,
                IsNested = firstSpec.IsNested,
                Specs = deduped.ToImmutableArray()
            });
        }

        return result;
    }

    private static List<CapabilityEndpointSpecRecord> DeDuplicateSpecs(
        IEnumerable<CapabilityEndpointSpecRecord> specs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<CapabilityEndpointSpecRecord>();

        foreach (var spec in specs)
        {
            var key = $"endpoint:{spec.CapabilityId}";
            if (seen.Add(key))
            {
                deduped.Add(spec);
            }
        }

        return deduped;
    }

    private static CapabilityEndpointSpecRecord? ExtractSpecRecord(
        GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        var specAttr = FindMatchingAttribute(ctx.Attributes, SpecAttributeMetadataName);
        if (specAttr is null)
            return null;

        var ctorArgs = specAttr.ConstructorArguments;
        if (ctorArgs.Length < 3)
            return null;

        var capabilityId = GetStringArg(ctorArgs[0]);
        if (string.IsNullOrEmpty(capabilityId))
            return null;

        var httpMethodValue = GetIntArg(ctorArgs[1]);
        var routePattern = GetStringArg(ctorArgs[2]) ?? string.Empty;

        var namedArgs = specAttr.NamedArguments;
        var capabilityVersion = GetNamedIntArg(namedArgs, "CapabilityVersion");
        var authorizationModeValue = GetNamedIntArg(namedArgs, "AuthorizationMode");
        var successStatusCode = GetNamedIntArg(namedArgs, "SuccessStatusCode");
        var operationId = GetNamedStringArg(namedArgs, "OperationId");
        var groupName = GetNamedStringArg(namedArgs, "GroupName");
        var tags = GetNamedStringArrayArg(namedArgs, "Tags");
        var summary = GetNamedStringArg(namedArgs, "Summary");
        var description = GetNamedStringArg(namedArgs, "Description");
        var deprecated = GetNamedBoolArg(namedArgs, "Deprecated");

        var containerType = classSymbol.ContainingType;
        string containerClassName;
        string containerNamespace;
        bool isNested;

        if (containerType is not null)
        {
            containerClassName = containerType.Name;
            containerNamespace = containerType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            isNested = true;
        }
        else
        {
            containerClassName = classSymbol.Name;
            containerNamespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            isNested = false;
        }

        var specClassName = classSymbol.Name;
        var inputRecords = ExtractInputRecords(classSymbol);

        return new CapabilityEndpointSpecRecord
        {
            CapabilityId = capabilityId,
            HttpMethodValue = httpMethodValue,
            RoutePattern = routePattern,
            CapabilityVersion = capabilityVersion,
            AuthorizationModeValue = authorizationModeValue,
            SuccessStatusCode = successStatusCode,
            OperationId = operationId,
            GroupName = groupName,
            Tags = tags,
            Summary = summary,
            Description = description,
            Deprecated = deprecated,
            SpecClassName = specClassName,
            ContainerClassName = containerClassName,
            ContainerNamespace = containerNamespace,
            IsNested = isNested,
            Inputs = inputRecords
        };
    }

    private static ImmutableArray<CapabilityEndpointInputRecord> ExtractInputRecords(
        INamedTypeSymbol classSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<CapabilityEndpointInputRecord>();

        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass is null)
                continue;

            var attrName = attr.AttributeClass.ToDisplayString();
            if (!attrName.EndsWith("CapabilityEndpointInputAttribute", StringComparison.Ordinal) &&
                attrName != InputAttributeMetadataName)
                continue;

            var ctorArgs = attr.ConstructorArguments;
            if (ctorArgs.Length < 1)
                continue;

            var typeSymbol = ctorArgs[0].Value as INamedTypeSymbol;
            var typeName = typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object";

            var namedArgs = attr.NamedArguments;
            var name = GetNamedStringArg(namedArgs, "Name") ?? string.Empty;
            var sourceValue = GetNamedIntArg(namedArgs, "Source", defaultValue: 3);
            var required = GetNamedBoolArg(namedArgs, "Required", defaultValue: true);
            var capabilityInputPath = GetNamedStringArg(namedArgs, "CapabilityInputPath");

            builder.Add(new CapabilityEndpointInputRecord
            {
                TypeName = typeName,
                Name = name,
                SourceValue = sourceValue,
                Required = required,
                CapabilityInputPath = capabilityInputPath
            });
        }

        return builder.ToImmutable();
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static AttributeData? FindMatchingAttribute(
        ImmutableArray<AttributeData> attributes,
        string metadataName)
    {
        foreach (var attr in attributes)
        {
            if (attr.AttributeClass is not null &&
                attr.AttributeClass.ToDisplayString() == metadataName)
            {
                return attr;
            }
        }

        return null;
    }

    private static string? GetStringArg(TypedConstant arg)
    {
        if (arg.IsNull)
            return null;
        return arg.Value as string;
    }

    private static int GetIntArg(TypedConstant arg)
    {
        if (arg.IsNull)
            return 0;
        if (arg.Value is int intValue)
            return intValue;
        return 0;
    }

    private static int GetNamedIntArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name,
        int defaultValue = 0)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value is { IsNull: false } && kvp.Value.Value is int intValue)
                    return intValue;
            }
        }

        return defaultValue;
    }

    private static string? GetNamedStringArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value.IsNull)
                    return null;
                return kvp.Value.Value as string;
            }
        }

        return null;
    }

    private static ImmutableArray<string> GetNamedStringArrayArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value.IsNull || kvp.Value.Kind == TypedConstantKind.Array)
                {
                    var values = kvp.Value.Values;
                    if (values.IsDefaultOrEmpty)
                        return ImmutableArray<string>.Empty;

                    var builder = ImmutableArray.CreateBuilder<string>(values.Length);
                    foreach (var val in values)
                    {
                        if (val.Value is string str)
                            builder.Add(str);
                    }

                    return builder.ToImmutable();
                }
            }
        }

        return ImmutableArray<string>.Empty;
    }

    private static bool GetNamedBoolArg(
        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArgs,
        string name,
        bool defaultValue = false)
    {
        foreach (var kvp in namedArgs)
        {
            if (kvp.Key == name)
            {
                if (kvp.Value is { IsNull: false } && kvp.Value.Value is bool boolValue)
                    return boolValue;
            }
        }

        return defaultValue;
    }

    private static void AddRangeIfNotEmpty(
        List<CapabilityEndpointSpecRecord> target,
        ImmutableArray<CapabilityEndpointSpecRecord> source)
    {
        if (!source.IsDefaultOrEmpty)
            target.AddRange(source);
    }
}
