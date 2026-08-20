using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.Testing.Contracts;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

internal sealed class PostgreSqlRuntimePersistenceContractDriver : IRuntimePersistenceContractDriver, IDisposable
{
    private readonly ServiceProvider _provider;

    public PostgreSqlRuntimePersistenceContractDriver(PostgreSqlRuntimePersistenceOptions options, string scopeId)
    {
        ScopeId = scopeId;
        _provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .BuildServiceProvider();
    }

    public string ScopeId { get; }
    public IRuntimeTransactionCoordinator Transactions => _provider.GetRequiredService<IRuntimeTransactionCoordinator>();
    public IWorkflowInstanceStore Workflows => _provider.GetRequiredService<IWorkflowInstanceStore>();
    public IHumanTaskInstanceStore HumanTasks => _provider.GetRequiredService<IHumanTaskInstanceStore>();
    public IDescriptorSnapshotStore Snapshots => _provider.GetRequiredService<IDescriptorSnapshotStore>();
    public ValueTask ResetAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public void Dispose() => _provider.Dispose();
}
