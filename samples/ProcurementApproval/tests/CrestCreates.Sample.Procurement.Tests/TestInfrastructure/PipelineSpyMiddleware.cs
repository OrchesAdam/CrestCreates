using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

public sealed class PipelineSpyMiddleware : ICapabilityPipelineMiddleware
{
    public List<(InvocationSource Source, string CapabilityId)> Invocations { get; } = [];

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        Invocations.Add((context.InvocationSource, context.CapabilityId));
        return await next(context);
    }
}
