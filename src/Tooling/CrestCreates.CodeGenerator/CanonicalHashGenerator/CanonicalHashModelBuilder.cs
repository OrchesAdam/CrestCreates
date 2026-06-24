using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

/// <summary>
/// Holds extracted info about a single profile class decorated with [CanonicalHashProfile]
/// or [CanonicalHashUnionProfile]. Only stores symbol references — attribute data is
/// re-read fresh from the compilation in the ModelBuilder to avoid stale data from
/// SyntaxProvider transforms.
/// </summary>
internal sealed class ProfileClassInfo
{
    public INamedTypeSymbol Symbol { get; }
    /// <summary>Number of methods carrying [CanonicalHashField] attributes.</summary>
    public int FieldMethodCount { get; }
    /// <summary>Whether this is a union profile (vs. normal profile).</summary>
    public bool IsUnion { get; }

    public ProfileClassInfo(INamedTypeSymbol symbol, int fieldMethodCount, bool isUnion)
    {
        Symbol = symbol;
        FieldMethodCount = fieldMethodCount;
        IsUnion = isUnion;
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

    private const string UnionProfileAttrFullName = "CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHashUnionProfileAttribute";
    private const string UnionCaseAttrFullName = "CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHashUnionCaseAttribute";
    private const string ProfileAttrFullName = "CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHashProfileAttribute";
    private const string FieldAttrFullName = "CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHashFieldAttribute";

    public CanonicalHashModelBuilder(Compilation compilation, SourceProductionContext context)
    {
        _compilation = compilation;
        _context = context;
    }

    public (IReadOnlyList<ProfileModel> Profiles, IReadOnlyList<UnionProfileModel> UnionProfiles) Build(
        ImmutableArray<ProfileClassInfo> normalProfileInfos,
        ImmutableArray<ProfileClassInfo> unionProfileInfos)
    {
        // Phase 1: Build normal profile models
        var profiles = new List<ProfileModel>();
        var profileBySymbol = new Dictionary<INamedTypeSymbol, ProfileModel>(SymbolEqualityComparer.Default);

        foreach (var info in normalProfileInfos)
        {
            var profile = BuildProfileModel(info);
            if (profile is not null)
            {
                profiles.Add(profile);
                profileBySymbol[info.Symbol] = profile;
            }
        }

        // Phase 1b: Build union profile shells (no case resolution yet)
        var unionProfiles = new List<UnionProfileModel>();
        foreach (var info in unionProfileInfos)
        {
            var unionProfile = BuildUnionProfileShell(info.Symbol);
            if (unionProfile is not null)
                unionProfiles.Add(unionProfile);
        }

        if (profiles.Count == 0 && unionProfiles.Count == 0)
            return (Array.Empty<ProfileModel>(), Array.Empty<UnionProfileModel>());

        // Build a lookup for union profiles by their profile class symbol
        var unionProfileBySymbol = new Dictionary<INamedTypeSymbol, UnionProfileModel>(SymbolEqualityComparer.Default);
        foreach (var up in unionProfiles)
        {
            unionProfileBySymbol[up.ProfileClassSymbol] = up;
        }

        // Phase 2: Resolve ElementProfile/ValueProfile references and parse filters
        foreach (var profile in profiles)
        {
            ResolveFieldProfileReferences(profile, profileBySymbol, unionProfileBySymbol);
        }

        // Phase 3: Resolve union cases to normal value profiles
        for (int i = 0; i < unionProfiles.Count; i++)
        {
            unionProfiles[i] = ResolveUnionCases(unionProfiles[i], profileBySymbol, unionProfileBySymbol);
        }

        // Phase 4: Validate normal profiles
        foreach (var profile in profiles)
        {
            ValidateProfile(profile);
        }

        // Phase 5: Validate union profiles
        foreach (var unionProfile in unionProfiles)
        {
            ValidateUnionProfile(unionProfile);
        }

        // Sort by profile class name for deterministic output
        profiles.Sort((a, b) => string.CompareOrdinal(a.ProfileClassName, b.ProfileClassName));

        return (profiles, unionProfiles);
    }

    private ProfileModel? BuildProfileModel(ProfileClassInfo info)
    {
        var classSymbol = info.Symbol;

        // Extract [CanonicalHashProfile] attribute
        var profileAttr = classSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == ProfileAttrFullName);

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

    private UnionProfileModel? BuildUnionProfileShell(INamedTypeSymbol classSymbol)
    {
        var unionAttr = classSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == UnionProfileAttrFullName);

        if (unionAttr is null) return null;

        var targetType = GetNamedArgTypeValue(unionAttr, "TargetType");
        var discriminator = GetNamedArgStringValue(unionAttr, "Discriminator") ?? string.Empty;

        // CCHASH015: TargetType missing or Discriminator empty
        if (targetType is null || string.IsNullOrEmpty(discriminator))
        {
            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                CanonicalHashDiagnostics.UnionProfileMissingRequiredProps,
                classSymbol.Locations.FirstOrDefault()));
            return null;
        }

        // Collect union case attributes (don't resolve ValueProfile yet)
        var caseAttrs = classSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == UnionCaseAttrFullName)
            .ToList();

        var cases = new List<UnionCaseModel>();
        foreach (var caseAttr in caseAttrs)
        {
            var ctorArgs = caseAttr.ConstructorArguments;
            if (ctorArgs.Length < 2) continue;

            var caseType = ctorArgs[0].Value as INamedTypeSymbol;
            var discriminatorValue = ctorArgs[1].Value?.ToString() ?? string.Empty;

            if (caseType is null) continue;

            // Location for diagnostics
            var location = caseAttr.ApplicationSyntaxReference?.GetSyntax().GetLocation();

            // Store a placeholder case — ValueProfile resolved in ResolveUnionCases
            cases.Add(new UnionCaseModel
            {
                CaseType = caseType,
                DiscriminatorValue = discriminatorValue,
                ValueProfile = null!, // will be resolved later
                Location = location
            });
        }

        return new UnionProfileModel
        {
            ProfileClassName = classSymbol.Name,
            ProfileClassSymbol = classSymbol,
            TargetType = targetType,
            TargetTypeName = targetType.Name,
            Discriminator = discriminator,
            Cases = cases,
            Location = classSymbol.Locations.FirstOrDefault()
        };
    }

    private UnionProfileModel ResolveUnionCases(
        UnionProfileModel unionProfile,
        Dictionary<INamedTypeSymbol, ProfileModel> profileBySymbol,
        Dictionary<INamedTypeSymbol, UnionProfileModel> unionProfileBySymbol)
    {
        var classSymbol = unionProfile.ProfileClassSymbol;
        var caseAttrs = classSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == UnionCaseAttrFullName)
            .ToList();

        var resolvedCases = new List<UnionCaseModel>();

        for (int i = 0; i < caseAttrs.Count && i < unionProfile.Cases.Count; i++)
        {
            var caseAttr = caseAttrs[i];
            var caseModel = unionProfile.Cases[i];

            // Resolve ValueProfile — required named argument
            var valueProfileType = GetNamedArgTypeValue(caseAttr, "ValueProfile");

            // CCHASH017: Missing ValueProfile
            if (valueProfileType is null)
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.UnionCaseMissingValueProfile,
                    caseModel.Location,
                    caseModel.CaseType.Name));
                continue;
            }

            // Resolve the profile model
            var profileModel = ResolveProfileType(valueProfileType, profileBySymbol, unionProfileBySymbol);
            if (profileModel is null) continue;

            // CCHASH016: Case type not assignable to union TargetType
            if (!IsAssignableTo(caseModel.CaseType, unionProfile.TargetType))
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.UnionCaseTypeNotAssignable,
                    caseModel.Location,
                    caseModel.CaseType.Name, unionProfile.TargetTypeName));
                continue;
            }

            // CCHASH022: ValueProfile.TargetType != CaseType
            if (!SymbolEqualityComparer.Default.Equals(profileModel.TargetType, caseModel.CaseType))
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.UnionCaseValueProfileTargetMismatch,
                    caseModel.Location,
                    profileModel.TargetTypeName, caseModel.CaseType.Name));
                continue;
            }

            // CCHASH020: Case type must be sealed
            if (!caseModel.CaseType.IsSealed)
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.UnionCaseTypeMustBeSealed,
                    caseModel.Location,
                    caseModel.CaseType.Name));
                continue;
            }

            resolvedCases.Add(caseModel with { ValueProfile = profileModel });
        }

        return unionProfile with { Cases = resolvedCases.AsReadOnly() };
    }

    private void ValidateUnionProfile(UnionProfileModel unionProfile)
    {
        var classSymbol = unionProfile.ProfileClassSymbol;
        var cases = unionProfile.Cases;

        // CCHASH018: Duplicate discriminator values
        var discriminatorSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < cases.Count; i++)
        {
            var caseModel = cases[i];
            if (!string.IsNullOrEmpty(caseModel.DiscriminatorValue))
            {
                if (discriminatorSeen.TryGetValue(caseModel.DiscriminatorValue, out var firstIndex))
                {
                    _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                        CanonicalHashDiagnostics.DuplicateUnionDiscriminator,
                        caseModel.Location,
                        caseModel.DiscriminatorValue, unionProfile.ProfileClassName));
                }
                else
                {
                    discriminatorSeen[caseModel.DiscriminatorValue] = i;
                }
            }
        }

        // CCHASH019: Duplicate case types (by symbol identity)
        var caseTypesSeen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var caseModel in cases)
        {
            if (!caseTypesSeen.Add(caseModel.CaseType))
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.DuplicateUnionCaseType,
                    caseModel.Location,
                    caseModel.CaseType.Name, unionProfile.ProfileClassName));
            }
        }

        // CCHASH021: Exhaustiveness — check for known direct sealed subtypes not declared
        if (unionProfile.Location is not null)
        {
            var targetType = unionProfile.TargetType;
            if (!targetType.IsSealed)
            {
                // For non-sealed base types (including abstract), scan compilation for sealed subtypes
                // that directly derive from TargetType and are not declared as cases.
                var declaredCaseTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (var c in cases)
                    declaredCaseTypes.Add(c.CaseType);

                var allTypes = GetAllTypesInCompilation();
                foreach (var type in allTypes)
                {
                    if (type.IsSealed && type.BaseType is not null &&
                        SymbolEqualityComparer.Default.Equals(type.BaseType, targetType))
                    {
                        if (!declaredCaseTypes.Contains(type))
                        {
                            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                                CanonicalHashDiagnostics.UnionCaseMissingKnownSubtype,
                                unionProfile.Location,
                                type.Name, targetType.Name));
                        }
                    }
                }
            }
        }
    }

    private IEnumerable<INamedTypeSymbol> GetAllTypesInCompilation()
    {
        // Walk all syntax trees and collect declared types
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var semanticModel = _compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var typeDecl in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (symbol is not null)
                    yield return symbol;
            }
        }
    }

    private static List<AttributeData> ReReadFieldAttributes(INamedTypeSymbol classSymbol)
    {
        var fieldAttrs = new List<AttributeData>();

        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                var methodFieldAttrs = method.GetAttributes()
                    .Where(a => a.AttributeClass?.ToDisplayString() == FieldAttrFullName)
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
            var filterType = GetNamedArgTypeValue(attr, "Filter");

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

            // CCHASH023: CustomWriter is unsupported — reject the field from the model
            if (customWriterType is not null)
            {
                _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                    CanonicalHashDiagnostics.CustomWriterUnsupported,
                    attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
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

            // Parse and validate Filter
            FieldFilterModel? filterModel = null;
            if (filterType is not null)
            {
                // CCHASH027: Filter not supported on dictionary fields
                if (isDictionary)
                {
                    _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                        CanonicalHashDiagnostics.FilterNotSupportedOnDictionary,
                        attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        propertyName));
                }
                // CCHASH024: Filter only for collection fields
                else if (!isCollection)
                {
                    _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                        CanonicalHashDiagnostics.FilterOnlyForCollection,
                        attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        propertyName));
                }
                else
                {
                    filterModel = ValidateFilter(filterType, propertyType,
                        attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                        propertyName);
                }
            }

            fields.Add(new ProfileFieldModel
            {
                PropertyName = propertyName,
                Classification = classification,
                Order = order,
                CollectionOrderMode = collectionOrderMode,
                OrderByProperty = orderByProperty,
                Reason = reason,
                PropertyType = propertyType,
                IsNullable = isNullable,
                IsCollection = isCollection,
                IsDictionary = isDictionary,
                Location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                ElementProfile = null,
                ValueProfile = null,
                Filter = filterModel,
            });

            attrIndex++;
        }

        return fields;
    }

    private FieldFilterModel? ValidateFilter(
        INamedTypeSymbol filterType,
        ITypeSymbol collectionType,
        Location? location,
        string propertyName)
    {
        var elementType = GetCollectionElementType(collectionType);
        if (elementType is null) return null;

        // Look for public or internal static method: bool Include(TElement value)
        var includeMethod = filterType.GetMembers("Include")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m.IsStatic &&
                m.ReturnType.SpecialType == SpecialType.System_Boolean &&
                m.Parameters.Length == 1 &&
                (m.DeclaredAccessibility == Accessibility.Public ||
                 m.DeclaredAccessibility == Accessibility.Internal));

        // CCHASH025: Invalid filter signature
        if (includeMethod is null)
        {
            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                CanonicalHashDiagnostics.InvalidFilterSignature,
                location,
                filterType.Name));
            return null;
        }

        var paramType = includeMethod.Parameters[0].Type;

        // CCHASH026: Filter element type mismatch
        if (!SymbolEqualityComparer.Default.Equals(paramType, elementType))
        {
            _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                CanonicalHashDiagnostics.FilterElementTypeMismatch,
                location,
                paramType.Name, elementType.Name));
            return null;
        }

        return new FieldFilterModel
        {
            FilterType = filterType,
            ElementType = elementType,
            FullyQualifiedTypeName = filterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };
    }

    private void ResolveFieldProfileReferences(
        ProfileModel profile,
        Dictionary<INamedTypeSymbol, ProfileModel> knownProfiles,
        Dictionary<INamedTypeSymbol, UnionProfileModel> unionProfileBySymbol)
    {
        // Get all CanonicalHashField attributes from the profile class's method(s)
        var fieldAttrsWithIndex = new List<(int index, AttributeData attr)>();

        foreach (var member in profile.ProfileClassSymbol.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                var attrs = method.GetAttributes()
                    .Where(a => a.AttributeClass?.ToDisplayString() == FieldAttrFullName)
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
                // Check if the referenced type is a union profile
                if (unionProfileBySymbol.TryGetValue(elementProfileType, out var unionElementProfile))
                {
                    // CCHASH013: Union ElementProfile target type mismatch
                    var collectionElementType = GetCollectionElementType(field.PropertyType);
                    if (collectionElementType is not null &&
                        !SymbolEqualityComparer.Default.Equals(unionElementProfile.TargetType, collectionElementType))
                    {
                        _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                            CanonicalHashDiagnostics.ElementProfileTypeMismatch,
                            field.Location,
                            unionElementProfile.TargetType.Name, collectionElementType.Name));
                    }
                    updatedFields[fi] = field with
                    {
                        ElementProfileReference = new ProfileReferenceModel
                        {
                            UnionProfile = unionElementProfile
                        }
                    };
                    changed = true;
                }
                else
                {
                    var resolved = ResolveProfileType(elementProfileType, knownProfiles, unionProfileBySymbol);
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
                        updatedFields[fi] = field with
                        {
                            ElementProfile = resolved,
                            ElementProfileReference = new ProfileReferenceModel
                            {
                                NormalProfile = resolved
                            }
                        };
                        changed = true;
                    }
                }
            }

            // Resolve ValueProfile
            if (valueProfileType is not null)
            {
                // Check if the referenced type is a union profile
                if (unionProfileBySymbol.TryGetValue(valueProfileType, out var unionValueProfile))
                {
                    // CCHASH013: Union ValueProfile target type mismatch
                    if (!SymbolEqualityComparer.Default.Equals(unionValueProfile.TargetType, field.PropertyType))
                    {
                        _context.ReportDiagnostic(CanonicalHashDiagnostics.Create(
                            CanonicalHashDiagnostics.ElementProfileTypeMismatch,
                            field.Location,
                            unionValueProfile.TargetType.Name, field.PropertyType.Name));
                    }
                    updatedFields[fi] = updatedFields[fi] with
                    {
                        ValueProfileReference = new ProfileReferenceModel
                        {
                            UnionProfile = unionValueProfile
                        }
                    };
                    changed = true;
                }
                else
                {
                    var resolved = ResolveProfileType(valueProfileType, knownProfiles, unionProfileBySymbol);
                    if (resolved is not null)
                    {
                        updatedFields[fi] = updatedFields[fi] with
                        {
                            ValueProfile = resolved,
                            ValueProfileReference = new ProfileReferenceModel
                            {
                                NormalProfile = resolved
                            }
                        };
                        changed = true;
                    }
                }
            }

            // CCHASH004: Complex fields require ElementProfile or ValueProfile
            // (CustomWriter is no longer a valid escape hatch — CCHASH023 rejects those fields from the model)
            // Union profiles are acceptable alternatives (checked via ProfileReference)
            var currentField = updatedFields[fi];
            if (currentField.IsCollection && currentField.ElementProfile is null
                && currentField.ElementProfileReference?.UnionProfile is null
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
                && currentField.ValueProfileReference?.UnionProfile is null
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
        Dictionary<INamedTypeSymbol, ProfileModel> knownProfiles,
        Dictionary<INamedTypeSymbol, UnionProfileModel> unionProfileBySymbol)
    {
        if (knownProfiles.TryGetValue(profileClassSymbol, out var known))
            return known;

        // Check if the referenced type from another assembly has [CanonicalHashProfile]
        var profileAttr = profileClassSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == ProfileAttrFullName);

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
                    a.AttributeClass?.ToDisplayString() == FieldAttrFullName));
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
        ResolveFieldProfileReferences(model, knownProfiles, unionProfileBySymbol);

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

    // ── Assignability helper ──

    private static bool IsAssignableTo(ITypeSymbol derived, ITypeSymbol baseType)
    {
        // Walk the base type chain
        var current = derived;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;

            if (current is INamedTypeSymbol namedType)
                current = namedType.BaseType;
            else
                break;
        }

        return false;
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

        // Supported scalar types — these have deterministic Utf8JsonWriter write methods
        if (underlying.SpecialType == SpecialType.System_String) return false;
        if (underlying.SpecialType == SpecialType.System_Int32) return false;
        if (underlying.SpecialType == SpecialType.System_Boolean) return false;
        if (underlying.SpecialType == SpecialType.System_Int64) return false;
        if (underlying.SpecialType == SpecialType.System_Single) return false;
        if (underlying.SpecialType == SpecialType.System_Double) return false;
        if (underlying.SpecialType == SpecialType.System_Decimal) return false;
        if (underlying.SpecialType == SpecialType.System_DateTime) return false;
        if (underlying.TypeKind == TypeKind.Enum) return false;

        // TimeSpan is a supported scalar — written as deterministic "c" format string
        if (underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.TimeSpan") return false;

        if (underlying is IArrayTypeSymbol) return false;
        if (IsCollectionType(underlying)) return false;
        if (IsDictionaryType(underlying)) return false;

        return underlying.TypeKind == TypeKind.Class || underlying.TypeKind == TypeKind.Struct
            || underlying.TypeKind == TypeKind.Interface;
    }
}
