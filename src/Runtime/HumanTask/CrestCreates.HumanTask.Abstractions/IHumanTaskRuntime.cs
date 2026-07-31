using CrestCreates.Runtime.Persistence.Abstractions.Keys;

namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskRuntime
{
    Task<HumanTaskInstance> PrepareAsync(HumanTaskCreationRequest request, CancellationToken ct = default);

    Task<HumanTaskInstance> CreateAsync(HumanTaskCreationRequest request, CancellationToken ct = default);

    Task<HumanTaskInstance> CompleteAsync(HumanTaskCompletionRequest request, CancellationToken ct = default);

    Task<HumanTaskInstance> CancelAsync(RuntimeInstanceKey humanTaskKey, string reason, CancellationToken ct = default);
}
