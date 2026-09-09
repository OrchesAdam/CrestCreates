using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Sample.AssetManagement.Contracts;

namespace CrestCreates.Sample.AssetManagement.Host;

public enum AssetMaintenanceTaskRole
{
    Initial,
    Terminal
}

/// <summary>
/// Resolves only the compiled Asset maintenance HumanTask contracts. The
/// explicit pin comparison prevents a same-ID/version descriptor with changed
/// canonical content from acquiring a business role.
/// </summary>
public sealed class AssetMaintenanceTaskContractResolver
{
    private readonly IRuntimeDescriptorPinResolver<HumanTaskDescriptor> _pins;

    public AssetMaintenanceTaskContractResolver(IRuntimeDescriptorPinResolver<HumanTaskDescriptor> pins)
        => _pins = pins;

    public AssetMaintenanceTaskRole Resolve(RuntimeDescriptorPin pin)
    {
        _pins.Resolve(pin);
        if (Matches(pin, AssetDescriptorCatalog.MaintenanceInitialHumanTask))
            return AssetMaintenanceTaskRole.Initial;
        if (Matches(pin, AssetDescriptorCatalog.MaintenanceHumanTask))
            return AssetMaintenanceTaskRole.Terminal;
        throw new InvalidOperationException("The maintenance HumanTask pin is not a known compiled Asset contract.");
    }

    private bool Matches(RuntimeDescriptorPin actual, HumanTaskDescriptor expected)
    {
        var compiled = _pins.Capture(expected).Pin;
        return actual.Ref == compiled.Ref
            && actual.ContractHash == compiled.ContractHash
            && actual.DefinitionHash == compiled.DefinitionHash;
    }
}
