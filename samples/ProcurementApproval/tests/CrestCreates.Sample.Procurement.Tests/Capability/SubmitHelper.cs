using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

internal static class SubmitHelper
{
    public static async Task<SubmitProcurementRequestResult> SubmitAsync(
        ICapabilityPipeline pipeline,
        decimal amount = 500m)
    {
        throw new NotImplementedException();
    }
}
