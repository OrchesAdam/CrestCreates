using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;

namespace CrestCreates.Metadata.Runtime;

public sealed class RuntimeDescriptorPinResolver<TDescriptor> : IRuntimeDescriptorPinResolver<TDescriptor>
    where TDescriptor : class, IVersionedDescriptor
{
    private readonly IVersionedDescriptorRegistry<TDescriptor> _registry;
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly string _expectedNamespace;
    private readonly DescriptorKind _expectedKind;

    public RuntimeDescriptorPinResolver(
        IVersionedDescriptorRegistry<TDescriptor> registry,
        IDescriptorStableHashBuilder hashBuilder,
        string expectedNamespace,
        DescriptorKind expectedKind)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _hashBuilder = hashBuilder ?? throw new ArgumentNullException(nameof(hashBuilder));
        _expectedNamespace = string.IsNullOrWhiteSpace(expectedNamespace)
            ? throw new ArgumentException("Expected namespace is required.", nameof(expectedNamespace))
            : expectedNamespace;
        _expectedKind = expectedKind == DescriptorKind.Unknown
            ? throw new ArgumentException("Expected descriptor kind is required.", nameof(expectedKind))
            : expectedKind;
    }

    public ResolvedRuntimeDescriptor<TDescriptor> Capture(TDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(descriptor.Namespace, _expectedNamespace, StringComparison.Ordinal)
            || descriptor.Kind != _expectedKind
            || descriptor.Version <= 0)
        {
            throw new RuntimeDescriptorPinValidationException(
                $"Descriptor '{descriptor.FullId}' does not satisfy the expected runtime identity.");
        }

        var hashes = _hashBuilder.Build(descriptor);
        var pin = new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef(descriptor.Namespace, descriptor.Id, descriptor.Version),
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash
        };
        ValidateHashProfile(pin);
        return new ResolvedRuntimeDescriptor<TDescriptor> { Descriptor = descriptor, Pin = pin };
    }

    public ResolvedRuntimeDescriptor<TDescriptor> Resolve(RuntimeDescriptorPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        try
        {
            pin.EnsureValid();
        }
        catch (Exception exception) when (exception is ArgumentException)
        {
            throw new RuntimeDescriptorPinValidationException("Descriptor pin shape is invalid.", exception);
        }

        if (!string.Equals(pin.Ref.Namespace, _expectedNamespace, StringComparison.Ordinal))
            throw new RuntimeDescriptorPinValidationException("Descriptor pin namespace does not match the runtime.");

        if (_expectedKind != ParseKind(pin.Ref.Namespace))
            throw new RuntimeDescriptorPinValidationException("Descriptor pin namespace has the wrong Descriptor kind.");

        var descriptor = _registry.GetByVersion(pin.Ref.Id, pin.Ref.Version!.Value);
        if (descriptor is null)
            throw new RuntimeDescriptorPinValidationException(
                $"Descriptor '{pin.Ref.FullId}' v{pin.Ref.Version.Value} is not available in the activated Registry.");

        if (!string.Equals(descriptor.Namespace, pin.Ref.Namespace, StringComparison.Ordinal)
            || !string.Equals(descriptor.Id, pin.Ref.Id, StringComparison.Ordinal)
            || descriptor.Version != pin.Ref.Version.Value
            || descriptor.Kind != _expectedKind)
        {
            throw new RuntimeDescriptorPinValidationException("Resolved Descriptor identity does not match the pin.");
        }

        var hashes = _hashBuilder.Build(descriptor);
        if (!StructuredHashEquals(pin.ContractHash, hashes.ContractHash))
            throw new RuntimeDescriptorPinValidationException("Descriptor contract hash does not match the pin.");
        if (!StructuredHashEquals(pin.DefinitionHash, hashes.DefinitionHash))
            throw new RuntimeDescriptorPinValidationException("Descriptor definition hash does not match the pin.");

        ValidateHashProfile(pin);
        return new ResolvedRuntimeDescriptor<TDescriptor> { Descriptor = descriptor, Pin = pin };
    }

    private void ValidateHashProfile(RuntimeDescriptorPin pin)
    {
        if (!StructuredHashProfileValid(pin.ContractHash, "Contract")
            || !StructuredHashProfileValid(pin.DefinitionHash, "Definition"))
        {
            throw new RuntimeDescriptorPinValidationException("Descriptor pin hash profile is not executable.");
        }
    }

    private static bool StructuredHashProfileValid(CanonicalHash hash, string purpose)
        => string.Equals(hash.Purpose, purpose, StringComparison.Ordinal)
            && string.Equals(hash.Scope, "InternalFull", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(hash.Algorithm)
            && !string.IsNullOrWhiteSpace(hash.AlgorithmVersion)
            && !string.IsNullOrWhiteSpace(hash.ContractVersion)
            && !string.IsNullOrWhiteSpace(hash.CanonicalShapeVersion);

    private static bool StructuredHashEquals(CanonicalHash left, CanonicalHash right)
        => string.Equals(left.Value, right.Value, StringComparison.Ordinal)
            && string.Equals(left.Algorithm, right.Algorithm, StringComparison.Ordinal)
            && string.Equals(left.AlgorithmVersion, right.AlgorithmVersion, StringComparison.Ordinal)
            && string.Equals(left.ArtifactKind, right.ArtifactKind, StringComparison.Ordinal)
            && string.Equals(left.DescriptorKind, right.DescriptorKind, StringComparison.Ordinal)
            && string.Equals(left.Scope, right.Scope, StringComparison.Ordinal)
            && string.Equals(left.Purpose, right.Purpose, StringComparison.Ordinal)
            && string.Equals(left.ContractVersion, right.ContractVersion, StringComparison.Ordinal)
            && string.Equals(left.CanonicalShapeVersion, right.CanonicalShapeVersion, StringComparison.Ordinal);

    private static DescriptorKind ParseKind(string descriptorNamespace)
        => descriptorNamespace switch
        {
            "schema" => DescriptorKind.Schema,
            "capability" => DescriptorKind.Capability,
            "event" => DescriptorKind.Event,
            "workflow" => DescriptorKind.Workflow,
            "form" => DescriptorKind.Form,
            "human-task" or "humantask" => DescriptorKind.HumanTask,
            "dynamic-api-endpoint" => DescriptorKind.DynamicApiEndpoint,
            "mcp-tool" => DescriptorKind.McpTool,
            "agent-tool" => DescriptorKind.AgentTool,
            _ => DescriptorKind.Unknown
        };
}
