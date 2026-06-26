using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    internal static class ObjectMappingDiagnostics
    {
        private const string Category = "ObjectMapping";

        public static readonly DiagnosticDescriptor SourceTypeNotFound = new(
            id: ObjectMappingDiagnosticCodes.SourceTypeNotFoundValue,
            title: "Source type not found",
            messageFormat: "Source type '{0}' could not be found",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TargetTypeNotFound = new(
            id: ObjectMappingDiagnosticCodes.TargetTypeNotFoundValue,
            title: "Target type not found",
            messageFormat: "Target type '{0}' could not be found",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SourcePropertyNotFound = new(
            id: ObjectMappingDiagnosticCodes.SourcePropertyNotFoundValue,
            title: "Source property not found",
            messageFormat: "Source property or path '{0}' not found on type '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TargetPropertyNotMapped = new(
            id: ObjectMappingDiagnosticCodes.TargetPropertyNotMappedValue,
            title: "Target property not mapped",
            messageFormat: "Target property '{0}' on type '{1}' has no matching source",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TypeIncompatibility = new(
            id: ObjectMappingDiagnosticCodes.TypeIncompatibilityValue,
            title: "Type incompatibility",
            messageFormat: "Cannot map property '{0}': type '{1}' is not compatible with '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ReadOnlyTarget = new(
            id: ObjectMappingDiagnosticCodes.ReadOnlyTargetValue,
            title: "Read-only target",
            messageFormat: "Target property '{0}' is read-only and cannot be mapped",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AmbiguousMapping = new(
            id: ObjectMappingDiagnosticCodes.AmbiguousMappingValue,
            title: "Ambiguous mapping",
            messageFormat: "Multiple source properties match target '{0}': {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingElementMapping = new(
            id: ObjectMappingDiagnosticCodes.MissingElementMappingValue,
            title: "Missing element mapping",
            messageFormat: "Cannot map collection '{0}': no mapping exists for element type '{1}' to '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ProtectedInputFieldWriteSkipped = new(
            id: ObjectMappingDiagnosticCodes.ProtectedInputFieldWriteSkippedValue,
            title: "Protected input field write skipped",
            messageFormat: "Property '{0}' is a protected input field and will not be assigned by mapping",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NullabilityMismatch = new(
            id: ObjectMappingDiagnosticCodes.NullabilityMismatchValue,
            title: "Nullability mismatch",
            messageFormat: "Source property '{0}' is nullable but target is non-nullable",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NavigationPathInvalid = new(
            id: ObjectMappingDiagnosticCodes.NavigationPathInvalidValue,
            title: "Navigation path invalid",
            messageFormat: "Navigation path '{0}' for property '{1}' has invalid segment '{2}' on type '{3}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidConverterType = new(
            id: ObjectMappingDiagnosticCodes.InvalidConverterTypeValue,
            title: "Custom converter invalid",
            messageFormat: "Converter type '{0}' for property '{1}' is not valid. Converter must be a static class with a Convert method.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args)
        {
            return Diagnostic.Create(descriptor, location, args);
        }
    }
}
