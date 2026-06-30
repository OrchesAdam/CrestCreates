using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record SchemaDescriptorDraftPayload(
    SchemaDescriptor Descriptor
) : DescriptorDraftPayload
{
    public override DescriptorKind DescriptorKind => DescriptorKind.Schema;
    public override IDescriptor GetDescriptor() => Descriptor;
    public override DescriptorDraftPayload Snapshot() => this with
    {
        Descriptor = new SchemaDescriptor
        {
            Id = Descriptor.Id,
            Name = Descriptor.Name,
            State = Descriptor.State,
            SupersededById = Descriptor.SupersededById,
            Version = Descriptor.Version,
            ChangeKind = Descriptor.ChangeKind,
            Fields = Descriptor.Fields.Select(CloneField).ToArray(),
            ValidationRules = Descriptor.ValidationRules.Select(CloneRule).ToArray(),
            References = Descriptor.References.ToArray()
        }
    };

    private static SchemaFieldDescriptor CloneField(SchemaFieldDescriptor field) => new()
    {
        Name = field.Name,
        FieldType = field.FieldType,
        IsRequired = field.IsRequired,
        IsNullable = field.IsNullable,
        MaxLength = field.MaxLength,
        MinLength = field.MinLength,
        MaxValue = field.MaxValue,
        MinValue = field.MinValue,
        Pattern = field.Pattern,
        IsCollection = field.IsCollection,
        CollectionElementType = field.CollectionElementType
    };

    private static SchemaValidationRule CloneRule(SchemaValidationRule rule) => new()
    {
        Name = rule.Name,
        Expression = rule.Expression,
        ErrorMessage = rule.ErrorMessage
    };
}
