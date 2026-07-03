using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft;

public sealed record FormDescriptorDraftPayload(
    FormDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Form;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Snapshot() => this with
    {
        Descriptor = new FormDescriptor
        {
            Id = Descriptor.Id,
            Name = Descriptor.Name,
            State = Descriptor.State,
            SupersededById = Descriptor.SupersededById,
            Version = Descriptor.Version,
            Schema = Descriptor.Schema,
            Fields = Descriptor.Fields.Select(CloneField).ToArray(),
            LayoutColumns = Descriptor.LayoutColumns
        }
    };

    private static FormFieldDescriptor CloneField(FormFieldDescriptor field) => new()
    {
        SchemaFieldName = field.SchemaFieldName,
        Label = field.Label,
        Placeholder = field.Placeholder,
        HelpText = field.HelpText,
        FormatHint = field.FormatHint,
        Order = field.Order,
        Group = field.Group,
        IsReadOnly = field.IsReadOnly,
        VisibilityCondition = field.VisibilityCondition,
        ControlType = field.ControlType,
        IsRequiredOverride = field.IsRequiredOverride,
        ValidationMessage = field.ValidationMessage,
        DefaultValueExpression = field.DefaultValueExpression,
        OptionsSource = field.OptionsSource,
        Metadata = field.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal)
    };
}
