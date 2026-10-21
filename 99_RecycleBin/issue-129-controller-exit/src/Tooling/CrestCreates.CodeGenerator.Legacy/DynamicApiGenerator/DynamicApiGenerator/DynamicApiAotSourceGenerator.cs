using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.DynamicApiGenerator;

[Generator]
public sealed class DynamicApiAotSourceGenerator : IIncrementalGenerator
{
    private const string DynamicApiAotGenerationPropertyName = "build_property.CrestCreatesDynamicApiAotGeneration";

    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generationContextProvider = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(context.AdditionalTextsProvider.Collect())
            .Select(static (input, _) =>
            {
                var ((compilation, optionsProvider), additionalTexts) = input;
                return IsGenerationDisabled(optionsProvider)
                    ? null
                    : BuildGenerationContext(compilation, additionalTexts);
            });

        context.RegisterSourceOutput(generationContextProvider, static (productionContext, generationContext) =>
        {
            if (generationContext is null || generationContext.Services.Length == 0)
            {
                return;
            }

            productionContext.AddSource(
                "GeneratedDynamicApiRegistry.g.cs",
                SourceText.From(GenerateRegistrySource(generationContext), Encoding.UTF8));

            productionContext.AddSource(
                "GeneratedDynamicApiEndpoints.g.cs",
                SourceText.From(GenerateEndpointsSource(generationContext), Encoding.UTF8));
        });
    }

    private static bool IsGenerationDisabled(AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (!optionsProvider.GlobalOptions.TryGetValue(DynamicApiAotGenerationPropertyName, out var value))
        {
            return false;
        }

        return value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("disable", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static GenerationContext? BuildGenerationContext(
        Compilation compilation,
        ImmutableArray<AdditionalText> additionalTexts)
    {
        // #128 S4: a service whose contract is owned by the CRUD precompile manifest already has its
        // formal HTTP surface; materializing the legacy Dynamic API registry/endpoints for it would
        // consume the legacy provider and body reader that §9 forbids. Ownership is asked once here,
        // from the same manifest reader the Entity/ServiceRegistration generators use.
        var ownedCrudArtifacts = CrudServiceGenerator.CrudArtifactOwnership.Read(additionalTexts);
        if (compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") is null ||
            compilation.GetTypeByMetadataName("CrestCreates.DynamicApi.IDynamicApiGeneratedProvider") is null)
        {
            return null;
        }

        var crestServiceAttribute = compilation.GetTypeByMetadataName("CrestCreates.Domain.Shared.Attributes.CrestServiceAttribute");

        var dynamicApiIgnoreAttribute = compilation.GetTypeByMetadataName("CrestCreates.Domain.Shared.Attributes.DynamicApiIgnoreAttribute");
        var compatibilityProjectionAttribute = compilation.GetTypeByMetadataName("CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityProjectionAttribute");
        var compatibilityIgnoreAttribute = compilation.GetTypeByMetadataName("CrestCreates.Domain.Shared.Attributes.CapabilityCompatibilityIgnoreAttribute");
        var dynamicApiRouteAttribute = compilation.GetTypeByMetadataName("CrestCreates.Domain.Shared.Attributes.DynamicApiRouteAttribute");
        var unitOfWorkAttribute = compilation.GetTypeByMetadataName("CrestCreates.Aop.Interceptors.UnitOfWorkMoAttribute");
        var services = new List<ServiceModel>();

        foreach (var type in EnumerateNamedTypes(compilation.Assembly))
        {
            if (ownedCrudArtifacts.Count > 0
                && ownedCrudArtifacts.Contains(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty)))
            {
                continue;
            }

            if (crestServiceAttribute is not null &&
                IsDynamicApiImplementation(type, crestServiceAttribute, dynamicApiIgnoreAttribute, compatibilityProjectionAttribute))
            {
                services.AddRange(BuildServiceModels(type, dynamicApiIgnoreAttribute, dynamicApiRouteAttribute, unitOfWorkAttribute, compatibilityProjectionAttribute, compatibilityIgnoreAttribute));
            }
        }

        return new GenerationContext(
            compilation.AssemblyName ?? "DynamicApiHost",
            services.OrderBy(service => service.RouteTemplate, StringComparer.Ordinal).ToImmutableArray());
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(IAssemblySymbol assembly)
    {
        return EnumerateNamespace(assembly.GlobalNamespace);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamespace(INamespaceSymbol namespaceSymbol)
    {
        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var nestedType in EnumerateNamespace(nestedNamespace))
            {
                yield return nestedType;
            }
        }

        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;

            foreach (var nestedType in EnumerateNestedTypes(type))
            {
                yield return nestedType;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol typeSymbol)
    {
        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            yield return nestedType;

            foreach (var child in EnumerateNestedTypes(nestedType))
            {
                yield return child;
            }
        }
    }

    private static bool IsDynamicApiImplementation(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol crestServiceAttribute,
        INamedTypeSymbol? dynamicApiIgnoreAttribute,
        INamedTypeSymbol? compatibilityProjectionAttribute)
    {
        if (typeSymbol.TypeKind != TypeKind.Class ||
            typeSymbol.IsAbstract ||
            typeSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (dynamicApiIgnoreAttribute is not null && HasAttribute(typeSymbol, dynamicApiIgnoreAttribute))
        {
            return false;
        }

        if (compatibilityProjectionAttribute is not null && HasAttribute(typeSymbol, compatibilityProjectionAttribute))
        {
            return false;
        }

        // Accept [CrestService] classes
        if (HasAttribute(typeSymbol, crestServiceAttribute))
        {
            return true;
        }

        // Also accept generated CRUD services that implement ICrudAppService<>.
        // Generated types from other source generators are visible in the same
        // compilation round when the CRUD generator executes before this generator.
        if (ImplementsCrudAppService(typeSymbol))
        {
            return true;
        }

        return false;
    }

    private static bool ImplementsCrudAppService(INamedTypeSymbol typeSymbol)
    {
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (iface.Name == "ICrudAppService" && iface.TypeArguments.Length >= 5)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<ServiceModel> BuildServiceModels(
        INamedTypeSymbol implementationType,
        INamedTypeSymbol? dynamicApiIgnoreAttribute,
        INamedTypeSymbol? dynamicApiRouteAttribute,
        INamedTypeSymbol? unitOfWorkAttribute,
        INamedTypeSymbol? compatibilityProjectionAttribute,
        INamedTypeSymbol? compatibilityIgnoreAttribute)
    {
        var serviceInterfaces = implementationType.Interfaces
            .Where(interfaceSymbol => interfaceSymbol.DeclaredAccessibility == Accessibility.Public)
            .Where(interfaceSymbol => !interfaceSymbol.IsGenericType)
            .Where(interfaceSymbol => interfaceSymbol.Name.EndsWith("AppService", StringComparison.Ordinal))
            .Where(interfaceSymbol => dynamicApiIgnoreAttribute is null || !HasAttribute(interfaceSymbol, dynamicApiIgnoreAttribute))
            .ToArray();

        foreach (var serviceType in serviceInterfaces)
        {
            var serviceName = DynamicApiConventionAnalyzer.TrimServiceName(serviceType.Name);
            var route = DynamicApiConventionAnalyzer.ResolveServiceRoute(serviceType, serviceName, dynamicApiRouteAttribute);
            var actions = BuildActionModels(serviceType, implementationType, serviceName, dynamicApiIgnoreAttribute, unitOfWorkAttribute, compatibilityProjectionAttribute, compatibilityIgnoreAttribute).ToImmutableArray();
            if (actions.Length == 0)
            {
                continue;
            }

            yield return new ServiceModel(
                serviceName,
                route.Template,
                route.IsCustom,
                serviceType.ToDisplayString(FullyQualifiedFormat),
                implementationType.ToDisplayString(FullyQualifiedFormat),
                actions);
        }
    }

    private static IEnumerable<ActionModel> BuildActionModels(
        INamedTypeSymbol serviceType,
        INamedTypeSymbol implementationType,
        string serviceName,
        INamedTypeSymbol? dynamicApiIgnoreAttribute,
        INamedTypeSymbol? unitOfWorkAttribute,
        INamedTypeSymbol? compatibilityProjectionAttribute,
        INamedTypeSymbol? compatibilityIgnoreAttribute)
    {
        var seenMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contractType in DynamicApiConventionAnalyzer.EnumerateContractTypes(serviceType))
        {
            foreach (var method in contractType.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (dynamicApiIgnoreAttribute is not null && HasAttribute(method, dynamicApiIgnoreAttribute))
                {
                    continue;
                }

                // P0-2 fix: unified check for [CapabilityCompatibilityProjection] on method
                // Checks both interface and implementation method regardless of which one we're iterating
                if (HasCompatibilityProjectionOnMethod(method, implementationType, compatibilityProjectionAttribute))
                {
                    continue;
                }

                if (compatibilityIgnoreAttribute is not null && HasAttribute(method, compatibilityIgnoreAttribute)
                    && compatibilityProjectionAttribute is not null && HasAttribute(implementationType, compatibilityProjectionAttribute))
                {
                    continue;
                }

                if (!seenMethodKeys.Add(DynamicApiConventionAnalyzer.CreateMethodKey(method)))
                {
                    continue;
                }

        var actionName = DynamicApiConventionAnalyzer.TrimAsyncSuffix(method.Name);
                var httpMethod = DynamicApiConventionAnalyzer.ResolveHttpMethod(method.Name);
                var relativeRoute = DynamicApiConventionAnalyzer.ResolveActionRoute(method);
                var parameters = ResolveParameters(method, relativeRoute, httpMethod).ToImmutableArray();
                var returnModel = ResolveReturnModel(method.ReturnType);
                var implementationMethod = implementationType.FindImplementationForInterfaceMember(method) as IMethodSymbol;
                var unitOfWork = ResolveUnitOfWork(implementationMethod, method, implementationType, serviceType, unitOfWorkAttribute, httpMethod);

                yield return new ActionModel(
                    actionName,
                    serviceType.Name,
                    $"{serviceType.Name}_{actionName}",
                    relativeRoute,
                    httpMethod,
                    DynamicApiConventionAnalyzer.ResolvePermission(serviceName, method.Name),
                    returnModel,
                    parameters,
                    method.Name,
                    serviceType.ToDisplayString(FullyQualifiedFormat),
                    unitOfWork.RequiresUnitOfWork,
                    unitOfWork.RequiresTransaction,
                    ImmutableArray<string>.Empty,
                    false);
            }
        }
    }

    private static ImmutableArray<ParameterModel> ResolveParameters(IMethodSymbol methodSymbol, string routePattern, string httpMethod)
    {
        var routeTokens = new HashSet<string>(routePattern.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.StartsWith("{", StringComparison.Ordinal) && segment.EndsWith("}", StringComparison.Ordinal))
            .Select(segment => segment.Substring(1, segment.Length - 2)), StringComparer.OrdinalIgnoreCase);

        var bodyAssigned = false;
        var parameters = ImmutableArray.CreateBuilder<ParameterModel>();
        foreach (var parameter in methodSymbol.Parameters)
        {
            var source = DynamicApiConventionAnalyzer.ResolveParameterSource(parameter, routeTokens, httpMethod, ref bodyAssigned);
            parameters.Add(new ParameterModel(
                parameter.Name,
                parameter.Type.ToDisplayString(FullyQualifiedFormat),
                source,
                parameter.IsOptional || parameter.HasExplicitDefaultValue || (source != ParameterSource.Body && DynamicApiConventionAnalyzer.IsNullableType(parameter.Type)),
                DynamicApiConventionAnalyzer.IsScalar(parameter.Type),
                source == ParameterSource.Query && !DynamicApiConventionAnalyzer.IsScalar(parameter.Type)
                    ? DynamicApiConventionAnalyzer.BuildQueryProperties(parameter.Type)
                    : ImmutableArray<QueryPropertyModel>.Empty));
        }

        return parameters.ToImmutable();
    }

    private static ReturnModel ResolveReturnModel(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType &&
            namedType.Name == "Task" &&
            namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        {
            if (namedType.TypeArguments.Length == 1)
            {
                return new ReturnModel(false, namedType.TypeArguments[0].ToDisplayString(FullyQualifiedFormat));
            }

            return new ReturnModel(true, null);
        }

        return new ReturnModel(returnType.SpecialType == SpecialType.System_Void, returnType.ToDisplayString(FullyQualifiedFormat));
    }

    private static (bool RequiresUnitOfWork, bool RequiresTransaction) ResolveUnitOfWork(
        IMethodSymbol? implementationMethod,
        IMethodSymbol serviceMethod,
        INamedTypeSymbol implementationType,
        INamedTypeSymbol serviceType,
        INamedTypeSymbol? unitOfWorkAttribute,
        string httpMethod)
    {
        if (unitOfWorkAttribute is not null)
        {
            var attribute = GetAttribute(implementationMethod, unitOfWorkAttribute)
                            ?? GetAttribute(serviceMethod, unitOfWorkAttribute)
                            ?? GetAttribute(implementationType, unitOfWorkAttribute)
                            ?? GetAttribute(serviceType, unitOfWorkAttribute);
            if (attribute is not null)
            {
                var requiresTransaction = true;
                if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is bool configured)
                {
                    requiresTransaction = configured;
                }

                return (true, requiresTransaction);
            }
        }

        return httpMethod == "GET"
            ? (false, false)
            : (true, true);
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
    {
        return symbol.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
    }

    /// <summary>
    /// P0-2 fix: unified check for [CapabilityCompatibilityProjection] on a method.
    /// Checks both the current method symbol AND the corresponding interface/implementation method.
    /// This handles the case where the attribute is on the interface method but the current
    /// iteration is over the class method (via EnumerateContractTypes yielding serviceType first).
    /// </summary>
    private static bool HasCompatibilityProjectionOnMethod(
        IMethodSymbol method,
        INamedTypeSymbol serviceType,
        INamedTypeSymbol? compatibilityProjectionAttribute)
    {
        if (compatibilityProjectionAttribute is null)
            return false;

        // Check the method itself
        if (HasAttribute(method, compatibilityProjectionAttribute))
            return true;

        // If method is on the class (implementation), check the interface methods
        if (method.ContainingType.TypeKind == TypeKind.Class)
        {
            foreach (var iface in serviceType.AllInterfaces)
            {
                foreach (var ifaceMethod in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (ifaceMethod.Name == method.Name &&
                        ifaceMethod.Parameters.Length == method.Parameters.Length)
                    {
                        if (HasAttribute(ifaceMethod, compatibilityProjectionAttribute))
                            return true;
                    }
                }
            }
        }

        // If method is on an interface, check the implementation method
        if (method.ContainingType.TypeKind == TypeKind.Interface)
        {
            var implMethod = serviceType.FindImplementationForInterfaceMember(method) as IMethodSymbol;
            if (implMethod is not null && HasAttribute(implMethod, compatibilityProjectionAttribute))
                return true;
        }

        return false;
    }

    private static AttributeData? GetAttribute(ISymbol? symbol, INamedTypeSymbol attributeSymbol)
    {
        if (symbol is null)
        {
            return null;
        }

        return symbol.GetAttributes().FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
    }

    private static string GenerateRegistrySource(GenerationContext context)
    {
        var providerTypeName = GetProviderTypeName(context.AssemblyName);
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using System.Reflection;");
        builder.AppendLine("using CrestCreates.DynamicApi;");
        builder.AppendLine();
        builder.AppendLine("namespace CrestCreates.DynamicApi.Generated;");
        builder.AppendLine();
        builder.AppendLine($"internal sealed class {providerTypeName} : IDynamicApiGeneratedProvider");
        builder.AppendLine("{");
        builder.AppendLine("    private static readonly (Assembly Assembly, Func<DynamicApiOptions, DynamicApiServiceDescriptor> DescriptorFactory)[] Entries = new (Assembly Assembly, Func<DynamicApiOptions, DynamicApiServiceDescriptor> DescriptorFactory)[]");
        builder.AppendLine("    {");
        for (var index = 0; index < context.Services.Length; index++)
        {
            var service = context.Services[index];
            builder.AppendLine($"        (typeof({service.ServiceAssemblyTypeName}).Assembly, CreateService{index}),");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public System.Collections.Generic.IReadOnlyCollection<Assembly> ServiceAssemblies => Entries.Select(entry => entry.Assembly).Distinct().ToArray();");
        builder.AppendLine();
        builder.AppendLine("    public System.Collections.Generic.IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors { get; } =");
        builder.AppendLine("        new DynamicApiEndpointDescriptor[]");
        builder.AppendLine("        {");
        for (var serviceIndex = 0; serviceIndex < context.Services.Length; serviceIndex++)
        {
            var service = context.Services[serviceIndex];
            var routePrefix = service.HasCustomRoute
                ? Escape(service.RouteTemplate)
                : $"api/{Escape(service.RouteTemplate)}";
            for (var actionIndex = 0; actionIndex < service.Actions.Length; actionIndex++)
            {
                var action = service.Actions[actionIndex];

                var routePattern = string.IsNullOrEmpty(action.RelativeRoute)
                    ? routePrefix
                    : $"{routePrefix}/{Escape(action.RelativeRoute)}";
                var requestType = ResolveRequestTypeFromAction(action);
                var responseType = action.ReturnModel.PayloadTypeName is not null
                    ? $"typeof({GetTypeOfTypeName(action.ReturnModel.PayloadTypeName)})"
                    : "null";
                var permissions = $"new[] {{ \"{Escape(action.PermissionName)}\" }}";
                var requiresTransaction = ToBooleanLiteral(action.RequiresTransaction);
                builder.AppendLine($"            new DynamicApiEndpointDescriptor(");
                builder.AppendLine($"                \"{Escape(service.ServiceName)}\",");
                builder.AppendLine($"                \"{Escape(action.ActionName)}\",");
                builder.AppendLine($"                \"{action.HttpMethod}\",");
                builder.AppendLine($"                \"{routePattern}\",");
                builder.AppendLine($"                typeof({service.ServiceTypeName}),");
                builder.AppendLine($"                {requestType},");
                builder.AppendLine($"                {responseType},");
                builder.AppendLine($"                {permissions},");
                builder.AppendLine($"                {requiresTransaction}),");
            }
        }
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("    public DynamicApiRegistry CreateRegistry(DynamicApiOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        var services = Entries.Where(entry => MatchesAssembly(options, entry.Assembly)).Select(entry => entry.DescriptorFactory(options)).ToArray();");
        builder.AppendLine("        return new DynamicApiRegistry(services);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, DynamicApiOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        GeneratedDynamicApiEndpoints.Map(endpoints, options);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static bool MatchesAssembly(DynamicApiOptions options, Assembly assembly)");
        builder.AppendLine("    {");
        builder.AppendLine("        return options.ServiceAssemblies.Count == 0 || options.ServiceAssemblies.Contains(assembly);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static string ResolveRoutePrefix(DynamicApiOptions options, string routeTemplate, bool hasCustomRoute)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (hasCustomRoute)");
        builder.AppendLine("        {");
        builder.AppendLine("            return routeTemplate;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return $\"{options.DefaultRoutePrefix.TrimEnd('/')}/{routeTemplate}\";");
        builder.AppendLine("    }");
        builder.AppendLine();

        for (var serviceIndex = 0; serviceIndex < context.Services.Length; serviceIndex++)
        {
            var service = context.Services[serviceIndex];
            builder.AppendLine($"    private static DynamicApiServiceDescriptor CreateService{serviceIndex}(DynamicApiOptions options)");
            builder.AppendLine("    {");
            builder.AppendLine($"        var routePrefix = ResolveRoutePrefix(options, \"{Escape(service.RouteTemplate)}\", {ToBooleanLiteral(service.HasCustomRoute)});");
            builder.AppendLine("        return new DynamicApiServiceDescriptor");
            builder.AppendLine("        {");
            builder.AppendLine($"            ServiceName = \"{Escape(service.ServiceName)}\",");
            builder.AppendLine("            RoutePrefix = routePrefix,");
            builder.AppendLine($"            ServiceType = typeof({service.ServiceTypeName}),");
            builder.AppendLine($"            ImplementationType = typeof({service.ServiceAssemblyTypeName}),");
            builder.AppendLine("            Actions = new DynamicApiActionDescriptor[]");
            builder.AppendLine("            {");
            for (var actionIndex = 0; actionIndex < service.Actions.Length; actionIndex++)
            {
                var action = service.Actions[actionIndex];

                builder.AppendLine("                new DynamicApiActionDescriptor");
                builder.AppendLine("                {");
                builder.AppendLine($"                    ActionName = \"{Escape(action.ActionName)}\",");
                builder.AppendLine($"                    DeclaringTypeName = \"{Escape(action.DeclaringTypeName)}\",");
                builder.AppendLine($"                    OperationId = \"{Escape(action.OperationId)}\",");
                builder.AppendLine($"                    RelativeRoute = \"{Escape(action.RelativeRoute)}\",");
                builder.AppendLine($"                    HttpMethod = \"{action.HttpMethod}\",");
                builder.AppendLine("                    RoutePrefix = routePrefix,");
                builder.AppendLine($"                    ReturnDescriptor = new DynamicApiReturnDescriptor {{ DeclaredType = typeof({GetTypeOfTypeName(action.ReturnModel.PayloadTypeName ?? "void")}), PayloadType = {(action.ReturnModel.PayloadTypeName is null ? "null" : $"typeof({GetTypeOfTypeName(action.ReturnModel.PayloadTypeName)})")}, IsVoid = {(action.ReturnModel.IsVoid ? "true" : "false")} }},");
                builder.AppendLine($"                    Permission = new DynamicApiPermissionMetadata {{ Permissions = new[] {{ \"{Escape(action.PermissionName)}\" }}, RequireAll = false }},");
                builder.AppendLine("                    Parameters = new DynamicApiParameterDescriptor[]");
                builder.AppendLine("                    {");
                for (var parameterIndex = 0; parameterIndex < action.Parameters.Length; parameterIndex++)
                {
                    var parameter = action.Parameters[parameterIndex];
                    builder.AppendLine("                        new DynamicApiParameterDescriptor");
                    builder.AppendLine("                        {");
                    builder.AppendLine($"                            Name = \"{Escape(parameter.Name)}\",");
                    builder.AppendLine($"                            ParameterType = typeof({GetTypeOfTypeName(parameter.TypeName)}),");
                    builder.AppendLine($"                            Source = DynamicApiParameterSource.{parameter.Source},");
                    builder.AppendLine($"                            IsOptional = {(parameter.IsOptional ? "true" : "false")}");
                    builder.AppendLine("                        },");
                }
                builder.AppendLine("                    }");
                builder.AppendLine("                },");
            }
            builder.AppendLine("            }");
            builder.AppendLine("        };");
            builder.AppendLine("    }");
            builder.AppendLine();
        }

        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("internal static class GeneratedDynamicApiRegistration");
        builder.AppendLine("{");
        builder.AppendLine("    [System.Runtime.CompilerServices.ModuleInitializer]");
        builder.AppendLine("    internal static void Register()");
        builder.AppendLine("    {");
        builder.AppendLine($"        DynamicApiGeneratedRegistryStore.Register(new {providerTypeName}());");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString().Replace("\\\"", "\"");
    }

    private static string GenerateEndpointsSource(GenerationContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Globalization;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using CrestCreates.Authorization.Abstractions;");
        builder.AppendLine("using CrestCreates.DynamicApi;");
        builder.AppendLine("using CrestCreates.Validation.Modules;");
        builder.AppendLine("using Microsoft.AspNetCore.Builder;");
        builder.AppendLine("using Microsoft.AspNetCore.Http;");
        builder.AppendLine("using Microsoft.AspNetCore.Mvc;");
        builder.AppendLine("using Microsoft.AspNetCore.Routing;");
        builder.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        builder.AppendLine();
        builder.AppendLine("namespace CrestCreates.DynamicApi.Generated;");
        builder.AppendLine();
        builder.AppendLine("internal static class GeneratedDynamicApiEndpoints");
        builder.AppendLine("{");
        builder.AppendLine("    public static void Map(IEndpointRouteBuilder endpoints, DynamicApiOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(endpoints);");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(options);");

        for (var serviceIndex = 0; serviceIndex < context.Services.Length; serviceIndex++)
        {
            var service = context.Services[serviceIndex];
            builder.AppendLine($"        var routePrefix_{serviceIndex} = ResolveRoutePrefix(options, \"{Escape(service.RouteTemplate)}\", {ToBooleanLiteral(service.HasCustomRoute)});");
            builder.AppendLine($"        if (MatchesAssembly(options, typeof({service.ServiceAssemblyTypeName}).Assembly))");
            builder.AppendLine("        {");
            for (var actionIndex = 0; actionIndex < service.Actions.Length; actionIndex++)
            {
                var action = service.Actions[actionIndex];

                var routeBuilderName = $"routeBuilder_{serviceIndex}_{actionIndex}";
                var permissionName = $"permission_{serviceIndex}_{actionIndex}";
                builder.AppendLine($"            var {permissionName} = new DynamicApiPermissionMetadata {{ Permissions = new[] {{ \"{Escape(action.PermissionName)}\" }}, RequireAll = false }};");
                builder.AppendLine($"            var {routeBuilderName} = endpoints.MapMethods(");
                builder.AppendLine($"                BuildRoute(routePrefix_{serviceIndex}, \"{Escape(action.RelativeRoute)}\"),");
                builder.AppendLine($"                new[] {{ \"{action.HttpMethod}\" }},");
                builder.AppendLine($"                async (HttpContext context, [FromServices] {action.ServiceTypeName} service, [FromServices] IValidationService? validationService, [FromServices] IPermissionChecker? permissionChecker) =>");
                builder.AppendLine("                {");
                if (!action.AllowAnonymous)
                {
                    builder.AppendLine($"                    await DynamicApiGeneratedRuntime.EnsurePermissionAsync(context, permissionChecker, {permissionName}.Permissions);");
                }
                foreach (var parameter in action.Parameters)
                {
                    builder.AppendLine(GenerateParameterBinding(parameter));
                    if (parameter.Source != ParameterSource.CancellationToken && !parameter.IsScalar)
                    {
                        builder.AppendLine($"                    await DynamicApiGeneratedRuntime.ValidateAsync(validationService, {parameter.Name});");
                    }
                }

                var callArguments = string.Join(", ", action.Parameters.Select(parameter => parameter.Source == ParameterSource.CancellationToken ? "kernelToken" : parameter.Name));
                var uowOptions = BuildUnitOfWorkOptionsLiteral(action.RequiresTransaction);
                if (action.ReturnModel.IsVoid)
                {
                    if (action.RequiresUnitOfWork)
                    {
                        builder.AppendLine($"                    await DynamicApiGeneratedRuntime.ExecuteAsync(context, {uowOptions}, async kernelToken =>");
                        builder.AppendLine("                    {");
                        builder.AppendLine($"                        await service.{action.ServiceMethodName}({callArguments});");
                        builder.AppendLine("                    });");
                    }
                    else
                    {
                        var plainArguments = string.Join(", ", action.Parameters.Select(parameter => parameter.Source == ParameterSource.CancellationToken ? "context.RequestAborted" : parameter.Name));
                        builder.AppendLine($"                    await service.{action.ServiceMethodName}({plainArguments});");
                    }
                    builder.AppendLine("                    return DynamicApiGeneratedRuntime.WrapVoidResult();");
                }
                else
                {
                    if (action.RequiresUnitOfWork)
                    {
                        builder.AppendLine($"                    var result = await DynamicApiGeneratedRuntime.ExecuteAsync(context, {uowOptions}, kernelToken => service.{action.ServiceMethodName}({callArguments}));");
                    }
                    else
                    {
                        var plainArguments = string.Join(", ", action.Parameters.Select(parameter => parameter.Source == ParameterSource.CancellationToken ? "context.RequestAborted" : parameter.Name));
                        builder.AppendLine($"                    var result = await service.{action.ServiceMethodName}({plainArguments});");
                    }
                    builder.AppendLine(action.HttpMethod == "GET"
                        ? "                    return DynamicApiGeneratedRuntime.WrapGetResult(result);"
                        : "                    return DynamicApiGeneratedRuntime.WrapResult(result);");
                }

                builder.AppendLine("                });");
                builder.AppendLine($"            {routeBuilderName}.WithDisplayName(\"{Escape(action.DeclaringTypeName)}.{Escape(action.ActionName)}\");");
                builder.AppendLine($"            {routeBuilderName}.WithMetadata({permissionName});");
                builder.AppendLine($"            {routeBuilderName}.WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute(\"{Escape(service.ServiceName)}\"));");
                builder.AppendLine();
                builder.AppendLine($"            var descriptor_{serviceIndex}_{actionIndex} = new global::CrestCreates.DynamicApi.DynamicApiEndpointDescriptor(");
                builder.AppendLine($"                \"{Escape(service.ServiceName)}\",");
                builder.AppendLine($"                \"{Escape(action.ActionName)}\",");
                builder.AppendLine($"                \"{action.HttpMethod}\",");
                builder.AppendLine($"                BuildRoute(routePrefix_{serviceIndex}, \"{Escape(action.RelativeRoute)}\"),");
                builder.AppendLine($"                typeof({action.ServiceTypeName}),");
                builder.AppendLine($"                {ResolveRequestTypeFromAction(action)},");
                builder.AppendLine($"                {(action.ReturnModel.PayloadTypeName is not null ? $"typeof({GetTypeOfTypeName(action.ReturnModel.PayloadTypeName)})" : "null")},");
                builder.AppendLine($"                new[] {{ \"{Escape(action.PermissionName)}\" }},");
                builder.AppendLine($"                {ToBooleanLiteral(action.RequiresTransaction)});");
                builder.AppendLine();
                builder.AppendLine($"            global::CrestCreates.DynamicApi.DynamicApiEndpointConventionRunner.Apply(");
                builder.AppendLine($"                endpoints.ServiceProvider,");
                builder.AppendLine($"                options,");
                builder.AppendLine($"                new global::CrestCreates.DynamicApi.DynamicApiEndpointConventionContext(");
                builder.AppendLine($"                    descriptor_{serviceIndex}_{actionIndex},");
                builder.AppendLine($"                    {routeBuilderName}));");
            }
            builder.AppendLine("        }");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static bool MatchesAssembly(DynamicApiOptions options, System.Reflection.Assembly assembly)");
        builder.AppendLine("    {");
        builder.AppendLine("        return options.ServiceAssemblies.Count == 0 || options.ServiceAssemblies.Contains(assembly);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static string ResolveRoutePrefix(DynamicApiOptions options, string routeTemplate, bool hasCustomRoute)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (hasCustomRoute)");
        builder.AppendLine("        {");
        builder.AppendLine("            return routeTemplate;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return $\"{options.DefaultRoutePrefix.TrimEnd('/')}/{routeTemplate}\";");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static string BuildRoute(string routePrefix, string relativeRoute)");
        builder.AppendLine("    {");
        builder.AppendLine("        return string.IsNullOrWhiteSpace(relativeRoute)");
        builder.AppendLine("            ? routePrefix");
        builder.AppendLine("            : $\"{routePrefix}/{relativeRoute}\";");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateParameterBinding(ParameterModel parameter)
    {
        return parameter.Source switch
        {
            ParameterSource.CancellationToken => $"                    var {parameter.Name} = context.RequestAborted;",
            ParameterSource.Route => $"                    var {parameter.Name} = {GenerateParseExpression(parameter.TypeName, $"""context.Request.RouteValues["{parameter.Name}"]?.ToString()!""", parameter.IsOptional)};",
            ParameterSource.Query when parameter.IsScalar => $"                    var {parameter.Name} = {GenerateParseExpression(parameter.TypeName, $"""context.Request.Query["{parameter.Name}"].ToString()""", parameter.IsOptional)};",
            ParameterSource.Query => GenerateQueryObjectBinding(parameter),
            ParameterSource.Body => GenerateBodyBinding(parameter),
            ParameterSource.Header => $"                    // CRUD delete concurrency token is bound from If-Match.\n                    var {parameter.Name} = context.Request.Headers[\"If-Match\"].FirstOrDefault()?.Trim('\"');",
            _ => $"                    {parameter.TypeName} {parameter.Name} = default!;"
        };
    }

    private static string GenerateQueryObjectBinding(ParameterModel parameter)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"                    var {parameter.Name} = new {parameter.TypeName}();");
        foreach (var property in parameter.QueryProperties)
        {
            builder.AppendLine($"                    if (!string.IsNullOrWhiteSpace(context.Request.Query[\"{property.Name}\"].ToString()))");
            builder.AppendLine("                    {");
            builder.AppendLine($"                        {parameter.Name}.{property.Name} = {GenerateParseExpression(property.TypeName, $"""context.Request.Query["{property.Name}"].ToString()""", property.IsOptional)};");
            builder.AppendLine("                    }");
        }

        return builder.ToString().TrimEnd();
    }

    private static string GenerateBodyBinding(ParameterModel parameter)
    {
        var builder = new StringBuilder();
        if (parameter.IsOptional)
        {
            builder.AppendLine($"                    var {parameter.Name} = await DynamicApiGeneratedRuntime.ReadBodyAsync<{parameter.TypeName}>(context, true);");
        }
        else
        {
            builder.AppendLine($"                    var {parameter.Name} = (await DynamicApiGeneratedRuntime.ReadBodyAsync<{parameter.TypeName}>(context, false))!;");
        }
        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<AttributeData> GetAttributes(ISymbol symbol, INamedTypeSymbol attributeSymbol)
    {
        return symbol.GetAttributes().Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
    }

    private static string GenerateParseExpression(string typeName, string rawExpression, bool optional)
    {
        var normalizedType = typeName.EndsWith("?", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 1) : typeName;

        if ((normalizedType == "string" || normalizedType == "global::System.String") && optional)
        {
            return $"string.IsNullOrWhiteSpace({rawExpression}) ? null : {rawExpression}";
        }

        if (normalizedType == "string" || normalizedType == "global::System.String")
        {
            return $"(!string.IsNullOrWhiteSpace({rawExpression}) ? {rawExpression} : throw new BadHttpRequestException(\"缺少参数\"))";
        }

        var parseExpression = normalizedType switch
        {
            "bool" or "global::System.Boolean" => $"bool.Parse({rawExpression})",
            "int" or "global::System.Int32" => $"int.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "long" or "global::System.Int64" => $"long.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "short" or "global::System.Int16" => $"short.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "byte" or "global::System.Byte" => $"byte.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "double" or "global::System.Double" => $"double.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "float" or "global::System.Single" => $"float.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "decimal" or "global::System.Decimal" => $"decimal.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "global::System.Guid" => $"Guid.Parse({rawExpression})",
            "global::System.DateTime" => $"DateTime.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "global::System.DateTimeOffset" => $"DateTimeOffset.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            "global::System.TimeSpan" => $"TimeSpan.Parse({rawExpression}, CultureInfo.InvariantCulture)",
            _ => $"({normalizedType})Enum.Parse(typeof({normalizedType}), {rawExpression}, true)"
        };

        return optional
            ? typeName.EndsWith("?", StringComparison.Ordinal)
                ? $"string.IsNullOrWhiteSpace({rawExpression}) ? ({normalizedType}?)null : {parseExpression}"
                : $"string.IsNullOrWhiteSpace({rawExpression}) ? default({normalizedType}) : {parseExpression}"
            : parseExpression;
    }

    private static string GetProviderTypeName(string assemblyName)
    {
        var builder = new StringBuilder("GeneratedDynamicApiProvider_");
        foreach (var character in assemblyName)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private static string GetTypeOfTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "void";
        }

        return typeName!.EndsWith("?", StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - 1)
            : typeName;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ToBooleanLiteral(bool value)
    {
        return value ? "true" : "false";
    }

    private static string BuildUnitOfWorkOptionsLiteral(bool requiresTransaction)
    {
        return requiresTransaction
            ? "new global::CrestCreates.Data.Abstractions.UnitOfWorkOptions()"
            : "new global::CrestCreates.Data.Abstractions.UnitOfWorkOptions { IsTransactional = false }";
    }

    private static string ResolveRequestTypeFromAction(ActionModel action)
    {
        foreach (var parameter in action.Parameters)
        {
            if (parameter.Source == ParameterSource.Body)
            {
                return $"typeof({GetTypeOfTypeName(parameter.TypeName)})";
            }
        }

        return "null";
    }

    private sealed record GenerationContext(
        string AssemblyName,
        ImmutableArray<ServiceModel> Services);

}
