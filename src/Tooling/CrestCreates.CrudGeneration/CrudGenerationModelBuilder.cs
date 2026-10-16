#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// CCCRUD001-008 as plain ids (§9). The analyzer reports them as Roslyn diagnostics, the build stage
/// as tool/MSBuild errors, from the same source, so neither side can drift into a second code table.
/// </summary>
public static class CrudDiagnosticIds
{
    public const string DeclarationUnsupported = "CCCRUD001";
    public const string ToolchainNotAssembled = "CCCRUD002";
    public const string OwnerIdentityConflict = "CCCRUD003";
    public const string StaticClosureUnsupported = "CCCRUD004";
    public const string RequiredReferenceMissing = "CCCRUD005";
    public const string PrecompileStateInvalid = "CCCRUD006";
    public const string QueryConfigurationUnsupported = "CCCRUD007";
    public const string LegacyContractMismatch = "CCCRUD008";

    /// <summary>Non-error marker used for informational records only.</summary>
    public const string Info = "CCCRUD000";
}

/// <summary>
/// Builds <see cref="CrudGenerationModel"/> instances from a real Roslyn compilation. Formal
/// declarations are recognised by the attribute's metadata full name plus symbol identity — never by
/// a short name or an EndsWith guess — and every unsupported declaration shape becomes a diagnostic
/// instead of a partially generated surface.
/// </summary>
public static class CrudGenerationModelBuilder
{
    public const string AttributeFullName =
        "CrestCreates.Domain.Shared.Attributes.GenerateCrudServiceAttribute";

    private const string EntityInterfaceFullName = "CrestCreates.Domain.Shared.Entities.IEntity`1";

    private const string ConcurrencyStampInterfaceFullName =
        "CrestCreates.Domain.Shared.Entities.Auditing.IHasConcurrencyStamp";

    /// <summary>The framework tenant marker actually shipped in DataFilter (Domain.Shared has none).</summary>
    private const string MultiTenantInterfaceFullName = "CrestCreates.DataFilter.Entities.IMultiTenant";

    /// <summary>Members that are never read from Create/Update bodies.</summary>
    private static readonly IReadOnlyList<string> CreateExcludedFields =
    [
        "Id", "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId",
        "IsDeleted", "DeletionTime", "DeleterId", "ConcurrencyStamp", "TenantId",
    ];

    /// <summary>Update keeps the concurrency stamp on the contract; the entity stamp is not overwritten.</summary>
    private static readonly IReadOnlyList<string> UpdateExcludedFields =
    [
        "Id", "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId",
        "IsDeleted", "DeletionTime", "DeleterId", "TenantId",
    ];

    private static readonly IReadOnlyList<string> ReadExcludedFields =
    [
        "IsDeleted", "DeletionTime", "DeleterId",
    ];

    private static readonly IReadOnlyList<string> NonWireMembers =
    [
        "DomainEvents",
    ];

    /// <summary>
    /// Referenced types the materialized service surface needs. Missing ones are CCCRUD005 — the
    /// producer must not emit half a chain into a contracts-only or Domain-only project. The HTTP
    /// projection is part of the formal surface, so the DynamicApi contribution contract, the
    /// capability invoker contract and the ASP.NET/DI types the activation surface binds are required
    /// exactly like the Application types.
    /// </summary>
    private static readonly IReadOnlyList<string> RequiredImplementationTypes =
    [
        "CrestCreates.Domain.Repositories.ICrestRepositoryBase`2",
        "CrestCreates.Application.Contracts.Interfaces.ICrudAppService`5",
        "CrestCreates.Authorization.Abstractions.IPermissionChecker",
        "CrestCreates.Application.Contracts.DTOs.Common.PagedRequestDto",
        "CrestCreates.Domain.Shared.ObjectMapping.GenerateObjectMappingAttribute",
        "CrestCreates.DynamicApi.CapabilityEndpointContribution",
        "CrestCreates.Capability.Abstractions.ICapabilityHandlerInvoker",
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
    ];

    public static CrudModelBuildResult Build(Compilation compilation, string assemblyName)
    {
        var models = new List<CrudGenerationModel>();
        var diagnostics = new List<CrudGenerationDiagnostic>();

        foreach (var declaration in DiscoverDeclarations(compilation))
        {
            var model = BuildOne(compilation, assemblyName, declaration, diagnostics);
            if (model is not null)
            {
                models.Add(model);
            }
        }

        foreach (var conflict in FindOwnerConflicts(models))
        {
            diagnostics.Add(conflict);
        }

        return new CrudModelBuildResult(models, diagnostics);
    }

    private static IEnumerable<INamedTypeSymbol> DiscoverDeclarations(Compilation compilation)
    {
        var attribute = compilation.GetTypeByMetadataName(AttributeFullName);
        if (attribute is null)
        {
            return Array.Empty<INamedTypeSymbol>();
        }

        return compilation.Assembly.GlobalNamespace.FlattenTypes()
            .Where(type => type.GetAttributes().Any(data =>
                SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute)))
            .ToArray();
    }

    private static CrudGenerationModel? BuildOne(
        Compilation compilation,
        string assemblyName,
        INamedTypeSymbol serviceClass,
        List<CrudGenerationDiagnostic> diagnostics)
    {
        var location = ToLocation(serviceClass.DeclaringSyntaxReferences.FirstOrDefault());

        if (serviceClass.IsGenericType || serviceClass.TypeKind != TypeKind.Class
            || !IsPartialType(serviceClass) || serviceClass.ContainingType is not null)
        {
            diagnostics.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.DeclarationUnsupported,
                $"'{serviceClass.ToDisplayString()}' must be a top-level, non-generic partial class.",
                location));
            return null;
        }

        var attributeData = serviceClass.GetAttributes()
            .First(data => SymbolEqualityComparer.Default.Equals(data.AttributeClass,
                compilation.GetTypeByMetadataName(AttributeFullName)));

        if (attributeData.ConstructorArguments.Length != 1
            || attributeData.ConstructorArguments[0].Value is not INamedTypeSymbol entityType)
        {
            diagnostics.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.DeclarationUnsupported,
                $"'{serviceClass.ToDisplayString()}' must declare the entity with typeof(Entity).",
                location));
            return null;
        }

        if (entityType.IsUnboundGenericType || entityType.TypeKind != TypeKind.Class
            || entityType.DeclaredAccessibility != Accessibility.Public)
        {
            diagnostics.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.DeclarationUnsupported,
                $"Entity '{entityType.ToDisplayString()}' must be an accessible, non-open generic type.",
                location));
            return null;
        }

        var missing = RequiredImplementationTypes
            .Where(type => compilation.GetTypeByMetadataName(type) is null)
            .ToArray();
        if (missing.Length > 0)
        {
            diagnostics.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.RequiredReferenceMissing,
                $"The formal CRUD surface for '{serviceClass.ToDisplayString()}' needs "
                + string.Join(", ", missing) + "; the target project does not reference them.",
                location));
            return null;
        }

        var idType = ResolveIdType(entityType);
        if (idType is null)
        {
            diagnostics.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.DeclarationUnsupported,
                $"Entity '{entityType.ToDisplayString()}' has no resolvable identity type "
                + "(IEntity<TId> or an Id member).",
                location));
            return null;
        }

        var idWireKind = ResolveIdWireKind(idType);
        if (idWireKind is null)
        {
            diagnostics.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.DeclarationUnsupported,
                $"Entity '{entityType.ToDisplayString()}' has identity type "
                + $"'{idType.ToDisplayString()}'; the CRUD wire route cannot parse it statically. "
                + "Supported identities: Guid, string, and the integer primitives.",
                location));
            return null;
        }

        var excludeProperties = ReadStringArray(attributeData, "ExcludeProperties");
        var allProperties = ReadProperties(entityType);

        var closureIssues = ValidateMapperClosure(entityType, allProperties, excludeProperties, location);
        if (closureIssues.Count > 0)
        {
            diagnostics.AddRange(closureIssues);
            return null;
        }

        var queryable = allProperties
            .Where(field => !ReadExcludedFields.Contains(field.Name, StringComparer.Ordinal))
            .Where(field => !excludeProperties.Contains(field.Name, StringComparer.Ordinal))
            .Select(field => field.Name)
            .ToArray();

        var filterable = ReadStringArray(attributeData, "FilterableProperties");
        var sortable = ReadStringArray(attributeData, "SortableProperties");
        foreach (var configured in filterable.Concat(sortable)
                     .Where(name => !queryable.Contains(name, StringComparer.Ordinal))
                     .Distinct(StringComparer.Ordinal))
        {
            diagnostics.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.QueryConfigurationUnsupported,
                $"'{configured}' is not a filterable/sortable field of "
                + $"'{entityType.ToDisplayString()}'.",
                location));
        }

        var entityName = entityType.Name;
        var contractsNamespace = ReadString(attributeData, "DtoNamespace")
            ?? serviceClass.ContainingNamespace.ToDisplayString() + ".Contracts";
        var routePrefix = ReadString(attributeData, "RoutePrefix")
            ?? "/api/" + ToKebab(entityName);
        var capabilityIdPrefix = ReadString(attributeData, "CapabilityIdPrefix")
            ?? assemblyName + "." + serviceClass.Name;
        var hasConcurrencyStamp = ImplementsInterface(entityType, ConcurrencyStampInterfaceFullName);

        // §8: a declared Filterable/Sortable set restricts what the typed query adapter supports;
        // absent (or empty) means every queryable field. Duplicates are collapsed here, so the emitted
        // switch can never carry two case labels for one field.
        var filterableFields = filterable.Count == 0
            ? queryable
            : filterable.Distinct(StringComparer.Ordinal).ToArray();
        var sortableFields = sortable.Count == 0
            ? queryable
            : sortable.Distinct(StringComparer.Ordinal).ToArray();

        return new CrudGenerationModel
        {
            OwnerAssemblyName = assemblyName,
            ServiceNamespace = serviceClass.ContainingNamespace.ToDisplayString(),
            ServiceClassName = serviceClass.Name,
            EntityClassName = entityName,
            EntityNamespace = entityType.ContainingNamespace.ToDisplayString(),
            EntityQualifiedDisplay = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IdTypeDisplay = idType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IdWireKind = idWireKind.Value,
            ContractsNamespace = contractsNamespace,
            RoutePrefix = routePrefix,
            CapabilityIdPrefix = capabilityIdPrefix,
            EntityHasConcurrencyStamp = hasConcurrencyStamp,
            EntityHasTenantId = ImplementsInterface(entityType, MultiTenantInterfaceFullName),
            SupportsPermissionNames = compilation.GetTypeByMetadataName(
                "CrestCreates.Core.Abstractions.Identity.PermissionName") is not null,
            ReadFields = allProperties
                .Where(field => !ReadExcludedFields.Contains(field.Name, StringComparer.Ordinal))
                .Where(field => !excludeProperties.Contains(field.Name, StringComparer.Ordinal))
                .ToArray(),
            CreateFields = allProperties
                .Where(field => !CreateExcludedFields.Contains(field.Name, StringComparer.Ordinal))
                .Where(field => !excludeProperties.Contains(field.Name, StringComparer.Ordinal))
                .ToArray(),
            UpdateFields = allProperties
                .Where(field => !UpdateExcludedFields.Contains(field.Name, StringComparer.Ordinal))
                .Where(field => !excludeProperties.Contains(field.Name, StringComparer.Ordinal))
                .ToArray(),
            QueryableFields = queryable,
            FilterableFields = filterableFields,
            SortableFields = sortableFields,
            EntityHasCreationTime = allProperties.Any(field =>
                string.Equals(field.Name, "CreationTime", StringComparison.Ordinal)),
            Actions = BuildActions(
                entityName,
                serviceClass.Name,
                contractsNamespace,
                routePrefix,
                capabilityIdPrefix,
                hasConcurrencyStamp),
            DeclarationLocation = location,
        };
    }

    /// <summary>
    /// The wire matrix of §7.4 defined once: route, method, permission, capability identity, the body
    /// payload or composed capability input envelope, and the result payload per action.
    /// </summary>
    private static IReadOnlyList<CrudActionModel> BuildActions(
        string entityName,
        string serviceClassName,
        string contractsNamespace,
        string routePrefix,
        string capabilityIdPrefix,
        bool hasConcurrencyStamp)
    {
        var readDto = contractsNamespace + "." + entityName + "Dto";
        var createDto = contractsNamespace + ".Create" + entityName + "Dto";
        var updateDto = contractsNamespace + ".Update" + entityName + "Dto";
        var listRequest = contractsNamespace + "." + entityName + "ListRequestDto";

        return
        [
            new CrudActionModel
            {
                Name = "Create",
                HttpMethod = "POST",
                RoutePattern = routePrefix,
                PermissionName = entityName + ".Create",
                CapabilityId = capabilityIdPrefix + ".Create",
                OperationKind = CrudOperationKind.Command,
                ServiceMethodName = "CreateAsync",
                BodyPayloadFullName = createDto,
                // §7.4: Create answers 200 + DynamicApiResponse<Dto>, so the created read contract is
                // the declared result payload (never the void envelope).
                ResultPayloadFullName = readDto,
            },
            new CrudActionModel
            {
                Name = "Get",
                HttpMethod = "GET",
                RoutePattern = routePrefix + "/{id}",
                PermissionName = entityName + ".Get",
                CapabilityId = capabilityIdPrefix + ".Get",
                OperationKind = CrudOperationKind.Query,
                ServiceMethodName = "GetByIdAsync",
                RouteIdentityParameter = "id",
                ResultPayloadFullName = readDto,
            },
            new CrudActionModel
            {
                Name = "Search",
                HttpMethod = "POST",
                RoutePattern = routePrefix + "/search",
                PermissionName = entityName + ".Search",
                CapabilityId = capabilityIdPrefix + ".Search",
                OperationKind = CrudOperationKind.Query,
                ServiceMethodName = "GetListAsync",
                BodyPayloadFullName = listRequest,
                ResultPayloadFullName = readDto,
                UsesPagedResult = true,
            },
            new CrudActionModel
            {
                Name = "Update",
                HttpMethod = "PUT",
                RoutePattern = routePrefix + "/{id}",
                PermissionName = entityName + ".Update",
                CapabilityId = capabilityIdPrefix + ".Update",
                OperationKind = CrudOperationKind.Command,
                ServiceMethodName = "UpdateAsync",
                BodyPayloadFullName = updateDto,
                RouteIdentityParameter = "id",
                UsesCapabilityInputEnvelope = true,
                ResultPayloadFullName = readDto,
            },
            new CrudActionModel
            {
                Name = "Delete",
                HttpMethod = "DELETE",
                RoutePattern = routePrefix + "/{id}",
                PermissionName = entityName + ".Delete",
                CapabilityId = capabilityIdPrefix + ".Delete",
                OperationKind = CrudOperationKind.Command,
                ServiceMethodName = "DeleteAsync",
                RouteIdentityParameter = "id",
                // If-Match composes a second input, so Delete always carries the envelope; the header
                // is only required when the entity keeps a concurrency stamp.
                UsesCapabilityInputEnvelope = true,
                HeaderInputs =
                [
                    new CrudHeaderInputModel(
                        MemberName: "IfMatch",
                        HeaderName: CrudWireConvention.ConcurrencyHeaderName,
                        Required: hasConcurrencyStamp,
                        TrimQuotes: true),
                ],
            },
        ];
    }

    /// <summary>
    /// §7.4: the route identity is parsed statically by the generated binder. Only identities with a
    /// bounded, culture-invariant parse surface are supported; anything else is a declaration-level
    /// rejection instead of an emitted parser that guesses.
    /// </summary>
    private static CrudIdWireKind? ResolveIdWireKind(INamedTypeSymbol idType)
    {
        // Guid is not a C# special type, so it is matched by its runtime identity.
        if (idType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid")
        {
            return CrudIdWireKind.Guid;
        }

        return idType.SpecialType switch
        {
            SpecialType.System_String => CrudIdWireKind.String,
            SpecialType.System_Int16 => CrudIdWireKind.Int16,
            SpecialType.System_Int32 => CrudIdWireKind.Int32,
            SpecialType.System_Int64 => CrudIdWireKind.Int64,
            SpecialType.System_UInt16 => CrudIdWireKind.UInt16,
            SpecialType.System_UInt32 => CrudIdWireKind.UInt32,
            SpecialType.System_UInt64 => CrudIdWireKind.UInt64,
            SpecialType.System_Byte => CrudIdWireKind.Byte,
            SpecialType.System_SByte => CrudIdWireKind.SByte,
            _ => null,
        };
    }

    private static IReadOnlyList<CrudFieldModel> ReadProperties(INamedTypeSymbol entityType)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<CrudFieldModel>();

        for (var current = entityType; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.DeclaredAccessibility != Accessibility.Public
                    || property.IsStatic
                    || !property.CanBeReferencedByName
                    || property.GetMethod is null
                    || NonWireMembers.Contains(property.Name)
                    || IsNavigationProperty(property)
                    || !seen.Add(property.Name))
                {
                    continue;
                }

                var fieldKind = ResolveFieldKind(property);
                fields.Add(new CrudFieldModel
                {
                    Name = property.Name,
                    TypeDisplay = PropertyTypeDisplay(property),
                    IsRequiredString = property.Type.SpecialType == SpecialType.System_String
                        && property.NullableAnnotation != NullableAnnotation.Annotated,
                    Kind = fieldKind.Kind,
                    IsNullable = IsNullableProperty(property),
                    EnumTypeDisplay = fieldKind.EnumTypeDisplay,
                });
            }
        }

        return fields;
    }

    /// <summary>
    /// §8: the field's static CLR kind, read from the real symbol (special type, Nullable&lt;T&gt;
    /// unwrapping, TypeKind.Enum). The display text is never parsed; a type without a bounded
    /// FilterValueReader call stays Unsupported and gets no filter case.
    /// </summary>
    private static (CrudFieldKind Kind, string? EnumTypeDisplay) ResolveFieldKind(IPropertySymbol property)
    {
        var type = property.Type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && nullable.TypeArguments.Length == 1)
        {
            type = nullable.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return (CrudFieldKind.Enum, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid")
        {
            return (CrudFieldKind.Guid, null);
        }

        return (type.SpecialType switch
        {
            SpecialType.System_String => CrudFieldKind.String,
            SpecialType.System_Boolean => CrudFieldKind.Bool,
            SpecialType.System_Int32 => CrudFieldKind.Int32,
            SpecialType.System_Int64 => CrudFieldKind.Int64,
            SpecialType.System_Decimal => CrudFieldKind.Decimal,
            SpecialType.System_Double => CrudFieldKind.Double,
            SpecialType.System_DateTime => CrudFieldKind.DateTime,
            _ => CrudFieldKind.Unsupported,
        }, null);
    }

    /// <summary>§8: true when the property's type accepts null (Nullable&lt;T&gt; or an annotated reference type).</summary>
    private static bool IsNullableProperty(IPropertySymbol property)
    {
        if (property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            return true;
        }

        return property.Type.IsReferenceType
            && property.Type.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private static string PropertyTypeDisplay(IPropertySymbol property)
    {
        var display = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (property.NullableAnnotation == NullableAnnotation.Annotated
            && !display.EndsWith("?", StringComparison.Ordinal))
        {
            display += "?";
        }

        return display;
    }

    private static IReadOnlyList<CrudGenerationDiagnostic> ValidateMapperClosure(
        INamedTypeSymbol entityType,
        IReadOnlyList<CrudFieldModel> fields,
        IReadOnlyList<string> excludeProperties,
        CrudSourceLocation location)
    {
        List<CrudGenerationDiagnostic> issues = [];

        if (!entityType.Constructors.Any(ctor =>
                ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public))
        {
            issues.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.StaticClosureUnsupported,
                $"Entity '{entityType.Name}' needs a public parameterless constructor for the "
                + "generated mapper.",
                location));
        }

        var unmappable = entityType.GetMembers().OfType<IPropertySymbol>()
            .Where(property => !CreateExcludedFields.Contains(property.Name, StringComparer.Ordinal))
            .Where(property => !excludeProperties.Contains(property.Name, StringComparer.Ordinal))
            .Where(property => !NonWireMembers.Contains(property.Name))
            .Where(property => !IsNavigationProperty(property))
            .Where(property => property.SetMethod is null
                               || property.SetMethod.DeclaredAccessibility != Accessibility.Public)
            .Select(property => property.Name)
            .ToArray();

        if (unmappable.Length > 0)
        {
            issues.Add(new CrudGenerationDiagnostic(
                CrudDiagnosticIds.StaticClosureUnsupported,
                $"Entity '{entityType.Name}' has input properties without public setters: "
                + string.Join(", ", unmappable) + ".",
                location));
        }

        return issues;
    }

    private static INamedTypeSymbol? ResolveIdType(INamedTypeSymbol entityType)
    {
        var entityInterface = entityType.AllInterfaces.FirstOrDefault(@interface =>
            @interface.OriginalDefinition.ToDisplayString() == EntityInterfaceFullName);
        if (entityInterface is not null && entityInterface.TypeArguments.Length == 1)
        {
            return entityInterface.TypeArguments[0] as INamedTypeSymbol;
        }

        var idProperty = FindProperty(entityType, "Id");
        return idProperty?.Type as INamedTypeSymbol;
    }

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string fullMetadataName) =>
        type.AllInterfaces.Any(@interface =>
            @interface.OriginalDefinition.ToDisplayString() == fullMetadataName);

    /// <summary>
    /// Partial-ness is a syntax fact, and the §5.1 contract needs it before emitting a partial
    /// implementation of the declared service.
    /// </summary>
    private static bool IsPartialType(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNavigationProperty(IPropertySymbol property)
    {
        var type = property.Type;
        if (type.TypeKind == TypeKind.Class
            && type.SpecialType == SpecialType.None
            && type.ToDisplayString() != "System.String"
            && type.ToDisplayString() != "System.DateTime"
            && type.ToDisplayString() != "System.DateTimeOffset"
            && type.ToDisplayString() != "System.Guid")
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<CrudGenerationDiagnostic> FindOwnerConflicts(
        IReadOnlyList<CrudGenerationModel> models)
    {
        foreach (var group in models.GroupBy(model =>
                     model.OwnerAssemblyName + "|" + model.ServiceFullName, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                yield return new CrudGenerationDiagnostic(
                    CrudDiagnosticIds.OwnerIdentityConflict,
                    $"Owner '{group.Key}' is declared more than once in this assembly.",
                    group.First().DeclarationLocation);
            }
        }

        foreach (var group in models.GroupBy(model => model.RoutePrefix, StringComparer.Ordinal))
        {
            if (group.Select(model => model.ServiceFullName).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                yield return new CrudGenerationDiagnostic(
                    CrudDiagnosticIds.OwnerIdentityConflict,
                    "Route prefix '" + group.Key + "' is claimed by several CRUD services.",
                    group.First().DeclarationLocation);
            }
        }

        // Artifact names are keyed by the entity, so two services over one entity in the same assembly
        // would declare the same DTO twice. That is an ownership conflict, not a duplicate to merge.
        foreach (var group in models.GroupBy(
                     model => model.OwnerAssemblyName + "|" + model.ReadDtoFullName, StringComparer.Ordinal))
        {
            if (group.Select(model => model.ServiceFullName).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                yield return new CrudGenerationDiagnostic(
                    CrudDiagnosticIds.OwnerIdentityConflict,
                    "'" + group.Key + "' is materialized by several CRUD services in one assembly; "
                    + "one entity gets one generated contract owner.",
                    group.First().DeclarationLocation);
            }
        }
    }

    private static CrudSourceLocation ToLocation(SyntaxReference? reference)
    {
        if (reference is null)
        {
            return new CrudSourceLocation("<unknown>", 0, 0);
        }

        var line = reference.SyntaxTree.GetLineSpan(reference.Span);
        return new CrudSourceLocation(
            reference.SyntaxTree.FilePath,
            line.StartLinePosition.Line + 1,
            line.StartLinePosition.Character + 1);
    }

    private static string? ReadString(AttributeData data, string name) =>
        data.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value as string;

    private static IReadOnlyList<string> ReadStringArray(AttributeData data, string name)
    {
        var value = data.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value;
        return value.Kind == TypedConstantKind.Array
            ? value.Values.Where(item => item.Value is string).Select(item => (string)item.Value!).ToArray()
            : Array.Empty<string>();
    }

    private static string ToKebab(string name)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}

file static class CrudNamespaceExtensions
{
    public static IEnumerable<INamedTypeSymbol> FlattenTypes(this INamespaceOrTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNamespace:
                    foreach (var nested in childNamespace.FlattenTypes())
                    {
                        yield return nested;
                    }

                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in type.FlattenTypes())
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }
}
