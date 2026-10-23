using Xunit;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// Tests that mutate or read the compile-time model-filter registries must not run concurrently:
/// the stores are process-wide and a temporary Clear is observable by other contexts.
/// </summary>
[CollectionDefinition("ModelFilterRegistry", DisableParallelization = true)]
public sealed class ModelFilterRegistryCollection
{
}
