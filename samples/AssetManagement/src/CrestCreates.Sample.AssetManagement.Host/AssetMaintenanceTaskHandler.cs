using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.Host;

internal sealed class AssetMaintenanceTaskHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) =>
        throw new InvalidOperationException("A capability context is required.");

    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        if (context.Input is not MaintenanceTaskCompletionInput input || context.TenantId is not { Length: > 0 } tenantId)
            throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A typed maintenance completion and tenant are required.");

        var assets = context.ServiceProvider.GetRequiredService<AssetApplicationService>();
        var asset = await assets.GetAsync(input.AssetId, tenantId, ct)
            ?? throw new CapabilityFailureException("CAPABILITY_RESOURCE_NOT_FOUND", "The maintenance asset is unavailable.");
        await context.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>()
            .CompleteAsync(input.AssetId, input.HumanTaskId, input.Outcome, input.Note, ct);
        asset = await assets.GetAsync(input.AssetId, tenantId, ct)
            ?? throw new CapabilityFailureException("CAPABILITY_RESOURCE_NOT_FOUND", "The completed maintenance asset is unavailable.");
        return new AssetOperationResult
        {
            AssetId = asset.Id,
            Status = asset.Status.ToString(),
            HumanTaskId = input.HumanTaskId
        };
    }
}
