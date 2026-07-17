using CrestCreates.Capability.Abstractions;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

public sealed class SubmitCompanyCertificationInvoker : ICapabilityContextAwareHandlerInvoker
{
    private readonly ICompanyCertificationStore _store;

    public SubmitCompanyCertificationInvoker(ICompanyCertificationStore store)
    {
        _store = store;
    }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct) =>
        throw new NotSupportedException("Use context-aware overload");

    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = CapabilityInputHelper.Extract<CertificationSubmitInput>(context);

        var record = await _store.CreateAsync(input, ct);

        CapabilityInputHelper.EmitDomainEvent(context, new CompanyCertificationSubmittedEvent
        {
            CertificationId = record.Id,
            CompanyName = record.CompanyName,
        });

        // Return a dictionary so CapabilityStepExecutor extracts CertificationId
        // into workflow variables for subsequent steps to find.
        return new Dictionary<string, object?>
        {
            ["CertificationId"] = record.Id,
            ["Result"] = new CertificationResult(
                CertificationId: record.Id.ToString(),
                Status: record.Status.ToString(),
                Message: $"Certification submitted for {record.CompanyName}"),
        };
    }
}

public sealed class ApproveCompanyCertificationInvoker : ICapabilityContextAwareHandlerInvoker
{
    private readonly ICompanyCertificationStore _store;

    public ApproveCompanyCertificationInvoker(ICompanyCertificationStore store)
    {
        _store = store;
    }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct) =>
        throw new NotSupportedException("Use context-aware overload");

    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = CapabilityInputHelper.Extract<CertificationReviewInput>(context);

        var certId = CapabilityInputHelper.ResolveCertificationId(context);

        var reviewerUserId = context.UserId ?? "system";
        await _store.ApproveAsync(certId, input, reviewerUserId, ct);

        CapabilityInputHelper.EmitDomainEvent(context, new CompanyCertificationApprovedEvent
        {
            CertificationId = certId,
            ApprovedBy = reviewerUserId,
        });

        var result = new CertificationResult(
            CertificationId: certId.ToString(),
            Status: CertificationStatus.Approved.ToString(),
            Message: "Certification approved");

        return result;
    }
}

public sealed class RejectCompanyCertificationInvoker : ICapabilityContextAwareHandlerInvoker
{
    private readonly ICompanyCertificationStore _store;

    public RejectCompanyCertificationInvoker(ICompanyCertificationStore store)
    {
        _store = store;
    }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct) =>
        throw new NotSupportedException("Use context-aware overload");

    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = CapabilityInputHelper.Extract<CertificationReviewInput>(context);

        var certId = CapabilityInputHelper.ResolveCertificationId(context);

        var reviewerUserId = context.UserId ?? "system";
        await _store.RejectAsync(certId, input, reviewerUserId, ct);

        CapabilityInputHelper.EmitDomainEvent(context, new CompanyCertificationRejectedEvent
        {
            CertificationId = certId,
            RejectedBy = reviewerUserId,
            Reason = input.ReviewerNotes ?? "No reason provided",
        });

        var result = new CertificationResult(
            CertificationId: certId.ToString(),
            Status: CertificationStatus.Rejected.ToString(),
            Message: "Certification rejected");

        return result;
    }
}

internal static class CapabilityInputHelper
{
    public static T Extract<T>(CapabilityExecutionContext context) where T : class
    {
        if (context.Input is T direct)
            return direct;
        if (context.Input is Dictionary<string, object?> vars)
        {
            // Check by type name (e.g., "CertificationReviewInput")
            if (vars.TryGetValue(typeof(T).Name, out var obj) && obj is T fromVars)
                return fromVars;
            // Check "lastStepResult" — WorkflowContinuationService stores HumanTask output here
            if (vars.TryGetValue("lastStepResult", out var stepResult) && stepResult is T fromStep)
                return fromStep;
        }
        throw new InvalidOperationException(
            $"Expected {typeof(T).Name}, got {context.Input?.GetType().Name ?? "null"}. " +
            "The workflow must pass typed input between steps via workflow variables.");
    }

    public static Guid ResolveCertificationId(CapabilityExecutionContext context)
    {
        if (TryGetVariable<Guid>(context, "CertificationId", out var g))
            return g;
        var inputKeys = context.Input is Dictionary<string, object?> vars
            ? string.Join(", ", vars.Keys) : "(not a dictionary)";
        throw new InvalidOperationException(
            "CertificationId not found in workflow/capability execution context. " +
            "The Submit step must store CertificationId as a workflow variable. " +
            $"Items keys: [{string.Join(", ", context.Items.Keys)}], " +
            $"Input keys: [{inputKeys}]");
    }

    public static bool TryGetVariable<T>(CapabilityExecutionContext context, string key, out T value)
    {
        value = default!;
        // Check Items first (same-execution context)
        if (context.Items.TryGetValue(key, out var obj) && obj is T t)
        {
            value = t;
            return true;
        }
        // Check Input dictionary (workflow variables passed between steps)
        if (context.Input is Dictionary<string, object?> vars
            && vars.TryGetValue(key, out var inputObj) && inputObj is T inputT)
        {
            value = inputT;
            return true;
        }
        return false;
    }

    public static void StoreVariable(CapabilityExecutionContext context, string key, object value)
    {
        context.Items[key] = value;
    }

    public static void EmitDomainEvent(CapabilityExecutionContext context, ILocalEvent @event)
    {
        if (!context.Items.TryGetValue("__domainEvents", out var value) || value is not List<ILocalEvent> list)
        {
            list = new List<ILocalEvent>();
            context.Items["__domainEvents"] = list;
        }
        list.Add(@event);
    }
}
