namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskAssigneeResolver
{
    Task<HumanTaskAssigneeResolution> ResolveAsync(
        HumanTaskDescriptor descriptor,
        HumanTaskCreationRequest request,
        CancellationToken cancellationToken = default);
}
