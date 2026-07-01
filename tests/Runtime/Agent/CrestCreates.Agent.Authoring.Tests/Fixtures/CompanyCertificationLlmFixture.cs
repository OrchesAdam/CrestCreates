using System.Text;
using System.Text.Json;

namespace CrestCreates.Agent.Authoring.Tests.Fixtures;

/// <summary>
/// Static fixture that provides the recorded provider output JSON
/// for the company certification HumanTask + Workflow scenario.
/// The JSON matches the parser's expected format (contractVersion "7g.v1",
/// promptInputHash, plan, items with HumanTask and Workflow payloads).
/// </summary>
public static class CompanyCertificationLlmFixture
{
    /// <summary>
    /// Returns the recorded LLM output for the company certification scenario.
    /// <paramref name="promptInputHash"/> is the actual hash computed from the
    /// authoring context at test time and embedded into the fixture JSON.
    /// </summary>
    public static string GetRecordedOutput(string promptInputHash)
    {
        var sb = new StringBuilder();

        // Use raw string literal for clean JSON
        sb.AppendLine("{");
        sb.AppendLine("  \"contractVersion\": \"7g.v1\",");
        sb.Append("  \"promptInputHash\": \"");
        sb.Append(promptInputHash);
        sb.AppendLine("\",");
        sb.AppendLine("  \"plan\": {");
        sb.AppendLine("    \"planId\": \"plan_company_certification_finance_review\",");
        sb.AppendLine("    \"intentText\": \"Add second-level finance review before approving company certification.\",");
        sb.AppendLine("    \"assumptions\": [");
        sb.AppendLine("      \"Finance team available for review\"");
        sb.AppendLine("    ],");
        sb.AppendLine("    \"plannedDescriptorRefs\": [");
        sb.AppendLine("      {");
        sb.AppendLine("        \"namespace\": \"humantask\",");
        sb.AppendLine("        \"id\": \"ht_finance_review_company_certification\",");
        sb.AppendLine("        \"version\": 1");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        \"namespace\": \"workflow\",");
        sb.AppendLine("        \"id\": \"wf_company_certification\",");
        sb.AppendLine("        \"version\": 1");
        sb.AppendLine("      }");
        sb.AppendLine("    ]");
        sb.AppendLine("  },");
        sb.AppendLine("  \"items\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"descriptorKind\": \"HumanTask\",");
        sb.AppendLine("      \"descriptorId\": \"ht_finance_review_company_certification\",");
        sb.AppendLine("      \"operation\": \"Create\",");
        sb.AppendLine("      \"rationale\": \"Need finance review step before approval\",");
        sb.AppendLine("      \"payload\": {");
        sb.AppendLine("        \"id\": \"ht_finance_review_company_certification\",");
        sb.AppendLine("        \"name\": \"humantask.FinanceReviewCompanyCertification\",");
        sb.AppendLine("        \"version\": 1,");
        sb.AppendLine("        \"permissions\": \"CompanyCertification.FinanceReview\",");
        sb.AppendLine("        \"interaction\": {");
        sb.AppendLine("          \"id\": \"form_company_certification_review\",");
        sb.AppendLine("          \"version\": 1");
        sb.AppendLine("        },");
        sb.AppendLine("        \"inputSchema\": {");
        sb.AppendLine("          \"id\": \"schema_company_certification_review_input\",");
        sb.AppendLine("          \"version\": 1");
        sb.AppendLine("        },");
        sb.AppendLine("        \"outputSchema\": {");
        sb.AppendLine("          \"id\": \"schema_company_certification_result\",");
        sb.AppendLine("          \"version\": 1");
        sb.AppendLine("        },");
        sb.AppendLine("        \"assigneeStrategy\": \"CandidateGroup\",");
        sb.AppendLine("        \"outcomes\": [");
        sb.AppendLine("          { \"condition\": \"Approve\" },");
        sb.AppendLine("          { \"condition\": \"Reject\" }");
        sb.AppendLine("        ]");
        sb.AppendLine("      },");
        sb.AppendLine("      \"assumptions\": [");
        sb.AppendLine("        \"Finance team available for review\"");
        sb.AppendLine("      ]");
        sb.AppendLine("    },");
        sb.AppendLine("    {");
        sb.AppendLine("      \"descriptorKind\": \"Workflow\",");
        sb.AppendLine("      \"descriptorId\": \"wf_company_certification\",");
        sb.AppendLine("      \"operation\": \"Update\",");
        sb.AppendLine("      \"rationale\": \"Insert finance review step between review and approve\",");
        sb.AppendLine("      \"payload\": {");
        sb.AppendLine("        \"id\": \"wf_company_certification\",");
        sb.AppendLine("        \"name\": \"workflow.CompanyCertification\",");
        sb.AppendLine("        \"version\": 1,");
        sb.AppendLine("        \"steps\": [");
        sb.AppendLine("          {");
        sb.AppendLine("            \"id\": \"step_submit\",");
        sb.AppendLine("            \"name\": \"Submit Certification\",");
        sb.AppendLine("            \"target\": {");
        sb.AppendLine("              \"kind\": \"Capability\",");
        sb.AppendLine("              \"capability\": {");
        sb.AppendLine("                \"namespace\": \"capability\",");
        sb.AppendLine("                \"id\": \"cap_submit_company_certification\",");
        sb.AppendLine("                \"version\": 1");
        sb.AppendLine("              }");
        sb.AppendLine("            },");
        sb.AppendLine("            \"transitions\": [\"step_review\"]");
        sb.AppendLine("          },");
        sb.AppendLine("          {");
        sb.AppendLine("            \"id\": \"step_review\",");
        sb.AppendLine("            \"name\": \"Review Certification\",");
        sb.AppendLine("            \"target\": {");
        sb.AppendLine("              \"kind\": \"HumanTask\",");
        sb.AppendLine("              \"humanTask\": {");
        sb.AppendLine("                \"namespace\": \"humantask\",");
        sb.AppendLine("                \"id\": \"ht_review_company_certification\",");
        sb.AppendLine("                \"version\": 1");
        sb.AppendLine("              }");
        sb.AppendLine("            },");
        sb.AppendLine("            \"condition\": \"previousOutcome == 'Approve'\",");
        sb.AppendLine("            \"transitions\": [\"step_finance_review\"]");
        sb.AppendLine("          },");
        sb.AppendLine("          {");
        sb.AppendLine("            \"id\": \"step_finance_review\",");
        sb.AppendLine("            \"name\": \"Finance Review Certification\",");
        sb.AppendLine("            \"target\": {");
        sb.AppendLine("              \"kind\": \"HumanTask\",");
        sb.AppendLine("              \"humanTask\": {");
        sb.AppendLine("                \"namespace\": \"humantask\",");
        sb.AppendLine("                \"id\": \"ht_finance_review_company_certification\",");
        sb.AppendLine("                \"version\": 1");
        sb.AppendLine("              }");
        sb.AppendLine("            },");
        sb.AppendLine("            \"transitions\": [\"step_approve\"]");
        sb.AppendLine("          },");
        sb.AppendLine("          {");
        sb.AppendLine("            \"id\": \"step_approve\",");
        sb.AppendLine("            \"name\": \"Finalize Approval\",");
        sb.AppendLine("            \"target\": {");
        sb.AppendLine("              \"kind\": \"Capability\",");
        sb.AppendLine("              \"capability\": {");
        sb.AppendLine("                \"namespace\": \"capability\",");
        sb.AppendLine("                \"id\": \"cap_approve_company_certification\",");
        sb.AppendLine("                \"version\": 1");
        sb.AppendLine("              }");
        sb.AppendLine("            },");
        sb.AppendLine("            \"condition\": \"previousOutcome == 'Approve'\",");
        sb.AppendLine("            \"transitions\": []");
        sb.AppendLine("          }");
        sb.AppendLine("        ]");
        sb.AppendLine("      },");
        sb.AppendLine("      \"assumptions\": [");
        sb.AppendLine("        \"Existing workflow structure preserved\"");
        sb.AppendLine("      ]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.Append("}");

        return sb.ToString();
    }
}
