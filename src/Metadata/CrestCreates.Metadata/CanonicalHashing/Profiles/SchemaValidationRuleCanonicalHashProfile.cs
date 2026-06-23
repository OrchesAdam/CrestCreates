using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="SchemaValidationRule"/>.
///
/// Validation rules are included only in <see cref="CanonicalHashFieldClassification.DefinitionOnly"/>
/// — they do not affect ContractHash. This matches the existing
/// <c>DescriptorStableHashBuilder</c> where <c>ValidationRules</c> appears
/// exclusively in <c>AppendDefinitionFields</c>.
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    TargetType = typeof(SchemaValidationRule),
    ContractShapeVersion = "schema-validation-rule-hash-v1",
    DefinitionShapeVersion = "schema-validation-rule-hash-v1")]
internal sealed class SchemaValidationRuleCanonicalHashProfile
{
    [CanonicalHashField(nameof(SchemaValidationRule.Name), CanonicalHashFieldClassification.DefinitionOnly, Order = 0)]
    [CanonicalHashField(nameof(SchemaValidationRule.Expression), CanonicalHashFieldClassification.DefinitionOnly, Order = 1)]
    [CanonicalHashField(nameof(SchemaValidationRule.ErrorMessage), CanonicalHashFieldClassification.DefinitionOnly, Order = 2)]

    private static void Fields() { }
}
