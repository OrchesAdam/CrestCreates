namespace CrestCreates.Runtime.Delivery.Abstractions.Handlers;

public sealed record OutboxRequiredConsumerResult(OutboxDeliveryOutcome Outcome, string? FailureCode = null, string? FailureMessage = null)
{
    public static OutboxRequiredConsumerResult Accepted() => new(OutboxDeliveryOutcome.Accepted);
    public static OutboxRequiredConsumerResult Duplicate() => new(OutboxDeliveryOutcome.Duplicate);
    public static OutboxRequiredConsumerResult Retry(string code, string message) => new(OutboxDeliveryOutcome.Retry, code, message);
    public static OutboxRequiredConsumerResult Conflict(string code, string message) => new(OutboxDeliveryOutcome.Conflict, code, message);
}
