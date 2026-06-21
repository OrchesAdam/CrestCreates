using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.AgentDraftContractGenerator;

internal enum FieldClassification
{
    EditableScalar,
    EditableReference,
    Preserve,
    Unsupported
}

internal enum PreserveStrategy
{
    CreateDefault,
    KnownDomainDefault,
    CreateUnsupported
}

internal sealed record ContractFieldSpec
{
    public required string PropertyName { get; init; }
    public required string ContractName { get; init; }
    public required FieldClassification Classification { get; init; }
    public required ITypeSymbol PropertyType { get; init; }
    public required bool IsNullable { get; init; }
    public required bool IsCollection { get; init; }
    public bool IsRequiredOnCreate { get; init; }
    public PreserveStrategy PreserveCreateStrategy { get; init; }
    public string? PreserveReason { get; init; }
    public string? UnsupportedReason { get; init; }
}

internal sealed record ContractKindSpec
{
    public required string KindName { get; init; }
    public required ITypeSymbol DescriptorType { get; init; }
    public required ITypeSymbol PayloadType { get; init; }
    public required IReadOnlyList<ContractFieldSpec> Fields { get; init; }
}

internal sealed record ContractModel
{
    public required IReadOnlyList<ContractKindSpec> Kinds { get; init; }
}
