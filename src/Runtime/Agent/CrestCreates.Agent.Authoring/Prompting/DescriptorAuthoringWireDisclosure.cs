using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Authoring.Parsing;

namespace CrestCreates.Agent.Authoring.Prompting;

/// <summary>
/// Internal wire reference for the bounded authoring subset. The parser DTOs and
/// their source-generated context remain the only envelope representation.
/// </summary>
internal static class DescriptorAuthoringWireDisclosure
{
    internal const string ExamplePromptInputHash = "<prompt-input-hash-from-visible-context>";

    internal static string BuildPromptSection()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Wire reference (contract 8a.v1):");
        builder.AppendLine("Return one JSON object with contractVersion, promptInputHash, outcome, and exactly one outcome section.");
        builder.AppendLine("outcome is required and is one of: drafts, unsupported, needsClarification.");
        builder.AppendLine("Send only the member names listed here, spelled exactly, and never repeat a member at one level. An unknown, mis-cased, repeated, or null-instead-of-omitted member is rejected as invalid provider output.");
        builder.AppendLine("outcome=drafts: include plan and a non-empty items array, and omit unsupported and needsClarification entirely.");
        builder.AppendLine("outcome=unsupported: include the unsupported section, and omit plan and items entirely (not null, not {}).");
        builder.AppendLine("outcome=needsClarification: include the needsClarification section, and omit plan and items entirely (not null, not {}).");
        builder.AppendLine("Each item has descriptorKind (HumanTask or Workflow), descriptorId, operation (Create or Update), and a payload object.");
        builder.AppendLine("Use the exact promptInputHash supplied by this request. References to existing descriptors must come from Visible Descriptors or Visible Descriptor Refs.");
        builder.AppendLine("A reference to a descriptor proposed in this same response is allowed only when that response declares the corresponding item; review validates the resulting references before activation.");
        builder.AppendLine("The IDs in the examples below are placeholders for syntax only; do not copy them as live references.");
        builder.AppendLine();
        builder.AppendLine("HumanTask payload syntax (inputSchema and outputSchema may be omitted or null):");
        AppendJsonExample(builder, CreateHumanTaskExample());
        builder.AppendLine();
        builder.AppendLine("Workflow payload syntax (the example shows all supported target shapes):");
        AppendJsonExample(builder, CreateWorkflowExample());
        builder.AppendLine();
        builder.AppendLine("Refusal and clarification sections (1 to 4 entries each; message and question text are 1 to 400 characters; evidenceRefs, missingInputs, and candidateValues hold at most 8 entries of at most 120 characters each; a question must name at least one missingInput; question id uses letters, digits, dot, underscore, or hyphen and is unique in the response):");
        builder.AppendLine("unsupported.reasons[].code is one of: CAPABILITY_UNSUPPORTED, OPERATION_UNSUPPORTED, GOVERNANCE_BOUNDARY.");
        builder.AppendLine("needsClarification.questions[].reasonCode is one of: MISSING_POLICY_INPUT, AMBIGUOUS_TARGET, CONFLICTING_REQUIREMENTS, MISSING_REFERENCE_SELECTION.");
        builder.AppendLine("Any other code is rejected. Use unsupported when the intent needs a capability, operation, or governance decision this surface cannot provide; use needsClarification when required information is missing. Do not describe a refusal or a question in prose instead of using these sections, and do not propose drafts for an intent you consider unsupported.");
        AppendJsonExample(builder, CreateUnsupportedExample());
        builder.AppendLine();
        AppendJsonExample(builder, CreateClarificationExample());
        builder.AppendLine();
        builder.AppendLine("Authoring requirements and parser fallbacks:");
        builder.AppendLine("Provide business-valid id, name, positive version, and references. A HumanTask should provide interaction; every Workflow step must provide a target.");
        builder.AppendLine("The parser does not enforce every business requirement: an omitted descriptor version becomes 0, an omitted target reference version becomes 1, an omitted assigneeStrategy becomes CandidateGroup, and omitted outcomes become an empty list. An omitted interaction becomes an empty reference. Do not treat those fallbacks as valid business values.");
        builder.AppendLine("Payload content is descriptor data: its inner field names are not part of the envelope vocabulary, but a repeated name inside a payload is still rejected. Fields outside the supported payload shape are ignored by materialization; do not expect them to survive or take effect.");
        builder.AppendLine("The bounded authoring subset does not support state, supersededById, timeout, outcome capability, variableSchema, defaultVariableScope, inputMapping, outputMapping, selectionMode, or expectedContractHash. Such runtime fields are ignored or do not survive parsing; do not include them expecting them to be applied.");
        return builder.ToString();
    }

    internal static string CreateHumanTaskExample() => Serialize(new DescriptorAuthoringProviderOutputDto
    {
        ContractVersion = "8a.v1",
        PromptInputHash = ExamplePromptInputHash,
        Outcome = "drafts",
        Plan = new DescriptorAuthoringProviderPlanDto
        {
            PlanId = "example-plan",
            IntentText = "Describe the intended change here.",
            PlannedDescriptorRefs =
            [
                new DescriptorAuthoringProviderDescriptorRefDto
                {
                    Namespace = "humantask",
                    Id = "<human-task-id>",
                    Version = 1
                }
            ]
        },
        Items =
        [
            new DescriptorAuthoringProviderItemDto
            {
                DescriptorKind = "HumanTask",
                DescriptorId = "<human-task-id>",
                Operation = "Create",
                Rationale = "Explain why this task is needed.",
                Payload = ParsePayload("""
                    {
                      "id": "<human-task-id>",
                      "name": "<human-task-name>",
                      "version": 1,
                      "permissions": "<permission-name>",
                      "interaction": { "id": "<interaction-id>", "version": 1 },
                      "inputSchema": null,
                      "outputSchema": null,
                      "assigneeStrategy": "CandidateGroup",
                      "outcomes": [ { "condition": "Approve" }, { "condition": "Reject" } ]
                    }
                    """)
            }
        ]
    });

    internal static string CreateWorkflowExample() => Serialize(new DescriptorAuthoringProviderOutputDto
    {
        ContractVersion = "8a.v1",
        PromptInputHash = ExamplePromptInputHash,
        Outcome = "drafts",
        Plan = new DescriptorAuthoringProviderPlanDto
        {
            PlanId = "example-plan",
            IntentText = "Describe the intended workflow change here.",
            PlannedDescriptorRefs =
            [
                new DescriptorAuthoringProviderDescriptorRefDto
                {
                    Namespace = "workflow",
                    Id = "<workflow-id>",
                    Version = 1
                }
            ]
        },
        Items =
        [
            new DescriptorAuthoringProviderItemDto
            {
                DescriptorKind = "Workflow",
                DescriptorId = "<workflow-id>",
                Operation = "Create",
                Rationale = "Explain why this workflow is needed.",
                Payload = ParsePayload("""
                    {
                      "id": "<workflow-id>",
                      "name": "<workflow-name>",
                      "version": 1,
                      "steps": [
                        {
                          "id": "step-human-task",
                          "name": "Review",
                          "target": { "kind": "HumanTask", "humanTask": { "id": "<human-task-id>", "version": 1 } },
                          "transitions": [ "step-capability" ],
                          "onError": "Fail"
                        },
                        {
                          "id": "step-capability",
                          "name": "Apply",
                          "target": { "kind": "Capability", "capability": { "id": "<capability-id>", "version": 2 } },
                          "transitions": [ "step-sub-workflow" ],
                          "onError": "Fail"
                        },
                        {
                          "id": "step-sub-workflow",
                          "name": "Continue",
                          "target": { "kind": "SubWorkflow", "subWorkflow": { "id": "<sub-workflow-id>", "version": 1 } },
                          "transitions": [],
                          "onError": "Fail"
                        }
                      ]
                    }
                    """)
            }
        ]
    });

    internal static string CreateUnsupportedExample() => Serialize(new DescriptorAuthoringProviderOutputDto
    {
        ContractVersion = "8a.v1",
        PromptInputHash = ExamplePromptInputHash,
        Outcome = "unsupported",
        Unsupported = new DescriptorAuthoringProviderUnsupportedDto
        {
            Reasons =
            [
                new DescriptorAuthoringProviderUnsupportedReasonDto
                {
                    Code = "CAPABILITY_UNSUPPORTED",
                    Message = "Condition-scoring that skips human review needs a new domain algorithm and a governance change.",
                    EvidenceRefs = ["visible:wf_asset_maintenance_review:1"]
                }
            ]
        }
    });

    internal static string CreateClarificationExample() => Serialize(new DescriptorAuthoringProviderOutputDto
    {
        ContractVersion = "8a.v1",
        PromptInputHash = ExamplePromptInputHash,
        Outcome = "needsClarification",
        Clarification = new DescriptorAuthoringProviderClarificationDto
        {
            Questions =
            [
                new DescriptorAuthoringProviderClarificationQuestionDto
                {
                    Id = "risk-threshold",
                    ReasonCode = "MISSING_POLICY_INPUT",
                    Question = "Which risk threshold decides the automatic maintenance outcome?",
                    MissingInputs = ["riskThreshold"],
                    CandidateValues = []
                }
            ]
        }
    });

    private static JsonElement ParsePayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Serialize(DescriptorAuthoringProviderOutputDto envelope)
        => JsonSerializer.Serialize(
            envelope,
            JsonDescriptorAuthoringOutputParser.Context.DescriptorAuthoringProviderOutputDto);

    private static void AppendJsonExample(StringBuilder builder, string json)
    {
        builder.AppendLine("```json");
        builder.AppendLine(json);
        builder.AppendLine("```");
    }
}
