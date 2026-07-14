using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CrestCreates.CodeGenerator.AgentDraftContractGenerator;

/// <summary>
/// Holds extracted info about a single spec class decorated with [AgentDraftContractSpec].
/// </summary>
internal sealed class SpecClassInfo
{
    public INamedTypeSymbol Symbol { get; }
    public string KindName { get; }

    public SpecClassInfo(INamedTypeSymbol symbol, string kindName)
    {
        Symbol = symbol;
        KindName = kindName;
    }
}

internal sealed class ContractModelBuilder
{
    private static readonly string[] KnownKinds =
    {
        "Capability", "Workflow", "HumanTask", "Form", "Event", "Schema"
    };

    // Properties on IDescriptor that are identity/infrastructure — never editable, never need classification
    private static readonly HashSet<string> InfrastructureProperties = new(StringComparer.Ordinal)
    {
        "Namespace",  // Registry domain, computed from descriptor type
        "Id",         // Immutable identity
        "Kind",       // Computed descriptor kind discriminator, never editable
        "ContractHash",  // Computed by IDescriptorStableHashBuilder, not a descriptor property
        "DefinitionHash", // Computed by IDescriptorStableHashBuilder, not a descriptor property
    };

    // Validates that KindName is one of the known descriptor kinds.
    private static bool IsKnownKind(string kindName)
    {
        foreach (var k in KnownKinds)
        {
            if (string.Equals(k, kindName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private readonly Compilation _compilation;
    private readonly SourceProductionContext _context;

    public ContractModelBuilder(Compilation compilation, SourceProductionContext context)
    {
        _compilation = compilation;
        _context = context;
    }

    public ContractModel? Build(ImmutableArray<SpecClassInfo> specs)
    {
        var specKinds = new HashSet<string>(StringComparer.Ordinal);
        var kinds = new List<ContractKindSpec>();

        foreach (var spec in specs)
        {
            specKinds.Add(spec.KindName);
            var kindSpec = BuildKindSpec(spec);
            if (kindSpec is not null)
                kinds.Add(kindSpec);
        }

        // ADP001: Check for known kinds that have no spec class
        foreach (var knownKind in KnownKinds)
        {
            if (!specKinds.Contains(knownKind))
            {
                _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                    AgentDraftContractDiagnostics.NoSpecForDescriptor,
                    null,
                    knownKind));
            }
        }

        if (kinds.Count == 0) return null;

        // Sort by KindName for determinism
        kinds.Sort((a, b) => string.CompareOrdinal(a.KindName, b.KindName));

        return new ContractModel { Kinds = kinds };
    }

    private ContractKindSpec? BuildKindSpec(SpecClassInfo spec)
    {
        if (!IsKnownKind(spec.KindName)) return null;

        var kindName = spec.KindName;
        var descriptorTypeName = $"{kindName}Descriptor";
        var payloadTypeName = $"{kindName}DescriptorDraftPayload";

        // Resolve descriptor type from compilation.
        // Primary pattern: CrestCreates.{KindName}.Abstractions.{KindName}Descriptor
        // Fallback pattern: CrestCreates.Metadata.{KindName}Descriptor (e.g., CapabilityDescriptor)
        var descriptorType = _compilation.GetTypeByMetadataName(
            $"CrestCreates.{kindName}.Abstractions.{descriptorTypeName}")
            ?? _compilation.GetTypeByMetadataName(
            $"CrestCreates.Metadata.{descriptorTypeName}");

        // Resolve payload type from compilation.
        // Pattern: CrestCreates.DescriptorDraft.{KindName}DescriptorDraftPayload
        var payloadType = _compilation.GetTypeByMetadataName(
            $"CrestCreates.DescriptorDraft.{payloadTypeName}");

        if (descriptorType is null || payloadType is null) return null;

        var fields = BuildFields(spec.Symbol, kindName);

        // ── Descriptor closure validation ──
        // Every public persistent property on the descriptor type must be classified in the spec.
        var classifiedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields)
            classifiedNames.Add(f.PropertyName);

        // (a) Descriptor properties not classified in the spec → ADP002
        foreach (var member in descriptorType.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.IsStatic || prop.IsImplicitlyDeclared) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (InfrastructureProperties.Contains(prop.Name)) continue;

            if (!classifiedNames.Contains(prop.Name))
            {
                _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                    AgentDraftContractDiagnostics.NoClassification,
                    prop.Locations.FirstOrDefault(),
                    prop.Name, kindName));
            }
        }

        // (b) Spec property names that don't exist on descriptor type → ADP010
        var descriptorPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in descriptorType.GetMembers())
        {
            if (member is IPropertySymbol prop && !prop.IsStatic && !prop.IsImplicitlyDeclared
                && prop.DeclaredAccessibility == Accessibility.Public
                && !InfrastructureProperties.Contains(prop.Name))
            {
                descriptorPropertyNames.Add(prop.Name);
            }
        }

        foreach (var fieldName in classifiedNames)
        {
            if (!descriptorPropertyNames.Contains(fieldName))
            {
                var specProp = spec.Symbol.GetMembers(fieldName).FirstOrDefault();
                _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                    AgentDraftContractDiagnostics.UnsupportedReference,
                    specProp?.Locations.FirstOrDefault(),
                    fieldName, fieldName, kindName));
            }
        }

        return new ContractKindSpec
        {
            KindName = kindName,
            DescriptorType = descriptorType,
            PayloadType = payloadType,
            Fields = fields
        };
    }

    private IReadOnlyList<ContractFieldSpec> BuildFields(INamedTypeSymbol specSymbol, string kindName)
    {
        var fields = new List<ContractFieldSpec>();

        foreach (var member in specSymbol.GetMembers())
        {
            if (member is not IPropertySymbol property) continue;
            if (property.IsStatic || property.IsImplicitlyDeclared) continue;

            // Skip infrastructure properties — they're computed, not from descriptor
            if (InfrastructureProperties.Contains(property.Name)) continue;

            var attrs = property.GetAttributes();

            // Determine primary classification
            FieldClassification? classification = null;
            AttributeData? fieldAttr = null;
            AttributeData? refAttr = null;
            AttributeData? preserveAttr = null;
            AttributeData? unsupportedAttr = null;

            foreach (var attr in attrs)
            {
                var attrClassName = attr.AttributeClass?.Name;
                if (attrClassName == "AgentDraftFieldAttribute") fieldAttr = attr;
                else if (attrClassName == "AgentDraftReferenceAttribute") refAttr = attr;
                else if (attrClassName == "AgentDraftPreserveAttribute") preserveAttr = attr;
                else if (attrClassName == "AgentDraftUnsupportedAttribute") unsupportedAttr = attr;
            }

            if (fieldAttr is not null) classification = FieldClassification.EditableScalar;
            if (refAttr is not null)
            {
                if (classification.HasValue)
                {
                    _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                        AgentDraftContractDiagnostics.MultipleClassifications,
                        property.Locations.FirstOrDefault(),
                        property.Name, kindName));
                    continue;
                }
                classification = FieldClassification.EditableReference;
            }
            if (preserveAttr is not null)
            {
                if (classification.HasValue)
                {
                    _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                        AgentDraftContractDiagnostics.MultipleClassifications,
                        property.Locations.FirstOrDefault(),
                        property.Name, kindName));
                    continue;
                }
                classification = FieldClassification.Preserve;
            }
            if (unsupportedAttr is not null)
            {
                if (classification.HasValue)
                {
                    _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                        AgentDraftContractDiagnostics.MultipleClassifications,
                        property.Locations.FirstOrDefault(),
                        property.Name, kindName));
                    continue;
                }
                classification = FieldClassification.Unsupported;
            }

            if (!classification.HasValue)
            {
                _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                    AgentDraftContractDiagnostics.NoClassification,
                    property.Locations.FirstOrDefault(),
                    property.Name, kindName));
                continue;
            }

            // Extract modifier attributes
            var isRequiredOnCreate = false;
            string? contractNameOverride = null;

            foreach (var attr in attrs)
            {
                var attrClassName = attr.AttributeClass?.Name;
                if (attrClassName == "AgentDraftRequiredOnCreateAttribute")
                    isRequiredOnCreate = true;
                else if (attrClassName == "AgentDraftContractNameAttribute")
                    contractNameOverride = attr.NamedArguments
                        .FirstOrDefault(kvp => kvp.Key == "Name").Value.Value?.ToString();
            }

            // ADP006: RequiredOnCreate is only valid on EditableScalar or EditableReference
            if (isRequiredOnCreate && classification != FieldClassification.EditableScalar && classification != FieldClassification.EditableReference)
            {
                _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                    AgentDraftContractDiagnostics.InvalidRequiredOnCreate,
                    property.Locations.FirstOrDefault(),
                    property.Name, kindName));
            }

            var contractName = contractNameOverride ?? property.Name;

            // Extract Preserve data
            PreserveStrategy preserveStrategy = PreserveStrategy.CreateDefault;
            string? preserveReason = null;
            string? unsupportedReason = null;

            if (classification == FieldClassification.Preserve && preserveAttr is not null)
            {
                preserveReason = GetNamedArgumentValue(preserveAttr, "Reason");
                var strategyName = GetNamedArgumentValue(preserveAttr, "CreateStrategy");
                if (!string.IsNullOrEmpty(strategyName) &&
                    Enum.TryParse<PreserveStrategy>(strategyName, out var strategy))
                {
                    preserveStrategy = strategy;
                }

                if (string.IsNullOrEmpty(preserveReason))
                {
                    _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                        AgentDraftContractDiagnostics.MissingReason,
                        property.Locations.FirstOrDefault(),
                        property.Name, kindName, "Preserve"));
                }
            }

            if (classification == FieldClassification.Unsupported && unsupportedAttr is not null)
            {
                unsupportedReason = GetNamedArgumentValue(unsupportedAttr, "Reason");
                if (string.IsNullOrEmpty(unsupportedReason))
                {
                    _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                        AgentDraftContractDiagnostics.MissingReason,
                        property.Locations.FirstOrDefault(),
                        property.Name, kindName, "Unsupported"));
                }
            }

            fields.Add(new ContractFieldSpec
            {
                PropertyName = property.Name,
                ContractName = contractName,
                Classification = classification.Value,
                PropertyType = property.Type,
                IsNullable = IsNullableType(property.Type),
                IsCollection = IsCollectionType(property.Type),
                IsRequiredOnCreate = isRequiredOnCreate,
                PreserveCreateStrategy = preserveStrategy,
                PreserveReason = preserveReason,
                UnsupportedReason = unsupportedReason
            });
        }

        // ADP008: Check for duplicate ContractNames within the same kind
        var contractNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!contractNames.Add(field.ContractName))
            {
                var specProp = specSymbol.GetMembers(field.PropertyName).FirstOrDefault();
                _context.ReportDiagnostic(AgentDraftContractDiagnostics.Create(
                    AgentDraftContractDiagnostics.InvalidContractName,
                    specProp?.Locations.FirstOrDefault(),
                    field.ContractName, field.PropertyName, kindName));
            }
        }

        // Sort by classification order, then by contract name for determinism
        return fields
            .OrderBy(f => (int)f.Classification)
            .ThenBy(f => f.ContractName, StringComparer.Ordinal)
            .ToList();
    }

    private static string? GetNamedArgumentValue(AttributeData attr, string argumentName)
    {
        return attr.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == argumentName).Value.Value?.ToString();
    }

    private static bool IsNullableType(ITypeSymbol type)
    {
        // Reference type with nullable annotation
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return true;

        // Nullable<T> for value types (e.g. int?, TimeSpan?)
        if (type is INamedTypeSymbol namedType &&
            namedType.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T)
            return true;

        return false;
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        // string is IEnumerable<char> but not a collection for our purposes
        if (type.SpecialType == SpecialType.System_String)
            return false;

        // Array types are collections
        if (type is IArrayTypeSymbol)
            return true;

        // Check if type implements IEnumerable<T> (via AllInterfaces)
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
}
