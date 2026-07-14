using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using CrestCreates.CodeGenerator.DynamicApiGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

[Generator]
public sealed class AppServiceCompatibilityGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var projectedServices = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds
                    && cds.AttributeLists.Count > 0,
                transform: static (ctx, ct) => GetServiceInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        var compilationProvider = context.CompilationProvider;

        context.RegisterSourceOutput(
            projectedServices.Combine(compilationProvider),
            static (spc, source) =>
            {
                GenerateAll(spc, source.Left, source.Right);
            });
    }

    private static CompatibilityServiceModel? GetServiceInfo(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        // Check for [CrestService]
        var hasCrestService = symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "CrestServiceAttribute");

        // Check for class-level [CapabilityCompatibilityProjection]
        var classProjectionAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute");

        // Find methods with method-level [CapabilityCompatibilityProjection] using contract type discovery
        var methodsWithProjection = new List<IMethodSymbol>();
        foreach (var contractType in DynamicApiConventionAnalyzer.EnumerateContractTypes(symbol))
        {
            foreach (var m in contractType.GetMembers().OfType<IMethodSymbol>())
            {
                if (m.MethodKind != MethodKind.Ordinary) continue;
                var implMethod = symbol.FindImplementationForInterfaceMember(m) as IMethodSymbol ?? m;
                if (m.GetAttributes().Any(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute")
                    || implMethod.GetAttributes().Any(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute"))
                    methodsWithProjection.Add(m);
            }
        }

        // CEP030: [CapabilityCompatibilityProjection] on non-[CrestService] class
        if (!hasCrestService && (classProjectionAttr != null || methodsWithProjection.Count > 0))
        {
            var cep030Diagnostics = new List<DiagnosticDescriptorAndLocation>();
            var location = classProjectionAttr?.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? methodsWithProjection.FirstOrDefault()?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                ?? classDecl.GetLocation();
            cep030Diagnostics.Add(new DiagnosticDescriptorAndLocation(
                AppServiceCompatibilityDiagnostics.CEP030, location,
                new object?[] { symbol.Name }));

            return new CompatibilityServiceModel(
                ServiceName: symbol.Name,
                StrippedName: string.Empty,
                SanitizedIdentifier: string.Empty,
                RoutePrefix: string.Empty,
                CapabilityIdPrefix: string.Empty,
                ServiceTypeName: symbol.ToDisplayString(),
                InterfaceTypeName: string.Empty,
                Actions: System.Array.Empty<CompatibilityActionModel>(),
                Diagnostics: cep030Diagnostics.ToArray());
        }

        if (!hasCrestService) return null;

        if (classProjectionAttr == null && methodsWithProjection.Count == 0)
            return null;

        var diagnostics = new List<DiagnosticDescriptorAndLocation>();

        // Validate CEP031 — DynamicApiIgnore conflict (check both contract and implementation methods)
        foreach (var contractMethod in methodsWithProjection)
        {
            var implMethod = symbol.FindImplementationForInterfaceMember(contractMethod) as IMethodSymbol ?? contractMethod;
                    if (HasAttributeOnContractOrImplementation(contractMethod, implMethod, symbol, "DynamicApiIgnoreAttribute"))
            {
                var syntaxRef = (contractMethod.DeclaringSyntaxReferences.FirstOrDefault()
                    ?? implMethod.DeclaringSyntaxReferences.FirstOrDefault());
                var location = syntaxRef?.GetSyntax().GetLocation() ?? Location.None;
                diagnostics.Add(new DiagnosticDescriptorAndLocation(
                    AppServiceCompatibilityDiagnostics.CEP031, location,
                    new object?[] { implMethod.Name }));
            }
        }

        // Build service model using DynamicApiConventionAnalyzer
        var serviceName = DynamicApiConventionAnalyzer.TrimServiceName(symbol.Name);
        var kebabName = DynamicApiConventionAnalyzer.ToKebabCase(serviceName);
        var sanitizedId = KebabToPascal(kebabName);

        // Resolve capability ID prefix from attribute or default
        var capabilityIdPrefix = GetNamedArgValue(classProjectionAttr, "CapabilityIdPrefix")
            ?? $"compat.appservice.{kebabName}";

        // Resolve route prefix
        string routePrefix;
        var usingDefaultPrefix = false;
        var explicitRoutePrefix = GetNamedArgValue(classProjectionAttr, "RoutePrefix");
        if (explicitRoutePrefix is not null)
        {
            routePrefix = explicitRoutePrefix.Trim('/');
        }
        else
        {
            // Find DynamicApiRouteAttribute on class first, then primary interface
            var dynamicApiRouteAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "DynamicApiRouteAttribute");
            if (dynamicApiRouteAttr is null)
            {
                var primaryInterface = symbol.AllInterfaces
                    .FirstOrDefault(i => i.Name == $"I{symbol.Name}");
                if (primaryInterface is not null)
                {
                    dynamicApiRouteAttr = primaryInterface.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "DynamicApiRouteAttribute");
                }
            }

            var routeModel = DynamicApiConventionAnalyzer.ResolveServiceRoute(
                symbol, serviceName,
                dynamicApiRouteAttr?.AttributeClass as INamedTypeSymbol);
            routePrefix = routeModel.IsCustom
                ? routeModel.Template
                : $"api/{routeModel.Template}";
            usingDefaultPrefix = !routeModel.IsCustom;
        }

        // Determine which methods to project using contract-type-based discovery
        // This discovers inherited CRUD methods (e.g. from ICrudAppService<>) that
        // the original symbol.GetMembers() missed.
        var actions = new List<CompatibilityActionModel>();
        var seenMethodKeys = new HashSet<string>(System.StringComparer.Ordinal);
        // Overload detection: track first method symbol per action name
        var firstMethodByActionName = new Dictionary<string, IMethodSymbol>(System.StringComparer.Ordinal);
        bool hasOverloadError = false;

        foreach (var contractType in DynamicApiConventionAnalyzer.EnumerateContractTypes(symbol))
        {
            foreach (var contractMethod in contractType.GetMembers().OfType<IMethodSymbol>())
            {
                if (contractMethod.MethodKind != MethodKind.Ordinary)
                    continue;

                // Map interface method to its concrete implementation on the class.
                // Returns null if contractMethod is from the class itself (non-interface).
                var implMethod = symbol.FindImplementationForInterfaceMember(contractMethod) as IMethodSymbol;
                if (implMethod == null)
                {
                    // Only accept methods whose containing type is symbol (class itself)
                    if (!SymbolEqualityComparer.Default.Equals(contractMethod.ContainingType, symbol))
                        continue;
                    implMethod = contractMethod;
                }

                // Apply visibility and static filters on implementation method
                if (implMethod.DeclaredAccessibility != Accessibility.Public) continue;
                if (implMethod.IsStatic) continue;

                // For class-level projection: check ignore attributes on both contract and implementation
                if (classProjectionAttr != null)
                {
                    if (HasAttributeOnContractOrImplementation(contractMethod, implMethod, symbol, "CapabilityCompatibilityIgnoreAttribute"))
                        continue;
            if (HasAttributeOnContractOrImplementation(contractMethod, implMethod, symbol, "DynamicApiIgnoreAttribute"))
                        continue;
                }
                else
                {
                    // For method-level projection: only include methods with [CapabilityCompatibilityProjection]
                    // Check both contract method and implementation method (P0-2 fix)
                    if (!HasAttributeOnContractOrImplementation(contractMethod, implMethod, symbol, "CapabilityCompatibilityProjectionAttribute"))
                        continue;
                }

                // CEP036: method-level CapabilityIdPrefix/RoutePrefix override is not supported.
                // Check ALL method-level projection attributes (both pure method-level opt-in
                // and redundant method-level attributes on class-projected services).
                var methodProjectionAttr = contractMethod.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute")
                    ?? implMethod.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "CapabilityCompatibilityProjectionAttribute");

                if (methodProjectionAttr is not null)
                {
                    var methodCapabilityIdPrefix = GetNamedArgValue(methodProjectionAttr, "CapabilityIdPrefix");
                    var methodRoutePrefix = GetNamedArgValue(methodProjectionAttr, "RoutePrefix");
                    if (methodCapabilityIdPrefix is not null)
                    {
                        var syntaxRef = (contractMethod.DeclaringSyntaxReferences.FirstOrDefault()
                            ?? implMethod.DeclaringSyntaxReferences.FirstOrDefault());
                        var location = syntaxRef?.GetSyntax().GetLocation() ?? Location.None;
                        diagnostics.Add(new DiagnosticDescriptorAndLocation(
                            AppServiceCompatibilityDiagnostics.CEP036, location,
                            new object?[] { implMethod.Name, "CapabilityIdPrefix" }));
                    }
                    if (methodRoutePrefix is not null)
                    {
                        var syntaxRef = (contractMethod.DeclaringSyntaxReferences.FirstOrDefault()
                            ?? implMethod.DeclaringSyntaxReferences.FirstOrDefault());
                        var location = syntaxRef?.GetSyntax().GetLocation() ?? Location.None;
                        diagnostics.Add(new DiagnosticDescriptorAndLocation(
                            AppServiceCompatibilityDiagnostics.CEP036, location,
                            new object?[] { implMethod.Name, "RoutePrefix" }));
                    }
                }

                // Deduplicate by method signature
                if (!seenMethodKeys.Add(DynamicApiConventionAnalyzer.CreateMethodKey(contractMethod)))
                    continue;

                // Use contractMethod for convention analysis (route, HTTP method, etc.)
                // but implMethod for attributes and parameter details.
                var method = implMethod;

        var httpMethod = DynamicApiConventionAnalyzer.ResolveHttpMethod(method.Name);

                var permission = DynamicApiConventionAnalyzer.ResolvePermission(serviceName, method.Name);

                // CEP035 — default route prefix warning
                if (usingDefaultPrefix && explicitRoutePrefix is null)
                {
                    var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                    var location = syntaxRef?.GetSyntax().GetLocation() ?? Location.None;
                    diagnostics.Add(new DiagnosticDescriptorAndLocation(
                        AppServiceCompatibilityDiagnostics.CEP035, location,
                        new object?[] { method.Name }));
                }

                var actionRoute = DynamicApiConventionAnalyzer.ResolveActionRoute(method);
                var methodStripped = DynamicApiConventionAnalyzer.TrimAsyncSuffix(method.Name);

                // Check for overloads — same action name from different methods
                if (firstMethodByActionName.TryGetValue(methodStripped, out var previousMethod))
                {
                    if (!hasOverloadError)
                    {
                        hasOverloadError = true;
                        var prevSyntaxRef = previousMethod.DeclaringSyntaxReferences.FirstOrDefault();
                        var prevLocation = prevSyntaxRef?.GetSyntax().GetLocation() ?? Location.None;
                        diagnostics.Add(new DiagnosticDescriptorAndLocation(
                            AppServiceCompatibilityDiagnostics.CEP034, prevLocation,
                            new object?[] { previousMethod.Name, symbol.Name }));
                    }
                    var overloadSyntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                    var overloadLocation = overloadSyntaxRef?.GetSyntax().GetLocation() ?? Location.None;
                    diagnostics.Add(new DiagnosticDescriptorAndLocation(
                        AppServiceCompatibilityDiagnostics.CEP034, overloadLocation,
                        new object?[] { method.Name, symbol.Name }));
                    continue;
                }
                firstMethodByActionName[methodStripped] = method;

                var methodKebab = DynamicApiConventionAnalyzer.ToKebabCase(methodStripped);
                var capabilityId = $"{capabilityIdPrefix}.{methodKebab}";
                var endpointId = $"endpoint:{capabilityId}";

                // Build full route pattern
                var routePattern = string.IsNullOrEmpty(actionRoute)
                    ? $"/{routePrefix}"
                    : $"/{routePrefix}/{actionRoute}";

                // Analyze parameters
                var routeTokens = new HashSet<string>();
                foreach (Match match in Regex.Matches(routePattern, @"\{(\w+)\}"))
                    routeTokens.Add(match.Groups[1].Value);

                bool bodyAssigned = false;
                var paramModels = new List<CompatibilityParameterModel>();
                bool hasCancellationToken = false;

                foreach (var param in method.Parameters)
                {
                    var source = DynamicApiConventionAnalyzer.ResolveParameterSource(
                        param, routeTokens, httpMethod, ref bodyAssigned);
                    var sourceStr = source.ToString();

                    if (source == DynamicApiGenerator.ParameterSource.CancellationToken)
                    {
                        hasCancellationToken = true;
                        paramModels.Add(new CompatibilityParameterModel(
                            Name: param.Name,
                            TypeName: param.Type.ToDisplayString(FullyQualifiedFormat),
                            TypeOfExpression: DynamicApiConventionAnalyzer.ToTypeOfExpression(param.Type),
                            Source: sourceStr,
                            PascalName: CapitalizeFirst(param.Name),
                            IsOptional: true,
                            IsQueryObject: false,
                            HeaderName: null,
                            QueryProperties: ImmutableArray<CompatibilityQueryPropertyModel>.Empty));
                    }
                    else
                    {
                        var isOptional = param.IsOptional || param.HasExplicitDefaultValue
                            || (source == DynamicApiGenerator.ParameterSource.Query
                                && DynamicApiConventionAnalyzer.IsNullableType(param.Type));
                        var isScalar = DynamicApiConventionAnalyzer.IsScalar(param.Type);
                        var isQueryObject = source == DynamicApiGenerator.ParameterSource.Query && !isScalar;
                        var queryProperties = isQueryObject
                            ? DynamicApiConventionAnalyzer.BuildQueryProperties(param.Type)
                                .Select(p => new CompatibilityQueryPropertyModel(p.Name, p.TypeName, p.IsScalar, p.IsOptional))
                                .ToImmutableArray()
                            : ImmutableArray<CompatibilityQueryPropertyModel>.Empty;

                        // Map parameter name to HTTP header name (expectedStamp → If-Match)
                        var headerName = source == DynamicApiGenerator.ParameterSource.Header
                            ? ResolveHttpHeaderName(param.Name)
                            : null;

                        paramModels.Add(new CompatibilityParameterModel(
                            Name: param.Name,
                            TypeName: param.Type.ToDisplayString(FullyQualifiedFormat),
                            TypeOfExpression: DynamicApiConventionAnalyzer.ToTypeOfExpression(param.Type),
                            Source: sourceStr,
                            PascalName: CapitalizeFirst(param.Name),
                            IsOptional: isOptional,
                            IsQueryObject: isQueryObject,
                            HeaderName: headerName,
                            QueryProperties: queryProperties));
                    }
                }

                var nonCancellationParams = paramModels
                    .Where(p => p.Source != "CancellationToken")
                    .ToList();

                var isSingleParam = nonCancellationParams.Count == 1
                    && nonCancellationParams[0].Source == "Body";

                var isSingleScalarParam = nonCancellationParams.Count == 1
                    && nonCancellationParams[0].Source != "Body";

                // Determine input type name
                string inputTypeName;
                string? envelopeTypeName;
                if (isSingleParam)
                {
                    inputTypeName = nonCancellationParams[0].TypeName;
                    envelopeTypeName = null;
                }
                else if (nonCancellationParams.Count > 0)
                {
                    var envelopeName = $"{symbol.Name}_{methodStripped}_CompatibilityInput";
                    inputTypeName = envelopeName;
                    envelopeTypeName = envelopeName;
                }
                else
                {
                    // No non-cancellation params
                    inputTypeName = "object";
                    envelopeTypeName = null;
                }

                // Determine return type
                var returnType = method.ReturnType;
                var returnNamedType = returnType as INamedTypeSymbol;
                var isVoidReturn = (returnNamedType != null && returnType.Name == "Task" && !returnNamedType.IsGenericType)
                    || (returnNamedType != null && returnType.Name == "ValueTask" && !returnNamedType.IsGenericType)
                    || returnType.SpecialType == SpecialType.System_Void;

                var returnTypeName = isVoidReturn ? "void"
                    : returnType is INamedTypeSymbol namedReturn && namedReturn.IsGenericType
                        ? namedReturn.TypeArguments[0].ToDisplayString(FullyQualifiedFormat)
                        : returnType.ToDisplayString(FullyQualifiedFormat);

                // CEP037: Body parameter type must be suitable for generated emptyBodyFactory
                var bodyParamModel = paramModels.FirstOrDefault(p => p.Source == "Body");
                if (bodyParamModel is not null)
                {
                    var bodyTypeSymbol = implMethod.Parameters
                        .FirstOrDefault(p => p.Type.ToDisplayString(FullyQualifiedFormat) == bodyParamModel.TypeName
                            || p.Name == bodyParamModel.Name)?.Type;

                    if (bodyTypeSymbol is not null && !SatisfiesNewConstraint(bodyTypeSymbol))
                    {
                        var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                        var location = syntaxRef?.GetSyntax().GetLocation() ?? Location.None;
                        diagnostics.Add(new DiagnosticDescriptorAndLocation(
                            AppServiceCompatibilityDiagnostics.CEP037, location,
                            new object?[] { implMethod.Name, bodyParamModel.TypeName }));
                        continue; // Fail-closed: skip action to avoid generating code with CS0310
                    }
                }

                actions.Add(new CompatibilityActionModel(
                    ActionName: methodStripped,
                    HttpMethod: httpMethod,
                    RoutePattern: routePattern,
                    CapabilityId: capabilityId,
                    EndpointId: endpointId,
                    PermissionName: permission ?? string.Empty,
                    ServiceMethodName: method.Name,
                    IsSingleParam: isSingleParam,
                    IsSingleScalarParam: isSingleScalarParam,
                    InputTypeName: inputTypeName,
                    EnvelopeTypeName: envelopeTypeName,
                    ReturnTypeName: returnTypeName,
                    IsVoidReturn: isVoidReturn,
                    HasCancellationToken: hasCancellationToken,
                    Parameters: paramModels.ToArray()));
            }
        }

        // If overload errors were detected, return only diagnostics (no actions) to prevent broken generation
        if (hasOverloadError)
        {
            return new CompatibilityServiceModel(
                ServiceName: serviceName,
                StrippedName: kebabName,
                SanitizedIdentifier: sanitizedId,
                RoutePrefix: routePrefix,
                CapabilityIdPrefix: capabilityIdPrefix,
                ServiceTypeName: symbol.ToDisplayString(FullyQualifiedFormat),
                InterfaceTypeName: symbol.ToDisplayString(FullyQualifiedFormat),
                Actions: System.Array.Empty<CompatibilityActionModel>(),
                Diagnostics: diagnostics.ToArray());
        }

        if (actions.Count == 0 && diagnostics.Count == 0) return null;

        // If there are diagnostics but no actions, return a model with empty actions
        // so diagnostics are reported in GenerateAll.
        if (actions.Count == 0)
        {
            return new CompatibilityServiceModel(
                ServiceName: serviceName,
                StrippedName: kebabName,
                SanitizedIdentifier: sanitizedId,
                RoutePrefix: routePrefix,
                CapabilityIdPrefix: capabilityIdPrefix,
                ServiceTypeName: symbol.ToDisplayString(FullyQualifiedFormat),
                InterfaceTypeName: symbol.ToDisplayString(FullyQualifiedFormat),
                Actions: System.Array.Empty<CompatibilityActionModel>(),
                Diagnostics: diagnostics.ToArray());
        }

        // Find interface type for DI resolution
        var interfaceType = symbol.AllInterfaces
            .FirstOrDefault(i => i.Name == $"I{symbol.Name}");
        var interfaceTypeName = interfaceType?.ToDisplayString(FullyQualifiedFormat)
            ?? symbol.ToDisplayString(FullyQualifiedFormat);

        return new CompatibilityServiceModel(
            ServiceName: serviceName,
            StrippedName: kebabName,
            SanitizedIdentifier: sanitizedId,
            RoutePrefix: routePrefix,
            CapabilityIdPrefix: capabilityIdPrefix,
            ServiceTypeName: symbol.ToDisplayString(FullyQualifiedFormat),
            InterfaceTypeName: interfaceTypeName,
            Actions: actions.ToArray(),
            Diagnostics: diagnostics.ToArray());
    }

    private static void GenerateAll(
        SourceProductionContext spc,
        ImmutableArray<CompatibilityServiceModel?> services,
        Compilation compilation)
    {
        var validServices = services.Where(s => s is not null).ToList();
        if (validServices.Count == 0) return;

        foreach (var service in validServices)
        {
            if (service == null) continue;

            // Report deferred diagnostics
            foreach (var diag in service.Diagnostics)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    diag.Descriptor, diag.Location, diag.MessageArgs));
            }

            // Fail-closed: skip code generation for services with error-level diagnostics
            // (CEP030, CEP031, CEP034, CEP037) to avoid generating code that would
            // produce additional compiler errors (e.g., CS0310 for new() constraint violation)
            if (service.Diagnostics.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error))
            {
                continue;
            }

            // 1. CapabilityDescriptor provider
            var capabilitySource = AppServiceCompatibilityCapabilityEmitter.Emit(service);
            spc.AddSource($"GeneratedAppServiceCompatibilityCapabilities_{service.SanitizedIdentifier}.g.cs", capabilitySource);

            // 2. EndpointDescriptor provider + bindings
            var (endpointsSource, bindingsSource) = AppServiceCompatibilityEndpointEmitter.Emit(service);
            spc.AddSource($"GeneratedAppServiceCompatibilityEndpoints_{service.SanitizedIdentifier}.g.cs", endpointsSource);
            spc.AddSource($"GeneratedAppServiceCompatibilityBindings_{service.SanitizedIdentifier}.g.cs", bindingsSource);

            // 3. Handler invokers
            var invokersSource = AppServiceCompatibilityInvokerEmitter.Emit(service);
            spc.AddSource($"GeneratedAppServiceCompatibilityInvokers_{service.SanitizedIdentifier}.g.cs", invokersSource);

            // 4. Manifest
            var manifestSource = AppServiceCompatibilityManifestEmitter.Emit(service);
            spc.AddSource($"GeneratedAppServiceCompatibilityManifest_{service.SanitizedIdentifier}.g.cs", manifestSource);

            // 5. Result contracts
            var resultContractsSource = AppServiceCompatibilityResultContractEmitter.Emit(service);
            spc.AddSource($"GeneratedAppServiceCompatibilityResultContracts_{service.SanitizedIdentifier}.g.cs", resultContractsSource);
        }
    }

    /// <summary>
    /// Checks whether an attribute with the given name exists on the method,
    /// considering both the contract (interface) method and the implementation (class) method.
    /// C# does not propagate interface method attributes to implementing class methods,
    /// so both must be checked explicitly.
    /// When the method being processed is a class method (contractType == serviceType),
    /// this uses FindImplementationForInterfaceMember reverse lookup to find the exact
    /// interface method that the class method implements, avoiding approximate signature matching.
    /// </summary>
    private static bool HasAttributeOnContractOrImplementation(
        IMethodSymbol contractMethod,
        IMethodSymbol implementationMethod,
        INamedTypeSymbol serviceType,
        string attributeName)
    {
        // Check the method we're currently iterating (could be interface or class method)
        if (contractMethod.GetAttributes().Any(a => a.AttributeClass?.Name == attributeName))
            return true;
        if (implementationMethod.GetAttributes().Any(a => a.AttributeClass?.Name == attributeName))
            return true;

        // When processing a class method directly (contractType == serviceType),
        // the interface method attributes won't be on the class method.
        // Use FindImplementationForInterfaceMember reverse lookup for exact symbol matching.
        if (SymbolEqualityComparer.Default.Equals(contractMethod.ContainingType, serviceType))
        {
            foreach (var iface in serviceType.AllInterfaces)
            {
                foreach (var ifaceMethod in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var mappedImpl = serviceType.FindImplementationForInterfaceMember(ifaceMethod) as IMethodSymbol;
                    if (mappedImpl != null && SymbolEqualityComparer.Default.Equals(mappedImpl, contractMethod))
                    {
                        // ifaceMethod is the exact interface method that contractMethod implements
                        if (ifaceMethod.GetAttributes().Any(a => a.AttributeClass?.Name == attributeName))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a body type is suitable for compatibility body binding.
    /// Single-dimensional arrays are allowed (the emitter uses Array.Empty&lt;T&gt;()
    /// instead of new T[]). Multi-dimensional arrays are rejected.
    /// For classes/records, a public parameterless constructor is required
    /// because the emitter generates <c>static () =&gt; new T()</c>.
    /// </summary>
    private static bool SatisfiesNewConstraint(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
            return arrayType.Rank == 1 && !ContainsTypeParameter(arrayType.ElementType);

        if (type is not INamedTypeSymbol named)
            return false;

        if (named.IsAbstract || named.IsStatic || named.TypeKind == TypeKind.Interface)
            return false;

        if (named.IsUnboundGenericType || named.TypeArguments.Any(ta => ta.TypeKind == TypeKind.TypeParameter))
            return false;

        // Structs always have parameterless constructor
        if (named.TypeKind == TypeKind.Struct)
            return true;

        // Classes need explicit parameterless constructor
        return named.InstanceConstructors.Any(c =>
            c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public);
    }

    /// <summary>
    /// Recursively checks whether a type symbol contains any type parameters
    /// (i.e., is an open generic type). Handles nested generics like
    /// Wrapper&lt;Outer&lt;List&lt;T&gt;&gt;&gt; by recursing through type arguments
    /// and array element types.
    /// </summary>
    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        return type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
            _ => false
        };
    }

    private static string? GetNamedArgValue(AttributeData? attr, string name)
    {
        if (attr == null) return null;
        foreach (var kvp in attr.NamedArguments)
        {
            if (kvp.Key == name && kvp.Value.Value is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }

        return null;
    }

    private static string KebabToPascal(string kebab)
    {
        if (string.IsNullOrEmpty(kebab)) return kebab;
        var parts = kebab.Split('-');
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    /// <summary>
    /// Maps parameter names to HTTP header names.
    /// Matches legacy DynamicApi convention: expectedStamp → If-Match (with ETag quote trimming).
    /// </summary>
    private static string? ResolveHttpHeaderName(string parameterName)
    {
        return parameterName switch
        {
            "expectedStamp" => "If-Match",
            _ => null, // Unknown header parameters — will use parameter name as header name
        };
    }

    private static string CapitalizeFirst(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
