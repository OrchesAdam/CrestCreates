namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptor
{
    DescriptorKind Kind { get; }
    string Id { get; }
    string Name { get; }
    DescriptorState State { get; }
    string ContractHash { get; }
    string DefinitionHash { get; }
    string? SupersededById { get; }
}
