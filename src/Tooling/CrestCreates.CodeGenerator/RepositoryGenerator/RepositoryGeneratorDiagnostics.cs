using System.Linq;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.RepositoryGenerator
{
    /// <summary>
    /// Frozen #130 diagnostics for generated repository requests that are outside the verified EF
    /// execution path. These are errors, not silent empty output or an EF fallback.
    /// </summary>
    internal static class RepositoryGeneratorDiagnostics
    {
        private static readonly DiagnosticDescriptor NonEfProvider = new(
            RepositoryGeneratorDiagnosticCodes.NonEfProviderNotSupported,
            "Generated repository provider is not supported",
            "Generated repositories are only supported for EF Core. The requested provider '{0}' on '{1}' must use the verified explicit asynchronous repository entry point.",
            "CodeGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnknownProvider = new(
            RepositoryGeneratorDiagnosticCodes.UnknownProvider,
            "Unknown ORM provider",
            "The ORM provider '{0}' requested by '{1}' is unknown. Generated repositories do not fall back to EF Core.",
            "CodeGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static Diagnostic? Create(INamedTypeSymbol entityClass, string ormProvider)
        {
            var location = entityClass.Locations.FirstOrDefault() ?? Location.None;
            if (string.Equals(ormProvider, "EfCore", System.StringComparison.Ordinal))
            {
                return null;
            }

            if (string.Equals(ormProvider, "FreeSql", System.StringComparison.Ordinal) ||
                string.Equals(ormProvider, "SqlSugar", System.StringComparison.Ordinal))
            {
                return Diagnostic.Create(NonEfProvider, location, ormProvider, entityClass.Name);
            }

            return Diagnostic.Create(UnknownProvider, location, ormProvider, entityClass.Name);
        }
    }
}
