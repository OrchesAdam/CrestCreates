namespace CrestCreates.HumanTask.Abstractions;

public sealed record HumanTaskCompletionObligationPolicyRegistration(
    string HumanTaskDescriptorId,
    int HumanTaskDescriptorVersion,
    string RequiredConsumerId);
