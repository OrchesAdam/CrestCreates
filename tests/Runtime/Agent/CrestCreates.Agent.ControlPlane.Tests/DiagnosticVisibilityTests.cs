using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests that diagnostic explanations use an allowlisted code table
/// and never echo caller-supplied diagnostic content.
/// </summary>
public class DiagnosticVisibilityTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task ExplainDiagnostics_KnownCode_Returns_Fixed_Content()
    {
        var service = CreateService();
        var context = CreateContext("ExplainDiagnostics");

        var request = new ExplainDiagnosticsRequest
        {
            Diagnostics =
            [
                new AgentToolDiagnostic
                {
                    Code = new DiagnosticCode("KIND_PAYLOAD_MISMATCH"),
                    Severity = SeverityLevel.Error,
                    Message = "Hostile: denied-ref-001"
                }
            ]
        };

        var result = await service.ExplainDiagnosticsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Explanations.Should().HaveCount(1);
        result.Value.Explanations[0].Code.Should().Be(new DiagnosticCode("KIND_PAYLOAD_MISMATCH"));
        // Must not echo the caller's hostile message
        result.Value.Explanations[0].Explanation.Should().NotContain("denied-ref-001");
        result.Value.Explanations[0].Explanation.Should().NotContain("Hostile");
    }

    [Fact]
    public async Task ExplainDiagnostics_UnknownCode_Returns_UNKNOWN_DIAGNOSTIC()
    {
        var service = CreateService();
        var context = CreateContext("ExplainDiagnostics");

        var request = new ExplainDiagnosticsRequest
        {
            Diagnostics =
            [
                new AgentToolDiagnostic
                {
                    Code = new DiagnosticCode("UNSUPPORTED_CODE_12345"),
                    Severity = SeverityLevel.Warning,
                    Message = "Some unknown error"
                }
            ]
        };

        var result = await service.ExplainDiagnosticsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Explanations.Should().HaveCount(1);
        // Unknown codes get UNKNOWN_DIAGNOSTIC, NOT the caller's original code
        result.Value.Explanations[0].Code.Should().Be(new DiagnosticCode("UNKNOWN_DIAGNOSTIC"));
        // Must not echo the caller's code
        result.Value.Explanations[0].Explanation.Should().NotContain("UNSUPPORTED_CODE_12345");
    }

    [Fact]
    public async Task ExplainDiagnostics_No_DraftId_Does_Not_Require_Owner()
    {
        var service = CreateService();
        var context = CreateContext("ExplainDiagnostics");

        // Without DraftId, explanations should work without owner resolution
        var request = new ExplainDiagnosticsRequest
        {
            Diagnostics =
            [
                new AgentToolDiagnostic
                {
                    Code = new DiagnosticCode("DRAFT_ID_EMPTY"),
                    Severity = SeverityLevel.Error,
                    Message = ""
                }
            ]
        };

        var result = await service.ExplainDiagnosticsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Explanations.Should().HaveCount(1);
        result.Value.Explanations[0].Code.Should().Be(new DiagnosticCode("DRAFT_ID_EMPTY"));
    }

    [Fact]
    public async Task ExplainDiagnostics_With_DraftId_Resolves_Owner()
    {
        var service = CreateService();
        var context = CreateContext("ExplainDiagnostics");

        var draft = CreateTestDraft(draftId: "draft-001", kind: DescriptorKind.Schema);
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var request = new ExplainDiagnosticsRequest
        {
            Diagnostics =
            [
                new AgentToolDiagnostic
                {
                    Code = new DiagnosticCode("INTENT_EMPTY"),
                    Severity = SeverityLevel.Warning,
                    Message = ""
                }
            ],
            DraftId = "draft-001"
        };

        var result = await service.ExplainDiagnosticsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Explanations.Should().HaveCount(1);
    }
}
