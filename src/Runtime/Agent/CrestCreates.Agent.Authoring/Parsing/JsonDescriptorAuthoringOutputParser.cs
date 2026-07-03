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
    private static readonly DescriptorAuthoringParserJsonSerializerContext ParserContext =
        new(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

    private const string ExpectedContractVersion = "7g.v1";

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

        // 1. Deserialize JSON
        DescriptorAuthoringProviderOutputDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(providerOutputText, ParserContext.DescriptorAuthoringProviderOutputDto);
        }
        catch (JsonException)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                "Failed to deserialize provider output as JSON.",
                SeverityLevel.Error));
            return CreateResult(DescriptorAuthoringStatus.InvalidProviderOutput, emptyPlan, emptyDraftSet, diagnostics);
        }

        if (dto is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                "Provider output deserialized to null.",
                SeverityLevel.Error));
            return CreateResult(DescriptorAuthoringStatus.InvalidProviderOutput, emptyPlan, emptyDraftSet, diagnostics);
        }

        // 2. Validate contract version
        if (!string.Equals(dto.ContractVersion, ExpectedContractVersion, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                $"Unsupported contract version '{dto.ContractVersion ?? "null"}'. Expected '{ExpectedContractVersion}'.",
                SeverityLevel.Error));
            return CreateResult(DescriptorAuthoringStatus.InvalidProviderOutput, emptyPlan, emptyDraftSet, diagnostics);
        }

        // 3. Validate prompt input hash
        if (!string.Equals(dto.PromptInputHash, context.ExpectedPromptInputHash, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.PromptHashMismatch,
                $"Prompt input hash mismatch. Expected '{context.ExpectedPromptInputHash}', got '{dto.PromptInputHash ?? "null"}'.",
                SeverityLevel.Blocker));
            return CreateResult(DescriptorAuthoringStatus.Blocked, emptyPlan, emptyDraftSet, diagnostics);
        }

        // 4. Validate plan exists
        if (dto.Plan is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                "Provider output is missing required 'plan' section.",
                SeverityLevel.Error));
            return CreateResult(DescriptorAuthoringStatus.InvalidProviderOutput, emptyPlan, emptyDraftSet, diagnostics);
        }

        // 5. Validate items exist
        if (dto.Items is null || dto.Items.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                "Provider output contains no items.",
                SeverityLevel.Error));
            return CreateResult(DescriptorAuthoringStatus.InvalidProviderOutput, emptyPlan, emptyDraftSet, diagnostics);
        }

        // 6. Validate each item with atomic semantics: fail all if any item is invalid
        var drafts = new List<CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft>();
        foreach (var item in dto.Items)
        {
            if (!TryParseItem(item, context, diagnostics, out var draft))
            {
                // Atomic failure: return empty drafts with blocked status
                return CreateResult(DescriptorAuthoringStatus.Blocked, emptyPlan, emptyDraftSet, diagnostics);
            }

            if (draft is not null)
            {
                drafts.Add(draft);
            }
        }

        // 7. Build plan
        var plannedRefs = dto.Plan.PlannedDescriptorRefs is null
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

        // 8. Build draft set
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
            BaseVersion = operation == DescriptorDraftOperation.Update ? proposedVersion : null
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
            InputSchema = ParseDescriptorRef<SchemaDescriptor>(element, "inputSchema"),
            OutputSchema = ParseDescriptorRef<SchemaDescriptor>(element, "outputSchema"),
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
