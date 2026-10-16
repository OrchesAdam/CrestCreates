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
