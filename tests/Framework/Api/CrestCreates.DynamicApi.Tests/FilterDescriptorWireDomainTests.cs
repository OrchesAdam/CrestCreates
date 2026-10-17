using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CrestCreates.Application.Contracts.DTOs.Common;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

/// <summary>
/// #128 S3: the finite filter wire domain of §8. The two baseline probes only prove that a real search
/// payload passes the normalizer; these cases pin down what is *inside* the domain (null, scalars,
/// same-kind scalar arrays, CLR compatibility, numeric precision through the staged representation) and
/// what must stay outside it as a deterministic rejection that the typed body reader turns into a 400.
/// </summary>
public class FilterDescriptorWireDomainTests
{
    private const string ScalarArrayPayload =
        """{"filters":[{"field":"Name","operator":2,"value":["alpha","beta"]}]}""";

    [Theory]
    [InlineData("""{"filters":[{"field":"Price","operator":2,"value":19.5}]}""")]
    [InlineData("""{"filters":[{"field":"Price","operator":2,"value":9223372036854775807}]}""")]
    [InlineData("""{"filters":[{"field":"Price","operator":2,"value":0.1234567890123456789012345678}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":1,"value":"alpha"}]}""")]
    [InlineData("""{"filters":[{"field":"IsDeleted","operator":1,"value":false}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":9,"value":null}]}""")]
    public void Bounded_scalar_values_must_keep_their_exact_wire_text(string payload)
    {
        var expectedRaw = JsonDocument.Parse(payload)
            .RootElement.GetProperty("filters")[0].GetProperty("value").GetRawText();

        var request = JsonSerializer.Deserialize(payload, FilterProbeJsonContext.Default.FilterProbeRequest);

        request.Should().NotBeNull();
        var staged = request!.Filters[0].Value;
        staged.Should().BeOfType<JsonElement>("the bounded wire staging is the constrained JsonElement");
        ((JsonElement)staged!).GetRawText().Should().Be(
            expectedRaw,
            "a number must not be re-typed or normalized on the way in");
    }

    [Fact]
    public void Same_kind_scalar_array_must_be_staged_without_type_guessing()
    {
        var request = JsonSerializer.Deserialize(
            ScalarArrayPayload, FilterProbeJsonContext.Default.FilterProbeRequest);

        var element = (JsonElement)request!.Filters[0].Value!;
        element.ValueKind.Should().Be(JsonValueKind.Array);
        element.EnumerateArray().Select(item => item.GetString()).Should().Equal("alpha", "beta");
    }

    [Theory]
    [InlineData("""{"filters":[{"field":"Name","operator":1,"value":{"nested":1}}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":1,"value":[[1,2]]}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":1,"value":[1,"alpha"]}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":1,"value":["alpha",null]}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":1,"value":[{"a":1}]}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":999,"value":"alpha"}]}""")]
    [InlineData("""{"filters":[{"field":"Name","operator":"NotAnOperator","value":"alpha"}]}""")]
    [InlineData("""{"filters":[{"field":42,"operator":1,"value":"alpha"}]}""")]
    [InlineData("""{"filters":["not-an-object"]}""")]
    public void Values_outside_the_finite_domain_must_be_rejected_at_the_wire(string payload)
    {
        // A JsonException here is exactly what the typed body reader converts into a 400 before any
        // business code runs, so this is the rejection contract of §8 rather than an implementation detail.
        var act = () => JsonSerializer.Deserialize(payload, FilterProbeJsonContext.Default.FilterProbeRequest);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Clr_callers_keep_the_existing_scalar_and_collection_surface()
    {
        Action[] accepted =
        [
            () => new FilterDescriptor("Name", FilterOperator.Equals, "alpha"),
            () => new FilterDescriptor("Price", FilterOperator.Equals, 19.5m),
            () => new FilterDescriptor("Count", FilterOperator.Equals, 42),
            () => new FilterDescriptor("Count", FilterOperator.Equals, 42L),
            () => new FilterDescriptor("Id", FilterOperator.Equals, Guid.NewGuid()),
            () => new FilterDescriptor("CreationTime", FilterOperator.Equals, DateTime.UtcNow),
            () => new FilterDescriptor("Duration", FilterOperator.Equals, TimeSpan.FromSeconds(1)),
            () => new FilterDescriptor("Flag", FilterOperator.Equals, true),
            () => new FilterDescriptor("Name", FilterOperator.In, new[] { "alpha", "beta" }),
            () => new FilterDescriptor("Count", FilterOperator.In, new[] { 1, 2, 3 }),
            () => new FilterDescriptor("Name", FilterOperator.Equals, null),
        ];

        foreach (var accept in accepted)
        {
            accept.Should().NotThrow("the CLR surface of FilterDescriptor is unchanged by the wire contract");
        }

        Action[] rejected =
        [
            () => new FilterDescriptor("Name", FilterOperator.Equals, new object()),
            () => new FilterDescriptor("Name", FilterOperator.Equals, new Dictionary<string, int> { ["a"] = 1 }),
            () => new FilterDescriptor("Name", FilterOperator.In, new[] { new { a = 1 } }),
            () => new FilterDescriptor("Name", FilterOperator.Equals, JsonDocument.Parse("""{"a":1}""").RootElement),
            () => new FilterDescriptor("Name", FilterOperator.In, JsonDocument.Parse("""[1,"a"]""").RootElement),
        ];

        foreach (var reject in rejected)
        {
            reject.Should().Throw<ArgumentException>(
                "nothing outside the finite value domain may be staged on the descriptor");
        }
    }

    [Fact]
    public void Staged_values_must_round_trip_back_to_the_bounded_wire()
    {
        var request = JsonSerializer.Deserialize(
            ScalarArrayPayload, FilterProbeJsonContext.Default.FilterProbeRequest);

        var json = JsonSerializer.Serialize(request, FilterProbeJsonContext.Default.FilterProbeRequest);

        json.Should().Contain("\"field\":\"Name\"");
        json.Should().Contain("\"operator\":2");
        json.Should().Contain("\"value\":[\"alpha\",\"beta\"]");
    }

    [Fact]
    public void Clr_values_must_round_trip_without_the_wire_staging()
    {
        var request = new FilterProbeRequest
        {
            Filters = [new FilterDescriptor("Price", FilterOperator.Equals, 19.5m)],
        };

        var json = JsonSerializer.Serialize(request, FilterProbeJsonContext.Default.FilterProbeRequest);

        json.Should().Contain("\"value\":19.5");
    }

    [Fact]
    public void Unknown_members_must_not_break_a_known_payload()
    {
        const string payload =
            """{"filters":[{"field":"Name","operator":1,"value":"alpha","future":true}]}""";

        var request = JsonSerializer.Deserialize(payload, FilterProbeJsonContext.Default.FilterProbeRequest);

        request!.Filters.Should().ContainSingle();
        request.Filters[0].Field.Should().Be("Name");
    }
}
