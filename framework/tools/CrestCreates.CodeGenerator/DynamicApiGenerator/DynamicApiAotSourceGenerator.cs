using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.DynamicApiGenerator;

[Generator]
public sealed class DynamicApiAotSourceGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generationContextProvider = context.CompilationProvider.Select(static (compilation, _) => BuildGenerationContext(compilation));
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

    private static GenerationContext? BuildGenerationContext(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") is null ||
            compilation.GetTypeByMetadataName("CrestCreates.DynamicApi.IDynamicApiGeneratedProvider") is null)
        {
            return null;
        }

        var crestServiceAttribute = compilation.GetTypeByMetadataName("CrestCreates.Domain.Shared.Attributes.CrestServiceAttribute");
        if (crestServiceAttribute is null)
        {
            return null;
        }

        var dynamicApiIgnoreAttribute = compilation.GetTypeByMetadataName("CrestCreates.DynamicApi.DynamicApiIgnoreAttribute");
        var dynamicApiRouteAttribute = compilation.GetTypeByMetadataName("CrestCreates.DynamicApi.DynamicApiRouteAttribute");
        var unitOfWorkAttribute = compilation.GetTypeByMetadataName("CrestCreates.Aop.Interceptors.UnitOfWorkMoAttribute");
        var services = new List<ServiceModel>();

        foreach (var type in EnumerateNamedTypes(compilation.Assembly).Concat(compilation.SourceModule.ReferencedAssemblySymbols.SelectMany(EnumerateNamedTypes)))
        {
            if (!IsDynamicApiImplementation(type, crestServiceAttribute, dynamicApiIgnoreAttribute))
            {
                continue;
            }

            services.AddRange(BuildServiceModels(type, dynamicApiIgnoreAttribute, dynamicApiRouteAttribute, unitOfWorkAttribute));
        }

        return new GenerationContext(
            compilation.AssemblyName ?? "DynamicApiHost",
            services.OrderBy(service => service.RoutePrefix, StringComparer.Ordinal).ToImmutableArray());
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
        INamedTypeSymbol? dynamicApiIgnoreAttribute)
    {
        if (typeSymbol.TypeKind != TypeKind.Class ||
            typeSymbol.IsAbstract ||
            typeSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (!HasAttribute(typeSymbol, crestServiceAttribute))
        {
            return false;
        }

        return dynamicApiIgnoreAttribute is null || !HasAttribute(typeSymbol, dynamicApiIgnoreAttribute);
    }

    private static IEnumerable<ServiceModel> BuildServiceModels(
        INamedTypeSymbol implementationType,
        INamedTypeSymbol? dynamicApiIgnoreAttribute,
        INamedTypeSymbol? dynamicApiRouteAttribute,
        INamedTypeSymbol? unitOfWorkAttribute)
    {
        var serviceInterfaces = implementationType.Interfaces
            .Where(interfaceSymbol => interfaceSymbol.DeclaredAccessibility == Accessibility.Public)
            .Where(interfaceSymbol => !interfaceSymbol.IsGenericType)
            .Where(interfaceSymbol => interfaceSymbol.Name.EndsWith("AppService", StringComparison.Ordinal))
            .Where(interfaceSymbol => dynamicApiIgnoreAttribute is null || !HasAttribute(interfaceSymbol, dynamicApiIgnoreAttribute))
            .ToArray();

        foreach (var serviceType in serviceInterfaces)
        {
            var serviceName = TrimServiceName(serviceType.Name);
            var routePrefix = ResolveServiceRoute(serviceType, serviceName, dynamicApiRouteAttribute);
            var actions = BuildActionModels(serviceType, implementationType, serviceName, routePrefix, dynamicApiIgnoreAttribute, unitOfWorkAttribute).ToImmutableArray();
            if (actions.Length == 0)
            {
                continue;
            }

            yield return new ServiceModel(
                serviceName,
                routePrefix,
                serviceType.ToDisplayString(FullyQualifiedFormat),
                implementationType.ToDisplayString(FullyQualifiedFormat),
                actions);
        }
    }

    private static IEnumerable<ActionModel> BuildActionModels(
        INamedTypeSymbol serviceType,
        INamedTypeSymbol implementationType,
        string serviceName,
        string routePrefix,
        INamedTypeSymbol? dynamicApiIgnoreAttribute,
        INamedTypeSymbol? unitOfWorkAttribute)
    {
        var seenMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contractType in EnumerateContractTypes(serviceType))
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

                if (!seenMethodKeys.Add(CreateMethodKey(method)))
                {
                    continue;
                }

                var actionName = TrimAsyncSuffix(method.Name);
                var httpMethod = ResolveHttpMethod(method.Name);
                var relativeRoute = ResolveActionRoute(method);
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
                    string.IsNullOrWhiteSpace(relativeRoute) ? routePrefix : $"{routePrefix}/{relativeRoute}",
                    ResolvePermission(serviceName, method.Name),
                    returnModel,
                    parameters,
                    method.Name,
                    serviceType.ToDisplayString(FullyQualifiedFormat),
                    unitOfWork.RequiresUnitOfWork,
                    unitOfWork.RequiresTransaction);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateContractTypes(INamedTypeSymbol serviceType)
    {
        yield return serviceType;

        foreach (var inheritedInterface in serviceType.AllInterfaces.Where(interfaceSymbol => interfaceSymbol.DeclaredAccessibility == Accessibility.Public))
        {
            yield return inheritedInterface;
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
            var source = ResolveParameterSource(parameter, routeTokens, httpMethod, ref bodyAssigned);
            parameters.Add(new ParameterModel(
                parameter.Name,
                parameter.Type.ToDisplayString(FullyQualifiedFormat),
                source,
                parameter.IsOptional || parameter.HasExplicitDefaultValue,
                IsScalar(parameter.Type),
                source == ParameterSource.Query && !IsScalar(parameter.Type)
                    ? BuildQueryProperties(parameter.Type)
                    : ImmutableArray<QueryPropertyModel>.Empty));
        }

        return parameters.ToImmutable();
    }

    private static ImmutableArray<QueryPropertyModel> BuildQueryProperties(ITypeSymbol parameterType)
    {
        if (parameterType is not INamedTypeSymbol namedType)
        {
            return ImmutableArray<QueryPropertyModel>.Empty;
        }

        return namedType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property.DeclaredAccessibility == Accessibility.Public)
            .Where(property => !property.IsStatic && !property.IsReadOnly)
            .Select(property => new QueryPropertyModel(
                property.Name,
                property.Type.ToDisplayString(FullyQualifiedFormat),
                IsScalar(property.Type),
                property.NullableAnnotation == NullableAnnotation.Annotated))
            .Where(property => property.IsScalar)
            .ToImmutableArray();
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

    private static AttributeData? GetAttribute(ISymbol? symbol, INamedTypeSymbol attributeSymbol)
    {
        if (symbol is null)
        {
            return null;
        }

        return symbol.GetAttributes().FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
    }


    private static string ResolveServiceRoute(INamedTypeSymbol serviceType, string serviceName, INamedTypeSymbol? dynamicApiRouteAttribute)
    {
        if (dynamicApiRouteAttribute is not null)
        {
            var routeAttribute = serviceType.GetAttributes()
                .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, dynamicApiRouteAttribute));
            if (routeAttribute is not null &&
                routeAttribute.ConstructorArguments.Length == 1 &&
                routeAttribute.ConstructorArguments[0].Value is string template &&
                !string.IsNullOrWhiteSpace(template))
            {
                return template.Trim('/');
            }
        }

        return $"api/{ToKebabCase(serviceName)}";
    }

    private static string ResolveActionRoute(IMethodSymbol methodSymbol)
    {
        var methodName = TrimAsyncSuffix(methodSymbol.Name);
        var parameters = methodSymbol.Parameters
            .Where(parameter => parameter.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .ToArray();

        return methodName switch
        {
            "Create" => string.Empty,
            "GetById" => "{id}",
            "Get" when parameters.Length == 1 && IsScalar(parameters[0].Type) => $"{{{parameters[0].Name}}}",
            "GetList" => string.Empty,
            "Update" => "{id}",
            "Delete" => "{id}",
            "GetAll" => "all",
            "Count" => "count",
            "Query" => "query",
            _ when methodName.StartsWith("GetBy", StringComparison.Ordinal) && parameters.Length == 1
                => $"by-{ToKebabCase(methodName.Substring("GetBy".Length))}/{{{parameters[0].Name}}}",
            _ when methodName.StartsWith("Get", StringComparison.Ordinal) && parameters.Length == 0
                => ToKebabCase(methodName.Substring("Get".Length)),
            _ when methodName.StartsWith("Get", StringComparison.Ordinal) && parameters.Length == 1 && IsScalar(parameters[0].Type)
                => $"{ToKebabCase(methodName.Substring("Get".Length))}/{{{parameters[0].Name}}}",
            _ when methodName.StartsWith("Exists", StringComparison.Ordinal) && parameters.Length == 1
                => $"{ToKebabCase(methodName)}/{{{parameters[0].Name}}}",
            _ => ToKebabCase(methodName)
        };
    }

    private static string ResolveHttpMethod(string methodName)
    {
        var normalized = TrimAsyncSuffix(methodName);
        if (normalized == "Create" || normalized == "Add" || normalized == "Insert" || normalized.StartsWith("Create", StringComparison.Ordinal))
        {
            return "POST";
        }

        if (normalized == "Update" || normalized == "Put" || normalized.StartsWith("Update", StringComparison.Ordinal))
        {
            return "PUT";
        }

        if (normalized == "Delete" || normalized == "Remove" || normalized.StartsWith("Delete", StringComparison.Ordinal))
        {
            return "DELETE";
        }

        if (normalized.StartsWith("Process", StringComparison.Ordinal) ||
            normalized.StartsWith("Return", StringComparison.Ordinal) ||
            normalized.StartsWith("Extend", StringComparison.Ordinal) ||
            normalized == "Query" ||
            normalized == "Search")
        {
            return "POST";
        }

        return "GET";
    }

    private static ParameterSource ResolveParameterSource(
        IParameterSymbol parameter,
        ISet<string> routeTokens,
        string httpMethod,
        ref bool bodyAssigned)
    {
        if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
        {
            return ParameterSource.CancellationToken;
        }

        if (routeTokens.Contains(parameter.Name))
        {
            return ParameterSource.Route;
        }

        if (!bodyAssigned &&
            (httpMethod == "POST" || httpMethod == "PUT" || httpMethod == "PATCH") &&
            !IsScalar(parameter.Type))
        {
            bodyAssigned = true;
            return ParameterSource.Body;
        }

        return ParameterSource.Query;
    }

    private static string ResolvePermission(string serviceName, string methodName)
    {
        var normalized = TrimAsyncSuffix(methodName);
        if (normalized == "Create")
        {
            return $"{serviceName}.Create";
        }

        if (normalized == "Update")
        {
            return $"{serviceName}.Update";
        }

        if (normalized == "Delete")
        {
            return $"{serviceName}.Delete";
        }

        if (normalized == "GetById" || normalized == "Get" || normalized.StartsWith("GetBy", StringComparison.Ordinal))
        {
            return $"{serviceName}.Get";
        }

        return $"{serviceName}.Search";
    }

    private static bool IsScalar(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            return IsScalar(namedType.TypeArguments[0]);
        }

        return typeSymbol.SpecialType switch
        {
            SpecialType.System_String => true,
            SpecialType.System_Boolean => true,
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Decimal => true,
            _ => typeSymbol.TypeKind == TypeKind.Enum ||
                 typeSymbol.ToDisplayString() == "System.Guid" ||
                 typeSymbol.ToDisplayString() == "System.DateTime" ||
                 typeSymbol.ToDisplayString() == "System.DateTimeOffset" ||
                 typeSymbol.ToDisplayString() == "System.TimeSpan"
        };
    }

    private static string CreateMethodKey(IMethodSymbol methodSymbol)
    {
        return $"{methodSymbol.Name}({string.Join(",", methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(FullyQualifiedFormat)))})";
    }

    private static string TrimServiceName(string serviceTypeName)
    {
        var name = serviceTypeName;
        if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1)
        {
            name = name.Substring(1);
        }

        if (name.EndsWith("AppService", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "AppService".Length);
        }

        return name;
    }

    private static string TrimAsyncSuffix(string methodName)
    {
        return methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Replace("\\\"", "\"");
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
        builder.AppendLine("    private static readonly (Assembly Assembly, DynamicApiServiceDescriptor Descriptor)[] Entries = new[]");
        builder.AppendLine("    {");
        for (var index = 0; index < context.Services.Length; index++)
        {
            var service = context.Services[index];
            builder.AppendLine($"        (typeof({service.ServiceAssemblyTypeName}).Assembly, CreateService{index}()),");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public System.Collections.Generic.IReadOnlyCollection<Assembly> ServiceAssemblies => Entries.Select(entry => entry.Assembly).Distinct().ToArray();");
        builder.AppendLine();
        builder.AppendLine("    public DynamicApiRegistry CreateRegistry(DynamicApiOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        var services = Entries.Where(entry => MatchesAssembly(options, entry.Assembly)).Select(entry => entry.Descriptor).ToArray();");
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

        for (var serviceIndex = 0; serviceIndex < context.Services.Length; serviceIndex++)
        {
            var service = context.Services[serviceIndex];
            builder.AppendLine($"    private static DynamicApiServiceDescriptor CreateService{serviceIndex}()");
            builder.AppendLine("    {");
            builder.AppendLine("        return new DynamicApiServiceDescriptor");
            builder.AppendLine("        {");
            builder.AppendLine($"            ServiceName = \"{Escape(service.ServiceName)}\",");
            builder.AppendLine($"            RoutePrefix = \"{Escape(service.RoutePrefix)}\",");
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
                builder.AppendLine($"                    RoutePrefix = \"{Escape(service.RoutePrefix)}\",");
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
            builder.AppendLine($"        if (MatchesAssembly(options, typeof({service.ServiceAssemblyTypeName}).Assembly))");
            builder.AppendLine("        {");
            for (var actionIndex = 0; actionIndex < service.Actions.Length; actionIndex++)
            {
                var action = service.Actions[actionIndex];
                var routeBuilderName = $"routeBuilder_{serviceIndex}_{actionIndex}";
                var permissionName = $"permission_{serviceIndex}_{actionIndex}";
                builder.AppendLine($"            var {permissionName} = new DynamicApiPermissionMetadata {{ Permissions = new[] {{ \"{Escape(action.PermissionName)}\" }}, RequireAll = false }};");
                builder.AppendLine($"            var {routeBuilderName} = endpoints.MapMethods(");
                builder.AppendLine($"                \"{Escape(action.FullRoute)}\",");
                builder.AppendLine($"                new[] {{ \"{action.HttpMethod}\" }},");
                builder.AppendLine($"                async (HttpContext context, [FromServices] {action.ServiceTypeName} service, [FromServices] IValidationService? validationService, [FromServices] IPermissionChecker? permissionChecker) =>");
                builder.AppendLine("                {");
                builder.AppendLine($"                    await DynamicApiGeneratedRuntime.EnsurePermissionAsync(context, permissionChecker, {permissionName}.Permissions);");
                foreach (var parameter in action.Parameters)
                {
                    builder.AppendLine(GenerateParameterBinding(parameter));
                    if (parameter.Source != ParameterSource.CancellationToken)
                    {
                        builder.AppendLine($"                    await DynamicApiGeneratedRuntime.ValidateAsync(validationService, {parameter.Name});");
                    }
                }

                var callArguments = string.Join(", ", action.Parameters.Select(parameter => parameter.Source == ParameterSource.CancellationToken ? "context.RequestAborted" : parameter.Name));
                if (action.ReturnModel.IsVoid)
                {
                    if (action.RequiresUnitOfWork)
                    {
                        builder.AppendLine($"                    await DynamicApiGeneratedRuntime.ExecuteAsync(context, {ToBooleanLiteral(action.RequiresTransaction)}, async () =>");
                        builder.AppendLine("                    {");
                        builder.AppendLine($"                        await service.{action.ServiceMethodName}({callArguments});");
                        builder.AppendLine("                    });");
                    }
                    else
                    {
                        builder.AppendLine($"                    await service.{action.ServiceMethodName}({callArguments});");
                    }
                    builder.AppendLine("                    return DynamicApiGeneratedRuntime.WrapVoidResult();");
                }
                else
                {
                    if (action.RequiresUnitOfWork)
                    {
                        builder.AppendLine($"                    var result = await DynamicApiGeneratedRuntime.ExecuteAsync(context, {ToBooleanLiteral(action.RequiresTransaction)}, () => service.{action.ServiceMethodName}({callArguments}));");
                    }
                    else
                    {
                        builder.AppendLine($"                    var result = await service.{action.ServiceMethodName}({callArguments});");
                    }
                    builder.AppendLine(action.HttpMethod == "GET"
                        ? "                    return DynamicApiGeneratedRuntime.WrapGetResult(result);"
                        : "                    return DynamicApiGeneratedRuntime.WrapResult(result);");
                }

                builder.AppendLine("                });");
                builder.AppendLine($"            {routeBuilderName}.WithDisplayName(\"{Escape(action.DeclaringTypeName)}.{Escape(action.ActionName)}\");");
                builder.AppendLine($"            {routeBuilderName}.WithMetadata({permissionName});");
                builder.AppendLine($"            {routeBuilderName}.ExcludeFromDescription();");
            }
            builder.AppendLine("        }");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static bool MatchesAssembly(DynamicApiOptions options, System.Reflection.Assembly assembly)");
        builder.AppendLine("    {");
        builder.AppendLine("        return options.ServiceAssemblies.Count == 0 || options.ServiceAssemblies.Contains(assembly);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateParameterBinding(ParameterModel parameter)
    {
        return parameter.Source switch
        {
            ParameterSource.CancellationToken => $"                    var {parameter.Name} = context.RequestAborted;",
            ParameterSource.Route => $"                    var {parameter.Name} = {GenerateParseExpression(parameter.TypeName, $"""context.Request.RouteValues["{parameter.Name}"]?.ToString()""", parameter.IsOptional)};",
            ParameterSource.Query when parameter.IsScalar => $"                    var {parameter.Name} = {GenerateParseExpression(parameter.TypeName, $"""context.Request.Query["{parameter.Name}"].ToString()""", parameter.IsOptional)};",
            ParameterSource.Query => GenerateQueryObjectBinding(parameter),
            ParameterSource.Body => GenerateBodyBinding(parameter),
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
        builder.AppendLine($"                    {parameter.TypeName}? {parameter.Name}Body = await context.Request.ReadFromJsonAsync<{parameter.TypeName}>(DynamicApiGeneratedRuntime.ResolveJsonSerializerOptions(context.RequestServices), context.RequestAborted);");
        builder.AppendLine(parameter.IsOptional
            ? $"                    var {parameter.Name} = {parameter.Name}Body;"
            : $"                    var {parameter.Name} = {parameter.Name}Body ?? new {parameter.TypeName}();");
        return builder.ToString().TrimEnd();
    }

    private static string GenerateParseExpression(string typeName, string rawExpression, bool optional)
    {
        var normalizedType = typeName.EndsWith("?", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 1) : typeName;
        return normalizedType switch
        {
            "string" or "global::System.String" => optional
                ? $"string.IsNullOrWhiteSpace({rawExpression}) ? null : {rawExpression}"
                : $"(!string.IsNullOrWhiteSpace({rawExpression}) ? {rawExpression} : throw new BadHttpRequestException(\"缺少参数\"))",
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
            _ when typeName.EndsWith("?", StringComparison.Ordinal) && normalizedType != typeName => $"string.IsNullOrWhiteSpace({rawExpression}) ? default({typeName}) : {GenerateParseExpression(normalizedType, rawExpression, false)}",
            _ => $"({typeName})Enum.Parse(typeof({typeName}), {rawExpression}, true)"
        };
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

        return typeName.EndsWith("?", StringComparison.Ordinal)
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

    private sealed record GenerationContext(string AssemblyName, ImmutableArray<ServiceModel> Services);

    private sealed record ServiceModel(
        string ServiceName,
        string RoutePrefix,
        string ServiceTypeName,
        string ServiceAssemblyTypeName,
        ImmutableArray<ActionModel> Actions);

    private sealed record ActionModel(
        string ActionName,
        string DeclaringTypeName,
        string OperationId,
        string RelativeRoute,
        string HttpMethod,
        string FullRoute,
        string PermissionName,
        ReturnModel ReturnModel,
        ImmutableArray<ParameterModel> Parameters,
        string ServiceMethodName,
        string ServiceTypeName,
        bool RequiresUnitOfWork,
        bool RequiresTransaction);

    private sealed record ParameterModel(
        string Name,
        string TypeName,
        ParameterSource Source,
        bool IsOptional,
        bool IsScalar,
        ImmutableArray<QueryPropertyModel> QueryProperties);

    private sealed record QueryPropertyModel(
        string Name,
        string TypeName,
        bool IsScalar,
        bool IsOptional);

    private sealed record ReturnModel(bool IsVoid, string? PayloadTypeName);

    private enum ParameterSource
    {
        Route,
        Query,
        Body,
        CancellationToken
    }
}
