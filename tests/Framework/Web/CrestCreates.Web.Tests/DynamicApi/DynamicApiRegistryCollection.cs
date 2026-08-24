using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DynamicApiRegistryCollection : ICollectionFixture<DynamicApiRegistryCollectionMarker>
{
    public const string Name = "Dynamic API generated registry";
}

public sealed class DynamicApiRegistryCollectionMarker
{
}
