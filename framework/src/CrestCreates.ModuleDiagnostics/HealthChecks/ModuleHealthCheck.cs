using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.ModuleDiagnostics.Stores;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.ModuleDiagnostics.HealthChecks;

public class ModuleHealthCheck : IHealthCheck
{
    private readonly IModuleDiagnosticsStore _store;

    public ModuleHealthCheck(IModuleDiagnosticsStore store)
    {
        _store = store;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var allEntries = _store.GetAll();
        var failedEntries = _store.GetFailed();

        var data = new Dictionary<string, object>
        {
            { "totalPhases", allEntries.Count },
            { "failedPhases", failedEntries.Count }
        };

        if (failedEntries.Count > 0)
        {
            var failedDetails = new List<Dictionary<string, string>>();
            foreach (var entry in failedEntries)
            {
                failedDetails.Add(new Dictionary<string, string>
                {
                    { "module", entry.ModuleName },
                    { "phase", entry.Phase },
                    { "error", entry.ErrorMessage ?? "Unknown error" }
                });
            }
            data["failedDetails"] = failedDetails;

            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Modules: {failedEntries.Count} phase(s) failed",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Modules: all {allEntries.Count} phases healthy",
            data));
    }
}