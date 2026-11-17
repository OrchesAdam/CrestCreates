namespace CrestCreates.ModuleDiagnostics.Modules;

public sealed class ModuleInitializationState
{
    private readonly object _sync = new();
    private readonly Dictionary<(Type Module, Phase Phase), Task> _phases = new();

    public Task RunPreInitializationAsync(Type module, Func<Task> initialize)
        => RunOnceAsync(module, Phase.PreInitialization, initialize);

    public Task RunInitializationAsync(Type module, Func<Task> initialize)
        => RunOnceAsync(module, Phase.Initialization, initialize);

    public Task RunPostInitializationAsync(Type module, Func<Task> initialize)
        => RunOnceAsync(module, Phase.PostInitialization, initialize);

    public Task RunApplicationInitializationAsync(Type module, Func<Task> initialize)
        => RunOnceAsync(module, Phase.ApplicationInitialization, initialize);

    private Task RunOnceAsync(Type module, Phase phase, Func<Task> initialize)
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_phases.TryGetValue((module, phase), out var existing)) return existing;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _phases.Add((module, phase), completion.Task);
        }
        _ = CompleteAsync(initialize, completion);
        return completion.Task;
    }

    private static async Task CompleteAsync(Func<Task> initialize, TaskCompletionSource completion)
    {
        try
        {
            await initialize().ConfigureAwait(false);
            completion.SetResult();
        }
        catch (OperationCanceledException error)
        {
            completion.SetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.SetException(error);
        }
    }

    private enum Phase { PreInitialization, Initialization, PostInitialization, ApplicationInitialization }
}
