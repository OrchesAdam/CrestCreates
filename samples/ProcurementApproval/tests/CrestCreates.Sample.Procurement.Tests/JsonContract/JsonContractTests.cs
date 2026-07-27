using System.Text.Json;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.Procurement.Tests.JsonContract;

public class JsonContractTests
{
    [Fact]
    public void Submit_request_input_round_trips_via_stj_source_generator()
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
    public void Submit_request_result_round_trips_via_stj_source_generator()
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

    [Fact]
    public void Approve_request_input_round_trips_via_stj_source_generator()
    {
        var input = new ApproveProcurementRequestInput
        {
            RequestId = Guid.NewGuid(),
            ApproverId = "approver-1",
            Comment = "Approved"
        };

        var json = JsonSerializer.Serialize(input, ProcurementJsonContext.Default.ApproveProcurementRequestInput);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.ApproveProcurementRequestInput);

        deserialized.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void Reject_request_input_round_trips_via_stj_source_generator()
    {
        var input = new RejectProcurementRequestInput
        {
            RequestId = Guid.NewGuid(),
            ApproverId = "approver-1",
            Reason = "Denied"
        };

        var json = JsonSerializer.Serialize(input, ProcurementJsonContext.Default.RejectProcurementRequestInput);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.RejectProcurementRequestInput);

        deserialized.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void Procurement_request_result_round_trips_via_stj_source_generator()
    {
        var result = new ProcurementRequestResult
        {
            RequestId = Guid.NewGuid(),
            Title = "Server Rack",
            Amount = 15000m,
            Currency = "USD",
            Status = "PendingApproval",
            RequesterId = "user-1",
            Category = "Infrastructure"
        };

        var json = JsonSerializer.Serialize(result, ProcurementJsonContext.Default.ProcurementRequestResult);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.ProcurementRequestResult);

        deserialized.Should().BeEquivalentTo(result);
    }

    [Fact]
    public void Get_request_input_round_trips_via_stj_source_generator()
    {
        var input = new GetProcurementRequestInput
        {
            RequestId = Guid.NewGuid()
        };

        var json = JsonSerializer.Serialize(input, ProcurementJsonContext.Default.GetProcurementRequestInput);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.GetProcurementRequestInput);

        deserialized.Should().BeEquivalentTo(input);
    }
}
