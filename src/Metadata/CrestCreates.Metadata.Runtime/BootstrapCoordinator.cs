using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Metadata;

public sealed class BootstrapCoordinator : IHostedService
{
    private readonly IEnumerable<IBootstrapTask> _tasks;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BootstrapCoordinator> _logger;

    public BootstrapCoordinator(
        IEnumerable<IBootstrapTask> tasks,
        IServiceProvider serviceProvider,
        ILogger<BootstrapCoordinator> logger)
    {
        _tasks = tasks;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var taskMap = _tasks.ToDictionary(t => t.TaskId);
        var sorted = TopologicalSort(taskMap);

        foreach (var task in sorted)
        {
            _logger.LogInformation("Bootstrapping {TaskId} ({TaskType})...", task.TaskId, task.ServiceType.Name);
            try
            {
                await task.ExecuteAsync(_serviceProvider, ct);
            }
            catch (Exception ex) when (!task.IsRequired)
            {
                _logger.LogWarning(ex, "Non-required bootstrap task {TaskId} failed, continuing", task.TaskId);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static IReadOnlyList<IBootstrapTask> TopologicalSort(Dictionary<string, IBootstrapTask> taskMap)
    {
        var result = new List<IBootstrapTask>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var path = new List<string>();

        foreach (var taskId in taskMap.Keys)
        {
            if (!visited.Contains(taskId))
                Visit(taskId, taskMap, visited, visiting, path, result);
        }

        return result;
    }

    private static void Visit(
        string taskId,
        Dictionary<string, IBootstrapTask> taskMap,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<string> path,
        List<IBootstrapTask> result)
    {
        if (visiting.Contains(taskId))
        {
            var cycleStart = path.IndexOf(taskId);
            var cycle = path.Skip(cycleStart).Concat([taskId]).ToList();
            throw new BootstrapDependencyException(cycle);
        }

        if (visited.Contains(taskId))
            return;

        visiting.Add(taskId);
        path.Add(taskId);

        if (taskMap.TryGetValue(taskId, out var task))
        {
            foreach (var dep in task.Dependencies)
            {
                if (taskMap.ContainsKey(dep))
                    Visit(dep, taskMap, visited, visiting, path, result);
            }
            result.Add(task);
        }

        visiting.Remove(taskId);
        path.RemoveAt(path.Count - 1);
        visited.Add(taskId);
    }
}
