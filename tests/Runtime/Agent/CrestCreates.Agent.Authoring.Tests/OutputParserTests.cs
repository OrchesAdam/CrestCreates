using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.DescriptorDraft.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class OutputParserTests
{
    private readonly JsonDescriptorAuthoringOutputParser _parser = new();
    private readonly DescriptorAuthoringParseContext _validContext = new()
    {
        TenantId = "tenant-test",
        AuthorId = "llm-descriptor-authoring-agent",
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        CreatedAt = DateTimeOffset.UnixEpoch,
        IntentText = "Add finance review",
        ExpectedPromptInputHash = "abc123hash"
    };

    [Fact]
    public void InvalidJson_Returns_InvalidProviderOutput()
    {
        var result = _parser.Parse("not json at all", _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput);
    }

    [Fact]
    public void WrongContractVersion_Returns_InvalidProviderOutput()
    {
        var json = JsonSerializer.Serialize(new
        {
            contractVersion = "wrong",
            promptInputHash = _validContext.ExpectedPromptInputHash,
            plan = new { planId = "p1", intentText = "test" },
            items = Array.Empty<object>()
        });

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput);
    }

    [Fact]
    public void PromptHashMismatch_Returns_PromptHashMismatch()
    {
        var json = BuildValidOutputJson("abc123hash");
        var context = _validContext with { ExpectedPromptInputHash = "different_hash" };

        var result = _parser.Parse(json, context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.PromptHashMismatch);
    }

    [Fact]
    public void UnknownDescriptorKind_Returns_Blocked()
    {
        var json = BuildOutputJsonWithItem("Schema", "schema_1", "Create");

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKind);
    }

    [Fact]
    public void UnsupportedOperation_Returns_Blocked()
    {
        var json = BuildOutputJsonWithItem("HumanTask", "ht_1", "Remove");

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.UnsupportedDraftOperation);
    }

    [Fact]
    public void AuthorityClaim_Returns_Blocked()
    {
        var json = BuildOutputJsonWithItemAndMemoryRefs("HumanTask", "ht_1", "Create", new[] { "authoritative" });

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolation);
    }

    [Fact]
    public void ValidHumanTaskItem_Returns_Succeeded()
    {
        var json = BuildValidHumanTaskOutputJson();

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.DraftSet.Drafts.Should().HaveCount(1);
        result.Plan.PlannedDescriptorRefs.Should().HaveCount(1);
    }

    [Fact]
    public void ValidWorkflowItem_Returns_Succeeded()
    {
        var json = BuildValidWorkflowOutputJson();

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.DraftSet.Drafts.Should().HaveCount(1);
    }

    [Fact]
    public void Parser_DoesNotHardCode_TenantId()
    {
        var json = BuildValidHumanTaskOutputJson();
        var context = _validContext with { TenantId = "custom-tenant-42" };

        var result = _parser.Parse(json, context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.DraftSet.Drafts[0].TenantId.Should().Be("custom-tenant-42");
    }

    [Fact]
    public void Parser_DoesNotHardCode_AuthorId()
    {
        var json = BuildValidHumanTaskOutputJson();
        var context = _validContext with { AuthorId = "custom-author-99" };

        var result = _parser.Parse(json, context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.DraftSet.Drafts[0].AuthorId.Should().Be("custom-author-99");
    }

    [Fact]
    public void AtomicFailure_OneInvalidItem_NoPartialDraftSet()
    {
        // Two items: one valid HumanTask, one invalid (Remove operation)
        var json = BuildOutputJsonWithTwoItems();

        var result = _parser.Parse(json, _validContext);

        // Should be blocked, not partially successful
        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.DraftSet.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void MissingPlan_Returns_InvalidProviderOutput()
    {
        var json = JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1",
            promptInputHash = _validContext.ExpectedPromptInputHash,
            items = new[] { new { descriptorKind = "HumanTask", descriptorId = "ht_1", operation = "Create" } }
        });

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
    }

    [Fact]
    public void EmptyItems_Returns_InvalidProviderOutput()
    {
        var json = JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1",
            promptInputHash = _validContext.ExpectedPromptInputHash,
            plan = new { planId = "p1", intentText = "test" },
            items = Array.Empty<object>()
        });

        var result = _parser.Parse(json, _validContext);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
    }

    // Helper methods to build test JSON

    private string BuildValidOutputJson(string promptHash) => JsonSerializer.Serialize(new
    {
        contractVersion = "7g.v1",
        promptInputHash = promptHash,
        plan = new
        {
            planId = "plan_test",
            intentText = "Add finance review",
            assumptions = new[] { "Finance team available" },
            plannedDescriptorRefs = new[]
            {
                new { @namespace = "humantask", id = "ht_finance_review", version = 1 }
            }
        },
        items = new object[]
        {
            new
            {
                descriptorKind = "HumanTask",
                descriptorId = "ht_finance_review",
                operation = "Create",
                rationale = "Need finance review step",
                payload = new { id = "ht_finance_review", name = "Finance Review", version = 1, permissions = "Finance.Review" },
                assumptions = new[] { "Finance team available" }
            }
        }
    });

    private string BuildOutputJsonWithItem(string kind, string id, string operation) => JsonSerializer.Serialize(new
    {
        contractVersion = "7g.v1",
        promptInputHash = _validContext.ExpectedPromptInputHash,
        plan = new { planId = "p1", intentText = "test", plannedDescriptorRefs = new[] { new { @namespace = kind.ToLower(), id, version = 1 } } },
        items = new object[]
        {
            new { descriptorKind = kind, descriptorId = id, operation, payload = new { id, name = "Test", version = 1 } }
        }
    });

    private string BuildOutputJsonWithItemAndMemoryRefs(string kind, string id, string operation, string[] memoryRefs) => JsonSerializer.Serialize(new
    {
        contractVersion = "7g.v1",
        promptInputHash = _validContext.ExpectedPromptInputHash,
        plan = new { planId = "p1", intentText = "test", plannedDescriptorRefs = new[] { new { @namespace = kind.ToLower(), id, version = 1 } } },
        items = new object[]
        {
            new { descriptorKind = kind, descriptorId = id, operation, payload = new { id, name = "Test", version = 1 }, memoryRefs }
        }
    });

    private string BuildValidHumanTaskOutputJson() => BuildValidOutputJson(_validContext.ExpectedPromptInputHash);

    private string BuildValidWorkflowOutputJson() => JsonSerializer.Serialize(new
    {
        contractVersion = "7g.v1",
        promptInputHash = _validContext.ExpectedPromptInputHash,
        plan = new
        {
            planId = "plan_test",
            intentText = "Add finance review",
            plannedDescriptorRefs = new[]
            {
                new { @namespace = "workflow", id = "wf_finance_review", version = 1 }
            }
        },
        items = new object[]
        {
            new
            {
                descriptorKind = "Workflow",
                descriptorId = "wf_finance_review",
                operation = "Create",
                rationale = "Need finance review workflow",
                payload = new { id = "wf_finance_review", name = "Finance Review Workflow", version = 1 },
                assumptions = Array.Empty<string>()
            }
        }
    });

    private string BuildOutputJsonWithTwoItems() => JsonSerializer.Serialize(new
    {
        contractVersion = "7g.v1",
        promptInputHash = _validContext.ExpectedPromptInputHash,
        plan = new { planId = "p1", intentText = "test", plannedDescriptorRefs = new[] { new { @namespace = "humantask", id = "ht_1", version = 1 }, new { @namespace = "humantask", id = "ht_2", version = 1 } } },
        items = new object[]
        {
            new { descriptorKind = "HumanTask", descriptorId = "ht_1", operation = "Create", payload = new { id = "ht_1", name = "Valid", version = 1 } },
            new { descriptorKind = "HumanTask", descriptorId = "ht_2", operation = "Remove", payload = new { id = "ht_2", name = "Invalid", version = 1 } }
        }
    });
}
