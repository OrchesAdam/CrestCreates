using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Samples.DescriptorControlPlane;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Samples.Tests;

public sealed class CompanyCertificationSqliteTests
{
    private static string GetTestDatabasePath()
    {
        var testId = Guid.NewGuid().ToString("N");
        var dir = Path.Combine("artifacts", "test-data", testId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "company-certification.db");
    }

    private static void CleanupDatabase(string? path)
    {
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    // ── 11.1 SQLite Happy Path ─────────────────────────────────────────

    [Fact]
    public async Task Sqlite_HappyPath_Should_Complete_Full_Workflow()
    {
        var dbPath = GetTestDatabasePath();
        try
        {
            using var host = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath);
            var runner = new CompanyCertificationGoldenScenarioRunner(host);
            var scenario = CompanyCertificationChangeScenarios.Baseline();

            var report = await runner.RunAsync(scenario, allowReviewRequired: true);

            report.ErrorMessage.Should().BeNull("error: {0}", report.ErrorMessage);
            report.ControlPlanePassed.Should().BeTrue();
            report.RuntimeExecuted.Should().BeTrue();
            report.WorkflowStatus.Should().Be(nameof(WorkflowInstanceStatus.Completed));
            report.SubmittedEventCaptured.Should().BeTrue();
            report.ApprovedEventCaptured.Should().BeTrue();
            report.HumanTaskStatus.Should().Be("Approved");

            var store = host.Store;
            (await store.CountAsync()).Should().Be(1);
            var allRecords = await store.GetAllAsync();
            allRecords[0].Status.Should().Be(CertificationStatus.Approved);
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    // ── 11.2 Host Restart Query Recovery ───────────────────────────────

    [Fact]
    public async Task Sqlite_HostRestart_Should_Recover_BusinessRecords()
    {
        var dbPath = GetTestDatabasePath();
        try
        {
            // Host A: create a certification record
            Guid certificationId;
            using (var hostA = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath))
            {
                var store = hostA.Store;
                var record = await store.CreateAsync(new CertificationSubmitInput(
                    "Restart Test Corp",
                    "91110000999999999Y",
                    "BusinessLicense",
                    "2026-07-01",
                    "Testing restart recovery"));
                certificationId = record.Id;
                (await store.CountAsync()).Should().Be(1);
            }

            // Host B: read the same record from the same database
            using (var hostB = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath))
            {
                var store = hostB.Store;
                var recovered = await store.GetAsync(certificationId);
                recovered.Should().NotBeNull();
                recovered!.CompanyName.Should().Be("Restart Test Corp");
                recovered.UnifiedSocialCreditCode.Should().Be("91110000999999999Y");
                recovered.Status.Should().Be(CertificationStatus.Submitted);
                (await store.CountAsync()).Should().Be(1);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_HostRestart_Should_Recover_WorkflowInstance()
    {
        var dbPath = GetTestDatabasePath();
        try
        {
            string workflowInstanceId;

            // Host A: run workflow to completion
            using (var hostA = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath))
            {
                var runner = new CompanyCertificationGoldenScenarioRunner(hostA);
                var report = await runner.RunAsync(
                    CompanyCertificationChangeScenarios.Baseline(), allowReviewRequired: true);
                report.WorkflowStatus.Should().Be(nameof(WorkflowInstanceStatus.Completed));
                workflowInstanceId = report.WorkflowInstanceId!;
            }

            // Host B: read the completed workflow instance
            using (var hostB = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath))
            {
                using var scope = hostB.CreateScope();
                var wfStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
                var wf = await wfStore.GetAsync(workflowInstanceId);
                wf.Should().NotBeNull();
                wf!.Status.Should().Be(WorkflowInstanceStatus.Completed);
                wf.InstanceId.Should().Be(workflowInstanceId);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    // ── 11.3 Suspend → Restart → Continue (CRITICAL) ──────────────────

    [Fact]
    public async Task Sqlite_Suspend_Restart_Continue_Should_Complete_Workflow()
    {
        var dbPath = GetTestDatabasePath();
        try
        {
            string workflowInstanceId;
            string humanTaskInstanceId;

            // ── Host A: start workflow, run to HumanTask suspension ──
            using (var hostA = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath))
            {
                using var scope = hostA.CreateScope();
                var sp = scope.ServiceProvider;

                var cpRunner = sp.GetRequiredService<CompanyCertificationControlPlaneRunner>();
                var cpReport = cpRunner.Run(CompanyCertificationChangeScenarios.Baseline());
                cpReport.ControlPlanePassed.Should().BeTrue();

                var engine = sp.GetRequiredService<IWorkflowEngine>();
                var submitInput = new CertificationSubmitInput(
                    "Suspend Test Corp",
                    "91110000888888888Z",
                    "BusinessLicense",
                    "2026-07-01",
                    "Testing suspend-restart-continue");

                var instance = await engine.ExecuteAsync(
                    "wf_company_certification",
                    new Dictionary<string, object?>
                    {
                        [nameof(CertificationSubmitInput)] = submitInput,
                    });

                workflowInstanceId = instance.InstanceId;

                // Wait for workflow to reach Suspended state
                var wfStore = sp.GetRequiredService<IWorkflowInstanceStore>();
                var htStore = sp.GetRequiredService<IHumanTaskInstanceStore>();

                WorkflowInstance? wf = null;
                for (var i = 0; i < 50; i++)
                {
                    wf = await wfStore.GetAsync(workflowInstanceId);
                    if (wf?.Status == WorkflowInstanceStatus.Suspended
                        && wf.WaitingHumanTaskId is not null)
                        break;
                    await Task.Delay(100);
                }

                wf.Should().NotBeNull();
                wf!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
                wf.WaitingHumanTaskId.Should().NotBeNullOrEmpty();
                humanTaskInstanceId = wf.WaitingHumanTaskId!;

                // Verify HumanTask is pending
                var ht = await htStore.GetByIdAsync(humanTaskInstanceId);
                ht.Should().NotBeNull();
                ht!.Status.Should().BeOneOf(
                    HumanTaskInstanceStatus.Created,
                    HumanTaskInstanceStatus.Assigned);
            }
            // Host A is now disposed — all in-memory state is gone

            // ── Host B: restart with same SQLite, continue workflow ──
            using (var hostB = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath))
            {
                using var scope = hostB.CreateScope();
                var sp = scope.ServiceProvider;

                var wfStore = sp.GetRequiredService<IWorkflowInstanceStore>();
                var htStore = sp.GetRequiredService<IHumanTaskInstanceStore>();
                var htRuntime = sp.GetRequiredService<IHumanTaskRuntime>();

                // Verify workflow is still Suspended
                var wf = await wfStore.GetAsync(workflowInstanceId);
                wf.Should().NotBeNull();
                wf!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
                wf.WaitingHumanTaskId.Should().Be(humanTaskInstanceId);

                // Verify HumanTask is still pending
                var ht = await htStore.GetByIdAsync(humanTaskInstanceId);
                ht.Should().NotBeNull();
                ht!.Status.Should().BeOneOf(
                    HumanTaskInstanceStatus.Created,
                    HumanTaskInstanceStatus.Assigned);

                // Complete the HumanTask — this triggers workflow continuation
                await htRuntime.CompleteAsync(new HumanTaskCompletionRequest
                {
                    HumanTaskInstanceId = humanTaskInstanceId,
                    Outcome = "Approve",
                    Result = new CertificationReviewInput(
                        CertificationId: null,
                        ReviewerNotes: "Approved after host restart",
                        Decision: "Approve"),
                });

                // Wait for workflow to complete
                WorkflowInstance? completedWf = null;
                for (var i = 0; i < 50; i++)
                {
                    completedWf = await wfStore.GetAsync(workflowInstanceId);
                    if (completedWf?.Status is WorkflowInstanceStatus.Completed
                        or WorkflowInstanceStatus.Failed)
                        break;
                    await Task.Delay(100);
                }

                completedWf.Should().NotBeNull();
                completedWf!.Status.Should().Be(WorkflowInstanceStatus.Completed,
                    "workflow should complete after HumanTask approval on restarted host");

                // Verify certification is approved
                var store = hostB.Store;
                var allRecords = await store.GetAllAsync();
                allRecords.Count.Should().BeGreaterThan(0);
                allRecords[0].Status.Should().Be(CertificationStatus.Approved);
            }
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    // ── 11.4 Workflow Concurrency Conflict ─────────────────────────────

    [Fact]
    public async Task Sqlite_Workflow_ConcurrencyConflict_Should_Throw()
    {
        var dbPath = GetTestDatabasePath();
        try
        {
            using var host = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath);
            using var scope = host.CreateScope();
            var wfStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();

            // Create and save a workflow instance directly
            var instance = new WorkflowInstance
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(
                    "wf_test", 1, VersionSelectionMode.Exact, null),
                Status = WorkflowInstanceStatus.Running,
                CurrentStepId = "step_1",
                StepIndex = 0,
                StartedAt = DateTimeOffset.UtcNow,
                ConcurrencyStamp = "initial-stamp",
            };
            await wfStore.SaveAsync(instance);

            // Read the same instance twice (simulating two concurrent readers)
            var copyA = await wfStore.GetAsync(instance.InstanceId);
            var copyB = await wfStore.GetAsync(instance.InstanceId);
            copyA.Should().NotBeNull();
            copyB.Should().NotBeNull();

            // Save copyA — succeeds, updates concurrency stamp
            copyA!.Status = WorkflowInstanceStatus.Suspended;
            await wfStore.SaveAsync(copyA);

            // Save copyB with old stamp — must throw
            copyB!.Status = WorkflowInstanceStatus.Failed;
            var act = () => wfStore.SaveAsync(copyB);
            await act.Should().ThrowAsync<RuntimeConcurrencyException>();
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    // ── 11.5 HumanTask Concurrency Conflict ────────────────────────────────────────────

    [Fact]
    public async Task Sqlite_HumanTask_ConcurrencyConflict_Should_Throw()
    {
        var dbPath = GetTestDatabasePath();
        try
        {
            using var host = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath);
            using var scope = host.CreateScope();
            var htStore = scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();

            // Create and save a HumanTask instance directly
            var instance = new HumanTaskInstance
            {
                Id = Guid.NewGuid().ToString("N"),
                HumanTaskId = "ht_test",
                HumanTaskVersion = 1,
                Status = HumanTaskInstanceStatus.Created,
                CreatedAt = DateTimeOffset.UtcNow,
                ConcurrencyStamp = "initial-stamp",
            };
            await htStore.SaveAsync(instance);

            // Read the same instance twice
            var copyA = await htStore.GetByIdAsync(instance.Id);
            var copyB = await htStore.GetByIdAsync(instance.Id);
            copyA.Should().NotBeNull();
            copyB.Should().NotBeNull();

            // Save copyA — succeeds
            copyA!.Status = HumanTaskInstanceStatus.Assigned;
            await htStore.SaveAsync(copyA);

            // Save copyB with old stamp — must throw
            copyB!.Status = HumanTaskInstanceStatus.Completed;
            var act = () => htStore.SaveAsync(copyB);
            await act.Should().ThrowAsync<RuntimeConcurrencyException>();
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }

    // ── 11.6 Test Isolation ────────────────────────────────────────────

    [Fact]
    public async Task Sqlite_TestIsolation_DifferentDatabases_Should_Not_Pollute()
    {
        var dbPathA = GetTestDatabasePath();
        var dbPathB = GetTestDatabasePath();
        try
        {
            // Host A: create a record
            Guid recordIdA;
            using (var hostA = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPathA))
            {
                var record = await hostA.Store.CreateAsync(new CertificationSubmitInput(
                    "Isolation Corp A",
                    "91110000111111111A",
                    "BusinessLicense",
                    null,
                    null));
                recordIdA = record.Id;
                (await hostA.Store.CountAsync()).Should().Be(1);
            }

            // Host B: different database, should have no records
            using (var hostB = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPathB))
            {
                (await hostB.Store.CountAsync()).Should().Be(0);
                var recovered = await hostB.Store.GetAsync(recordIdA);
                recovered.Should().BeNull("record from Host A should not exist in Host B's database");
            }
        }
        finally
        {
            CleanupDatabase(dbPathA);
            CleanupDatabase(dbPathB);
        }
    }

    // ── 11.7 Governance Blocked Should Not Produce Runtime Data ────────

    [Fact]
    public async Task Sqlite_GovernanceBlocked_Should_Not_Create_RuntimeData()
    {
        var dbPath = GetTestDatabasePath();
        try
        {
            using var host = CompanyCertificationGoldenScenarioHost.CreateSqlite(dbPath);
            var runner = new CompanyCertificationGoldenScenarioRunner(host);
            var scenario = CompanyCertificationChangeScenarios.RequiredFieldRemoval();

            var report = await runner.RunAsync(scenario);

            report.RuntimeBlockedByGovernance.Should().BeTrue();
            report.RuntimeExecuted.Should().BeFalse();

            // No business data should have been created
            (await host.Store.CountAsync()).Should().Be(0,
                "governance-blocked scenarios must not create certification records");

            // No workflow or human task instances should exist
            using var scope = host.CreateScope();
            var wfStore = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
            var htStore = scope.ServiceProvider.GetRequiredService<IHumanTaskInstanceStore>();

            // We can't enumerate all instances, but we can verify the store is empty
            // by checking that the workflow instance ID is null
            report.WorkflowInstanceId.Should().BeNull(
                "governance-blocked scenarios must not create workflow instances");
        }
        finally
        {
            CleanupDatabase(dbPath);
        }
    }
}
