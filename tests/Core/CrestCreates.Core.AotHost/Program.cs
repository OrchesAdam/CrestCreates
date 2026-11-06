using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Core.Abstractions.Threading;

namespace CrestCreates.Core.AotHost;

/// <summary>
/// NativeAOT fixture for the Core contracts: semantic identity value types, their JSON converters,
/// severity ordering and the unit-of-work cancellation snapshot. It runs reflection-free
/// (<c>JsonSerializerIsReflectionEnabledByDefault=false</c>) so a reflective fallback cannot make
/// the scenario pass.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!args.Contains("--golden-scenario", StringComparer.Ordinal))
        {
            Console.Error.WriteLine("Usage: CrestCreates.Core.AotHost --golden-scenario");
            return 2;
        }

        try
        {
            Run("semantic-ids", SemanticIds);
            Run("severity-ranking", SeverityRanking);
            Run("json-roundtrip", JsonRoundTrip);
            Run("json-rejects-unknown-severity", JsonRejectsUnknownSeverity);
            Run("json-rejects-empty-diagnostic", JsonRejectsEmptyDiagnostic);
            Run("cancellation-snapshot", CancellationSnapshotAndAttach);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("CORE_SAMPLE_FAILED: " + exception);
            return 1;
        }

        Console.WriteLine("CRESTCREATES_CORE_SAMPLE_OK");
        return 0;
    }

    private static void Run(string name, Action scenario)
    {
        scenario();
        Console.WriteLine("CORE_SCENARIO_OK:" + name);
    }

    private static void SemanticIds()
    {
        Require(new CapabilityId("capability.test").RequireValue() == "capability.test", "capability id");
        Require(new DiagnosticCode("CCHASH001").RequireValue() == "CCHASH001", "diagnostic code");
        Require(new ErrorCode("Crest.Test").RequireValue() == "Crest.Test", "error code");
        Require(new EventName("activation.rejected").RequireValue() == "activation.rejected", "event name");
        Require(new HumanTaskId("review-task").RequireValue() == "review-task", "human task id");
        Require(new MessageTemplateId("report.activation").RequireValue() == "report.activation", "message template");
        Require(new PermissionName("agent.draft.create").RequireValue() == "agent.draft.create", "permission name");
        Require(new PolicyName("Permission:agent.draft.create").RequireValue() == "Permission:agent.draft.create", "policy name");
        Require(new DescriptorId("schema.T1").RequireValue() == "schema.T1", "descriptor id");
        Require(new VersionKey("event.test:v1").RequireValue() == "event.test:v1", "version key");
        Require(new WorkflowId("workflow.test").RequireValue() == "workflow.test", "workflow id");

        Require(default(CapabilityId).IsEmpty, "default capability id must be empty");
        Require(default(SeverityLevel).IsEmpty, "default severity must be empty");
        Require(Throws<ArgumentException>(() => _ = new CapabilityId(" ")), "whitespace id must be rejected");
        Require(Throws<InvalidOperationException>(() => default(DescriptorId).RequireValue()),
            "default RequireValue must throw");
        string implicitValue = new EventName("implicit.value");
        Require(implicitValue == "implicit.value", "implicit string conversion");
        Require(new DescriptorId("same") == new DescriptorId("same"), "value equality");
    }

    private static void SeverityRanking()
    {
        Require(SeverityLevel.Blocker.CompareTo(SeverityLevel.Error) > 0, "blocker > error");
        Require(SeverityLevel.Error.CompareTo(SeverityLevel.Review) > 0, "error > review");
        Require(SeverityLevel.Review.CompareTo(SeverityLevel.Warning) > 0, "review > warning");
        Require(SeverityLevel.Warning.CompareTo(SeverityLevel.Info) > 0, "warning > info");
        Require(SeverityLevel.Info.CompareTo(default) > 0, "info > default");
        Require(new DiagnosticCode("A").CompareTo(new DiagnosticCode("B")) < 0, "diagnostic ordinal order");
    }

    private static void JsonRoundTrip()
    {
        var envelope = new JsonEnvelope(new DiagnosticCode("CCHASH001"), SeverityLevel.Review);
        var json = JsonSerializer.Serialize(envelope, CoreJsonContext.Default.JsonEnvelope);
        Require(json == "{\"diagnostic\":\"CCHASH001\",\"severity\":\"Review\"}",
            "unexpected wire shape: " + json);

        var parsed = JsonSerializer.Deserialize(json, CoreJsonContext.Default.JsonEnvelope);
        Require(parsed is not null, "round-trip must produce a value");
        Require(parsed!.Diagnostic.RequireValue() == "CCHASH001", "diagnostic round-trip");
        Require(parsed.Severity == SeverityLevel.Review, "severity round-trip");
    }

    private static void JsonRejectsUnknownSeverity()
    {
        Require(Throws<JsonException>(() => JsonSerializer.Deserialize(
            "\"Fatal\"", CoreJsonContext.Default.SeverityLevel)), "unknown severity must be rejected");
    }

    private static void JsonRejectsEmptyDiagnostic()
    {
        Require(Throws<JsonException>(() => JsonSerializer.Deserialize(
            "\"\"", CoreJsonContext.Default.DiagnosticCode)), "empty diagnostic code must be rejected");
        Require(Throws<JsonException>(() => JsonSerializer.Deserialize(
            "null", CoreJsonContext.Default.DiagnosticCode)), "null diagnostic code must be rejected");
    }

    private static void CancellationSnapshotAndAttach()
    {
        using var innerCaller = new CancellationTokenSource();
        using var outerCaller = new CancellationTokenSource();
        using var derived = new CancellationTokenSource();

        var innerFrame = new UnitOfWorkCancellationFrame(
            innerCaller.Token,
            innerCaller.Token,
            CallerTokenWasCancelled: false,
            DeadlineExpired: true,
            DerivedTokens: [derived.Token],
            DepthFromRoot: 2);
        var outerFrame = new UnitOfWorkCancellationFrame(
            outerCaller.Token,
            outerCaller.Token,
            CallerTokenWasCancelled: true,
            DeadlineExpired: false,
            DerivedTokens: [],
            DepthFromRoot: 1);

        var inner = new UnitOfWorkCancellationSnapshot([innerFrame]);
        var merged = inner.Merge(new UnitOfWorkCancellationSnapshot([outerFrame]));

        Require(merged.Frames.Count == 2, "merge must keep both frames");
        Require(merged.RootCallerToken == outerCaller.Token, "root caller must be the outermost frame");
        Require(merged.DeadlineExpired, "any frame deadline must surface");
        Require(merged.ContainsToken(derived.Token), "derived tokens must be discoverable");

        var cancellation = new OperationCanceledException(innerCaller.Token);
        UnitOfWorkCancellationSnapshot.Attach(cancellation, inner);
        UnitOfWorkCancellationSnapshot.Attach(cancellation, new UnitOfWorkCancellationSnapshot([outerFrame]));

        var attached = (UnitOfWorkCancellationSnapshot)cancellation.Data[
            UnitOfWorkCancellationSnapshot.ExceptionDataKey]!;
        Require(attached.Frames.Count == 2, "attach must merge instead of replacing");
        Require(attached.RootCallerToken == outerCaller.Token, "merged root caller must survive attach");

        var notCancellation = new InvalidOperationException("boom");
        UnitOfWorkCancellationSnapshot.Attach(notCancellation, inner);
        Require(!notCancellation.Data.Contains(UnitOfWorkCancellationSnapshot.ExceptionDataKey),
            "non-cancellation exceptions must not be annotated");
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Core scenario assertion failed: " + message);
        }
    }
}

internal sealed record JsonEnvelope(DiagnosticCode Diagnostic, SeverityLevel Severity);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JsonEnvelope))]
[JsonSerializable(typeof(DiagnosticCode))]
[JsonSerializable(typeof(SeverityLevel))]
internal partial class CoreJsonContext : JsonSerializerContext;
