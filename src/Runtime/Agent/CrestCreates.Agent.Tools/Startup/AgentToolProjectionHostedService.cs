using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Agent.Tools;

internal sealed class AgentToolProjectionHostedService : IHostedService
{
    private readonly AgentToolProjectionStartupBuilder _startup;
    private readonly IServiceProvider _services;

    public AgentToolProjectionHostedService(
        AgentToolProjectionStartupBuilder startup,
        IServiceProvider services)
    {
        _startup = startup;
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _startup.BuildAndPublish();
        if (snapshot.Entries.Count == 0)
            return Task.CompletedTask;

        Require<IAgentToolInvocationGate>(AgentToolStartupDiagnosticCodes.MissingInvocationGate);
        Require<IAgentToolInvocationLeaseAbandoner>(AgentToolStartupDiagnosticCodes.MissingInvocationLeaseAbandoner);
        Require<IAgentToolApprovalGate>(AgentToolStartupDiagnosticCodes.MissingApprovalGate);
        Require<IAgentToolBudgetGate>(AgentToolStartupDiagnosticCodes.MissingBudgetGate);
        Require<IAgentToolGovernanceAuditor>(AgentToolStartupDiagnosticCodes.MissingGovernanceAuditor);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Require<T>(string diagnosticCode) where T : class
    {
        if (_services.GetService<T>() is null)
        {
            throw new AgentToolConfigurationException(
                diagnosticCode,
                $"Active Agent Tools require a configured {typeof(T).Name}.");
        }
    }
}
