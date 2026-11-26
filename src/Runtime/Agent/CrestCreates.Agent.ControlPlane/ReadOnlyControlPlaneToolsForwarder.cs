using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

internal sealed class ReadOnlyControlPlaneToolsForwarder : IReadOnlyControlPlaneTools
{
    private readonly IAgentControlPlaneToolService _facade;

    public ReadOnlyControlPlaneToolsForwarder(IAgentControlPlaneToolService facade)
    {
        _facade = facade;
    }

    public Task<AgentToolResult<MetadataContextPack>> BuildMetadataContextPackAsync(
        AgentToolInvocationContext context,
        MetadataContextPackRequest request,
        CancellationToken ct = default) => _facade.BuildMetadataContextPackAsync(context, request, ct);

    public Task<AgentToolResult<MetadataContextPack>> BuildRuntimeScenarioContextPackAsync(
        AgentToolInvocationContext context,
        MetadataContextPackRequest request,
        CancellationToken ct = default) => _facade.BuildRuntimeScenarioContextPackAsync(context, request, ct);

    public Task<AgentToolResult<DescriptorInfo>> GetDescriptorByRefAsync(
        AgentToolInvocationContext context,
        DescriptorRef descriptorRef,
        CancellationToken ct = default) => _facade.GetDescriptorByRefAsync(context, descriptorRef, ct);

    public Task<AgentToolResult<DescriptorSearchResult>> SearchDescriptorsAsync(
        AgentToolInvocationContext context,
        DescriptorSearchRequest request,
        CancellationToken ct = default) => _facade.SearchDescriptorsAsync(context, request, ct);

    public Task<AgentToolResult<DescriptorRelationshipsResult>> ListDescriptorRelationshipsAsync(
        AgentToolInvocationContext context,
        DescriptorRef descriptorRef,
        CancellationToken ct = default) => _facade.ListDescriptorRelationshipsAsync(context, descriptorRef, ct);

    public Task<AgentToolResult<TopologySummaryResult>> GetTopologySummaryAsync(
        AgentToolInvocationContext context,
        CancellationToken ct = default) => _facade.GetTopologySummaryAsync(context, ct);

}
