using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Application.Handlers;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using CrestCreates.Sample.Procurement.Host;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Sample.Procurement.Tests.Acceptance;

public sealed class AuthoritativeDecisionPathAcceptanceTests
{
    [Fact]
    public async Task HttpApprove_CompletesHumanTaskAndWorkflow()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(
            client, "HTTP approval", 30_000m);
        var state = await PendingStateAsync(factory.Services, "tenant-a", submitted.RequestId);

        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "manager-a", "procurement-manager");
        using var content = JsonContent.Create(
            new ApproveProcurementRequestInput { Comment = "Approved through task" },
            ProcurementJsonContext.Default.ApproveProcurementRequestInput);
        using var response = await client.PostAsync(
            $"/api/procurement/requests/{submitted.RequestId}/approve",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await ProjectionCompositionAcceptanceTests.ReadAsync(
            response,
            ProcurementJsonContext.Default.ProcurementRequestResult);
        approved.Status.Should().Be("Approved");
        approved.ApproverId.Should().Be("manager-a");
        await AssertTerminalRuntimeAsync(factory.Services, state, "Approve");
        AssertOneAuthoritativeDispatch(factory.Services, ProcurementContractIds.ApplyApprovalDecisionCapability);
    }

    [Fact]
    public async Task HttpReject_CompletesHumanTaskAndWorkflow()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(
            client, "HTTP rejection", 30_000m);
        var state = await PendingStateAsync(factory.Services, "tenant-a", submitted.RequestId);

        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "manager-a", "procurement-manager");
        using var content = JsonContent.Create(
            new RejectProcurementRequestInput { Reason = "Rejected through task" },
            ProcurementJsonContext.Default.RejectProcurementRequestInput);
        using var response = await client.PostAsync(
            $"/api/procurement/requests/{submitted.RequestId}/reject",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await ProjectionCompositionAcceptanceTests.ReadAsync(
            response,
            ProcurementJsonContext.Default.ProcurementRequestResult);
        rejected.Status.Should().Be("Rejected");
        rejected.ApproverId.Should().Be("manager-a");
        await AssertTerminalRuntimeAsync(factory.Services, state, "Reject");
        AssertOneAuthoritativeDispatch(factory.Services, ProcurementContractIds.ApplyRejectionDecisionCapability);
    }

    [Fact]
    public async Task DirectDecision_IsForbidden_AndLeavesHumanTaskPending()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(
            client, "Direct decision denied", 30_000m);
        var state = await PendingStateAsync(factory.Services, "tenant-a", submitted.RequestId);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SampleExecutionIdentity>()
            .Set("tenant-a", "manager-a", "procurement-manager");
        var input = new ApproveProcurementRequestInput
        {
            RequestId = submitted.RequestId,
            Comment = "Bypass"
        };
        var result = await scope.ServiceProvider.GetRequiredService<ICapabilityDispatcher>()
            .DispatchAsync(
                ProcurementContractIds.ApplyApprovalDecisionCapability,
                InvocationSource.Http,
                input,
                context => context.InputJson = JsonSerializer.SerializeToElement(
                    input,
                    ProcurementJsonContext.Default.ApproveProcurementRequestInput));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CAPABILITY_INVOCATION_SOURCE_FORBIDDEN");
        factory.Services.GetRequiredService<InMemoryProcurementRequestStore>()
            .GetById("tenant-a", submitted.RequestId)!.Status.ToString()
            .Should().Be("PendingApproval");
        (await factory.Services.GetRequiredService<IHumanTaskInstanceStore>()
                .GetPendingByWorkflowAsync(state.WorkflowInstanceId))
            .Should().ContainSingle(task => task.Id == state.HumanTaskInstanceId);
    }

    [Fact]
    public async Task RepeatedHumanTaskCompletion_DoesNotApplySecondDecision()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(
            client, "Repeated completion", 30_000m);
        var state = await PendingStateAsync(factory.Services, "tenant-a", submitted.RequestId);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SampleExecutionIdentity>()
            .Set("tenant-a", "manager-a", "procurement-manager");
        var service = scope.ServiceProvider.GetRequiredService<ProcurementApprovalTaskService>();
        await service.CompleteAsync(state.HumanTaskInstanceId, "Approve", "First");
        var second = () => service.CompleteAsync(state.HumanTaskInstanceId, "Approve", "Second");

        await second.Should().ThrowAsync<InvalidOperationException>();
        AssertOneAuthoritativeDispatch(factory.Services, ProcurementContractIds.ApplyApprovalDecisionCapability);
    }

    private static async Task<PendingRuntimeState> PendingStateAsync(
        IServiceProvider services,
        string tenantId,
        Guid requestId)
    {
        var aggregate = services.GetRequiredService<InMemoryProcurementRequestStore>()
            .GetById(tenantId, requestId)!;
        aggregate.WorkflowInstanceId.Should().NotBeNullOrWhiteSpace();
        var workflow = await services.GetRequiredService<IWorkflowInstanceStore>()
            .GetAsync(aggregate.WorkflowInstanceId!);
        workflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        var tasks = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetPendingByWorkflowAsync(workflow.InstanceId);
        tasks.Should().ContainSingle();
        workflow.WaitingHumanTaskId.Should().Be(tasks[0].Id);
        return new PendingRuntimeState(workflow.InstanceId, tasks[0].Id);
    }

    private static async Task AssertTerminalRuntimeAsync(
        IServiceProvider services,
        PendingRuntimeState state,
        string outcome)
    {
        var workflow = await services.GetRequiredService<IWorkflowInstanceStore>()
            .GetAsync(state.WorkflowInstanceId);
        workflow!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        workflow.WaitingHumanTaskId.Should().BeNull();
        (await services.GetRequiredService<IHumanTaskInstanceStore>()
                .GetPendingByWorkflowAsync(state.WorkflowInstanceId))
            .Should().BeEmpty();
        var task = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetByIdAsync(state.HumanTaskInstanceId);
        task!.Status.Should().Be(HumanTaskInstanceStatus.Completed);
        task.Outcome.Should().Be(outcome);
    }

    private static void AssertOneAuthoritativeDispatch(IServiceProvider services, string capabilityId)
        => services.GetRequiredService<ICapabilityAuditStore>()
            .Should().BeOfType<InMemoryCapabilityAuditStore>()
            .Which.GetRecords()
            .Should().ContainSingle(record =>
                record.Source == InvocationSource.HumanTask
                && record.CapabilityId == capabilityId
                && record.IsSuccess);

    private sealed record PendingRuntimeState(
        string WorkflowInstanceId,
        string HumanTaskInstanceId);
}

public sealed class WorkflowAtomicityAcceptanceTests
{
    [Fact]
    public async Task HighValueSubmit_WhenWorkflowFails_ReturnsFailure_AndDoesNotPersistRequest()
    {
        using var factory = new ProcurementWebApplicationFactory(services =>
        {
            services.RemoveAll<IWorkflowEngine>();
            services.AddScoped<IWorkflowEngine, FailingWorkflowEngine>();
        });
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "requester-a", "procurement-requester");

        using var response = await SubmitRawAsync(client, "Workflow failure");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        factory.Services.GetRequiredService<InMemoryProcurementRequestStore>().Count.Should().Be(0);
        factory.Services.GetRequiredService<InMemoryWorkflowInstanceStore>().GetAll().Should().BeEmpty();
        factory.Services.GetRequiredService<InMemoryHumanTaskInstanceStore>().GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task HighValueSubmit_WhenHumanTaskCreationFails_HasNoOrphanWorkflow()
    {
        using var factory = new ProcurementWebApplicationFactory(services =>
        {
            services.RemoveAll<IHumanTaskRuntime>();
            services.AddScoped<IHumanTaskRuntime, FailingHumanTaskRuntime>();
        });
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "requester-a", "procurement-requester");

        using var response = await SubmitRawAsync(client, "HumanTask failure");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        factory.Services.GetRequiredService<InMemoryProcurementRequestStore>().Count.Should().Be(0);
        factory.Services.GetRequiredService<InMemoryWorkflowInstanceStore>().GetAll().Should().BeEmpty();
        factory.Services.GetRequiredService<InMemoryHumanTaskInstanceStore>().GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task HighValueSubmit_SucceedsOnlyWithOnePendingHumanTask()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "requester-a", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(
            client, "Valid workflow", 30_000m);
        var aggregate = factory.Services.GetRequiredService<InMemoryProcurementRequestStore>()
            .GetById("tenant-a", submitted.RequestId)!;
        var workflow = await factory.Services.GetRequiredService<IWorkflowInstanceStore>()
            .GetAsync(aggregate.WorkflowInstanceId!);
        var pending = await factory.Services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetPendingByWorkflowAsync(aggregate.WorkflowInstanceId!);

        workflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        pending.Should().ContainSingle();
        workflow.WaitingHumanTaskId.Should().Be(pending[0].Id);
    }

    private static Task<HttpResponseMessage> SubmitRawAsync(HttpClient client, string title)
        => client.PostAsJsonAsync("/api/procurement/requests", new SubmitProcurementRequestInput
        {
            Title = title,
            Description = "Atomicity",
            Amount = 30_000m,
            Currency = "USD",
            Category = "Infrastructure"
        }, ProcurementJsonContext.Default.SubmitProcurementRequestInput);

    private sealed class FailingWorkflowEngine : IWorkflowEngine
    {
        public Task<WorkflowInstance> ExecuteAsync(
            string workflowId,
            Dictionary<string, object?>? inputVariables = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Synthetic workflow failure.");
    }

    private sealed class FailingHumanTaskRuntime : IHumanTaskRuntime
    {
        public Task<HumanTaskInstance> CreateAsync(
            HumanTaskCreationRequest request,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Synthetic HumanTask creation failure.");

        public Task<HumanTaskInstance> CompleteAsync(
            HumanTaskCompletionRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<HumanTaskInstance> CancelAsync(
            string instanceId,
            string reason,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

public sealed class DecisionRecoveryAndAuditAcceptanceTests
{
    [Fact]
    public async Task HumanTaskDecisionFailure_HasDeterministicRecoverableState()
    {
        using var factory = new ProcurementWebApplicationFactory(services =>
        {
            services.RemoveAll<ICapabilityHandlerModule>();
            services.AddSingleton<ICapabilityHandlerModule>(new FailOnceDecisionModule());
        });
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var identity = services.GetRequiredService<SampleExecutionIdentity>();
        identity.Set("tenant-a", "requester-a", "procurement-requester");
        var submitted = await SubmitAsync(services, "Recoverable decision");
        var aggregate = services.GetRequiredService<InMemoryProcurementRequestStore>()
            .GetById("tenant-a", submitted.RequestId)!;
        var pending = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetPendingByWorkflowAsync(aggregate.WorkflowInstanceId!);
        pending.Should().ContainSingle();
        identity.Set("tenant-a", "manager-a", "procurement-manager");
        var approval = services.GetRequiredService<ProcurementApprovalTaskService>();

        var first = () => approval.CompleteAsync(pending[0].Id, "Approve", "Retry me");
        await first.Should().ThrowAsync<ProcurementDecisionDispatchException>();

        aggregate.Status.ToString().Should().Be("PendingApproval");
        var taskAfterFailure = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetByIdAsync(pending[0].Id);
        taskAfterFailure!.Status.Should().BeOneOf(
            HumanTaskInstanceStatus.Created,
            HumanTaskInstanceStatus.Assigned);
        var workflowAfterFailure = await services.GetRequiredService<IWorkflowInstanceStore>()
            .GetAsync(aggregate.WorkflowInstanceId!);
        workflowAfterFailure!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        var reconciliation = services.GetRequiredService<ProcurementDecisionReconciliationStore>()
            .Get(pending[0].Id);
        reconciliation.Should().NotBeNull();
        reconciliation!.IsResolved.Should().BeFalse();
        reconciliation.ObservedTaskStatus.Should().Be(HumanTaskInstanceStatus.Completed);
        reconciliation.ObservedWorkflowStatus.Should().Be(WorkflowInstanceStatus.Suspended);
        reconciliation.ErrorCode.Should().Be("CAPABILITY_TRANSIENT_DECISION_FAILURE");

        await approval.CompleteAsync(pending[0].Id, "Approve", "Retry me");

        aggregate.Status.ToString().Should().Be("Approved");
        (await services.GetRequiredService<IWorkflowInstanceStore>()
                .GetAsync(aggregate.WorkflowInstanceId!))!.Status
            .Should().Be(WorkflowInstanceStatus.Completed);
        services.GetRequiredService<ProcurementDecisionReconciliationStore>()
            .Get(pending[0].Id)!.IsResolved.Should().BeTrue();
    }

    [Fact]
    public async Task HumanTaskContinuationFailure_DoesNotReportSuccess_AndIsDetectable()
    {
        using var factory = new ProcurementWebApplicationFactory(services =>
        {
            services.RemoveAll<IWorkflowContinuationService>();
            services.AddScoped<IWorkflowContinuationService, FailingContinuationService>();
        });
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var identity = services.GetRequiredService<SampleExecutionIdentity>();
        identity.Set("tenant-a", "requester-a", "procurement-requester");
        var submitted = await SubmitAsync(services, "Continuation failure");
        var aggregate = services.GetRequiredService<InMemoryProcurementRequestStore>()
            .GetById("tenant-a", submitted.RequestId)!;
        var pending = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetPendingByWorkflowAsync(aggregate.WorkflowInstanceId!);
        identity.Set("tenant-a", "manager-a", "procurement-manager");

        var complete = () => services.GetRequiredService<ProcurementApprovalTaskService>()
            .CompleteAsync(pending[0].Id, "Approve", "Continuation fails");

        await complete.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Synthetic continuation failure.");
        var task = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetByIdAsync(pending[0].Id);
        task!.Status.Should().BeOneOf(
            HumanTaskInstanceStatus.Created,
            HumanTaskInstanceStatus.Assigned);
        var workflow = await services.GetRequiredService<IWorkflowInstanceStore>()
            .GetAsync(aggregate.WorkflowInstanceId!);
        workflow!.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        var reconciliation = services.GetRequiredService<ProcurementDecisionReconciliationStore>()
            .Get(pending[0].Id);
        reconciliation.Should().NotBeNull();
        reconciliation!.IsResolved.Should().BeFalse();
        reconciliation.ObservedTaskStatus.Should().Be(HumanTaskInstanceStatus.Completed);
        reconciliation.ObservedWorkflowStatus.Should().Be(WorkflowInstanceStatus.Suspended);
        reconciliation.ErrorCode.Should().Be("PROCUREMENT_DECISION_CONTINUATION_FAILED");
    }

    [Fact]
    public async Task NotFound_ResponseAndAudit_UseSameErrorCode()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "user-a", "procurement-requester");

        using var response = await client.GetAsync($"/api/procurement/requests/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Services.GetRequiredService<ICapabilityAuditStore>()
            .Should().BeOfType<InMemoryCapabilityAuditStore>()
            .Which.GetRecords().Should().ContainSingle(record =>
                record.CapabilityId == ProcurementContractIds.GetCapability
                && record.ErrorCode == "CAPABILITY_RESOURCE_NOT_FOUND");
    }

    [Fact]
    public async Task Forbidden_ResponseAndAudit_UseSameErrorCode()
    {
        using var factory = new ProcurementWebApplicationFactory();
        using var client = factory.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "same-user", "procurement-requester");
        var submitted = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(
            client, "Self approval audit", 30_000m);
        ProjectionCompositionAcceptanceTests.SetIdentity(
            client, "tenant-a", "same-user", "procurement-manager");
        using var content = JsonContent.Create(
            new ApproveProcurementRequestInput { Comment = "self" },
            ProcurementJsonContext.Default.ApproveProcurementRequestInput);

        using var response = await client.PostAsync(
            $"/api/procurement/requests/{submitted.RequestId}/approve",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Services.GetRequiredService<ICapabilityAuditStore>()
            .Should().BeOfType<InMemoryCapabilityAuditStore>()
            .Which.GetRecords().Should().Contain(record =>
                record.CapabilityId == ProcurementContractIds.ApproveCapability
                && record.ErrorCode == "CAPABILITY_FORBIDDEN");
    }

    [Fact]
    public void HumanTaskInteraction_ReferencesRegisteredFormDescriptor()
    {
        using var factory = new ProcurementWebApplicationFactory();
        _ = factory.CreateClient();

        factory.Services.GetRequiredService<IFormRegistry>()
            .GetByVersion(ProcurementContractIds.ApprovalForm, 1)
            .Should().NotBeNull();
        factory.Services.GetRequiredService<IDescriptorLookup>()
            .Exists(new DescriptorRef("form", ProcurementContractIds.ApprovalForm, 1))
            .Should().BeTrue();
    }

    private static async Task<SubmitProcurementRequestResult> SubmitAsync(
        IServiceProvider services,
        string title)
    {
        var input = new SubmitProcurementRequestInput
        {
            Title = title,
            Description = "Recovery",
            Amount = 30_000m,
            Currency = "USD",
            Category = "Infrastructure"
        };
        var result = await services.GetRequiredService<ICapabilityDispatcher>().DispatchAsync(
            ProcurementContractIds.SubmitCapability,
            InvocationSource.Http,
            input,
            context => context.InputJson = JsonSerializer.SerializeToElement(
                input,
                ProcurementJsonContext.Default.SubmitProcurementRequestInput));
        result.IsSuccess.Should().BeTrue();
        return result.Output.Should().BeOfType<SubmitProcurementRequestResult>().Subject;
    }

    private sealed class FailOnceDecisionModule : ICapabilityHandlerModule
    {
        public string Id => "procurement";

        public void Apply(CapabilityHandlerResolver resolver)
        {
            resolver.Register(ProcurementContractIds.SubmitCapability, new SubmitProcurementRequestHandler());
            resolver.Register(ProcurementContractIds.GetCapability, new GetProcurementRequestHandler());
            resolver.Register(ProcurementContractIds.ApproveCapability, new ApproveProcurementRequestHandler());
            resolver.Register(ProcurementContractIds.RejectCapability, new RejectProcurementRequestHandler());
            resolver.Register(
                ProcurementContractIds.ApplyApprovalDecisionCapability,
                new FailOnceDecisionHandler(new ApplyApprovalDecisionHandler()));
            resolver.Register(
                ProcurementContractIds.ApplyRejectionDecisionCapability,
                new ApplyRejectionDecisionHandler());
        }
    }

    private sealed class FailingContinuationService : IWorkflowContinuationService
    {
        public Task ContinueAsync(
            WorkflowContinuationRequest request,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Synthetic continuation failure.");
    }

    private sealed class FailOnceDecisionHandler(ICapabilityContextAwareHandlerInvoker inner)
        : ICapabilityContextAwareHandlerInvoker
    {
        private int _attempts;

        public Task<object?> InvokeAsync(object? input, CancellationToken ct)
            => throw new InvalidOperationException("Capability execution context is required.");

        public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new CapabilityFailureException(
                    "CAPABILITY_TRANSIENT_DECISION_FAILURE",
                    "Synthetic transient decision failure.");
            }
            return inner.InvokeAsync(context, ct);
        }
    }
}
