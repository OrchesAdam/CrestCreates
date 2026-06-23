using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

/// <summary>
/// Canonical hash profile for <see cref="FormFieldDescriptor"/>.
/// Sub-structure of <see cref="FormDescriptor"/>, not a standalone descriptor.
///
/// Contract fields (in both ContractHash and DefinitionHash):
///   SchemaFieldName, IsReadOnly, Order, VisibilityCondition,
///   ControlType, IsRequiredOverride, OptionsSource
///
/// DefinitionOnly fields (only in DefinitionHash):
///   Label, Placeholder, HelpText, FormatHint, Group,
///   ValidationMessage, DefaultValueExpression, Metadata
/// </summary>
[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(FormFieldDescriptor),
    ContractShapeVersion = "form-field-contract-hash-v1",
    DefinitionShapeVersion = "form-field-definition-hash-v1")]
internal sealed class FormFieldCanonicalHashProfile
{
    // ── Contract fields (common to both ContractHash and DefinitionHash) ──

    [CanonicalHashField(nameof(FormFieldDescriptor.SchemaFieldName), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(FormFieldDescriptor.IsReadOnly), CanonicalHashFieldClassification.Contract, Order = 10)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Order), CanonicalHashFieldClassification.Contract, Order = 11)]
    [CanonicalHashField(nameof(FormFieldDescriptor.VisibilityCondition), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(FormFieldDescriptor.ControlType), CanonicalHashFieldClassification.Contract, Order = 30)]
    [CanonicalHashField(nameof(FormFieldDescriptor.IsRequiredOverride), CanonicalHashFieldClassification.Contract, Order = 40)]
    [CanonicalHashField(nameof(FormFieldDescriptor.OptionsSource), CanonicalHashFieldClassification.Contract, Order = 50)]

    // ── DefinitionOnly fields (only in DefinitionHash) ──

    [CanonicalHashField(nameof(FormFieldDescriptor.Label), CanonicalHashFieldClassification.DefinitionOnly, Order = 100)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Placeholder), CanonicalHashFieldClassification.DefinitionOnly, Order = 101)]
    [CanonicalHashField(nameof(FormFieldDescriptor.HelpText), CanonicalHashFieldClassification.DefinitionOnly, Order = 102)]
    [CanonicalHashField(nameof(FormFieldDescriptor.FormatHint), CanonicalHashFieldClassification.DefinitionOnly, Order = 103)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Group), CanonicalHashFieldClassification.DefinitionOnly, Order = 104)]
    [CanonicalHashField(nameof(FormFieldDescriptor.ValidationMessage), CanonicalHashFieldClassification.DefinitionOnly, Order = 110)]
    [CanonicalHashField(nameof(FormFieldDescriptor.DefaultValueExpression), CanonicalHashFieldClassification.DefinitionOnly, Order = 111)]
    [CanonicalHashField(nameof(FormFieldDescriptor.Metadata), CanonicalHashFieldClassification.DefinitionOnly, Order = 120,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrderedKeyValue)]

    private static void Fields() { }
}
