using Xunit;
using FluentAssertions;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Localization.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class DescriptorReviewMessageTemplateCatalogTests
{
    private readonly IDescriptorReviewMessageTemplateCatalog _catalog = new DefaultDescriptorReviewMessageTemplateCatalog();

    [Fact]
    public void TemplateVersion_IsPresent()
    {
        _catalog.TemplateVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Format_KnownTemplate_ReturnsFormattedMessage()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DiagnosticCount"] = "5"
        };
        var result = _catalog.Format("report.summary.valid", parameters);

        result.Should().Be("Draft validation passed with 5 diagnostics.");
    }

    [Fact]
    public void Format_KnownTemplate_WithMissingParam_LeavesPlaceholder()
    {
        var result = _catalog.Format("report.summary.valid", new Dictionary<string, string>(StringComparer.Ordinal));

        result.Should().Contain("{DiagnosticCount}");
    }

    [Fact]
    public void Format_UnknownTemplate_ReturnsPlaceholder()
    {
        var result = _catalog.Format("unknown.template", new Dictionary<string, string>(StringComparer.Ordinal));

        result.Should().Be("[Unknown template: unknown.template]");
    }

    [Fact]
    public void Format_UnknownTemplate_ReturnsPlaceholder_RegardlessOfParameters()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Key"] = "Value"
        };
        var result = _catalog.Format("non.existent.template", parameters);

        result.Should().Be("[Unknown template: non.existent.template]");
    }

    [Fact]
    public void Format_DeterministicAcrossCalls()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ErrorCount"] = "2",
            ["BlockerCount"] = "1"
        };

        var result1 = _catalog.Format("report.summary.invalid", parameters);
        var result2 = _catalog.Format("report.summary.invalid", parameters);

        result1.Should().Be(result2);
        result1.Should().Be("Draft validation failed with 2 errors and 1 blockers.");
    }

    [Fact]
    public void Format_WithExtraParams_IgnoresThem()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AffectedCount"] = "10",
            ["ExtraUnused"] = "ignored"
        };
        var result = _catalog.Format("report.impact.affected", parameters);

        result.Should().Be("Impact analysis found 10 affected descriptors.");
    }

    [Fact]
    public void Format_TemplateVersion_IsStable()
    {
        _catalog.TemplateVersion.Should().Be("7d.v1");
    }

    [Theory]
    [MemberData(nameof(EnglishCompatibilityCases))]
    public void DescriptorGovernanceWithoutLocalizationService_Should_PreserveEnglishBehavior(
        string templateId,
        IReadOnlyDictionary<string, string> parameters,
        string expected)
    {
        _catalog.Format(templateId, parameters).Should().Be(expected);
    }

    [Fact]
    public void DescriptorGovernanceMessage_Should_Resolve_ByCurrentCulture()
    {
        var catalog = CreateCatalog(new StubLocalizationService("zh-CN"));

        catalog.Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                Parameters(("DiagnosticCount", "5")))
            .Should().Be("草稿验证通过，共有 5 条诊断。");
    }

    [Fact]
    public void DescriptorGovernanceMessage_Should_Resolve_ExternalContributor_BeforeBuiltInResource()
    {
        var service = new StubLocalizationService(
            "zh-CN",
            (key, _) => key == DescriptorReviewReportMessageTemplateIds.SummaryValid
                ? "外部模板：{DiagnosticCount}"
                : key);
        var catalog = CreateCatalog(service);

        catalog.Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                Parameters(("DiagnosticCount", "5")))
            .Should().Be("外部模板：5");
    }

    [Fact]
    public void DescriptorGovernanceParentCulture_Should_FallbackToEn()
    {
        var catalog = CreateCatalog(new StubLocalizationService("en-US"));

        catalog.Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                Parameters(("DiagnosticCount", "5")))
            .Should().Be("Draft validation passed with 5 diagnostics.");
    }

    [Fact]
    public void DescriptorGovernanceLocalizationMissing_Should_FallbackToStableTemplate()
    {
        var catalog = CreateCatalog(new StubLocalizationService("ja"));

        catalog.Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                Parameters(("DiagnosticCount", "5")))
            .Should().Be("Draft validation passed with 5 diagnostics.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DescriptorGovernanceLocalizationEmpty_Should_FallbackToBuiltInResource(string localizedValue)
    {
        var catalog = CreateCatalog(new StubLocalizationService(
            "zh-CN",
            (_, _) => localizedValue));

        catalog.Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                Parameters(("DiagnosticCount", "5")))
            .Should().Be("草稿验证通过，共有 5 条诊断。");
    }

    [Fact]
    public void DescriptorGovernanceLocalizationFailure_Should_FallbackToStableTemplate()
    {
        var catalog = CreateCatalog(new StubLocalizationService(
            "ja",
            (_, _) => throw new InvalidOperationException("provider unavailable")));

        catalog.Format(
                DescriptorReviewReportMessageTemplateIds.SummaryValid,
                Parameters(("DiagnosticCount", "5")))
            .Should().Be("Draft validation passed with 5 diagnostics.");
    }

    [Fact]
    public void DescriptorGovernanceResources_Should_CoverExactStableTemplateIdSet()
    {
        var expectedIds = typeof(DescriptorReviewReportMessageTemplateIds)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(property => ((CrestCreates.Core.Abstractions.Identity.MessageTemplateId)property.GetValue(null)!).Value!)
            .Concat(typeof(DescriptorActivationMessageTemplateIds)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Select(property => ((CrestCreates.Core.Abstractions.Identity.MessageTemplateId)property.GetValue(null)!).Value!))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        expectedIds.Should().HaveCount(31);
        var resources = new DescriptorReviewMessageResourceCatalog(
            NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance);

        foreach (var culture in new[] { "en", "zh-CN" })
        {
            var templates = resources.GetExactTemplatesForTesting(culture);
            templates.Keys.OrderBy(value => value, StringComparer.Ordinal).Should().Equal(expectedIds);

            foreach (var templateId in expectedIds)
            {
                ExtractPlaceholders(templates[templateId]).Should().BeEquivalentTo(
                    ExtractPlaceholders(resources.GetExactTemplatesForTesting("en")[templateId]),
                    $"resource '{culture}' must preserve the placeholder schema of '{templateId}'");
            }
        }
    }

    public static IEnumerable<object[]> EnglishCompatibilityCases()
    {
        yield return Case("report.activation.eligible", "Draft is eligible for activation handoff.");
        yield return Case("report.activation.blocked", "Draft is not eligible: blocked.", ("BlockingReasons", "blocked"));
        yield return Case("report.governance.approved", "Governance decision: approved. rationale", ("Rationale", "rationale"));
        yield return Case("report.governance.rejected", "Governance decision: rejected. rationale", ("Rationale", "rationale"));
        yield return Case("report.governance.review_required", "Governance decision: review required. rationale", ("Rationale", "rationale"));
        yield return Case("report.diagnostics.missing_ref", "Descriptor 'descriptor' references missing 'reference'.", ("DescriptorId", "descriptor"), ("ReferenceId", "reference"));
        yield return Case("report.compatibility.schema", "Schema change is incompatible: details.", ("Details", "details"));
        yield return Case("report.summary.valid", "Draft validation passed with 2 diagnostics.", ("DiagnosticCount", "2"));
        yield return Case("report.summary.invalid", "Draft validation failed with 2 errors and 1 blockers.", ("ErrorCount", "2"), ("BlockerCount", "1"));
        yield return Case("report.human_review.required", "Human review required: reason.", ("Reason", "reason"));
        yield return Case("report.recommendation.no_action", "No action required at this time.");
        yield return Case("report.recommendation.activation_handoff", "Draft is ready for activation handoff.");
        yield return Case("report.recommendation.human_review", "Human review is required before proceeding.");
        yield return Case("report.recommendation.apply_fix", "Fix proposal available: fix-1.", ("FixProposalId", "fix-1"));
        yield return Case("report.recommendation.revise_draft", "Draft needs revision before proceeding.");
        yield return Case("report.recommendation.cancel_draft", "Draft should be cancelled.");
        yield return Case("report.package.available", "Package preview available with 2 descriptors.", ("DescriptorCount", "2"));
        yield return Case("report.hashes.computed", "Stable hashes computed for 2 items.", ("HashCount", "2"));
        yield return Case("report.draft_identity.info", "Draft 'draft-1' of kind 'Event', operation Create, status Created.", ("DraftId", "draft-1"), ("DescriptorKind", "Event"), ("Operation", "Create"), ("Status", "Created"));
        yield return Case("report.proposed_changes.materialized", "Materialization produced 2 proposed descriptors.", ("ProposedCount", "2"));
        yield return Case("report.proposed_changes.failed", "Materialization failed: reason.", ("Reason", "reason"));
        yield return Case("report.impact.affected", "Impact analysis found 2 affected descriptors.", ("AffectedCount", "2"));
        yield return Case("report.impact.none", "No descriptors affected by this draft.");
        yield return Case("report.dependency.summary", "Topology: 2 nodes, 3 edges.", ("NodeCount", "2"), ("EdgeCount", "3"));
        yield return Case("report.compatibility.compatible", "All 2 descriptors are compatible.", ("DescriptorCount", "2"));
        yield return Case("report.compatibility.incompatible", "1 of 2 descriptors are incompatible.", ("IncompatibleCount", "1"), ("TotalCount", "2"));
        yield return Case("report.diagnostics.count", "4 diagnostics: 1 info, 1 warnings, 1 errors, 1 blockers.", ("TotalCount", "4"), ("InfoCount", "1"), ("WarningCount", "1"), ("ErrorCount", "1"), ("BlockerCount", "1"));
        yield return Case("report.stable_hashes.present", "Stable hashes available for 2 items.", ("HashCount", "2"));
        yield return Case("report.stable_hashes.none", "No stable hashes computed.");
        yield return Case("report.package_preview.present", "Package preview with 2 descriptors, 3 hashes.", ("DescriptorCount", "2"), ("HashCount", "3"));
        yield return Case("report.package_preview.none", "No package preview available.");
    }

    private static DefaultDescriptorReviewMessageTemplateCatalog CreateCatalog(ILocalizationService service)
        => new(service, NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance);

    private static object[] Case(
        string templateId,
        string expected,
        params (string Key, string Value)[] parameters)
        => new object[] { templateId, Parameters(parameters), expected };

    private static IReadOnlyDictionary<string, string> Parameters(
        params (string Key, string Value)[] values)
        => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    private static string[] ExtractPlaceholders(string template)
    {
        var placeholders = new List<string>();
        var index = 0;
        while (index < template.Length)
        {
            var start = template.IndexOf('{', index);
            if (start < 0)
                break;
            var end = template.IndexOf('}', start + 1);
            if (end < 0)
                break;
            placeholders.Add(template[(start + 1)..end]);
            index = end + 1;
        }
        return placeholders.ToArray();
    }

    private sealed class StubLocalizationService : ILocalizationService
    {
        private readonly Func<string, string, string?> _resolver;

        public StubLocalizationService(
            string currentCulture,
            Func<string, string, string?>? resolver = null)
        {
            CurrentCulture = currentCulture;
            _resolver = resolver ?? ((key, _) => key);
        }

        public string CurrentCulture { get; }
        public string GetString(string key) => GetString(key, CurrentCulture);
        public string GetString(string key, params object[] arguments) => GetString(key);
        public string GetString(string key, string cultureName) => _resolver(key, cultureName) ?? key;
        public string GetString(string key, string cultureName, params object[] arguments) => GetString(key, cultureName);
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(GetString(key));
        public Task<string?> GetStringAsync(string key, params object[] arguments) => GetStringAsync(key);
        public Task<string?> GetStringAsync(string key, string cultureName) => Task.FromResult<string?>(GetString(key, cultureName));
        public Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments) => GetStringAsync(key, cultureName);
        public IDisposable ChangeCulture(string cultureName) => throw new NotSupportedException();
        public Task<IDisposable> ChangeCultureAsync(string cultureName) => throw new NotSupportedException();
    }
}
