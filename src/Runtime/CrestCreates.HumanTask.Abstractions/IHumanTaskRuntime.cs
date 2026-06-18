namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskRuntime
{
    Task<HumanTaskInstance> CreateAsync(HumanTaskCreationRequest request, CancellationToken ct = default);

    Task<HumanTaskInstance> CompleteAsync(HumanTaskCompletionRequest request, CancellationToken ct = default);

    Task<HumanTaskInstance> CancelAsync(string instanceId, string reason, CancellationToken ct = default);
}
