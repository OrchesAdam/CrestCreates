using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.Transactions;

public sealed class RuntimeTransactionContractShapeTests
{
    [Fact]
    public void RuntimeTransactionCoordinator_ShouldExposeRequiredPropagationOnly()
    {
        var methods = typeof(IRuntimeTransactionCoordinator).GetMethods();

        methods.Should().HaveCount(2);
        methods.Should().OnlyContain(method => method.Name == "ExecuteAsync");
        methods.SelectMany(method => method.GetParameters())
            .Should().NotContain(parameter =>
                parameter.ParameterType.Name.Contains("Transaction", StringComparison.Ordinal)
                || parameter.ParameterType.Name.Contains("Isolation", StringComparison.Ordinal)
                || parameter.ParameterType.Name.Contains("Provider", StringComparison.Ordinal));
    }
}
