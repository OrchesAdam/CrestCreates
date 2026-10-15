using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Application.Contracts.DTOs.Common;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DynamicApi.Tests;

internal sealed class FilterProbeRequest
{
    public List<FilterDescriptor> Filters { get; set; } = new();
}

[JsonSerializable(typeof(FilterProbeRequest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class FilterProbeJsonContext : JsonSerializerContext
{
}

/// <summary>
/// #128 S1 baselines for the bounded search/filter wire contract: scalar and same-type scalar-array
/// values must pass the finite wire normalizer and resolve to a bounded representation (CLR scalar /
/// CLR array, or the constrained JsonElement staging explicitly allowed into
/// <see cref="FilterDescriptor.Value"/>). Today the setter rejects the JsonElement that STJ restores
/// into the object-typed member, so a real search payload fails deserialization outright.
/// </summary>
public class CrudQueryFilterWireBaselineTests
{
    [Fact]
    public void Scalar_array_filter_values_must_pass_the_finite_wire_normalizer()
    {
        const string payload =
            """{"filters":[{"field":"Name","operator":5,"value":["alpha","beta"]}]}""";

        var request = JsonSerializer.Deserialize(payload, FilterProbeJsonContext.Default.FilterProbeRequest);

        request.Should().NotBeNull();
        request!.Filters.Should().ContainSingle();
        AssertBoundedRepresentation(request.Filters[0].Value);
    }

    [Fact]
    public void Scalar_filter_values_must_pass_the_finite_wire_normalizer()
    {
        const string payload = """{"filters":[{"field":"Price","operator":2,"value":19.5}]}""";

        var request = JsonSerializer.Deserialize(payload, FilterProbeJsonContext.Default.FilterProbeRequest);

        request.Should().NotBeNull();
        request!.Filters.Should().ContainSingle();
        AssertBoundedRepresentation(request.Filters[0].Value);
    }

    private static void AssertBoundedRepresentation(object? value)
    {
        value.Should().NotBeNull("the finite wire normalizer must materialize the filter value");
        if (value is JsonElement element)
        {
            element.ValueKind.Should().BeOneOf(
                new[] { JsonValueKind.Array, JsonValueKind.String, JsonValueKind.Number },
                "only bounded scalar/array representations may be staged on the descriptor");
            return;
        }

        (value is string or bool or int or long or double or decimal or System.Guid or System.DateTime
            or IEnumerable<string> or IEnumerable<int> or IEnumerable<long> or IEnumerable<System.Guid>)
            .Should().BeTrue($"the materialized value must stay in the finite scalar domain, observed '{value.GetType()}'");
    }
}
