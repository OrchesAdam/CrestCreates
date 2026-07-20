using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Supplies one source-generated JSON resolver using the runtime's normalized
/// options profile. System.Text.Json contexts own and freeze their options, so
/// each contributor receives an equivalent generated-context template rather
/// than a mutable resolver chain. Contributors are registered explicitly by
/// an enabled module; loaded-assembly discovery is not part of the runtime
/// path.
/// </summary>
public interface IAgentToolJsonContextContributor
{
    string Id { get; }

    int Order { get; }

    /// <summary>Explicit module opt-in key. The default module is always eligible.</summary>
    string ModuleId => "default";

    JsonSerializerContext Create(JsonSerializerOptions sharedOptions);

    /// <summary>
    /// Generated contract declarations for binding roots and nested metadata.
    /// Older contributors may return an empty collection while they migrate;
    /// root ownership is still validated through <see cref="BindingRootTypes"/>.
    /// </summary>
    IReadOnlyList<AgentToolJsonTypeContract> TypeContracts => Array.Empty<AgentToolJsonTypeContract>();

    /// <summary>
    /// Binding input/output roots owned by this context. Nested metadata may
    /// occur in more than one context only after contract parity is proven.
    /// </summary>
    IReadOnlyCollection<Type> BindingRootTypes { get; }
}

public sealed record AgentToolJsonTypeContract
{
    public required Type ClrType { get; init; }
    public required string ContributorId { get; init; }
    public required VersionedDescriptorRef<SchemaDescriptor> SchemaRef { get; init; }
    public required CanonicalHash ContractFingerprint { get; init; }
    public required bool IsBindingRoot { get; init; }
}
