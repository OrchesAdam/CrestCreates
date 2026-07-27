using CrestCreates.Core.Abstractions.Serialization;
using FluentAssertions;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Contracts;

public sealed class JsonContractSurfaceAttributeTests
{
    [Fact]
    public void JsonContractSurfaceAttribute_TargetsClassOnly()
    {
        var usage = typeof(JsonContractSurfaceAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void JsonContractSurfaceAttribute_AllowsMultipleAndIsNotInherited()
    {
        var usage = typeof(JsonContractSurfaceAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeTrue();
        usage.Inherited.Should().BeFalse();
    }

    [Fact]
    public void JsonContractSurfaceAttribute_PreservesSurfaceType()
    {
        new JsonContractSurfaceAttribute(typeof(IDisposable)).SurfaceType
            .Should().Be(typeof(IDisposable));
    }

    [Fact]
    public void JsonContractSurfaceAttribute_DefaultExcludedParameterTypesIsEmpty()
    {
        new JsonContractSurfaceAttribute(typeof(IDisposable)).ExcludedParameterTypes
            .Should().BeEmpty();
    }

    [Fact]
    public void JsonContractSurfaceAttribute_PreservesConfiguredExcludedParameterTypes()
    {
        var attribute = new JsonContractSurfaceAttribute(typeof(IDisposable))
        {
            ExcludedParameterTypes = [typeof(CancellationToken), typeof(IServiceProvider)]
        };

        attribute.ExcludedParameterTypes.Should().Equal(typeof(CancellationToken), typeof(IServiceProvider));
    }
}
