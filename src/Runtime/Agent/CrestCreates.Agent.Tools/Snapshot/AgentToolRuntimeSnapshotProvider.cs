using System.Runtime.ExceptionServices;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolRuntimeSnapshotProvider
{
    private readonly object _transitionLock = new();
    private AgentToolRuntimeSnapshot? _snapshot;
    private ExceptionDispatchInfo? _failure;

    public bool IsPublished => Volatile.Read(ref _snapshot) is not null;

    public bool IsFailed => Volatile.Read(ref _failure) is not null;

    public void Publish(AgentToolRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_transitionLock)
        {
            if (_failure is not null)
            {
                throw new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure,
                    "Agent Tool runtime snapshot build previously failed. Restart is required.");
            }

            if (_snapshot is not null)
            {
                throw new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure,
                    "Agent Tool runtime snapshot is already published.");
            }

            Volatile.Write(ref _snapshot, snapshot);
        }
    }

    public void MarkFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_transitionLock)
        {
            if (_snapshot is not null)
            {
                throw new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure,
                    "A published Agent Tool runtime snapshot cannot transition to Failed.");
            }

            if (_failure is null)
                Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(exception));
        }
    }

    public AgentToolRuntimeSnapshot GetRequired()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null)
            return snapshot;

        var failure = Volatile.Read(ref _failure);
        if (failure is not null)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure,
                "Agent Tool runtime snapshot build failed. Restart is required.",
                failure.SourceException);
        }

        throw new AgentToolConfigurationException(
            AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure,
            "Agent Tool runtime snapshot has not been published.");
    }
}
