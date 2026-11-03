using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Json;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Agent.Authoring.Parsing;

public sealed class JsonDescriptorAuthoringOutputParser : IDescriptorAuthoringOutputParser
{
    // Stage B reads the projected values with both name-strictness options pinned: no last-value-wins
    // on a repeated member, and no case-insensitive binding that would let a casing alias through.
    // WhenWritingNull is kept so the disclosure examples, serialized through this same context, show an
    // omitted section rather than a null one — a null placeholder would be a forbidden member.
    private static readonly DescriptorAuthoringParserJsonSerializerContext ParserContext =
        new(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            AllowDuplicateProperties = false,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

    internal static DescriptorAuthoringParserJsonSerializerContext Context => ParserContext;

    internal const string ExpectedContractVersion = "8a.v1";

    private const string OutcomeDrafts = "drafts";
    private const string OutcomeUnsupported = "unsupported";
    private const string OutcomeNeedsClarification = "needsClarification";

    private const int MaxOutcomeEntries = 4;
    private const int MaxTextLength = 400;
    private const int MaxCollectionEntries = 8;
    private const int MaxEntryLength = 120;
    private const int MinQuestionIdLength = 1;
    private const int MaxQuestionIdLength = 64;

    private const string RootPath = DescriptorAuthoringEnvelopeFacts.RootPath;

    public DescriptorAuthoringResult Parse(
        string providerOutputText,
        DescriptorAuthoringParseContext context)
    {
        var diagnostics = new List<DescriptorAuthoringDiagnostic>();
        var emptyPlan = new DescriptorAuthoringPlan
        {
            PlanId = string.Empty,
            IntentText = context.IntentText
        };
        var emptyDraftSet = new DescriptorDraftSet
        {
            DraftSetId = string.Empty
        };

        DescriptorAuthoringResult Invalid(string message)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                message,
                SeverityLevel.Error));
            return CreateResult(DescriptorAuthoringStatus.InvalidProviderOutput, emptyPlan, emptyDraftSet, diagnostics);
        }

        // 1. Raw JSON must be an object document.
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(providerOutputText);
        }
        catch (JsonException)
        {
            return Invalid("Failed to deserialize provider output as JSON.");
        }

        using (document)
        {
            // 2-3. Envelope member names: duplicates and unknown/alias members decide first, at every
            // protocol level and recursing through payload, before any value is interpreted.
            if (!DescriptorAuthoringEnvelopeFacts.TryRead(document.RootElement, out var envelope, out var envelopeFailure))
            {
                return Invalid(envelopeFailure!);
            }

            var root = document.RootElement;

            // 4. Contract version boundary is single-valued.
            var contractVersion = ReadString(root, "contractVersion");
            if (!string.Equals(contractVersion, ExpectedContractVersion, StringComparison.Ordinal))
            {
                return Invalid($"Unsupported contract version '{contractVersion ?? "null"}'. Expected '{ExpectedContractVersion}'.");
            }

            // 5. promptInputHash must be an actual string member.
            var promptInputHash = ReadString(root, "promptInputHash");
            if (promptInputHash is null)
            {
                return Invalid("Provider output has no usable 'promptInputHash' string member.");
            }

            // 6. Integrity before semantics.
            if (!string.Equals(promptInputHash, context.ExpectedPromptInputHash, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DescriptorAuthoringDiagnosticCodes.PromptHashMismatch,
                    $"Prompt input hash mismatch. Expected '{context.ExpectedPromptInputHash}', got '{promptInputHash}'.",
                    SeverityLevel.Blocker));
                return CreateResult(DescriptorAuthoringStatus.Blocked, emptyPlan, emptyDraftSet, diagnostics);
            }

            // 7. The outcome discriminator is required and closed.
            var outcome = ReadString(root, "outcome");
            if (outcome is null)
            {
                return Invalid("Provider output is missing the required 'outcome' discriminator.");
            }

            if (outcome is not (OutcomeDrafts or OutcomeUnsupported or OutcomeNeedsClarification))
            {
                return Invalid($"Unsupported outcome '{outcome}'. Expected '{OutcomeDrafts}', '{OutcomeUnsupported}', or '{OutcomeNeedsClarification}'.");
            }

            // 8. Shape: required members present with the declared kind, forbidden members absent as members
            // at all (null, {}, [] and content are all appearances).
            if (ValidateShape(outcome, envelope) is { } shapeFailure)
            {
                return Invalid(shapeFailure);
            }

            // 9. Stage B projection of the values.
            DescriptorAuthoringProviderOutputDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize(providerOutputText, ParserContext.DescriptorAuthoringProviderOutputDto);
            }
            catch (JsonException ex)
            {
                return Invalid($"Failed to deserialize provider output as JSON: {ex.Message}");
            }

            if (dto is null)
            {
                return Invalid("Provider output deserialized to null.");
            }

            // 10-13. Bounds, normalization, closed code tables, then the terminal outcome.
            if (outcome == OutcomeUnsupported)
            {
                return BuildUnsupportedResult(dto.Unsupported!, envelope, context, diagnostics);
            }

            if (outcome == OutcomeNeedsClarification)
            {
                return BuildClarificationResult(dto.Clarification!, envelope, context, diagnostics);
            }

            return ParseDrafts(dto, context, diagnostics);
        }
    }

    private static string? ValidateShape(string outcome, EnvelopeMemberInventory envelope)
    {
        if (outcome == OutcomeDrafts)
        {
            if (!envelope.HasKind(RootPath, "plan", JsonValueKind.Object))
            {
                return "Provider output with outcome 'drafts' is missing the required 'plan' object.";
            }

            if (!envelope.HasKind(RootPath, "items", JsonValueKind.Array))
            {
                return "Provider output with outcome 'drafts' is missing the required 'items' array.";
            }

            // Item-level required members are shape facts too: an absent or null payload is a contract
            // violation decided here, before any per-item materialization.
            var itemCount = envelope.ArrayLength($"{RootPath}.items") ?? 0;
            for (var i = 0; i < itemCount; i++)
            {
                if (!envelope.HasKind($"{RootPath}.items[{i}]", "payload", JsonValueKind.Object))
                {
                    return $"Provider output item {i} is missing the required 'payload' object.";
                }
            }

            if (envelope.Has(RootPath, "unsupported"))
            {
                return "Provider output with outcome 'drafts' must not carry the 'unsupported' member.";
            }

            if (envelope.Has(RootPath, "needsClarification"))
            {
                return "Provider output with outcome 'drafts' must not carry the 'needsClarification' member.";
            }

            return null;
        }

        var required = outcome == OutcomeUnsupported ? "unsupported" : "needsClarification";
        if (!envelope.HasKind(RootPath, required, JsonValueKind.Object))
        {
            return $"Provider output with outcome '{outcome}' is missing the required '{required}' object.";
        }

        var entriesMember = outcome == OutcomeUnsupported ? "reasons" : "questions";
        if (!envelope.HasKind($"{RootPath}.{required}", entriesMember, JsonValueKind.Array))
        {
            return $"Provider output with outcome '{outcome}' is missing the required '{required}.{entriesMember}' array.";
        }

        foreach (var forbidden in new[] { "plan", "items", "unsupported", "needsClarification" })
        {
            if (forbidden == required)
            {
                continue;
            }

            if (envelope.Has(RootPath, forbidden))
            {
                return $"Provider output with outcome '{outcome}' must not carry the '{forbidden}' member.";
            }
        }

        return null;
    }

    private DescriptorAuthoringResult ParseDrafts(
        DescriptorAuthoringProviderOutputDto dto,
        DescriptorAuthoringParseContext context,
        List<DescriptorAuthoringDiagnostic> diagnostics)
    {
        var emptyPlan = new DescriptorAuthoringPlan
        {
            PlanId = string.Empty,
            IntentText = context.IntentText
        };
        var emptyDraftSet = new DescriptorDraftSet
        {
            DraftSetId = string.Empty
        };

        DescriptorAuthoringResult Invalid(string message)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                message,
                SeverityLevel.Error));
            return CreateResult(DescriptorAuthoringStatus.InvalidProviderOutput, emptyPlan, emptyDraftSet, diagnostics);
        }

        // 10. items carries at least one entry.
        if (dto.Items is null || dto.Items.Count == 0)
        {
            return Invalid("Provider output contains no items.");
        }

        // 13a. Existing per-item atomic validation, unchanged in semantics.
        var drafts = new List<CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft>();
        foreach (var item in dto.Items)
        {
            if (!TryParseItem(item, context, diagnostics, out var draft))
            {
                return CreateResult(DescriptorAuthoringStatus.Blocked, emptyPlan, emptyDraftSet, diagnostics);
            }

            if (draft is not null)
            {
                drafts.Add(draft);
            }
        }

        var plannedRefs = dto.Plan!.PlannedDescriptorRefs is null
            ? Array.Empty<DescriptorRef>()
            : dto.Plan.PlannedDescriptorRefs
                .Select(r => new DescriptorRef(r.Namespace ?? string.Empty, r.Id ?? string.Empty, r.Version))
                .ToArray();

        var plan = new DescriptorAuthoringPlan
        {
            PlanId = dto.Plan.PlanId ?? $"plan-{context.ExpectedPromptInputHash[..Math.Min(16, context.ExpectedPromptInputHash.Length)]}",
            IntentText = dto.Plan.IntentText ?? context.IntentText,
            PlannedDescriptorRefs = plannedRefs,
            Assumptions = dto.Plan.Assumptions?.ToArray() ?? Array.Empty<string>()
        };

        var draftSet = new DescriptorDraftSet
        {
            DraftSetId = plan.PlanId,
            Drafts = drafts.ToArray()
        };

        return CreateResult(
            diagnostics.Count > 0 ? DescriptorAuthoringStatus.SucceededWithDiagnostics : DescriptorAuthoringStatus.Succeeded,
            plan,
            draftSet,
            diagnostics);
    }

    private DescriptorAuthoringResult BuildUnsupportedResult(
        DescriptorAuthoringProviderUnsupportedDto unsupported,
        EnvelopeMemberInventory envelope,
        DescriptorAuthoringParseContext context,
        List<DescriptorAuthoringDiagnostic> diagnostics)
    {
        DescriptorAuthoringResult Invalid(string message)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                message,
                SeverityLevel.Error));
            return CreateResult(
                DescriptorAuthoringStatus.InvalidProviderOutput,
                new DescriptorAuthoringPlan { PlanId = string.Empty, IntentText = context.IntentText },
                new DescriptorDraftSet { DraftSetId = string.Empty },
                diagnostics);
        }

        var reasons = unsupported.Reasons!;
        var path = $"{RootPath}.unsupported";
        var count = envelope.ArrayLength($"{path}.reasons") ?? 0;
        if (count < 1 || count > MaxOutcomeEntries)
        {
            return Invalid($"'{path}.reasons' must hold 1..{MaxOutcomeEntries} entries.");
        }

        var entries = new List<DescriptorAuthoringUnsupportedReason>(count);
        var seenEntries = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < reasons.Count; i++)
        {
            var reasonPath = $"{path}.reasons[{i}]";
            var reason = reasons[i];
            if (envelope.Kind(reasonPath, "code") != JsonValueKind.String
                || envelope.Kind(reasonPath, "message") != JsonValueKind.String)
            {
                return Invalid($"'{reasonPath}' requires string 'code' and 'message' members.");
            }

            if (envelope.Has(reasonPath, "evidenceRefs")
                && !envelope.HasKind(reasonPath, "evidenceRefs", JsonValueKind.Array))
            {
                return Invalid($"'{reasonPath}.evidenceRefs' must be an array.");
            }

            if (!DescriptorAuthoringUnsupportedReasonCodes.IsKnown(reason.Code))
            {
                return Invalid($"'{reasonPath}.code' is outside the closed code table: '{Normalize(reason.Code)}'.");
            }

            var message = Normalize(reason.Message);
            if (message.Length < 1 || message.Length > MaxTextLength)
            {
                return Invalid($"'{reasonPath}.message' must hold 1..{MaxTextLength} characters after normalization.");
            }

            if (ValidateEntryList(reason.EvidenceRefs, envelope, reasonPath, "evidenceRefs", allowEmpty: true, out var evidenceRefs) is { } evidenceFailure)
            {
                return Invalid(evidenceFailure);
            }

            if (!seenEntries.Add($"{reason.Code}•{message}"))
            {
                return Invalid($"'{path}.reasons' repeats the same obstacle at '{reasonPath}'.");
            }

            entries.Add(new DescriptorAuthoringUnsupportedReason
            {
                Code = reason.Code!,
                Message = message,
                EvidenceRefs = evidenceRefs
            });
        }

        var outcome = new DescriptorAuthoringUnsupportedOutcome { Reasons = entries.ToArray() };
        for (var i = 0; i < entries.Count; i++)
        {
            diagnostics.Add(new DescriptorAuthoringDiagnostic
            {
                Code = DescriptorAuthoringDiagnosticCodes.IntentUnsupported,
                Message = entries[i].Message,
                Severity = SeverityLevel.Warning,
                Path = $"unsupported.reasons[{i}]"
            });
        }

        return new DescriptorAuthoringResult
        {
            Status = DescriptorAuthoringStatus.UnsupportedIntent,
            Plan = new DescriptorAuthoringPlan { PlanId = "plan_intent_unsupported", IntentText = context.IntentText },
            DraftSet = new DescriptorDraftSet { DraftSetId = "draftset_intent_unsupported" },
            Unsupported = outcome,
            Diagnostics = diagnostics.ToArray()
        };
    }

    private DescriptorAuthoringResult BuildClarificationResult(
        DescriptorAuthoringProviderClarificationDto clarification,
        EnvelopeMemberInventory envelope,
        DescriptorAuthoringParseContext context,
        List<DescriptorAuthoringDiagnostic> diagnostics)
    {
        DescriptorAuthoringResult Invalid(string message)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                message,
                SeverityLevel.Error));
            return CreateResult(
                DescriptorAuthoringStatus.InvalidProviderOutput,
                new DescriptorAuthoringPlan { PlanId = string.Empty, IntentText = context.IntentText },
                new DescriptorDraftSet { DraftSetId = string.Empty },
                diagnostics);
        }

        var path = $"{RootPath}.needsClarification";
        var questions = clarification.Questions!;
        var count = envelope.ArrayLength($"{path}.questions") ?? 0;
        if (count < 1 || count > MaxOutcomeEntries)
        {
            return Invalid($"'{path}.questions' must hold 1..{MaxOutcomeEntries} entries.");
        }

        var entries = new List<DescriptorAuthoringClarificationQuestion>(count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < questions.Count; i++)
        {
            var questionPath = $"{path}.questions[{i}]";
            var question = questions[i];
            if (envelope.Kind(questionPath, "id") != JsonValueKind.String
                || envelope.Kind(questionPath, "reasonCode") != JsonValueKind.String
                || envelope.Kind(questionPath, "question") != JsonValueKind.String)
            {
                return Invalid($"'{questionPath}' requires string 'id', 'reasonCode', and 'question' members.");
            }

            foreach (var list in new[] { "missingInputs", "candidateValues" })
            {
                if (envelope.Has(questionPath, list) && !envelope.HasKind(questionPath, list, JsonValueKind.Array))
                {
                    return Invalid($"'{questionPath}.{list}' must be an array.");
                }
            }

            var rawId = question.Id ?? string.Empty;
            if (rawId.Length < MinQuestionIdLength || rawId.Length > MaxQuestionIdLength || !IsIdentifierChar(rawId))
            {
                return Invalid($"'{questionPath}.id' must hold 1..{MaxQuestionIdLength} characters of [A-Za-z0-9._-].");
            }

            if (!seenIds.Add(rawId))
            {
                return Invalid($"'{path}.questions' repeats question id '{rawId}'.");
            }

            if (!DescriptorAuthoringClarificationReasonCodes.IsKnown(question.ReasonCode))
            {
                return Invalid($"'{questionPath}.reasonCode' is outside the closed code table: '{Normalize(question.ReasonCode)}'.");
            }

            var text = Normalize(question.Question);
            if (text.Length < 1 || text.Length > MaxTextLength)
            {
                return Invalid($"'{questionPath}.question' must hold 1..{MaxTextLength} characters after normalization.");
            }

            if (ValidateEntryList(question.MissingInputs, envelope, questionPath, "missingInputs", allowEmpty: false, out var missingInputs) is { } missingFailure)
            {
                return Invalid(missingFailure);
            }

            if (ValidateEntryList(question.CandidateValues, envelope, questionPath, "candidateValues", allowEmpty: true, out var candidateValues) is { } candidateFailure)
            {
                return Invalid(candidateFailure);
            }

            entries.Add(new DescriptorAuthoringClarificationQuestion
            {
                Id = rawId,
                ReasonCode = Normalize(question.ReasonCode),
                Question = text,
                MissingInputs = missingInputs,
                CandidateValues = candidateValues
            });
        }

        var outcome = new DescriptorAuthoringClarificationOutcome { Questions = entries.ToArray() };
        for (var i = 0; i < entries.Count; i++)
        {
            diagnostics.Add(new DescriptorAuthoringDiagnostic
            {
                Code = DescriptorAuthoringDiagnosticCodes.IntentNeedsClarification,
                Message = $"{entries[i].ReasonCode}: {entries[i].Question}",
                Severity = SeverityLevel.Warning,
                Path = $"needsClarification.questions[{i}]"
            });
        }

        return new DescriptorAuthoringResult
        {
            Status = DescriptorAuthoringStatus.NeedsClarification,
            Plan = new DescriptorAuthoringPlan { PlanId = "plan_needs_clarification", IntentText = context.IntentText },
            DraftSet = new DescriptorDraftSet { DraftSetId = "draftset_needs_clarification" },
            Clarification = outcome,
            Diagnostics = diagnostics.ToArray()
        };
    }

    /// <summary>
    /// Bounds measured on the normalized form: 0..8 or 1..8 entries, each 1..120 characters and
    /// non-empty after normalization.
    /// </summary>
    private static string? ValidateEntryList(
        List<string>? entries,
        EnvelopeMemberInventory envelope,
        string ownerPath,
        string member,
        bool allowEmpty,
        out IReadOnlyList<string> normalized)
    {
        normalized = Array.Empty<string>();
        var present = envelope.Has(ownerPath, member);
        if (!present)
        {
            return allowEmpty ? null : $"'{ownerPath}.{member}' requires 1..{MaxCollectionEntries} entries.";
        }

        if (entries is null)
        {
            return $"'{ownerPath}.{member}' must be an array of strings.";
        }

        if (entries.Count > MaxCollectionEntries || (!allowEmpty && entries.Count < 1))
        {
            return $"'{ownerPath}.{member}' must hold {(allowEmpty ? 0 : 1)}..{MaxCollectionEntries} entries.";
        }

        var results = new List<string>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var value = Normalize(entries[i]);
            if (value.Length < 1 || value.Length > MaxEntryLength)
            {
                return $"'{ownerPath}.{member}[{i}]' must hold 1..{MaxEntryLength} characters after normalization.";
            }

            results.Add(value);
        }

        normalized = results.ToArray();
        return null;
    }

    /// <summary>
    /// Control characters (U+0000-U+001F, U+007F) become spaces, whitespace runs collapse to one
    /// space, and the ends are trimmed. No visible content is truncated or rewritten.
    /// </summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || character is < '\u0020' or '\u007F')
            {
                if (builder.Length > 0)
                {
                    pendingSpace = true;
                }

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsIdentifierChar(string value)
    {
        foreach (var character in value)
        {
            var isAllowed = character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '.' or '_' or '-';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadString(JsonElement target, string name)
        => target.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // 13. Existing per-item atomic validation, unchanged in semantics.

    private static bool TryParseItem(
        DescriptorAuthoringProviderItemDto item,
        DescriptorAuthoringParseContext context,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        out CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft? draft)
    {
        draft = null;

        // Memory authority check: reject if any memoryRef contains "authoritative"
        if (item.MemoryRefs is not null)
        {
            foreach (var memRef in item.MemoryRefs)
            {
                if (memRef.Contains("authoritative", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolation,
                        $"Item '{item.DescriptorId ?? "unknown"}' claims authoritative memory via memoryRef '{memRef}'. Such claims must be made through governance channels.",
                        SeverityLevel.Error));
                    return false;
                }
            }
        }

        // Validate descriptor kind
        if (!TryParseDescriptorKind(item.DescriptorKind, out var kind))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKind,
                $"Unsupported descriptor kind '{item.DescriptorKind ?? "null"}' for item '{item.DescriptorId ?? "unknown"}'.",
                SeverityLevel.Error));
            return false;
        }

        // Validate operation
        if (!TryParseOperation(item.Operation, out var operation))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.UnsupportedDraftOperation,
                $"Unsupported operation '{item.Operation ?? "null"}' for item '{item.DescriptorId ?? "unknown"}'. Only Create and Update are supported.",
                SeverityLevel.Error));
            return false;
        }

        string? explicitBaseVersion = null;
        if (item.BaseVersion is not null)
        {
            if (operation != DescriptorDraftOperation.Update)
            {
                diagnostics.Add(CreateDiagnostic(
                    DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                    $"Item '{item.DescriptorId ?? "unknown"}' specifies baseVersion, but baseVersion is valid only for Update operations.",
                    SeverityLevel.Error));
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.BaseVersion)
                || !int.TryParse(item.BaseVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBaseVersion)
                || parsedBaseVersion <= 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                    $"Item '{item.DescriptorId ?? "unknown"}' has invalid baseVersion '{item.BaseVersion}'. Expected a positive invariant-culture integer string.",
                    SeverityLevel.Error));
                return false;
            }

            explicitBaseVersion = parsedBaseVersion.ToString(CultureInfo.InvariantCulture);
        }

        // Materialize payload
        if (item.Payload is not JsonElement payloadElement || payloadElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Item '{item.DescriptorId ?? "unknown"}' has missing or invalid payload.",
                SeverityLevel.Error));
            return false;
        }

        DescriptorDraftPayload payload;
        try
        {
            payload = MaterializePayload(kind, payloadElement, diagnostics, out var payloadValid);
            if (!payloadValid)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Failed to materialize payload for item '{item.DescriptorId ?? "unknown"}': {ex.Message}",
                SeverityLevel.Error));
            return false;
        }

        // Build draft
        var descriptorId = item.DescriptorId ?? string.Empty;

        // Resolve proposed/base version from the materialized payload descriptor
        var payloadDescriptor = payload.GetDescriptor();
        var resolvedVersion = (payloadDescriptor as IVersionedDescriptor)?.Version;
        var proposedVersion = resolvedVersion?.ToString();

        draft = new CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft
        {
            TenantId = context.TenantId,
            DraftId = $"{descriptorId}-{Guid.NewGuid():N}",
            DescriptorKind = kind,
            DescriptorId = descriptorId,
            Operation = operation,
            AuthorKind = context.AuthorKind,
            AuthorId = context.AuthorId,
            CreatedAt = context.CreatedAt,
            Payload = payload,
            Intent = context.IntentText,
            Rationale = item.Rationale,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ProposedVersion = proposedVersion,
            BaseVersion = operation == DescriptorDraftOperation.Update
                ? explicitBaseVersion ?? proposedVersion
                : null
        };

        return true;
    }

    private static bool TryParseDescriptorKind(string? kindStr, out DescriptorKind kind)
    {
        if (string.IsNullOrEmpty(kindStr))
        {
            kind = DescriptorKind.Unknown;
            return false;
        }

        if (kindStr.Equals("HumanTask", StringComparison.OrdinalIgnoreCase))
        {
            kind = DescriptorKind.HumanTask;
            return true;
        }

        if (kindStr.Equals("Workflow", StringComparison.OrdinalIgnoreCase))
        {
            kind = DescriptorKind.Workflow;
            return true;
        }

        kind = DescriptorKind.Unknown;
        return false;
    }

    private static bool TryParseOperation(string? operationStr, out DescriptorDraftOperation operation)
    {
        if (string.IsNullOrEmpty(operationStr))
        {
            operation = default;
            return false;
        }

        if (operationStr.Equals("Create", StringComparison.OrdinalIgnoreCase))
        {
            operation = DescriptorDraftOperation.Create;
            return true;
        }

        if (operationStr.Equals("Update", StringComparison.OrdinalIgnoreCase))
        {
            operation = DescriptorDraftOperation.Update;
            return true;
        }

        // Deprecate and Remove are unsupported
        operation = default;
        return false;
    }

    private static DescriptorDraftPayload MaterializePayload(
        DescriptorKind kind,
        JsonElement payloadElement,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        out bool valid)
    {
        valid = true;
        return kind switch
        {
            DescriptorKind.HumanTask => MaterializeHumanTaskPayload(payloadElement),
            DescriptorKind.Workflow => MaterializeWorkflowPayload(payloadElement, diagnostics, out valid),
            _ => throw new NotSupportedException($"Descriptor kind '{kind}' is not supported for payload materialization.")
        };
    }

    private static DescriptorDraftPayload MaterializeHumanTaskPayload(JsonElement element)
    {
        var outcomes = Array.Empty<CompletionOutcome>();
        if (element.TryGetProperty("outcomes", out var outcomesProp) && outcomesProp.ValueKind == JsonValueKind.Array)
        {
            var outcomeList = new List<CompletionOutcome>();
            foreach (var outcomeElement in outcomesProp.EnumerateArray())
            {
                var condition = CompletionCondition.AnyInput;
                if (outcomeElement.TryGetProperty("condition", out var condProp) && condProp.ValueKind == JsonValueKind.String)
                {
                    Enum.TryParse<CompletionCondition>(condProp.GetString(), ignoreCase: true, out condition);
                }
                outcomeList.Add(new CompletionOutcome { Condition = condition });
            }
            outcomes = outcomeList.ToArray();
        }

        var assigneeStrategy = AssigneeStrategy.CandidateGroup;
        if (element.TryGetProperty("assigneeStrategy", out var asProp) && asProp.ValueKind == JsonValueKind.String)
        {
            Enum.TryParse<AssigneeStrategy>(asProp.GetString(), ignoreCase: true, out assigneeStrategy);
        }

        var descriptor = new HumanTaskDescriptor
        {
            Id = element.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
            Name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
            Version = element.TryGetProperty("version", out var verProp) && verProp.ValueKind == JsonValueKind.Number
                ? verProp.GetInt32()
                : 0,
            Permissions = element.TryGetProperty("permissions", out var permProp) ? permProp.GetString() : null,
            Interaction = ParseDescriptorRef<IInteractionDescriptor>(element, "interaction"),
            InputSchema = ParseOptionalDescriptorRef<SchemaDescriptor>(element, "inputSchema"),
            OutputSchema = ParseOptionalDescriptorRef<SchemaDescriptor>(element, "outputSchema"),
            AssigneeStrategy = assigneeStrategy,
            Outcomes = outcomes
        };

        return new HumanTaskDescriptorDraftPayload(descriptor);
    }

    private static DescriptorDraftPayload MaterializeWorkflowPayload(
        JsonElement element,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        out bool valid)
    {
        valid = true;
        var steps = Array.Empty<WorkflowStep>();
        if (element.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
        {
            var stepList = new List<WorkflowStep>();
            foreach (var stepElement in stepsProp.EnumerateArray())
            {
                var stepId = stepElement.TryGetProperty("id", out var sidProp) ? sidProp.GetString() ?? string.Empty : string.Empty;
                var step = MaterializeWorkflowStep(stepElement, diagnostics, stepId, out var stepValid);
                if (!stepValid)
                {
                    valid = false;
                }
                stepList.Add(step);
            }
            steps = stepList.ToArray();
        }

        var descriptor = new WorkflowDescriptor
        {
            Id = element.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
            Name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
            Version = element.TryGetProperty("version", out var verProp) && verProp.ValueKind == JsonValueKind.Number
                ? verProp.GetInt32()
                : 0,
            Steps = steps
        };

        return new WorkflowDescriptorDraftPayload(descriptor);
    }

    private static WorkflowStep MaterializeWorkflowStep(
        JsonElement element,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        string stepId,
        out bool valid)
    {
        valid = true;

        InteractionTarget? target = null;
        if (element.TryGetProperty("target", out var targetProp) && targetProp.ValueKind == JsonValueKind.Object)
        {
            if (!TryMaterializeInteractionTarget(targetProp, diagnostics, stepId, out target))
            {
                valid = false;
            }
        }
        else
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' is missing required 'target'.",
                SeverityLevel.Error));
            valid = false;
        }

        var transitions = Array.Empty<string>();
        if (element.TryGetProperty("transitions", out var transProp) && transProp.ValueKind == JsonValueKind.Array)
        {
            transitions = transProp.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String)
                .Select(t => t.GetString() ?? string.Empty)
                .ToArray();
        }

        var onError = StepErrorBehavior.Fail;
        if (element.TryGetProperty("onError", out var onErrorProp) && onErrorProp.ValueKind == JsonValueKind.String)
        {
            var onErrorStr = onErrorProp.GetString();
            if (Enum.TryParse<StepErrorBehavior>(onErrorStr, ignoreCase: true, out var parsed))
            {
                onError = parsed;
            }
        }

        return new WorkflowStep
        {
            Id = stepId,
            Name = element.TryGetProperty("name", out var stepNameProp) ? stepNameProp.GetString() ?? string.Empty : string.Empty,
            Target = target ?? new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(string.Empty, 0) },
            Condition = element.TryGetProperty("condition", out var condProp) ? condProp.GetString() : null,
            Transitions = transitions,
            OnError = onError
        };
    }

    private static bool TryMaterializeInteractionTarget(
        JsonElement element,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        string stepId,
        out InteractionTarget? target)
    {
        target = null;
        var kind = element.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() ?? string.Empty : string.Empty;

        if (string.IsNullOrEmpty(kind))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' target is missing required 'kind'.",
                SeverityLevel.Error));
            return false;
        }

        return kind switch
        {
            "HumanTask" => TryMaterializeHumanTaskTarget(element, diagnostics, stepId, out target),
            "Capability" => TryMaterializeCapabilityTarget(element, diagnostics, stepId, out target),
            "SubWorkflow" => TryMaterializeSubWorkflowTarget(element, diagnostics, stepId, out target),
            _ => ReportUnsupportedTargetKind(kind, stepId, diagnostics)
        };
    }

    private static bool ReportUnsupportedTargetKind(
        string kind,
        string stepId,
        List<DescriptorAuthoringDiagnostic> diagnostics)
    {
        diagnostics.Add(CreateDiagnostic(
            DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
            $"Workflow step '{stepId}' has unsupported target kind '{kind}'. Supported kinds: HumanTask, Capability, SubWorkflow.",
            SeverityLevel.Error));
        return false;
    }

    private static bool TryMaterializeHumanTaskTarget(
        JsonElement element,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        string stepId,
        out InteractionTarget? target)
    {
        target = null;

        if (!element.TryGetProperty("humanTask", out var htProp) || htProp.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' HumanTask target is missing required 'humanTask' reference.",
                SeverityLevel.Error));
            return false;
        }

        var id = htProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(id))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' HumanTask target has empty 'id'.",
                SeverityLevel.Error));
            return false;
        }

        var version = htProp.TryGetProperty("version", out var verProp) && verProp.ValueKind == JsonValueKind.Number
            ? verProp.GetInt32()
            : 1;

        target = new HumanTaskTarget
        {
            HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(id, version)
        };
        return true;
    }

    private static bool TryMaterializeCapabilityTarget(
        JsonElement element,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        string stepId,
        out InteractionTarget? target)
    {
        target = null;

        if (!element.TryGetProperty("capability", out var capProp) || capProp.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' Capability target is missing required 'capability' reference.",
                SeverityLevel.Error));
            return false;
        }

        var id = capProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(id))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' Capability target has empty 'id'.",
                SeverityLevel.Error));
            return false;
        }

        var version = capProp.TryGetProperty("version", out var verProp) && verProp.ValueKind == JsonValueKind.Number
            ? verProp.GetInt32()
            : 1;

        target = new CapabilityTarget
        {
            Capability = new VersionedDescriptorRef<IVersionedDescriptor>(id, version)
        };
        return true;
    }

    private static bool TryMaterializeSubWorkflowTarget(
        JsonElement element,
        List<DescriptorAuthoringDiagnostic> diagnostics,
        string stepId,
        out InteractionTarget? target)
    {
        target = null;

        if (!element.TryGetProperty("subWorkflow", out var swProp) || swProp.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' SubWorkflow target is missing required 'subWorkflow' reference.",
                SeverityLevel.Error));
            return false;
        }

        var id = swProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(id))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Workflow step '{stepId}' SubWorkflow target has empty 'id'.",
                SeverityLevel.Error));
            return false;
        }

        var version = swProp.TryGetProperty("version", out var verProp) && verProp.ValueKind == JsonValueKind.Number
            ? verProp.GetInt32()
            : 1;

        target = new SubWorkflowTarget
        {
            SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>(id, version)
        };
        return true;
    }

    private static VersionedDescriptorRef<T> ParseDescriptorRef<T>(JsonElement element, string propertyName)
        where T : class, IVersionedDescriptor
    {
        if (element.TryGetProperty(propertyName, out var refProp) && refProp.ValueKind == JsonValueKind.Object)
        {
            var id = refProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
            var version = refProp.TryGetProperty("version", out var verProp) && verProp.ValueKind == JsonValueKind.Number
                ? verProp.GetInt32()
                : 1;
            return new VersionedDescriptorRef<T>(id, version);
        }
        return default;
    }

    private static VersionedDescriptorRef<T>? ParseOptionalDescriptorRef<T>(JsonElement element, string propertyName)
        where T : class, IVersionedDescriptor
    {
        if (!element.TryGetProperty(propertyName, out var refProp) || refProp.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ParseDescriptorRef<T>(element, propertyName);
    }

    private static DescriptorAuthoringDiagnostic CreateDiagnostic(
        DiagnosticCode code,
        string message,
        SeverityLevel severity)
    {
        return new DescriptorAuthoringDiagnostic
        {
            Code = code,
            Message = message,
            Severity = severity
        };
    }

    private static DescriptorAuthoringResult CreateResult(
        DescriptorAuthoringStatus status,
        DescriptorAuthoringPlan plan,
        DescriptorDraftSet draftSet,
        List<DescriptorAuthoringDiagnostic> diagnostics)
    {
        return new DescriptorAuthoringResult
        {
            Status = status,
            Plan = plan,
            DraftSet = draftSet,
            Diagnostics = diagnostics.ToArray()
        };
    }
}
