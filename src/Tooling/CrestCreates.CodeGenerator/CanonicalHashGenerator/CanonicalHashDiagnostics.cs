using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

internal static class CanonicalHashDiagnostics
{
    private const string Category = "CanonicalHash";

    public static readonly DiagnosticDescriptor UnclassifiedProperty = new(
        id: "CCHASH001",
        title: "Descriptor public property is not classified",
        messageFormat: "Descriptor public property '{0}' is not classified by any CanonicalHashProfile. Add a [CanonicalHashField] declaration or set CanonicalHashStrictProfileValidation MSBuild property to configure severity.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyNotFound = new(
        id: "CCHASH002",
        title: "Property name not found on target type",
        messageFormat: "CanonicalHashField references property '{0}' that does not exist on '{1}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CollectionRequiresOrderMode = new(
        id: "CCHASH003",
        title: "Collection field requires explicit CollectionOrderMode",
        messageFormat: "Collection field '{0}' requires explicit CollectionOrderMode.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ComplexFieldRequiresProfile = new(
        id: "CCHASH004",
        title: "Nested complex field requires ElementProfile or ValueProfile",
        messageFormat: "Nested complex field '{0}' requires ElementProfile or ValueProfile.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // CCHASH005 and CCHASH006 removed: Contract/DefinitionOnly/Excluded classification
    // is correctly enforced by SG construction — Excluded fields are never included in
    // payloads, and DefinitionOnly fields are never included in Contract payloads.
    // These invariants are guaranteed by the field-filtering logic in
    // CanonicalHashProjectionWriter and CanonicalHashWriterWriter, not by
    // diagnostic checks that would never fire in a correct implementation.

    public static readonly DiagnosticDescriptor MissingRequiredProfileProps = new(
        id: "CCHASH007",
        title: "TargetType and shape versions required",
        messageFormat: "CanonicalHashProfile TargetType and ContractShapeVersion/DefinitionShapeVersion are required.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateOrder = new(
        id: "CCHASH008",
        title: "Duplicate hash field order",
        messageFormat: "Duplicate hash field order {0} in profile '{1}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TargetTypeDescriptorKindMismatch = new(
        id: "CCHASH009",
        title: "TargetType does not match DescriptorKind",
        messageFormat: "Profile TargetType '{0}' does not match DescriptorKind '{1}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReservedArtifactKind = new(
        id: "CCHASH010",
        title: "Reserved ArtifactKind",
        messageFormat: "ArtifactKind '{0}' is reserved but not supported by SG v1.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrdinalByPropertyRequiresOrderBy = new(
        id: "CCHASH011",
        title: "OrdinalByProperty requires OrderByProperty",
        messageFormat: "CollectionOrderMode.OrdinalByProperty on field '{0}' requires OrderByProperty.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrderedKeyValueOnlyForDictionaries = new(
        id: "CCHASH012",
        title: "OrderedKeyValue only for dictionary-like fields",
        messageFormat: "CollectionOrderMode.OrderedKeyValue can only be used on dictionary-like fields. Field '{0}' is not a dictionary.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ElementProfileTypeMismatch = new(
        id: "CCHASH013",
        title: "ElementProfile target type mismatch",
        messageFormat: "ElementProfile target type '{0}' does not match collection element type '{1}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleFieldMethods = new(
        id: "CCHASH014",
        title: "Multiple field-block methods",
        messageFormat: "Profile class must contain exactly one method carrying CanonicalHashField attributes. Found {0} methods.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args)
    {
        return Diagnostic.Create(descriptor, location, args);
    }
}
