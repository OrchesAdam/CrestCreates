namespace CrestCreates.Workflow.Abstractions;

public sealed class UnsupportedWorkflowTargetException : Exception
{
    public Type TargetType { get; }

    public UnsupportedWorkflowTargetException(Type targetType)
        : base($"No executor registered for target type '{targetType.Name}'.")
    {
        TargetType = targetType;
    }
}
