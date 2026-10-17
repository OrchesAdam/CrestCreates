#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Query;

internal enum ProbeState
{
    Active = 1,
    Archived = 2,
}

// PR150-R1-CRUD128-04 acceptance: one enum per CLR underlying type, with boundary members.
internal enum ByteState : byte
{
    Inactive = 0,
    Active = 1,
    Max = 255,
}

internal enum SByteState : sbyte
{
    Min = -128,
    Active = 1,
    Max = 127,
}

internal enum ShortState : short
{
    Min = -32768,
    Active = 1,
    Max = 32767,
}

internal enum UShortState : ushort
{
    Active = 1,
    Max = 65535,
}

internal enum UIntState : uint
{
    Active = 1,
    Max = 4294967295,
}

internal enum LongState : long
{
    Active = 1,
    Large = 4294967296,
    Max = 9223372036854775807,
}

internal enum ULongState : ulong
{
    Active = 1,
    Max = 18446744073709551615,
}

/// <summary>
/// #128 S3 (§8): the static value reader behind generated field adapters. Values are read according to
/// the field's static CLR type — exact wire text for decimals and 64-bit integers, no token-based
/// numeric guessing, no truncation — and anything that does not fit the field type is rejected before a
/// query can execute.
/// </summary>
public class FilterValueReaderTests
{
    private static JsonElement Stage(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Decimal_fields_must_read_the_exact_wire_text()
    {
        var staged = Stage("0.1234567890123456789012345678");

        FilterValueReader.ReadDecimal(staged, "Price").Should().Be(0.1234567890123456789012345678m);
    }

    [Fact]
    public void Values_beyond_the_field_type_must_be_rejected_not_degraded()
    {
        var beyondDecimal = Stage("1e100");

        var act = () => FilterValueReader.ReadDecimal(beyondDecimal, "Price");
        act.Should().Throw<FilterValueFormatException>()
            .WithMessage("*Price*decimal*");

        var fractional = Stage("19.5");
        var intAct = () => FilterValueReader.ReadInt32(fractional, "Count");
        intAct.Should().Throw<FilterValueFormatException>(
            "a fractional number must not be truncated into an integer field");
    }

    [Fact]
    public void Int64_fields_must_keep_the_full_range()
    {
        var staged = Stage("9223372036854775807");

        FilterValueReader.ReadInt64(staged, "Total").Should().Be(long.MaxValue);
    }

    [Fact]
    public void Scalar_readers_must_reject_mismatched_json_kinds()
    {
        var number = Stage("42");
        var text = Stage("\"alpha\"");

        ((Action)(() => FilterValueReader.ReadString(number, "Name")))
            .Should().Throw<FilterValueFormatException>();
        ((Action)(() => FilterValueReader.ReadBool(number, "Flag")))
            .Should().Throw<FilterValueFormatException>();
        ((Action)(() => FilterValueReader.ReadGuid(text, "Id")))
            .Should().Throw<FilterValueFormatException>();
        ((Action)(() => FilterValueReader.ReadDateTime(text, "CreationTime")))
            .Should().Throw<FilterValueFormatException>();
    }

    [Fact]
    public void Enum_fields_must_accept_only_defined_name_or_number()
    {
        FilterValueReader.ReadEnum<ProbeState>(Stage("\"Active\""), "State").Should().Be(ProbeState.Active);
        FilterValueReader.ReadEnum<ProbeState>(Stage("2"), "State").Should().Be(ProbeState.Archived);

        ((Action)(() => FilterValueReader.ReadEnum<ProbeState>(Stage("\"Unknown\""), "State")))
            .Should().Throw<FilterValueFormatException>();
        ((Action)(() => FilterValueReader.ReadEnum<ProbeState>(Stage("7"), "State")))
            .Should().Throw<FilterValueFormatException>(
                "an undefined numeric value must not be coerced into the enum default");
    }

    [Fact]
    public void Enum_fields_must_read_every_underlying_type_by_its_own_range()
    {
        FilterValueReader.ReadEnum<ByteState>(Stage("1"), "State").Should().Be(ByteState.Active);
        FilterValueReader.ReadEnum<ByteState>(Stage("255"), "State").Should().Be(ByteState.Max);
        FilterValueReader.ReadEnum<SByteState>(Stage("-128"), "State").Should().Be(SByteState.Min);
        FilterValueReader.ReadEnum<SByteState>(Stage("127"), "State").Should().Be(SByteState.Max);
        FilterValueReader.ReadEnum<ShortState>(Stage("-32768"), "State").Should().Be(ShortState.Min);
        FilterValueReader.ReadEnum<ShortState>(Stage("32767"), "State").Should().Be(ShortState.Max);
        FilterValueReader.ReadEnum<UShortState>(Stage("65535"), "State").Should().Be(UShortState.Max);
        FilterValueReader.ReadEnum<UIntState>(Stage("4294967295"), "State").Should().Be(UIntState.Max);
        FilterValueReader.ReadEnum<LongState>(Stage("1"), "State").Should().Be(LongState.Active);
        FilterValueReader.ReadEnum<LongState>(Stage("4294967296"), "State").Should().Be(LongState.Large);
        FilterValueReader.ReadEnum<LongState>(Stage("9223372036854775807"), "State").Should().Be(LongState.Max);
        FilterValueReader.ReadEnum<ULongState>(Stage("18446744073709551615"), "State").Should().Be(ULongState.Max);

        // Names stay accepted for every underlying type.
        FilterValueReader.ReadEnum<ByteState>(Stage("\"Active\""), "State").Should().Be(ByteState.Active);
        FilterValueReader.ReadEnum<ShortState>(Stage("\"Active\""), "State").Should().Be(ShortState.Active);
        FilterValueReader.ReadEnum<LongState>(Stage("\"Active\""), "State").Should().Be(LongState.Active);
        FilterValueReader.ReadEnum<ULongState>(Stage("\"Active\""), "State").Should().Be(ULongState.Active);
    }

    [Fact]
    public void Enum_numbers_outside_the_underlying_range_or_undefined_must_be_rejected()
    {
        ((Action)(() => FilterValueReader.ReadEnum<ByteState>(Stage("256"), "State")))
            .Should().Throw<FilterValueFormatException>("a byte enum member cannot exceed byte.MaxValue");
        ((Action)(() => FilterValueReader.ReadEnum<ByteState>(Stage("-1"), "State")))
            .Should().Throw<FilterValueFormatException>("a byte enum has no negative members");
        ((Action)(() => FilterValueReader.ReadEnum<ByteState>(Stage("200"), "State")))
            .Should().Throw<FilterValueFormatException>("an in-range but undefined value is a rejection");
        ((Action)(() => FilterValueReader.ReadEnum<ByteState>(Stage("1.5"), "State")))
            .Should().Throw<FilterValueFormatException>("fractional numbers are never truncated into an enum");
        ((Action)(() => FilterValueReader.ReadEnum<SByteState>(Stage("128"), "State")))
            .Should().Throw<FilterValueFormatException>();
        ((Action)(() => FilterValueReader.ReadEnum<ULongState>(Stage("18446744073709551616"), "State")))
            .Should().Throw<FilterValueFormatException>("beyond ulong.MaxValue the value is not representable");
        ((Action)(() => FilterValueReader.ReadEnum<LongState>(Stage("\"Unknown\""), "State")))
            .Should().Throw<FilterValueFormatException>();
    }

    [Fact]
    public void Enum_arrays_must_read_element_wise_with_the_field_reader()
    {
        FilterValueReader.ReadArray(
                Stage("[0,1,255]"), "State", FilterValueReader.ReadEnum<ByteState>)
            .Should().Equal(ByteState.Inactive, ByteState.Active, ByteState.Max);
        FilterValueReader.ReadArray(
                Stage("[1,4294967296]"), "State", FilterValueReader.ReadEnum<LongState>)
            .Should().Equal(LongState.Active, LongState.Large);

        ((Action)(() => FilterValueReader.ReadArray(
                Stage("[1,200]"), "State", FilterValueReader.ReadEnum<ByteState>)))
            .Should().Throw<FilterValueFormatException>(
                "an undefined array element rejects the whole filter instead of being skipped");
        ((Action)(() => FilterValueReader.ReadArray(
                Stage("[1,256]"), "State", FilterValueReader.ReadEnum<ByteState>)))
            .Should().Throw<FilterValueFormatException>();
    }

    [Fact]
    public void Array_fields_must_read_element_wise_and_reject_null_elements()
    {
        var staged = Stage("[1,2,3]");

        FilterValueReader.ReadArray(staged, "Count", FilterValueReader.ReadInt32)
            .Should().Equal(1, 2, 3);

        var withNull = Stage("[1,null]");
        ((Action)(() => FilterValueReader.ReadArray(withNull, "Count", FilterValueReader.ReadInt32)))
            .Should().Throw<FilterValueFormatException>();
    }

    [Fact]
    public void Null_scalar_values_must_be_rejected_by_the_scalar_readers()
    {
        var act = () => FilterValueReader.ReadString(null, "Name");

        act.Should().Throw<FilterValueFormatException>(
            "the null comparison is the adapter's own operator branch, not a scalar read");
    }

    [Fact]
    public void Clr_callers_must_pass_through_without_staging()
    {
        var id = Guid.NewGuid();
        var moment = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        FilterValueReader.ReadInt32(42, "Count").Should().Be(42);
        FilterValueReader.ReadInt64(42L, "Total").Should().Be(42L);
        FilterValueReader.ReadDecimal(19.5m, "Price").Should().Be(19.5m);
        FilterValueReader.ReadString("alpha", "Name").Should().Be("alpha");
        FilterValueReader.ReadBool(true, "Flag").Should().BeTrue();
        FilterValueReader.ReadGuid(id, "Id").Should().Be(id);
        FilterValueReader.ReadDateTime(moment, "CreationTime").Should().Be(moment);
        FilterValueReader.ReadEnum<ProbeState>(ProbeState.Active, "State").Should().Be(ProbeState.Active);
        FilterValueReader.ReadArray(new[] { 1, 2 }, "Count", FilterValueReader.ReadInt32).Should().Equal(1, 2);
    }

    [Fact]
    public void Bounded_wire_staging_from_the_filter_contract_must_be_readable_by_the_adapters()
    {
        // End to end through the S3-1 wire domain: the payload stages a bounded JsonElement, and the
        // adapter reads it as the field's static type.
        const string payload = """{"filters":[{"field":"Price","operator":2,"value":19.5}]}""";
        var request = JsonSerializer.Deserialize(
            payload, FilterWireProbeJsonContext.Default.FilterWireProbeRequest);

        var staged = request!.Filters.Single().Value;
        staged.Should().BeOfType<JsonElement>();

        FilterValueReader.ReadDecimal(staged, "Price").Should().Be(19.5m);
    }
}
