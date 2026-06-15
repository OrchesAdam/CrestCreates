using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class CompanyCertificationGoldenScenarioRunner
{
    private readonly CompanyCertificationGoldenScenarioHost _host;

    public CompanyCertificationGoldenScenarioRunner(CompanyCertificationGoldenScenarioHost host)
    {
        _host = host;
    }

    public async Task<CompanyCertificationGoldenScenarioReport> RunAsync(
        CompanyCertificationChangeScenario scenario, bool allowReviewRequired = false)
    {
        using var scope = _host.CreateScope();
        var sp = scope.ServiceProvider;

        var cpRunner = sp.GetRequiredService<CompanyCertificationControlPlaneRunner>();
        var cpReport = cpRunner.Run(scenario);

        var report = new CompanyCertificationGoldenScenarioReport
        {
            ScenarioName = scenario.Name,
            ControlPlanePassed = cpReport.ControlPlanePassed,
            GovernanceDecision = cpReport.GovernanceDecision.ToString(),
        };

        if (!cpReport.ControlPlanePassed)
        {
            report = report with
            {
                RuntimeBlockedByGovernance = true,
                ErrorMessage = $"Control plane blocked: {cpReport.GovernanceDecision}",
            };
            return report;
        }

        if (cpReport.GovernanceDecision ==
            CrestCreates.Metadata.Abstractions.DescriptorLifecycle.DescriptorLifecycleDecisionKind.ReviewRequired
            && !allowReviewRequired)
        {
            report = report with
            {
                RuntimeBlockedByGovernance = true,
                ErrorMessage = "Governance requires review; set allowReviewRequired to proceed",
            };
            return report;
        }

        try
        {
            var engine = sp.GetRequiredService<IWorkflowEngine>();
            var htRuntime = sp.GetRequiredService<IHumanTaskRuntime>();
            var htStore = sp.GetRequiredService<IHumanTaskInstanceStore>();
            var wfStore = sp.GetRequiredService<IWorkflowInstanceStore>();

            var submitInput = new CertificationSubmitInput(
                "Acme Corp",
                "91110000123456789X",
                "BusinessLicense",
                "2026-06-15",
                "Initial certification application");

            var instance = await engine.ExecuteAsync(
                "wf_company_certification",
                new Dictionary<string, object?>
                {
                    [nameof(CertificationSubmitInput)] = submitInput,
                });

            report = report with
            {
                WorkflowInstanceId = instance.InstanceId,
            };

            // Workflow should be suspended on the human task step
            if (instance.Status == WorkflowInstanceStatus.Suspended
                && instance.WaitingHumanTaskId is not null)
            {
                report = report with
                {
                    HumanTaskInstanceId = instance.WaitingHumanTaskId,
                };

                // Complete the human task with Approve outcome
                await htRuntime.CompleteAsync(new HumanTaskCompletionRequest
                {
                    HumanTaskInstanceId = instance.WaitingHumanTaskId,
                    Outcome = "Approve",
                    Result = new CertificationReviewInput(
                        CertificationId: null,
                        ReviewerNotes: "All documents verified",
                        Decision: "Approve"),
                });
            }

            // Wait for workflow to complete (continuation is event-driven)
            var maxAttempts = 20;
            for (var i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(100);
                var wf = await wfStore.GetAsync(instance.InstanceId);
                if (wf?.Status is WorkflowInstanceStatus.Completed
                    or WorkflowInstanceStatus.Failed
                    or WorkflowInstanceStatus.Compensated)
                {
                    report = report with
                    {
                        WorkflowStatus = wf.Status.ToString(),
                        RuntimeExecuted = wf.Status == WorkflowInstanceStatus.Completed,
                        ErrorMessage = wf.Status != WorkflowInstanceStatus.Completed
                            ? wf.ErrorMessage : null,
                    };

                    if (wf.Status == WorkflowInstanceStatus.Completed)
                    {
                        var store = _host.Store;
                        report = report with
                        {
                            HumanTaskStatus = "Approved",
                            SubmittedEventCaptured = store.Count > 0,
                            ApprovedEventCaptured = store.Get(store.GetAll().FirstOrDefault()?.Id
                                ?? Guid.Empty)?.Status == CertificationStatus.Approved,
                        };
                    }
                    return report;
                }
            }

            report = report with
            {
                WorkflowStatus = "Timeout",
                ErrorMessage = "Workflow did not complete within expected time",
            };
        }
        catch (Exception ex)
        {
            report = report with
            {
                ErrorMessage = ex.Message,
                RuntimeExecuted = false,
            };
        }

        return report;
    }
}
