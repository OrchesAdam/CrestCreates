using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.ObjectMappingGenerator
{
    /// <summary>
    /// Represents a mapping declaration from source type to target type.
    /// </summary>
    internal sealed class MappingDeclaration
    {
        public INamedTypeSymbol SourceType { get; set; } = null!;
        public INamedTypeSymbol TargetType { get; set; } = null!;
        public string MapperClassName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public MapDirection Direction { get; set; } = MapDirection.Both;
        public Location? Location { get; set; }
    }

    /// <summary>
    /// Represents a resolved property mapping.
    /// </summary>
    internal sealed class PropertyMapping
    {
        public IPropertySymbol SourceProperty { get; set; } = null!;
        public IPropertySymbol TargetProperty { get; set; } = null!;
        public bool IsIgnored { get; set; }
        public bool IsReadOnly { get; set; }
        public string? CustomSourceName { get; set; }
        public bool NeedsNullCheck { get; set; }
    }

    /// <summary>
    /// Represents the complete mapping model for code generation.
    /// </summary>
    internal sealed class ObjectMappingModel
    {
        public MappingDeclaration Declaration { get; set; } = null!;
        public List<PropertyMapping> PropertyMappings { get; set; } = new();
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public bool IsValid => Diagnostics.Count == 0 || Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    }

    internal enum MapDirection
    {
        Create = 1,
        Apply = 2,
        Both = 3
    }
}
