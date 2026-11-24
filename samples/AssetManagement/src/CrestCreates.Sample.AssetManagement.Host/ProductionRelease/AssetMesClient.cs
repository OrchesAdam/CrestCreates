using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts.Mes;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

public sealed class AssetMesClient(IHttpClientFactory clients, IConfiguration configuration)
{
    public const string ClientName = "asset-maintenance-mes";
    public const string AddressSetting = "AssetManagement:Mes:BaseAddress";
    private const string TenantCredentials = "AssetManagement:Mes:TenantCredentials";
    private const string ReleaseCredentials = "AssetManagement:Mes:ReleaseCredentials";

    public async Task<ProductionLockState> RegisterAsync(string tenantId, Guid equipmentId, string workOrderId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Put, tenantId, equipmentId, workOrderId);
        request.Content = JsonContent.Create(new RegisterProductionLockRequest(true), MesJsonContext.Default.RegisterProductionLockRequest);
        var state = await SendAsync(request, MesJsonContext.Default.ProductionLockState, cancellationToken);
        RequireScope(state.TenantId, state.EquipmentId, state.WorkOrderId, tenantId, equipmentId, workOrderId);
        if (!state.IsLocked)
        {
            throw new CapabilityFailureException("ASSET_MES_LOCK_ALREADY_RELEASED", "A new maintenance workflow requires a production lock.");
        }

        return state;
    }

    public async Task<ProductionLockState> GetLockAsync(string tenantId, Guid equipmentId, string workOrderId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, tenantId, equipmentId, workOrderId);
        var state = await SendAsync(request, MesJsonContext.Default.ProductionLockState, cancellationToken);
        RequireScope(state.TenantId, state.EquipmentId, state.WorkOrderId, tenantId, equipmentId, workOrderId);
        return state;
    }

    public async Task<TrialObservation> GetObservationAsync(string tenantId, Guid equipmentId, string workOrderId,
        Guid observationId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, tenantId, equipmentId, workOrderId, $"/observations/{observationId:D}");
        var observation = await SendAsync(request, MesJsonContext.Default.TrialObservation, cancellationToken);
        RequireScope(observation.TenantId, observation.EquipmentId, observation.WorkOrderId, tenantId, equipmentId, workOrderId);
        if (observation.ObservationId != observationId || observation.SourceEventId == Guid.Empty)
        {
            throw InvalidEvidence();
        }

        return observation;
    }

    public async Task<TrialObservation> ValidateReleaseEvidenceAsync(string tenantId, Guid equipmentId, string workOrderId,
        Guid observationId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, tenantId, equipmentId, workOrderId,
            $"/observations/{observationId:D}/release-validation", release: true);
        var observation = await SendAsync(request, MesJsonContext.Default.TrialObservation, cancellationToken);
        RequireScope(observation.TenantId, observation.EquipmentId, observation.WorkOrderId, tenantId, equipmentId, workOrderId);
        if (observation.ObservationId != observationId || observation.SourceEventId == Guid.Empty ||
            observation.Outcome != TrialOutcome.Passed)
        {
            throw InvalidEvidence();
        }

        return observation;
    }

    public async Task<ProductionReleaseReceipt> ReleaseAsync(string tenantId, Guid equipmentId, string workOrderId,
        ReleaseProductionLockRequest command, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, tenantId, equipmentId, workOrderId, "/release", release: true);
        request.Content = JsonContent.Create(command, MesJsonContext.Default.ReleaseProductionLockRequest);
        var receipt = await SendAsync(request, MesJsonContext.Default.ProductionReleaseReceipt, cancellationToken);
        var evidence = receipt.Evidence;
        RequireScope(evidence.TenantId, evidence.EquipmentId, evidence.WorkOrderId, tenantId, equipmentId, workOrderId);
        if (receipt.OperationId != command.OperationId || evidence.ObservationId != command.ObservationId ||
            evidence.SourceEventId != command.SourceEventId || evidence.Outcome != TrialOutcome.Passed)
        {
            throw InvalidEvidence();
        }

        return receipt;
    }

    public async Task<ProductionReleaseReceipt?> FindReleaseAsync(string tenantId, Guid equipmentId, string workOrderId,
        Guid operationId, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, tenantId, equipmentId, workOrderId, $"/releases/{operationId:D}", release: true);
        using var client = clients.CreateClient(ClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync(MesJsonContext.Default.ProductionReleaseReceipt, cancellationToken)
            ?? throw InvalidEvidence();
        var evidence = receipt.Evidence;
        RequireScope(evidence.TenantId, evidence.EquipmentId, evidence.WorkOrderId, tenantId, equipmentId, workOrderId);
        if (receipt.OperationId != operationId || evidence.Outcome != TrialOutcome.Passed)
        {
            throw InvalidEvidence();
        }

        return receipt;
    }

    private HttpRequestMessage Request(HttpMethod method, string tenantId, Guid equipmentId, string workOrderId, string suffix = "", bool release = false)
    {
        var credential = configuration.GetSection(release ? ReleaseCredentials : TenantCredentials).GetChildren()
            .SingleOrDefault(tenant => string.Equals(tenant.Key, tenantId, StringComparison.Ordinal))?.Value;
        if (string.IsNullOrWhiteSpace(credential))
        {
            throw new CapabilityFailureException("ASSET_MES_TENANT_NOT_CONFIGURED", release
                ? "The tenant has no MES release credential."
                : "The tenant has no MES service credential.");
        }

        var request = new HttpRequestMessage(method, $"api/mes/production-locks/{equipmentId:D}/{Uri.EscapeDataString(workOrderId)}{suffix}")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", credential) }
        };
        if (method == HttpMethod.Post)
        {
            request.Options.Set(new HttpRequestOptionsKey<bool>("AssetMesReleaseAttempt"), true);
        }

        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> responseType, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(ClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException &&
            request.Options.TryGetValue(new HttpRequestOptionsKey<bool>("AssetMesReleaseAttempt"), out var release) && release)
        {
            throw new CapabilityFailureException("ASSET_MES_RELEASE_UNCONFIRMED", "The MES release requires receipt reconciliation: " + exception.GetType().Name);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                throw InvalidEvidence();
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync(responseType, cancellationToken) ?? throw InvalidEvidence();
        }
    }

    private static void RequireScope(string observedTenantId, Guid observedEquipmentId, string observedWorkOrderId,
        string tenantId, Guid equipmentId, string workOrderId)
    {
        if (observedTenantId != tenantId || observedEquipmentId != equipmentId || observedWorkOrderId != workOrderId)
        {
            throw InvalidEvidence();
        }
    }

    private static CapabilityFailureException InvalidEvidence() =>
        new("ASSET_MES_EVIDENCE_REJECTED", "MES did not confirm evidence for the exact tenant, equipment and work order.");
}
