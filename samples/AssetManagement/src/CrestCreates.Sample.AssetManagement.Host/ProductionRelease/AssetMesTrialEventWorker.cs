using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrestCreates.Sample.AssetManagement.Contracts.Mes;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

internal sealed class AssetMesTrialEventWorker(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IServiceScopeFactory scopes,
    ILogger<AssetMesTrialEventWorker> logger,
    string approvedTenantId) : BackgroundService
{
    private const string TenantCredentialsSetting = "AssetManagement:Mes:TenantCredentials";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tenants = configuration.GetSection(TenantCredentialsSetting).GetChildren()
            .Where(tenant => string.Equals(tenant.Key, approvedTenantId, StringComparison.Ordinal)).ToArray();
        var cursors = new Dictionary<string, Guid>(StringComparer.Ordinal);
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            foreach (var tenant in tenants)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var credential = tenant.Value;
                if (string.IsNullOrWhiteSpace(credential))
                    continue;

                var after = cursors.GetValueOrDefault(tenant.Key);
                try
                {
                    var next = await PollTenantAsync(tenant.Key, credential, after, stoppingToken);
                    if (next == Guid.Empty)
                        cursors.Remove(tenant.Key);
                    else
                        cursors[tenant.Key] = next;
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Unable to read MES trial events for tenant {TenantId}", tenant.Key);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<Guid> PollTenantAsync(string tenantId, string credential, Guid after, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(AssetMesClient.ClientName);
        var authorization = new AuthenticationHeaderValue("Bearer", credential);
        var path = after == Guid.Empty ? MesTrialEventFeed.Path : $"{MesTrialEventFeed.Path}?after={after:D}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Authorization = authorization } };
        using var response = await client.SendAsync(request, cancellationToken);
        if (after != Guid.Empty && response.StatusCode == HttpStatusCode.NotFound)
            return Guid.Empty;

        response.EnsureSuccessStatusCode();
        var observations = await response.Content.ReadFromJsonAsync(MesJsonContext.Default.TrialObservationArray, cancellationToken);
        if (observations is null || observations.Length > MesTrialEventFeed.DefaultBatchSize ||
            observations.Any(observation => observation is null ||
                !string.Equals(observation.TenantId, tenantId, StringComparison.Ordinal) ||
                observation.SourceEventId == Guid.Empty || observation.ObservationId == Guid.Empty ||
                observation.EquipmentId == Guid.Empty || string.IsNullOrWhiteSpace(observation.WorkOrderId) ||
                observation.Outcome is not (TrialOutcome.Passed or TrialOutcome.Failed) || observation.ObservedAt == default))
        {
            throw new InvalidDataException("MES returned an invalid or out-of-scope trial event batch.");
        }

        foreach (var observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool applied;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var workflows = scope.ServiceProvider.GetRequiredService<AssetMaintenanceWorkflowService>();
                    applied = await workflows.ApplyMesTrialEventAsync(observation, cancellationToken);
                }

                if (applied)
                {
                    using var ack = new HttpRequestMessage(HttpMethod.Put, $"{MesTrialEventFeed.Path}/{observation.SourceEventId:D}/ack")
                    {
                        Headers = { Authorization = authorization }
                    };
                    using var acknowledged = await client.SendAsync(ack, cancellationToken);
                    acknowledged.EnsureSuccessStatusCode();
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Unable to process MES trial event {SourceEventId} for tenant {TenantId}",
                    observation.SourceEventId, tenantId);
            }
        }

        // The cursor only advances this scan. Unacknowledged events reappear on the next scan.
        return observations.Length == MesTrialEventFeed.DefaultBatchSize ? observations[^1].SourceEventId : Guid.Empty;
    }
}
