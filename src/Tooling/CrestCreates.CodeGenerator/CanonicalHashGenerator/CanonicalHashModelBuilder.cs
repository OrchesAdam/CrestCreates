using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

/// <summary>
/// Holds extracted info about a single profile class decorated with [CanonicalHashProfile].
/// Only stores symbol references — attribute data is re-read fresh from the compilation
/// in the ModelBuilder to avoid stale data from SyntaxProvider transforms.
/// </summary>
internal sealed class ProfileClassInfo
{
    public INamedTypeSymbol Symbol { get; }
    /// <summary>Number of methods carrying [CanonicalHashField] attributes.</summary>
    public int FieldMethodCount { get; }

    public ProfileClassInfo(INamedTypeSymbol symbol, int fieldMethodCount)
    {
        Symbol = symbol;
        FieldMethodCount = fieldMethodCount;
    }
}

internal sealed class CanonicalHashModelBuilder
{
    private readonly Compilation _compilation;
    private readonly SourceProductionContext _context;

    private static readonly HashSet<string> NonDescriptorArtifactKinds = new(StringComparer.Ordinal)
        { "ReviewResult", "Package", "Report" };

    private static readonly HashSet<string> InfrastructureProperties = new(StringComparer.Ordinal)
    {
        "Namespace", "Kind", "ContractHash", "DefinitionHash", "FullId"
    };

    public CanonicalHashModelBuilder(Compilation compilation, SourceProductionContext context)
    {
        _compilation = compilation;
        _context = context;
    }

    public IReadOnlyList<ProfileModel> Build(ImmutableArray<ProfileClassInfo> profileClassInfos)
    {
        if (profileClassInfos.IsDefaultOrEmpty)
            return Array.Empty<ProfileModel>();

        // Phase 1: Build initial profile models
        var profiles = new List<ProfileModel>();
        var profileBySymbol = new Dictionary<INamedTypeSymbol, ProfileModel>(SymbolEqualityComparer.Default);

        foreach (var info in profileClassInfos)
        {
            var profile = BuildProfileModel(info);
            if (profile is not null)
            {
                profiles.Add(profile);
                profileBySymbol[info.Symbol] = profile;
            }
        }

        if (profiles.Count == 0)
            return Array.Empty<ProfileModel>();

        // Phase 2: Resolve ElementProfile/ValueProfile references
        foreach (var profile in profiles)
        {
            ResolveFieldProfileReferences(profile, profileBySymbol);
        }

        // Phase 3: Validate
        foreach (var profile in profiles)
        {
            ValidateProfile(profile);
        }

        // Sort by profile class name for deterministic output
        profiles.Sort((a, b) => string.CompareOrdinal(a.ProfileClassName, b.ProfileClassName));

        return profiles;
    }

    private ProfileModel? BuildProfileModel(ProfileClassInfo info)
    {
        var classSymbol = info.Symbol;

        // Extract [CanonicalHashProfile] attribute
        var profileAttr = classSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "CrestCreates.Metadata.Abstractions.CanonicalHashProfileAttribute");

        if (profileAttr is null) return null;

        // CCHASH014: Check for multiple field-block methods
        if (info.FieldMethodCount != 1)
        {
            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                CanonicalHashDiagnostics.MultipleFieldMethods,
                classSymbol.Locations.FirstOrDefault(),
                info.FieldMethodCount));
            return null;
        }

        // All properties are init-only named arguments (no constructor args)
        var targetType = GetNamedArgTypeValue(profileAttr, "TargetType");
        if (targetType is null)
        {
            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                CanonicalHashDiagnostics.MissingRequiredProfileProps,
                classSymbol.Locations.FirstOrDefault()));
            return null;
        }

        var artifactKind = ResolveNamedArgEnum(profileAttr, "ArtifactKind", "Descriptor");
        var descriptorKind = ResolveNamedArgEnum(profileAttr, "DescriptorKind", "Unknown");
        var contractShapeVersion = GetNamedArgStringValue(profileAttr, "ContractShapeVersion") ?? string.Empty;
        var definitionShapeVersion = GetNamedArgStringValue(profileAttr, "DefinitionShapeVersion") ?? string.Empty;

        // CCHASH007: Validate required fields
        if (string.IsNullOrEmpty(contractShapeVersion) || string.IsNullOrEmpty(definitionShapeVersion))
        {
            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                CanonicalHashDiagnostics.MissingRequiredProfileProps,
                classSymbol.Locations.FirstOrDefault()));
            return null;
        }

        // CCHASH010: Warn on reserved ArtifactKind
        if (NonDescriptorArtifactKinds.Contains(artifactKind))
        {
            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                CanonicalHashDiagnostics.ReservedArtifactKind,
                classSymbol.Locations.FirstOrDefault(),
                artifactKind));
        }

        // Re-read CanonicalHashField attributes from the Fields() method using current compilation
        var fieldAttributes = ReReadFieldAttributes(classSymbol);

        // Build fields from CanonicalHashField attributes
        var fields = BuildFieldModels(fieldAttributes, targetType, classSymbol.Name);

        return new ProfileModel
        {
            ProfileClassName = classSymbol.Name,
            ProfileClassSymbol = classSymbol,
            TargetTypeName = targetType.Name,
            ArtifactKind = artifactKind,
            DescriptorKind = descriptorKind,
            TargetType = targetType,
            ContractShapeVersion = contractShapeVersion,
            DefinitionShapeVersion = definitionShapeVersion,
            Fields = fields,
            Location = classSymbol.Locations.FirstOrDefault()
        };
    }

    private static List<AttributeData> ReReadFieldAttributes(INamedTypeSymbol classSymbol)
    {
        var fieldAttrs = new List<AttributeData>();
        const string fieldAttrFullName = "CrestCreates.Metadata.Abstractions.CanonicalHashFieldAttribute";

        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                var methodFieldAttrs = method.GetAttributes()
                    .Where(a => a.AttributeClass?.ToDisplayString() == fieldAttrFullName)
                    .ToList();

                if (methodFieldAttrs.Count > 0)
                {
                    fieldAttrs.AddRange(methodFieldAttrs);
                }
            }
        }

        return fieldAttrs;
    }

    private List<ProfileFieldModel> BuildFieldModels(
        List<AttributeData> fieldAttributes,
        INamedTypeSymbol targetType,
        string profileClassName)
    {
        var fields = new List<ProfileFieldModel>();
        var ordersSeen = new Dictionary<int, string>();

        int attrIndex = 0;
        foreach (var attr in fieldAttributes)
        {
            // Extract constructor args: propertyName (string), classification (enum)
            var ctorArgs = attr.ConstructorArguments;
            if (ctorArgs.Length < 2)
            {
                attrIndex++;
                continue;
            }

            var propertyName = ctorArgs[0].Value?.ToString();
            if (string.IsNullOrEmpty(propertyName))
            {
                attrIndex++;
                continue;
            }

            var classification = ResolveEnumValue(ctorArgs[1], "Contract");

            // Named args
            var hasExplicitOrder = attr.NamedArguments.Any(na => na.Key == "Order");
            var order = hasExplicitOrder ? GetNamedArgIntValue(attr, "Order", 0) : 10_000 + attrIndex;
            var collectionOrderMode = ResolveNamedArgEnum(attr, "CollectionOrderMode", "None");
            var orderByProperty = GetNamedArgStringValue(attr, "OrderByProperty");
            var reason = GetNamedArgStringValue(attr, "Reason");
            var customWriterType = GetNamedArgTypeValue(attr, "CustomWriter");

            // Resolve property on target type
            var propertySymbol = targetType.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            if (propertySymbol is null)
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.PropertyNotFound,
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                    propertyName, targetType.Name));
                attrIndex++;
                continue;
            }

            // CCHASH008: Duplicate order (only validate explicit orders)
            if (hasExplicitOrder)
            {
                if (ordersSeen.TryGetValue(order, out var existing))
                {
                    _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                        CanonicalHashDiagnostics.DuplicateOrder,
                        attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        order, profileClassName));
                    attrIndex++;
                    continue;
                }
                ordersSeen[order] = propertyName;
            }

            var propertyType = propertySymbol.Type;
            var isNullable = IsNullableType(propertyType);
            var isCollection = IsCollectionType(propertyType);
            var isDictionary = IsDictionaryType(propertyType);

            // CCHASH003: Collection field requires explicit CollectionOrderMode
            if (isCollection && collectionOrderMode == "None" && classification != "Excluded")
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.CollectionRequiresOrderMode,
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                    propertyName));
            }

            // CCHASH012: OrderedKeyValue only for dictionary-like fields
            if (collectionOrderMode == "OrderedKeyValue" && !isDictionary)
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.OrderedKeyValueOnlyForDictionaries,
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                    propertyName));
            }

            // CCHASH011: OrdinalByProperty requires OrderByProperty
            if (collectionOrderMode == "OrdinalByProperty" && string.IsNullOrEmpty(orderByProperty) && isCollection && classification != "Excluded")
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.OrdinalByPropertyRequiresOrderBy,
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                    propertyName));
            }

            fields.Add(new ProfileFieldModel
            {
                PropertyName = propertyName,
                Classification = classification,
                Order = order,
                CollectionOrderMode = collectionOrderMode,
                OrderByProperty = orderByProperty,
                Reason = reason,
                CustomWriterTypeName = customWriterType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                PropertyType = propertyType,
                IsNullable = isNullable,
                IsCollection = isCollection,
                IsDictionary = isDictionary,
                Location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                ElementProfile = null,
                ValueProfile = null,
            });

            attrIndex++;
        }

        return fields;
    }

    private void ResolveFieldProfileReferences(
        ProfileModel profile,
        Dictionary<INamedTypeSymbol, ProfileModel> knownProfiles)
    {
        // Get all CanonicalHashField attributes from the profile class's method(s)
        var fieldAttrsWithIndex = new List<(int index, AttributeData attr)>();

        foreach (var member in profile.ProfileClassSymbol.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                var attrs = method.GetAttributes()
                    .Where(a => a.AttributeClass?.ToDisplayString() ==
                        "CrestCreates.Metadata.Abstractions.CanonicalHashFieldAttribute")
                    .ToList();

                for (int i = 0; i < attrs.Count; i++)
                    fieldAttrsWithIndex.Add((i, attrs[i]));
            }
        }

        // For each field in the profile, resolve its ElementProfile/ValueProfile
        var updatedFields = profile.Fields.ToList();
        bool changed = false;

        for (int fi = 0; fi < updatedFields.Count && fi < fieldAttrsWithIndex.Count; fi++)
        {
            var field = updatedFields[fi];
            var (_, attr) = fieldAttrsWithIndex[fi];

            // Verify this is the right attribute
            var ctorArgs = attr.ConstructorArguments;
            if (ctorArgs.Length < 2) continue;
            var attrPropName = ctorArgs[0].Value?.ToString();
            if (!string.Equals(attrPropName, field.PropertyName, StringComparison.Ordinal)) continue;

            var elementProfileType = GetNamedArgTypeValue(attr, "ElementProfile");
            var valueProfileType = GetNamedArgTypeValue(attr, "ValueProfile");

            // Resolve ElementProfile
            if (elementProfileType is not null)
            {
                var resolved = ResolveProfileType(elementProfileType, knownProfiles);
                if (resolved is not null)
                {
                    // CCHASH013: ElementProfile target type mismatch
                    var collectionElementType = GetCollectionElementType(field.PropertyType);
                    if (collectionElementType is not null &&
                        !SymbolEqualityComparer.Default.Equals(resolved.TargetType, collectionElementType))
                    {
                        _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                            CanonicalHashDiagnostics.ElementProfileTypeMismatch,
                            field.Location,
                            resolved.TargetTypeName, collectionElementType.Name));
                    }
                    updatedFields[fi] = field with { ElementProfile = resolved };
                    changed = true;
                }
            }

            // Resolve ValueProfile
            if (valueProfileType is not null)
            {
                var resolved = ResolveProfileType(valueProfileType, knownProfiles);
                if (resolved is not null)
                {
                    updatedFields[fi] = updatedFields[fi] with { ValueProfile = resolved };
                    changed = true;
                }
            }

            // CCHASH004: Complex fields require ElementProfile, ValueProfile, or CustomWriter
            var currentField = updatedFields[fi];
            if (currentField.CustomWriterTypeName is not null)
            {
                // Custom writer handles serialization — no profile needed
                continue;
            }
            if (currentField.IsCollection && currentField.ElementProfile is null
                && !currentField.IsDictionary && currentField.Classification != "Excluded")
            {
                var elementType = GetCollectionElementType(currentField.PropertyType);
                if (elementType is not null && IsComplexType(elementType))
                {
                    _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                        CanonicalHashDiagnostics.ComplexFieldRequiresProfile,
                        currentField.Location,
                        currentField.PropertyName));
                }
            }
            else if (!currentField.IsCollection && currentField.ValueProfile is null
                && currentField.Classification != "Excluded")
            {
                if (IsComplexType(currentField.PropertyType))
                {
                    _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                        CanonicalHashDiagnostics.ComplexFieldRequiresProfile,
                        currentField.Location,
                        currentField.PropertyName));
                }
            }
        }

        if (changed)
            profile.Fields = updatedFields;
    }

    private ProfileModel? ResolveProfileType(
        INamedTypeSymbol profileClassSymbol,
        Dictionary<INamedTypeSymbol, ProfileModel> knownProfiles)
    {
        if (knownProfiles.TryGetValue(profileClassSymbol, out var known))
            return known;

        // Check if the referenced type from another assembly has [CanonicalHashProfile]
        var profileAttr = profileClassSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "CrestCreates.Metadata.Abstractions.CanonicalHashProfileAttribute");

        if (profileAttr is null) return null;

        var targetType = GetNamedArgTypeValue(profileAttr, "TargetType");
        if (targetType is null) return null;

        var artifactKind = ResolveNamedArgEnum(profileAttr, "ArtifactKind", "Descriptor");
        var descriptorKind = ResolveNamedArgEnum(profileAttr, "DescriptorKind", "Unknown");
        var contractShapeVersion = GetNamedArgStringValue(profileAttr, "ContractShapeVersion") ?? string.Empty;
        var definitionShapeVersion = GetNamedArgStringValue(profileAttr, "DefinitionShapeVersion") ?? string.Empty;

        if (string.IsNullOrEmpty(contractShapeVersion) || string.IsNullOrEmpty(definitionShapeVersion))
            return null;

        // Collect CanonicalHashField attributes
        var fieldAttrs = new List<AttributeData>();
        foreach (var member in profileClassSymbol.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                fieldAttrs.AddRange(method.GetAttributes().Where(a =>
                    a.AttributeClass?.ToDisplayString() ==
                    "CrestCreates.Metadata.Abstractions.CanonicalHashFieldAttribute"));
            }
        }

        var fields = BuildFieldModels(fieldAttrs, targetType, profileClassSymbol.Name);

        var model = new ProfileModel
        {
            ProfileClassName = profileClassSymbol.Name,
            ProfileClassSymbol = profileClassSymbol,
            TargetTypeName = targetType.Name,
            ArtifactKind = artifactKind,
            DescriptorKind = descriptorKind,
            TargetType = targetType,
            ContractShapeVersion = contractShapeVersion,
            DefinitionShapeVersion = definitionShapeVersion,
            Fields = fields,
            Location = null
        };

        knownProfiles[profileClassSymbol] = model;

        // Recursively resolve
        ResolveFieldProfileReferences(model, knownProfiles);

        return model;
    }

    private void ValidateProfile(ProfileModel profile)
    {
        // CCHASH009: TargetType does not match DescriptorKind
        if (profile.DescriptorKind != "Unknown" && profile.ArtifactKind == "Descriptor")
        {
            var expectedTypeName = profile.DescriptorKind + "Descriptor";
            if (!string.Equals(profile.TargetTypeName, expectedTypeName, StringComparison.Ordinal))
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.TargetTypeDescriptorKindMismatch,
                    profile.Location,
                    profile.TargetTypeName, profile.DescriptorKind));
            }
        }

        // CCHASH001: Unclassified public properties on TargetType
        var classifiedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in profile.Fields)
            classifiedNames.Add(f.PropertyName);

        foreach (var member in profile.TargetType.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.IsStatic || prop.IsImplicitlyDeclared) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (InfrastructureProperties.Contains(prop.Name)) continue;

            if (!classifiedNames.Contains(prop.Name))
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.UnclassifiedProperty,
                    prop.Locations.FirstOrDefault(),
                    prop.Name));
            }
        }
    }

    // ── Enum resolution helpers ──

    private static string ResolveEnumValue(TypedConstant constant, string defaultName)
    {
        if (constant.Kind == TypedConstantKind.Enum)
        {
            var enumType = constant.Type as INamedTypeSymbol;
            var enumValue = (int)constant.Value!;
            return enumType?.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(m => m is { HasConstantValue: true } && (int)m.ConstantValue! == enumValue)?
                .Name ?? defaultName;
        }
        return constant.Value?.ToString() ?? defaultName;
    }

    private string ResolveNamedArgEnum(AttributeData attr, string argName, string defaultName)
    {
        var arg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == argName);
        if (arg.Value.Kind == TypedConstantKind.Enum)
            return ResolveEnumValue(arg.Value, defaultName);
        return arg.Value.Value?.ToString() ?? defaultName;
    }

    private static string? GetNamedArgStringValue(AttributeData attr, string argName)
    {
        return attr.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == argName).Value.Value?.ToString();
    }

    private static int GetNamedArgIntValue(AttributeData attr, string argName, int defaultValue)
    {
        var arg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == argName);
        if (arg.Value.Kind == TypedConstantKind.Primitive && arg.Value.Value is int intVal)
            return intVal;
        return defaultValue;
    }

    private static INamedTypeSymbol? GetNamedArgTypeValue(AttributeData attr, string argName)
    {
        var arg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == argName);
        if (arg.Value.Kind == TypedConstantKind.Type)
            return arg.Value.Value as INamedTypeSymbol;
        return null;
    }

    // ── Type analysis helpers ──

    private static bool IsNullableType(ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return true;

        if (type is INamedTypeSymbol namedType &&
            namedType.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T)
            return true;

        return false;
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (type is IArrayTypeSymbol)
            return true;

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces)
            {
                if (iface.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IEnumerable_T)
                    return true;
            }
        }

        return false;
    }

    private static bool IsDictionaryInterface(INamedTypeSymbol typeDef)
    {
        var name = typeDef.Name;
        var ns = typeDef.ContainingNamespace?.ToDisplayString();

        // IDictionary<TKey, TValue>
        if (name == "IDictionary" && ns == "System.Collections.Generic")
            return true;

        // IReadOnlyDictionary<TKey, TValue>
        if (name == "IReadOnlyDictionary" && ns == "System.Collections.Generic")
            return true;

        return false;
    }

    private static bool IsDictionaryType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            // Check if the type itself is a dictionary interface (e.g., IReadOnlyDictionary<TKey, TValue>)
            if (IsDictionaryInterface(namedType.OriginalDefinition))
                return true;

            foreach (var iface in namedType.AllInterfaces)
            {
                if (IsDictionaryInterface(iface.OriginalDefinition))
                    return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? GetCollectionElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
            return arrayType.ElementType;

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces)
            {
                if (iface.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IEnumerable_T
                    && iface.TypeArguments.Length == 1)
                {
                    return iface.TypeArguments[0];
                }
            }
        }

        return null;
    }

    private static bool IsComplexType(ITypeSymbol type)
    {
        var underlying = type;
        if (type is INamedTypeSymbol nullableNamed
            && nullableNamed.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T
            && nullableNamed.TypeArguments.Length == 1)
        {
            underlying = nullableNamed.TypeArguments[0];
        }

        if (underlying.SpecialType == SpecialType.System_String) return false;
        if (underlying.SpecialType == SpecialType.System_Int32) return false;
        if (underlying.SpecialType == SpecialType.System_Boolean) return false;
        if (underlying.SpecialType == SpecialType.System_Int64) return false;
        if (underlying.SpecialType == SpecialType.System_Single) return false;
        if (underlying.SpecialType == SpecialType.System_Double) return false;
        if (underlying.SpecialType == SpecialType.System_DateTime) return false;
        if (underlying.ToDisplayString() == "System.TimeSpan") return false;
        if (underlying.TypeKind == TypeKind.Enum) return false;
        if (underlying.SpecialType == SpecialType.System_Object) return false;

        if (underlying is IArrayTypeSymbol) return false;
        if (IsCollectionType(underlying)) return false;
        if (IsDictionaryType(underlying)) return false;

        return underlying.TypeKind == TypeKind.Class || underlying.TypeKind == TypeKind.Struct
            || underlying.TypeKind == TypeKind.Interface;
    }
}
