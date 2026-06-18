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
        public string SourceTypeName { get; set; } = string.Empty;
        public string TargetTypeName { get; set; } = string.Empty;
        public bool SourceTypeResolved { get; set; }
        public bool TargetTypeResolved { get; set; }
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
        public IPropertySymbol? TargetProperty { get; set; }
        public string TargetPropertyName { get; set; } = string.Empty;
        public bool IsIgnored { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsProtected { get; set; }
        public string? CustomSourceName { get; set; }
        public bool NeedsNullCheck { get; set; }
        public bool NeedsCollectionConversion { get; set; }
        public string? CollectionConversionMethod { get; set; } // "ToList()", "ToArray()"
        public string? SourceNavigationPath { get; set; }
        public List<string>? NavigationSegments { get; set; }
        public bool NavigationPathCanReturnNull { get; set; }
        public string? ConverterTypeFullName { get; set; }
        public ObjectMappingConversionKind ConversionKind { get; set; }
    }

    /// <summary>
    /// Represents the complete mapping model for code generation.
    /// </summary>
    internal sealed class ObjectMappingModel
    {
        public MappingDeclaration Declaration { get; set; } = null!;
        public List<PropertyMapping> PropertyMappings { get; set; } = new();
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public bool IncludeToTargetExpression { get; set; } = true;
        public bool IsValid => Diagnostics.Count == 0 || Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    }

    internal enum MapDirection
    {
        Create = 1,
        Apply = 2,
        Both = 3
    }

    /// <summary>
    /// Kind of simple static type conversion supported by the mapping generator.
    /// </summary>
    internal enum ObjectMappingConversionKind
    {
        None = 0,
        EnumToString,
        StringToEnum,
        EnumToInt,
        IntToEnum,
        StringToInt,
        IntToString,
        StringToGuid,
        GuidToString,
        NumericCast
    }
}
