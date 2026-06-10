using System.Collections.Frozen;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class DefaultStepExecutorRegistry : IWorkflowStepExecutorRegistry
{
    private readonly FrozenDictionary<Type, IWorkflowStepExecutor> _executors;

    public DefaultStepExecutorRegistry(
        CapabilityStepExecutor capabilityExecutor,
        HumanTaskStepExecutor humanTaskExecutor)
    {
        _executors = new Dictionary<Type, IWorkflowStepExecutor>
        {
            [typeof(CapabilityTarget)] = capabilityExecutor,
            [typeof(HumanTaskTarget)] = humanTaskExecutor
        }.ToFrozenDictionary();
    }

    public IWorkflowStepExecutor Resolve(InteractionTarget target)
    {
        if (_executors.TryGetValue(target.GetType(), out var executor))
            return executor;

        throw new UnsupportedWorkflowTargetException(target.GetType());
    }
}
