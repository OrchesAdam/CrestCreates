using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form.Abstractions;

public sealed class FormDescriptor : IInteractionDescriptor
{
    public string Namespace => "form";
    public DescriptorKind Kind => DescriptorKind.Form;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }
    public int Version { get; init; }

    public VersionedDescriptorRef<SchemaDescriptor> Schema { get; init; }
    public IReadOnlyList<FormFieldDescriptor> Fields { get; init; } = Array.Empty<FormFieldDescriptor>();
    public string? LayoutColumns { get; init; }
}
