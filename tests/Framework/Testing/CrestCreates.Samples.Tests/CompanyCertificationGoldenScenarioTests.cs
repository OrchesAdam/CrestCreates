using System.Linq;
using System.Threading.Tasks;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Samples.DescriptorControlPlane;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
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
        _host = CompanyCertificationGoldenScenarioHost.CreateInMemory();
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
        var allRecords = await store.GetAllAsync();
        (await store.CountAsync()).Should().Be(1);
        allRecords[0].Status.Should().Be(CertificationStatus.Approved);
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
        var wf = await wfStore.GetAsync(new RuntimeInstanceKey(null, report.WorkflowInstanceId!));
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

    [Fact]
    public void GoldenScenarioHost_Should_Build_RuntimeRegistries_From_ExplicitInventory()
    {
        var inventory = CompanyCertificationDescriptorCloner.CopyAllDescriptors()
            .Where(d => d.Id != "ht_review_company_certification")
            .ToList();

        var original = (HumanTaskDescriptor)CompanyCertificationDescriptorCloner
            .CopyDescriptor(CompanyCertificationDescriptors.ReviewCompanyCertification);

        var financeTask = new HumanTaskDescriptor
        {
            Id = "ht_finance_review_company_certification",
            Name = "humantask.FinanceReviewCompanyCertification",
            Version = original.Version,
            State = original.State,
            SupersededById = original.SupersededById,
            Interaction = original.Interaction,
            InputSchema = original.InputSchema,
            OutputSchema = original.OutputSchema,
            AssigneeStrategy = original.AssigneeStrategy,
            Timeout = original.Timeout,
            Permissions = "CompanyCertification.FinanceReview",
            Outcomes = original.Outcomes
        };
        inventory.Add(financeTask);

        using var host = CompanyCertificationGoldenScenarioHost.CreateInMemory(inventory);
        using var scope = host.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IHumanTaskRegistry>();

        registry.GetById("ht_review_company_certification").Should().BeNull();
        registry.GetById("ht_finance_review_company_certification").Should().NotBeNull();
    }
}
