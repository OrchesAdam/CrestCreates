using CrestCreates.Accountability.Bootstrap;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowCompositionTests
{
    [Fact]
    public async Task WorkflowHostWithoutAccountabilityFailsDuringStartup()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine();
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "WorkflowAccountabilityCompositionValidator");

        var action = () => validator.StartAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WORKFLOW_ACCOUNTABILITY_FOUNDATION_MISSING*");
    }

    [Fact]
    public async Task WorkflowHostWithAccountabilityPassesStartupValidation()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine();
        services.AddAccountability();
        using var provider = services.BuildServiceProvider();
        var validator = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "WorkflowAccountabilityCompositionValidator");

        var action = () => validator.StartAsync(CancellationToken.None);

        await action.Should().NotThrowAsync();
    }
}
