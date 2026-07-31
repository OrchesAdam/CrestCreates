using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.Providers;

public sealed class RuntimePersistenceProviderTierTests
{
    [Fact]
    public void RuntimeProviderTierContract_ShouldDistinguishFullSemanticAndFullDurable()
    {
        RuntimePersistenceProviderTier.FullSemantic
            .Should().NotBe(RuntimePersistenceProviderTier.FullDurable);
        ((int)RuntimePersistenceProviderTier.Unknown)
            .Should().BeLessThan((int)RuntimePersistenceProviderTier.FullSemantic);
    }
}
