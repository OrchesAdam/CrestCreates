using System.Threading.Tasks;
using CrestCreates.Samples.DescriptorControlPlane;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Samples.Tests;

public sealed class CompanyCertificationGoldenScenarioTests : IAsyncLifetime
{
    private CompanyCertificationGoldenScenarioHost _host = null!;
    private CompanyCertificationGoldenScenarioRunner _runner = null!;

    public Task InitializeAsync()
    {
        _host = new CompanyCertificationGoldenScenarioHost();
        _runner = new CompanyCertificationGoldenScenarioRunner(_host);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GoldenScenario_Baseline_Should_Start_And_Run_ControlPlane()
    {
        var scenario = CompanyCertificationChangeScenarios.Baseline();

        var report = await _runner.RunAsync(scenario, allowReviewRequired: true);

        report.ErrorMessage.Should().BeNull("error: {0}", report.ErrorMessage);
        report.ControlPlanePassed.Should().BeTrue();
        report.RuntimeExecuted.Should().BeTrue();
        report.WorkflowStatus.Should().Be(nameof(WorkflowInstanceStatus.Completed));
        report.SubmittedEventCaptured.Should().BeTrue();
        report.ApprovedEventCaptured.Should().BeTrue();
        report.RuntimeBlockedByGovernance.Should().BeFalse();
    }

    [Fact]
    public async Task GoldenScenario_HappyPath_Should_Complete_CompanyCertificationWorkflow()
    {
        var scenario = CompanyCertificationChangeScenarios.Baseline();

        var report = await _runner.RunAsync(scenario, allowReviewRequired: true);

        report.RuntimeExecuted.Should().BeTrue();
        report.WorkflowStatus.Should().Be(nameof(WorkflowInstanceStatus.Completed));
        report.HumanTaskStatus.Should().Be("Approved");
        report.WorkflowInstanceId.Should().NotBeNullOrEmpty();
        report.HumanTaskInstanceId.Should().NotBeNullOrEmpty();

        var store = _host.Store;
        store.Count.Should().Be(1);
        store.GetAll()[0].Status.Should().Be(CertificationStatus.Approved);
    }

    [Fact]
    public async Task GoldenScenario_Approval_Should_Publish_CompanyCertificationApprovedEvent()
    {
        var scenario = CompanyCertificationChangeScenarios.Baseline();

        var report = await _runner.RunAsync(scenario, allowReviewRequired: true);

        report.ApprovedEventCaptured.Should().BeTrue();
        report.SubmittedEventCaptured.Should().BeTrue();

        using var scope = _host.CreateScope();
        var sp = scope.ServiceProvider;
        var wfStore = sp.GetRequiredService<IWorkflowInstanceStore>();
        var wf = await wfStore.GetAsync(report.WorkflowInstanceId!);
        wf.Should().NotBeNull();
        wf!.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }

    [Fact]
    public async Task GoldenScenario_BreakingSchemaChange_Should_Be_Detected_Before_RuntimeActivation()
    {
        var scenario = CompanyCertificationChangeScenarios.RequiredFieldRemoval();

        var report = await _runner.RunAsync(scenario);

        report.GovernanceDecision.Should().NotBe("Allowed",
            "breaking changes must require review or be blocked");
        report.RuntimeBlockedByGovernance.Should().BeTrue();
        report.RuntimeExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task GoldenScenario_MissingWorkflowTarget_Should_Block_RuntimeActivation()
    {
        var scenario = CompanyCertificationChangeScenarios.MissingWorkflowTarget();

        var report = await _runner.RunAsync(scenario);

        report.ControlPlanePassed.Should().BeFalse();
        report.RuntimeBlockedByGovernance.Should().BeTrue();
        report.RuntimeExecuted.Should().BeFalse();
    }
}
