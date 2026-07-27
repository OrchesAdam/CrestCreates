using System.Text.Json;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;

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
}
