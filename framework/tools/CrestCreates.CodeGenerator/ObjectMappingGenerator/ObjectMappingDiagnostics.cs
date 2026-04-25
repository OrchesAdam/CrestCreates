using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    internal static class ObjectMappingDiagnostics
    {
        private const string Category = "ObjectMapping";

        public static readonly DiagnosticDescriptor SourceTypeNotFound = new(
            id: "OM001",
            title: "Source type not found",
            messageFormat: "Source type '{0}' could not be found",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TargetTypeNotFound = new(
            id: "OM002",
            title: "Target type not found",
            messageFormat: "Target type '{0}' could not be found",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SourcePropertyNotFound = new(
            id: "OM003",
            title: "Source property not found",
            messageFormat: "Source property '{0}' not found on type '{1}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TargetPropertyNotMapped = new(
            id: "OM004",
            title: "Target property not mapped",
            messageFormat: "Target property '{0}' on type '{1}' has no matching source",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TypeIncompatibility = new(
            id: "OM005",
            title: "Type incompatibility",
            messageFormat: "Cannot map property '{0}': type '{1}' is not compatible with '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ReadOnlyTarget = new(
            id: "OM006",
            title: "Read-only target",
            messageFormat: "Target property '{0}' is read-only and cannot be mapped in Apply direction",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AmbiguousMapping = new(
            id: "OM007",
            title: "Ambiguous mapping",
            messageFormat: "Multiple source properties match target '{0}': {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingElementMapping = new(
            id: "OM008",
            title: "Missing element mapping",
            messageFormat: "Cannot map collection '{0}': no mapping exists for element type '{1}' to '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NullabilityMismatch = new(
            id: "OM009",
            title: "Nullability mismatch",
            messageFormat: "Source property '{0}' is nullable but target is non-nullable. Using default value fallback.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args)
        {
            return Diagnostic.Create(descriptor, location, args);
        }
    }
}
