using CrestCreates.Metadata.Abstractions.Bootstrap;

namespace CrestCreates.Runtime.Persistence.InMemory.Bootstrap;

internal sealed class InMemoryRuntimeSchemaCompatibilityBootstrapTask : IBootstrapTask
{
    public string TaskId => "runtime-schema-compatibility";
    public Type ServiceType => typeof(InMemoryRuntimeSchemaCompatibilityBootstrapTask);
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();
    public bool IsRequired => true;
    public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct) => Task.CompletedTask;
}
