using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;

namespace CrestCreates.HumanTask;

internal sealed class HumanTaskCompletionObligationCompositionCheck(
    IHumanTaskCompletionObligationPreflight? preflight,
    IEnumerable<HumanTaskCompletionObligationPolicyRegistration> policies,
    IEnumerable<OutboxRequiredConsumerMetadata> consumers) : IOutboxDurableCompositionCheck
{
    public string CheckId => "runtime-humantask-completion-obligations";

    public ValueTask ValidateAsync(CancellationToken cancellationToken)
    {
        var policyList = policies.ToArray();
        if (policyList.Length == 0 || preflight is null)
            return ValueTask.CompletedTask;
        return preflight.ValidateAsync(policyList, consumers.Select(consumer => consumer.ConsumerId).ToHashSet(StringComparer.Ordinal), cancellationToken);
    }
}
