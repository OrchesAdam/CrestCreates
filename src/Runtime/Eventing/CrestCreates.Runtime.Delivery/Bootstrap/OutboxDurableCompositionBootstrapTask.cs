using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Delivery.Bootstrap;

internal sealed class OutboxDurableCompositionBootstrapTask : IBootstrapTask
{
    public string TaskId => "runtime-delivery-durable-composition";
    public Type ServiceType => typeof(OutboxDurableCompositionBootstrapTask);
    public IReadOnlyList<string> Dependencies => ["runtime-schema-compatibility"];
    public bool IsRequired => true;

    public async Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct)
    {
        var checks = serviceProvider.GetServices<IOutboxDurableCompositionCheck>()
            .OrderBy(check => check.CheckId, StringComparer.Ordinal)
            .ToArray();
        var duplicate = checks.GroupBy(check => check.CheckId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null || checks.Any(check => string.IsNullOrWhiteSpace(check.CheckId)))
            throw new OutboxCompositionException("Outbox durable composition checks must have unique non-empty IDs.");

        foreach (var check in checks)
            await check.ValidateAsync(ct).ConfigureAwait(false);

        serviceProvider.GetRequiredService<OutboxCompositionReadiness>().Open();
    }
}
