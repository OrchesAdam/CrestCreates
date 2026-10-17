using System.Text.Json;
using CrestCreates.Application.Contracts.Query;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

public class Review150EnumFilterTests
{
    [Fact]
    public void Review150_enum_integer_and_name_positive_controls()
    {
        Assert.Equal(IntStatus.Active, Read<IntStatus>("1"));
        Assert.Equal(ByteStatus.Active, Read<ByteStatus>("\"Active\""));
        Assert.Equal(LongStatus.Active, Read<LongStatus>("\"Active\""));
    }

    [Fact]
    public void Review150_byte_enum_numeric_value_must_be_read() =>
        Assert.Equal(ByteStatus.Active, Read<ByteStatus>("1"));

    [Theory]
    [InlineData("1", LongStatus.Active)]
    [InlineData("4294967296", LongStatus.Large)]
    public void Review150_long_enum_numeric_value_must_be_read(string json, LongStatus expected) =>
        Assert.Equal(expected, Read<LongStatus>(json));

    private static T Read<T>(string json) where T : struct, Enum
    {
        using var document = JsonDocument.Parse(json);
        return FilterValueReader.ReadEnum<T>(document.RootElement, "Status");
    }

    public enum IntStatus { Active = 1 }
    public enum ByteStatus : byte { Active = 1 }
    public enum LongStatus : long { Active = 1, Large = 4294967296 }
}
