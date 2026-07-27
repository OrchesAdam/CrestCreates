using System.Text.Json;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using CrestCreates.Sample.Procurement.Host.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.Procurement.AotFixture.Tests;

public class ProcurementAotFixtureTests
{
    [Fact]
    public void JsonTypeInfo_ResolvesSubmitRequestInput()
    {
        var typeInfo = ProcurementJsonContext.Default.SubmitProcurementRequestInput;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for SubmitProcurementRequestInput");
    }

    [Fact]
    public void JsonTypeInfo_ResolvesSubmitRequestResult()
    {
        var typeInfo = ProcurementJsonContext.Default.SubmitProcurementRequestResult;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for SubmitProcurementRequestResult");
    }

    [Fact]
    public void JsonTypeInfo_ResolvesProcurementRequestResult()
    {
        var typeInfo = ProcurementJsonContext.Default.ProcurementRequestResult;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for ProcurementRequestResult");
    }

    [Fact]
    public void JsonTypeInfo_ResolvesDynamicApiResponse()
    {
        var typeInfo = ProcurementHostJsonContext.Default.DynamicApiResponseObject;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for DynamicApiResponse<object>");
    }

    [Fact]
    public void SubmitRequestInput_RoundTripsViaSourceGenerator()
    {
        var input = new SubmitProcurementRequestInput
        {
            Title = "Office Supplies",
            Amount = 500m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var json = JsonSerializer.Serialize(input, ProcurementJsonContext.Default.SubmitProcurementRequestInput);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.SubmitProcurementRequestInput);

        deserialized.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void SubmitRequestResult_RoundTripsViaSourceGenerator()
    {
        var result = new SubmitProcurementRequestResult
        {
            RequestId = Guid.NewGuid(),
            Status = "PendingApproval",
            Amount = 15000m,
            Currency = "USD",
            RequiresApproval = true
        };

        var json = JsonSerializer.Serialize(result, ProcurementJsonContext.Default.SubmitProcurementRequestResult);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.SubmitProcurementRequestResult);

        deserialized.Should().BeEquivalentTo(result);
    }
}
