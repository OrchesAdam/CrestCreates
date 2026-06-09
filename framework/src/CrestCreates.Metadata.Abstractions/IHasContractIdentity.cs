namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Descriptors with compatibility and implementation identity hashes.
/// Used for version compatibility checks, topology analysis, AI reasoning.
/// </summary>
public interface IHasContractIdentity
{
    string ContractHash { get; }
    string DefinitionHash { get; }
}
