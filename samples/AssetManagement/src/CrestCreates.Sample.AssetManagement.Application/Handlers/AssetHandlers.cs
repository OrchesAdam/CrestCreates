using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.Application.Handlers;

internal abstract class AssetHandlerBase
{
    protected static AssetApplicationService Service(CapabilityExecutionContext context)
        => context.ServiceProvider.GetRequiredService<AssetApplicationService>();

    protected static string Tenant(CapabilityExecutionContext context)
        => context.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");

    protected static string User(CapabilityExecutionContext context)
        => context.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed class RegisterAssetHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        => await Service(context).RegisterAsync((RegisterAssetInput)context.Input!, Tenant(context), User(context), ct);
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
    private static string User(CapabilityExecutionContext c) => c.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed class GetAssetHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        => await Service(context).GetAsync(((AssetQueryInput)context.Input!).AssetId ?? Guid.Empty, Tenant(context), ct);
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
}

public sealed class QueryAssetsHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        => await Service(context).QueryAsync((AssetQueryInput)context.Input!, Tenant(context), ct);
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
}

public sealed class UpdateAssetHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = (UpdateAssetInput)context.Input!;
        return await Service(context).UpdateAsync(input.AssetId, input, Tenant(context), User(context), ct);
    }
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
    private static string User(CapabilityExecutionContext c) => c.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed class AssignAssetHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = (AssignAssetInput)context.Input!;
        return await Service(context).AssignAsync(input.AssetId, input, Tenant(context), User(context), ct);
    }
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
    private static string User(CapabilityExecutionContext c) => c.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed class ReturnAssetHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        => await Service(context).ReturnAsync(((AssetIdInput)context.Input!).AssetId, Tenant(context), User(context), ct);
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
    private static string User(CapabilityExecutionContext c) => c.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed class TransferAssetHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = (TransferAssetInput)context.Input!;
        return await Service(context).TransferAsync(input.AssetId, input, Tenant(context), User(context), ct);
    }
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
    private static string User(CapabilityExecutionContext c) => c.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed class RequestMaintenanceHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = (MaintenanceRequestInput)context.Input!;
        return await Service(context).RequestMaintenanceAsync(input.AssetId, input, Tenant(context), User(context), ct);
    }
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
    private static string User(CapabilityExecutionContext c) => c.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed class ApplyMaintenanceDecisionHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) => throw new InvalidOperationException("A capability context is required.");
    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        if (context.InvocationSource != InvocationSource.HumanTask)
            throw new CapabilityFailureException("CAPABILITY_INVOCATION_SOURCE_FORBIDDEN", "Maintenance decisions require HumanTask completion.");
        var input = (MaintenanceDecisionCommand)context.Input!;
        return await Service(context).ApplyMaintenanceDecisionAsync(input.AssetId, input.Decision, Tenant(context), User(context), input.RequesterId, input.WorkflowInstanceId, ct);
    }
    private static AssetApplicationService Service(CapabilityExecutionContext c) => c.ServiceProvider.GetRequiredService<AssetApplicationService>();
    private static string Tenant(CapabilityExecutionContext c) => c.TenantId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A tenant context is required.");
    private static string User(CapabilityExecutionContext c) => c.UserId ?? throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A user context is required.");
}

public sealed record MaintenanceDecisionCommand(Guid AssetId, MaintenanceDecisionInput Decision, string WorkflowInstanceId, string RequesterId);
