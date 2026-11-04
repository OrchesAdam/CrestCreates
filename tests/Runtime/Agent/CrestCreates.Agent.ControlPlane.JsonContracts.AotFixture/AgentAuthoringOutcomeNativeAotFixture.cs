using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Json;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;

/// <summary>
/// Issue #159 S3 native scenarios: the two explicit terminal outcomes, the presence-only shape rule,
/// the closed code tables, and the two Stage A rules (declared-name vocabulary, duplicate-before-hash
/// precedence) executed inside the published NativeAOT binary. Each scenario is a green run — the
/// expected refusal is asserted, so the process still exits 0 — and each gate negative control flips
/// exactly one scenario's expectation, which must redden that scenario alone and withhold the total
/// sentinel. The native claim covers the envelopes enumerated here; the full §10.1 matrix stays unit-side.
/// </summary>
internal static class AgentAuthoringOutcomeNativeAotFixture
{
    private const string Hash = "aot-authoring-outcome-envelope";
    private const string DisclosureHash = "<prompt-input-hash-from-visible-context>";
    private const string DisclosureIntent = "Describe the intended change here.";
    private const string DraftId = "ht_asset_maintenance_initial_review";

    private const string UnsupportedReason = "Condition-scoring that skips human review needs a new domain algorithm.";
    private const string ClarificationQuestion = "Which risk threshold decides the automatic maintenance outcome?";

    private const string ShapeAcceptedControl = "outcome-shape-accepted";
    private const string CodeTableAcceptedControl = "code-table-accepted";
    private const string UnknownMemberControl = "unknown-member-accepted";
    private const string DuplicatePrecedenceControl = "duplicate-precedence-blocked";
    private const string DisclosureControl = "wire-disclosure-outcome-accepted";
    private const string UnsupportedDroppedControl = "unsupported-outcome-dropped";
    private const string ClarificationDroppedControl = "clarification-outcome-dropped";

    internal static readonly string[] ControlNames =
    [
        UnsupportedDroppedControl,
        ClarificationDroppedControl,
        ShapeAcceptedControl,
        CodeTableAcceptedControl,
        UnknownMemberControl,
        DuplicatePrecedenceControl,
        DisclosureControl,
    ];

    /// <summary>
    /// The new contract is only AOT-safe if the roots the production path serializes really resolve.
    /// A missing root fails here instead of falling back to reflection inside the native binary.
    /// </summary>
    public static bool RunRequiredRootsResolve()
        => RunScenario("AgentAuthoringOutcomeRequiredRoots", RequiredRootsResolve);

    private static bool RequiredRootsResolve()
    {
        var context = DescriptorAuthoringJsonSerializerContext.Default;
        var requiredRoots = new[]
        {
            typeof(DescriptorAuthoringResult),
            typeof(DescriptorAuthoringUnsupportedOutcome),
            typeof(DescriptorAuthoringUnsupportedReason),
            typeof(DescriptorAuthoringClarificationOutcome),
            typeof(DescriptorAuthoringClarificationQuestion),
        };

        return requiredRoots.All(root => context.GetTypeInfo(root) is not null)
            && !context.Options.TypeInfoResolverChain.Any(resolver => resolver is DefaultJsonTypeInfoResolver);
    }

    public static bool Run(string? control)
    {
        var passed = true;
        passed &= RunScenario("AgentAuthoringUnsupportedOutcome", () => UnsupportedOutcome(control));
        passed &= RunScenario("AgentAuthoringClarificationOutcome", () => ClarificationOutcome(control));
        passed &= RunScenario("AgentAuthoringOutcomeShapeConflict", () => OutcomeShapeConflict(control));
        passed &= RunScenario("AgentAuthoringCodeTableRejection", () => CodeTableRejection(control));
        passed &= RunScenario("AgentAuthoringEnvelopeUnknownMember", () => EnvelopeUnknownMember(control));
        passed &= RunScenario("AgentAuthoringEnvelopeDuplicatePrecedence", () => EnvelopeDuplicatePrecedence(control));
        passed &= RunScenario("AgentAuthoringWireDisclosure", () => WireDisclosure(control));
        if (passed)
            Console.WriteLine("CONTROL_PLANE_AGENT_AUTHORING_OUTCOME_NATIVEAOT_OK");

        return passed;
    }

    private static bool RunScenario(string name, Func<bool> scenario)
    {
        try
        {
            var passed = scenario();
            Console.WriteLine($"{name}:{(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL [{name}]: {ex.Message}");
            Console.WriteLine($"{name}:FAIL");
            return false;
        }
    }

    private static bool UnsupportedOutcome(string? control)
    {
        var result = Parse(EnvelopeUnsupported);
        var restored = RoundTrip(result);
        var json = JsonSerializer.Serialize(
            result, typeof(DescriptorAuthoringResult), DescriptorAuthoringJsonSerializerContext.Default);

        var terminal = result.Status == DescriptorAuthoringStatus.UnsupportedIntent
            && result.Unsupported is not null
            && result.Clarification is null
            && result.DraftSet.Drafts.Count == 0
            && result.DraftSet.DraftSetId == "draftset_intent_unsupported"
            && result.Plan.PlanId == "plan_intent_unsupported"
            && result.Unsupported.Reasons.Count == 2
            && result.Unsupported.Reasons[0].Code == "CAPABILITY_UNSUPPORTED"
            && result.Unsupported.Reasons[0].Message == UnsupportedReason
            && result.Unsupported.Reasons[0].EvidenceRefs.SequenceEqual(
                ["visible:wf_asset_maintenance_review:1"], StringComparer.Ordinal)
            && result.Unsupported.Reasons[1].Code == "GOVERNANCE_BOUNDARY"
            && result.Diagnostics.Count == 2
            && result.Diagnostics.All(diagnostic =>
                diagnostic.Code == DescriptorAuthoringDiagnosticCodes.IntentUnsupported
                && diagnostic.Severity == SeverityLevel.Warning);

        // The control asserts that the source-generated metadata dropped the structured outcome, which
        // real code contradicts: the round-trip has to keep both reasons and their refs.
        var roundTrip = control == UnsupportedDroppedControl
            ? restored.Unsupported is null
            : restored.Unsupported is not null
                && restored.Unsupported.Reasons.Count == 2
                && restored.Unsupported.Reasons[0].Code == "CAPABILITY_UNSUPPORTED"
                && restored.Unsupported.Reasons[0].Message == UnsupportedReason
                && restored.Unsupported.Reasons[0].EvidenceRefs.Count == 1
                && restored.Unsupported.Reasons[1].Code == "GOVERNANCE_BOUNDARY"
                && restored.Clarification is null
                && restored.DraftSet.Drafts.Count == 0
                && restored.Snapshot().Unsupported!.Reasons.Count == 2;

        var wireNames = json.Contains("\"unsupported\"", StringComparison.Ordinal)
            && json.Contains("\"reasons\"", StringComparison.Ordinal)
            && json.Contains("\"code\"", StringComparison.Ordinal)
            && json.Contains("\"message\"", StringComparison.Ordinal)
            && json.Contains("\"evidenceRefs\"", StringComparison.Ordinal)
            && !json.Contains("\"clarification\"", StringComparison.Ordinal);

        return terminal && roundTrip && wireNames;
    }

    private static bool ClarificationOutcome(string? control)
    {
        var result = Parse(EnvelopeClarification);
        var restored = RoundTrip(result);
        var json = JsonSerializer.Serialize(
            result, typeof(DescriptorAuthoringResult), DescriptorAuthoringJsonSerializerContext.Default);

        var terminal = result.Status == DescriptorAuthoringStatus.NeedsClarification
            && result.Clarification is not null
            && result.Unsupported is null
            && result.DraftSet.Drafts.Count == 0
            && result.DraftSet.DraftSetId == "draftset_needs_clarification"
            && result.Plan.PlanId == "plan_needs_clarification"
            && result.Clarification.Questions.Count == 1
            && result.Clarification.Questions[0].Id == "risk-threshold"
            && result.Clarification.Questions[0].ReasonCode == "MISSING_POLICY_INPUT"
            && result.Clarification.Questions[0].Question == ClarificationQuestion
            && result.Clarification.Questions[0].MissingInputs.SequenceEqual(["riskThreshold"], StringComparer.Ordinal)
            && result.Clarification.Questions[0].CandidateValues.Count == 0
            && result.Diagnostics.Count == 1
            && result.Diagnostics[0].Code == DescriptorAuthoringDiagnosticCodes.IntentNeedsClarification
            && result.Diagnostics[0].Severity == SeverityLevel.Warning;

        var roundTrip = control == ClarificationDroppedControl
            ? restored.Clarification is null
            : restored.Clarification is not null
                && restored.Clarification.Questions.Count == 1
                && restored.Clarification.Questions[0].Id == "risk-threshold"
                && restored.Clarification.Questions[0].ReasonCode == "MISSING_POLICY_INPUT"
                && restored.Clarification.Questions[0].Question == ClarificationQuestion
                && restored.Clarification.Questions[0].MissingInputs.Count == 1
                && restored.Clarification.Questions[0].CandidateValues.Count == 0
                && restored.Unsupported is null
                && restored.DraftSet.Drafts.Count == 0
                && restored.Snapshot().Clarification!.Questions.Count == 1;

        var wireNames = json.Contains("\"clarification\"", StringComparison.Ordinal)
            && json.Contains("\"questions\"", StringComparison.Ordinal)
            && json.Contains("\"reasonCode\"", StringComparison.Ordinal)
            && json.Contains("\"missingInputs\"", StringComparison.Ordinal)
            && json.Contains("\"candidateValues\"", StringComparison.Ordinal)
            && !json.Contains("\"unsupported\"", StringComparison.Ordinal);

        return terminal && roundTrip && wireNames;
    }

    private static bool OutcomeShapeConflict(string? control)
    {
        // A forbidden member is rejected because it appears, whatever its value: null, {}, [] and a
        // fully populated section all refuse, and each diagnostic names the member that appeared. The
        // absent form is the green control of the two outcome scenarios above, so it is not a case here.
        // The control asserts the null-only reading, which cannot tell these forms apart.
        var expectAccepted = control == ShapeAcceptedControl;
        var samples = new (string Label, string Envelope, DescriptorAuthoringStatus Terminal, string Member)[]
        {
            ("unsupported/items-null", EnvelopeUnsupportedWithItemsNull,
                DescriptorAuthoringStatus.UnsupportedIntent, "items"),
            ("unsupported/plan-empty", EnvelopeUnsupportedWithEmptyPlan,
                DescriptorAuthoringStatus.UnsupportedIntent, "plan"),
            ("unsupported/items-empty-array", EnvelopeUnsupportedWithEmptyItemsArray,
                DescriptorAuthoringStatus.UnsupportedIntent, "items"),
            ("unsupported/plan-content", EnvelopeUnsupportedWithPlanContent,
                DescriptorAuthoringStatus.UnsupportedIntent, "plan"),
            ("drafts/unsupported-empty", EnvelopeDraftsWithEmptyUnsupported,
                DescriptorAuthoringStatus.Succeeded, "unsupported"),
            ("drafts/clarification-content", EnvelopeDraftsWithClarificationContent,
                DescriptorAuthoringStatus.Succeeded, "needsClarification"),
            ("clarification/items-null", EnvelopeClarificationWithItemsNull,
                DescriptorAuthoringStatus.NeedsClarification, "items"),
        };

        var passed = true;
        foreach (var sample in samples)
        {
            var result = Parse(sample.Envelope);
            passed &= result.Status == (expectAccepted ? sample.Terminal : DescriptorAuthoringStatus.InvalidProviderOutput)
                && result.Diagnostics.Count == 1
                && result.Diagnostics[0].Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput
                && result.Diagnostics[0].Message.Contains(
                    $"must not carry the '{sample.Member}' member", StringComparison.Ordinal)
                && result.DraftSet.Drafts.Count == 0
                && result.Unsupported is null
                && result.Clarification is null;
        }

        return passed
            && Parse(EnvelopeUnsupported).Status == DescriptorAuthoringStatus.UnsupportedIntent
            && Parse(EnvelopeClarification).Status == DescriptorAuthoringStatus.NeedsClarification;
    }

    private static bool CodeTableRejection(string? control)
    {
        // The native execution of the closed code tables. Each rejection is paired in the same run with
        // the identical envelope whose only difference is a legal code, so the code table is what refused.
        var expectAccepted = control == CodeTableAcceptedControl;

        var unknownReasonCode = Parse(EnvelopeUnsupportedWithUnknownCode);
        var unknownReasonPassed = expectAccepted
            ? unknownReasonCode.Status == DescriptorAuthoringStatus.UnsupportedIntent
                && unknownReasonCode.Unsupported!.Reasons.Count == 1
            : unknownReasonCode.Status == DescriptorAuthoringStatus.InvalidProviderOutput
                && unknownReasonCode.Diagnostics.Count == 1
                && unknownReasonCode.Diagnostics[0].Message.Contains(
                    "unsupported.reasons[0].code' is outside the closed code table", StringComparison.Ordinal)
                && unknownReasonCode.DraftSet.Drafts.Count == 0
                && unknownReasonCode.Unsupported is null;

        var unknownClarificationCode = Parse(EnvelopeClarificationWithUnknownReasonCode);
        var unknownClarificationPassed = expectAccepted
            ? unknownClarificationCode.Status == DescriptorAuthoringStatus.NeedsClarification
                && unknownClarificationCode.Clarification!.Questions.Count == 1
            : unknownClarificationCode.Status == DescriptorAuthoringStatus.InvalidProviderOutput
                && unknownClarificationCode.Diagnostics.Count == 1
                && unknownClarificationCode.Diagnostics[0].Message.Contains(
                    "needsClarification.questions[0].reasonCode' is outside the closed code table", StringComparison.Ordinal)
                && unknownClarificationCode.DraftSet.Drafts.Count == 0
                && unknownClarificationCode.Clarification is null;

        // The table is ordinal: the same code lower-cased is still outside it.
        var lowercaseRejected = Parse(EnvelopeUnsupportedWithLowercaseCode).Diagnostics.Any(diagnostic =>
            diagnostic.Message.Contains("outside the closed code table", StringComparison.Ordinal));

        var legalPair = Parse(EnvelopeUnsupported).Status == DescriptorAuthoringStatus.UnsupportedIntent
            && Parse(EnvelopeClarification).Status == DescriptorAuthoringStatus.NeedsClarification;

        return unknownReasonPassed && unknownClarificationPassed && lowercaseRejected && legalPair;
    }

    private static bool EnvelopeUnknownMember(string? control)
    {
        // Stage A's declared-name vocabulary executed natively, as a pair: the answer carrying one
        // undeclared member refuses and the same answer without it still produces its draft.
        var expectAccepted = control == UnknownMemberControl;

        var withExtra = Parse(EnvelopeDraftsWithItemExtra);
        var extraPassed = expectAccepted
            ? withExtra.Status == DescriptorAuthoringStatus.Succeeded
                && withExtra.DraftSet.Drafts.Count == 1
            : withExtra.Status == DescriptorAuthoringStatus.InvalidProviderOutput
                && withExtra.Diagnostics.Count == 1
                && withExtra.Diagnostics[0].Message.Contains("unknown member 'extra'", StringComparison.Ordinal)
                && withExtra.DraftSet.Drafts.Count == 0;

        // An alias is not a declared name: with Stage B's casing option off it would simply go unbound,
        // so rejecting it is Stage A's contribution.
        var alias = Parse(EnvelopeClarificationWithAliasMember);
        var aliasPassed = expectAccepted
            ? alias.Status == DescriptorAuthoringStatus.NeedsClarification
            : alias.Status == DescriptorAuthoringStatus.InvalidProviderOutput
                && alias.Diagnostics[0].Message.Contains("unknown member 'Unsupported'", StringComparison.Ordinal)
                && alias.DraftSet.Drafts.Count == 0;

        var legalControl = Parse(EnvelopeDraftsLegal);
        return extraPassed
            && aliasPassed
            && legalControl.Status == DescriptorAuthoringStatus.Succeeded
            && legalControl.DraftSet.Drafts.Count == 1
            && legalControl.DraftSet.Drafts[0].DescriptorId == DraftId;
    }

    private static bool EnvelopeDuplicatePrecedence(string? control)
    {
        // Stage A's position in the order executed natively: a duplicate key inside a payload sub-object,
        // and a duplicate 'outcome', each paired with one wrong hash, is decided by the duplicate rule.
        // Without Stage A the hash check fires first and the result class becomes Blocked — which is what
        // the control asserts, so it cannot pass against real code.
        var expectHashBlock = control == DuplicatePrecedenceControl;
        var expectedStatus = expectHashBlock
            ? DescriptorAuthoringStatus.Blocked
            : DescriptorAuthoringStatus.InvalidProviderOutput;

        var payloadDuplicate = Parse(EnvelopePayloadDuplicateWithWrongHash);
        var outcomeDuplicate = Parse(EnvelopeDuplicateOutcomeWithWrongHash);

        var precedence = payloadDuplicate.Status == expectedStatus
            && payloadDuplicate.Diagnostics.Count == 1
            && payloadDuplicate.Diagnostics[0].Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput
            && payloadDuplicate.Diagnostics[0].Message.Contains("duplicate member 'id'", StringComparison.Ordinal)
            && payloadDuplicate.DraftSet.Drafts.Count == 0
            && outcomeDuplicate.Status == expectedStatus
            && outcomeDuplicate.Diagnostics.Count == 1
            && outcomeDuplicate.Diagnostics[0].Message.Contains("duplicate member 'outcome'", StringComparison.Ordinal)
            && !outcomeDuplicate.Diagnostics[0].Message.Contains(
                DescriptorAuthoringDiagnosticCodes.PromptHashMismatchValue, StringComparison.Ordinal);

        // The hash check still owns its own input when no Stage A fact precedes it.
        var hashMismatch = Parse(EnvelopeClarificationWithWrongHash);
        return precedence
            && hashMismatch.Status == DescriptorAuthoringStatus.Blocked
            && hashMismatch.Diagnostics.Any(diagnostic =>
                diagnostic.Code == DescriptorAuthoringDiagnosticCodes.PromptHashMismatch)
            && hashMismatch.DraftSet.Drafts.Count == 0;
    }

    private static bool WireDisclosure(string? control)
    {
        var prompt = new DefaultDescriptorAuthoringPromptBuilder().Build(new DescriptorAuthoringPromptInput
        {
            ContractVersion = "8a.v1",
            TenantId = "aot-wire-disclosure-tenant",
            IntentText = DisclosureIntent,
            Metadata = new DescriptorAuthoringMetadataContextProjection(),
            Memory = new DescriptorAuthoringMemoryProjection { IsAuthoritative = false },
            PromptInputHash = new CanonicalHash
            {
                Value = DisclosureHash,
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "Descriptor",
                Scope = "InternalFull",
                Purpose = "Contract",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "schema-contract-hash-v1",
            }
        });

        var examples = prompt.UserPrompt
            .Split("```json", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(block => block[..block.IndexOf("```", StringComparison.Ordinal)].Trim())
            .ToArray();
        if (examples.Length != 4)
            throw new InvalidOperationException($"Expected four wire examples, got {examples.Length}.");

        var parser = new JsonDescriptorAuthoringOutputParser();
        var context = new DescriptorAuthoringParseContext
        {
            TenantId = "aot-wire-disclosure-tenant",
            AuthorId = "aot-wire-disclosure-agent",
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = DateTimeOffset.UnixEpoch,
            IntentText = DisclosureIntent,
            ExpectedPromptInputHash = DisclosureHash
        };

        // The disclosed examples are consumed by the same parser the native chain runs, so the
        // disclosure cannot drift from the DTO or from the contract it teaches.
        var humanTask = parser.Parse(examples[0], context);
        var humanTaskDraft = humanTask.Status == DescriptorAuthoringStatus.Succeeded
            && humanTask.DraftSet.Drafts.Count == 1
            ? humanTask.DraftSet.Drafts[0].Payload.GetDescriptor() as HumanTaskDescriptor
            : null;
        var humanTaskPassed = humanTaskDraft is not null
            && humanTaskDraft.Id == "<human-task-id>"
            && humanTaskDraft.Name == "<human-task-name>"
            && humanTaskDraft.Version == 1
            && humanTaskDraft.Interaction.Id == "<interaction-id>"
            && humanTaskDraft.Permissions == "<permission-name>"
            && humanTaskDraft.InputSchema is null
            && humanTaskDraft.OutputSchema is null
            && humanTaskDraft.Outcomes.Count == 2
            && humanTaskDraft.Outcomes[0].Condition == CompletionCondition.Approve
            && humanTaskDraft.Outcomes[1].Condition == CompletionCondition.Reject;

        var workflow = parser.Parse(examples[1], context);
        var workflowDraft = workflow.Status == DescriptorAuthoringStatus.Succeeded
            && workflow.DraftSet.Drafts.Count == 1
            ? workflow.DraftSet.Drafts[0].Payload.GetDescriptor() as WorkflowDescriptor
            : null;
        var workflowPassed = workflowDraft is not null
            && workflowDraft.Id == "<workflow-id>"
            && workflowDraft.Version == 1
            && workflowDraft.Steps.Count == 3
            && workflowDraft.Steps[0].Target is HumanTaskTarget firstTarget
            && firstTarget.HumanTask.Id == "<human-task-id>"
            && firstTarget.HumanTask.Version == 1
            && workflowDraft.Steps[0].Transitions.SequenceEqual(["step-capability"], StringComparer.Ordinal)
            && workflowDraft.Steps[0].OnError == StepErrorBehavior.Fail
            && workflowDraft.Steps[1].Target is CapabilityTarget secondTarget
            && secondTarget.Capability.Id == "<capability-id>"
            && secondTarget.Capability.Version == 2
            && workflowDraft.Steps[1].Transitions.SequenceEqual(["step-sub-workflow"], StringComparer.Ordinal)
            && workflowDraft.Steps[2].Target is SubWorkflowTarget thirdTarget
            && thirdTarget.SubWorkflow.Id == "<sub-workflow-id>"
            && thirdTarget.SubWorkflow.Version == 1
            && workflowDraft.Steps[2].Transitions.Count == 0
            && workflowDraft.Steps[2].OnError == StepErrorBehavior.Fail;

        var unsupported = parser.Parse(examples[2], context);
        var clarification = parser.Parse(examples[3], context);
        var expectDisclosedTerminals = control != DisclosureControl;
        var terminalsPassed = expectDisclosedTerminals
            ? unsupported.Status == DescriptorAuthoringStatus.UnsupportedIntent
                && unsupported.DraftSet.Drafts.Count == 0
                && unsupported.Unsupported!.Reasons[0].Code == "CAPABILITY_UNSUPPORTED"
                && clarification.Status == DescriptorAuthoringStatus.NeedsClarification
                && clarification.DraftSet.Drafts.Count == 0
                && clarification.Clarification!.Questions[0].ReasonCode == "MISSING_POLICY_INPUT"
            : unsupported.Status == DescriptorAuthoringStatus.InvalidProviderOutput
                || clarification.Status == DescriptorAuthoringStatus.InvalidProviderOutput;

        var rules = prompt.UserPrompt.Contains("Wire reference (contract 8a.v1):", StringComparison.Ordinal)
            && prompt.ContractVersion == "8a.v1"
            && prompt.PromptTemplateVersion == "descriptor-authoring-prompt-template-v3"
            && prompt.UserPrompt.Contains(
                "outcome is required and is one of: drafts, unsupported, needsClarification.",
                StringComparison.Ordinal)
            && prompt.UserPrompt.Contains("never repeat a member at one level", StringComparison.Ordinal)
            && prompt.UserPrompt.Contains(
                "An unknown, mis-cased, repeated, or null-instead-of-omitted member is rejected",
                StringComparison.Ordinal)
            && prompt.UserPrompt.Contains(
                "omit plan and items entirely (not null, not {})", StringComparison.Ordinal)
            && prompt.UserPrompt.Contains(
                "unsupported.reasons[].code is one of: CAPABILITY_UNSUPPORTED, OPERATION_UNSUPPORTED, GOVERNANCE_BOUNDARY.",
                StringComparison.Ordinal)
            && prompt.UserPrompt.Contains(
                "needsClarification.questions[].reasonCode is one of: MISSING_POLICY_INPUT, AMBIGUOUS_TARGET, CONFLICTING_REQUIREMENTS, MISSING_REFERENCE_SELECTION.",
                StringComparison.Ordinal)
            && prompt.UserPrompt.Contains(
                "Do not describe a refusal or a question in prose", StringComparison.Ordinal)
            && prompt.UserPrompt.Contains("The parser does not enforce every business requirement",
                StringComparison.Ordinal);

        return rules && humanTaskPassed && workflowPassed && terminalsPassed;
    }

    private static DescriptorAuthoringResult Parse(string envelope, string expectedHash = Hash)
        => new JsonDescriptorAuthoringOutputParser().Parse(envelope, new DescriptorAuthoringParseContext
        {
            TenantId = "aot-authoring-tenant",
            AuthorId = "aot-authoring-agent",
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = DateTimeOffset.UnixEpoch,
            IntentText = Intent,
            ExpectedPromptInputHash = expectedHash,
        });

    private static DescriptorAuthoringResult RoundTrip(DescriptorAuthoringResult result)
    {
        var context = DescriptorAuthoringJsonSerializerContext.Default;
        var json = JsonSerializer.Serialize(result, typeof(DescriptorAuthoringResult), context);
        return (DescriptorAuthoringResult?)JsonSerializer.Deserialize(
                json, typeof(DescriptorAuthoringResult), context)
            ?? throw new InvalidOperationException("Authoring result round-trip returned null.");
    }

    private const string Intent =
        "Make Asset maintenance skip the human review stage using a condition-scoring algorithm.";

    private const string EnvelopeUnsupported = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"unsupported","unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"Condition-scoring that skips human review needs a new domain algorithm.","evidenceRefs":["visible:wf_asset_maintenance_review:1"]},{"code":"GOVERNANCE_BOUNDARY","message":"Removing the review stage is a governance decision."}]}}
        """;

    private const string EnvelopeClarification = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"needsClarification","needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold decides the automatic maintenance outcome?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
        """;

    private const string EnvelopeUnsupportedWithItemsNull = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"unsupported","items":null,"unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No condition-scoring domain algorithm exists."}]}}
        """;

    private const string EnvelopeUnsupportedWithEmptyPlan = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"unsupported","plan":{},"unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No condition-scoring domain algorithm exists."}]}}
        """;

    private const string EnvelopeUnsupportedWithEmptyItemsArray = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"unsupported","items":[],"unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No condition-scoring domain algorithm exists."}]}}
        """;

    private const string EnvelopeUnsupportedWithPlanContent = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"unsupported","plan":{"planId":"plan_unsupported","intentText":"Skip the review stage","plannedDescriptorRefs":[{"namespace":"humantask","id":"ht_asset_maintenance_initial_review","version":1}]},"unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No condition-scoring domain algorithm exists."}]}}
        """;

    private const string EnvelopeDraftsWithEmptyUnsupported = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"drafts","plan":{"planId":"plan_drafts","intentText":"Create the review task"},"items":[{"descriptorKind":"HumanTask","descriptorId":"ht_asset_maintenance_initial_review","operation":"Create","payload":{"id":"ht_asset_maintenance_initial_review","name":"Asset maintenance initial review","version":1}}],"unsupported":{}}
        """;

    private const string EnvelopeDraftsWithClarificationContent = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"drafts","plan":{"planId":"plan_drafts","intentText":"Create the review task"},"items":[{"descriptorKind":"HumanTask","descriptorId":"ht_asset_maintenance_initial_review","operation":"Create","payload":{"id":"ht_asset_maintenance_initial_review","name":"Asset maintenance initial review","version":1}}],"needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
        """;

    private const string EnvelopeClarificationWithItemsNull = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"needsClarification","items":null,"needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold decides the outcome?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
        """;

    private const string EnvelopeUnsupportedWithUnknownCode = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"unsupported","unsupported":{"reasons":[{"code":"TELEPORTATION_UNSUPPORTED","message":"No condition-scoring domain algorithm exists."}]}}
        """;

    private const string EnvelopeUnsupportedWithLowercaseCode = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"unsupported","unsupported":{"reasons":[{"code":"capability_unsupported","message":"No condition-scoring domain algorithm exists."}]}}
        """;

    private const string EnvelopeClarificationWithUnknownReasonCode = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"needsClarification","needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_APPROVAL","question":"Which risk threshold decides the outcome?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
        """;

    private const string EnvelopeDraftsLegal = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"drafts","plan":{"planId":"plan_drafts","intentText":"Create the review task","plannedDescriptorRefs":[{"namespace":"humantask","id":"ht_asset_maintenance_initial_review","version":1}]},"items":[{"descriptorKind":"HumanTask","descriptorId":"ht_asset_maintenance_initial_review","operation":"Create","payload":{"id":"ht_asset_maintenance_initial_review","name":"Asset maintenance initial review","state":"Active","version":1,"interaction":{"namespace":"form","id":"form_asset_maintenance_review","version":1},"assigneeStrategy":"CandidateGroup","outcomes":[{"condition":"Approve"},{"condition":"Reject"}]}}]}
        """;

    private const string EnvelopeDraftsWithItemExtra = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"drafts","plan":{"planId":"plan_drafts","intentText":"Create the review task","plannedDescriptorRefs":[{"namespace":"humantask","id":"ht_asset_maintenance_initial_review","version":1}]},"items":[{"descriptorKind":"HumanTask","descriptorId":"ht_asset_maintenance_initial_review","operation":"Create","extra":"declared nowhere","payload":{"id":"ht_asset_maintenance_initial_review","name":"Asset maintenance initial review","state":"Active","version":1,"interaction":{"namespace":"form","id":"form_asset_maintenance_review","version":1},"assigneeStrategy":"CandidateGroup","outcomes":[{"condition":"Approve"},{"condition":"Reject"}]}}]}
        """;

    private const string EnvelopeClarificationWithAliasMember = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope","outcome":"needsClarification","Unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"No condition-scoring domain algorithm exists."}]},"needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold decides the outcome?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
        """;

    private const string EnvelopePayloadDuplicateWithWrongHash = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope-wrong","outcome":"drafts","plan":{"planId":"plan_drafts","intentText":"Update the maintenance workflow","plannedDescriptorRefs":[{"namespace":"workflow","id":"wf_asset_maintenance","version":1}]},"items":[{"descriptorKind":"Workflow","descriptorId":"wf_asset_maintenance","operation":"Create","payload":{"id":"wf_asset_maintenance","name":"Asset maintenance workflow","version":1,"notes":{"id":"FIRST","id":"SECOND"}}}]}
        """;

    private const string EnvelopeDuplicateOutcomeWithWrongHash = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope-wrong","outcome":"drafts","outcome":"drafts","plan":{"planId":"plan_drafts","intentText":"Create the review task","plannedDescriptorRefs":[{"namespace":"humantask","id":"ht_asset_maintenance_initial_review","version":1}]},"items":[{"descriptorKind":"HumanTask","descriptorId":"ht_asset_maintenance_initial_review","operation":"Create","payload":{"id":"ht_asset_maintenance_initial_review","name":"Asset maintenance initial review","version":1}}]}
        """;

    private const string EnvelopeClarificationWithWrongHash = """
        {"contractVersion":"8a.v1","promptInputHash":"aot-authoring-outcome-envelope-wrong","outcome":"needsClarification","needsClarification":{"questions":[{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold decides the outcome?","missingInputs":["riskThreshold"],"candidateValues":[]}]}}
        """;
}
