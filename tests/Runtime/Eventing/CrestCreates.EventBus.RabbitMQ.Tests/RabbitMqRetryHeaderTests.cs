using System.Text;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using FluentAssertions;
using RabbitMQ.Client;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

/// <summary>
/// The retry budget header parser: documented AMQP value shapes are accepted; negative, overflowing,
/// mistyped or garbage values are deterministically rejected (never silently reset to zero, which would
/// allow an unbounded redelivery loop).
/// </summary>
public class RabbitMqRetryHeaderTests
{
    private static IReadOnlyBasicProperties PropertiesWithHeader(object? value)
    {
        var properties = new BasicProperties();
        properties.Headers = new Dictionary<string, object?> { [RabbitMqRetryHeader.Key] = value };
        return properties;
    }

    [Fact]
    public void Absent_header_reads_as_zero()
    {
        RabbitMqRetryHeader.TryRead(new BasicProperties(), out var count, out var problem).Should().BeTrue();
        count.Should().Be(0);
        problem.Should().BeNull();
        RabbitMqRetryHeader.TryRead(null, out count, out problem).Should().BeTrue();
        count.Should().Be(0);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("2147483647", int.MaxValue)]
    public void Utf8_string_bytes_are_parsed(string text, int expected)
    {
        RabbitMqRetryHeader.TryRead(PropertiesWithHeader(Encoding.UTF8.GetBytes(text)), out var count, out var problem)
            .Should().BeTrue();
        count.Should().Be(expected);
        problem.Should().BeNull();
    }

    [Fact]
    public void Plain_string_values_are_parsed()
    {
        RabbitMqRetryHeader.TryRead(PropertiesWithHeader("7"), out var count, out _).Should().BeTrue();
        count.Should().Be(7);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    public void Integer_values_are_parsed(int value, int expected)
    {
        RabbitMqRetryHeader.TryRead(PropertiesWithHeader(value), out var count, out _).Should().BeTrue();
        count.Should().Be(expected);
    }

    [Fact]
    public void Long_values_are_parsed_with_range_check()
    {
        RabbitMqRetryHeader.TryRead(PropertiesWithHeader(9L), out var count, out _).Should().BeTrue();
        count.Should().Be(9);

        RabbitMqRetryHeader.TryRead(PropertiesWithHeader((long)int.MaxValue + 1), out _, out var problem).Should().BeFalse();
        problem.Should().Contain("overflows");
    }

    [Fact]
    public void Negative_values_are_rejected()
    {
        RabbitMqRetryHeader.TryRead(PropertiesWithHeader("-1"), out _, out var problem).Should().BeFalse();
        problem.Should().Contain("negative");

        RabbitMqRetryHeader.TryRead(PropertiesWithHeader(-5), out _, out problem).Should().BeFalse();
        problem.Should().Contain("negative");
    }

    [Fact]
    public void Garbage_values_are_rejected()
    {
        RabbitMqRetryHeader.TryRead(PropertiesWithHeader(Encoding.UTF8.GetBytes("abc")), out _, out var problem).Should().BeFalse();
        problem.Should().Contain("not an integer");

        RabbitMqRetryHeader.TryRead(PropertiesWithHeader("12.5"), out _, out problem).Should().BeFalse();
        problem.Should().Contain("not an integer");
    }

    [Fact]
    public void Unsupported_types_are_rejected()
    {
        RabbitMqRetryHeader.TryRead(PropertiesWithHeader(new object()), out _, out var problem).Should().BeFalse();
        problem.Should().Contain("unsupported type");
    }
}
