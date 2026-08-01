using CrestCreates.Runtime.Persistence.Abstractions.State;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.State;

public sealed class RuntimeStateValueTests
{
    [Fact]
    public void RuntimeStateValue_ShouldDistinguishAbsentFromTypedNull()
    {
        RuntimeStateValue? absent = null;
        var typedNull = new RuntimeStateValue
        {
            TypeId = "test/value/v1",
            JsonPayload = "null"
        };

        absent.Should().BeNull();
        typedNull.Should().NotBeNull();
        typedNull.JsonPayload.Should().Be("null");
    }
}
