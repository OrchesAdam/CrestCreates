namespace CrestCreates.HumanTask.Abstractions;

internal interface IHumanTaskCompletionObligationPreflight
{
    ValueTask ValidateAsync(
        IReadOnlyList<HumanTaskCompletionObligationPolicyRegistration> policies,
        IReadOnlySet<string> activeConsumerIds,
        CancellationToken cancellationToken = default);
}
