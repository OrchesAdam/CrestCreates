using CrestCreates.Capability.Abstractions;
using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

internal static class SubmitHelper
{
    public static async Task<SubmitProcurementRequestResult> SubmitAsync(
        ICapabilityPipeline pipeline,
        decimal amount = 500m)
    {
        var input = new SubmitProcurementRequestInput
        {
            Title = "Test Request",
            Amount = amount,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var result = await pipeline.ExecuteAsync(
            "procurement.submit-request",
            input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Http,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return result.Output!;
    }
}
