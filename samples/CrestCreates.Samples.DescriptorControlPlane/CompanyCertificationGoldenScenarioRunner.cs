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
            ActivatedWorkflowDescriptorId = scenario.After.OfType<WorkflowDescriptor>().FirstOrDefault()?.Id,
            ActivatedWorkflowVersion = scenario.After.OfType<WorkflowDescriptor>().FirstOrDefault()?.Version,
            ActivatedHumanTaskDescriptorIds = scenario.After.OfType<HumanTaskDescriptor>().Select(h => h.Id).ToList().AsReadOnly(),
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
            var wfRegistry = sp.GetRequiredService<IWorkflowRegistry>();

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

            // Extract workflow step sequence from registry
            var wfDesc = wfRegistry.GetById("wf_company_certification");
            if (wfDesc is not null)
            {
                report = report with
                {
                    WorkflowStepSequence = wfDesc.Steps.Select(s => s.Id).ToList().AsReadOnly(),
                };
            }

            // Multi-step HumanTask completion loop
            var observedHumanTaskDescriptorIds = new List<string>();
            var completedCount = 0;
            string? initialReviewHtId = null;
            string? financeReviewHtId = null;

            var maxTotalAttempts = 60;
            for (var totalAttempt = 0; totalAttempt < maxTotalAttempts; totalAttempt++)
            {
                var wf = await wfStore.GetAsync(instance.InstanceId);
                if (wf is null) break;

                // Terminal state?
                if (wf.Status is WorkflowInstanceStatus.Completed
                    or WorkflowInstanceStatus.Failed
                    or WorkflowInstanceStatus.Compensated)
                {
                    report = report with
                    {
                        WorkflowStatus = wf.Status.ToString(),
                        RuntimeExecuted = wf.Status == WorkflowInstanceStatus.Completed,
                        ErrorMessage = wf.Status != WorkflowInstanceStatus.Completed
                            ? wf.ErrorMessage : null,
                        ObservedHumanTaskDescriptorIds = observedHumanTaskDescriptorIds,
                        CompletedHumanTaskCount = completedCount,
                        InitialReviewHumanTaskInstanceId = initialReviewHtId,
                        FinanceReviewHumanTaskInstanceId = financeReviewHtId,
                        HumanTaskInstanceId = initialReviewHtId,
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

                // Suspended on a HumanTask?
                if (wf.Status == WorkflowInstanceStatus.Suspended
                    && wf.WaitingHumanTaskId is not null)
                {
                    var htInstance = await htStore.GetByIdAsync(wf.WaitingHumanTaskId);
                    var humanTaskId = htInstance?.HumanTaskId;

                    if (humanTaskId is not null)
                        observedHumanTaskDescriptorIds.Add(humanTaskId);

                    // Track specific HumanTask instances
                    if (initialReviewHtId is null)
                        initialReviewHtId = wf.WaitingHumanTaskId;
                    else if (financeReviewHtId is null)
                        financeReviewHtId = wf.WaitingHumanTaskId;

                    // Complete with Approve
                    await htRuntime.CompleteAsync(new HumanTaskCompletionRequest
                    {
                        HumanTaskInstanceId = wf.WaitingHumanTaskId,
                        Outcome = "Approve",
                        Result = new CertificationReviewInput(
                            CertificationId: null,
                            ReviewerNotes: "All documents verified",
                            Decision: "Approve"),
                    });
                    completedCount++;
                }

                await Task.Delay(100);
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
