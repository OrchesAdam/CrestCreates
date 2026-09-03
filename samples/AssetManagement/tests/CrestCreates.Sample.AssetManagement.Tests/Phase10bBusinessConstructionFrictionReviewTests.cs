using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CrestCreates.Sample.AssetManagement.Tests;

public sealed class Phase10bBusinessConstructionFrictionReviewTests
{
    private const string ReviewRelativePath = "docs/reviews/2026-09-03-phase-10b-business-construction-friction-review.md";
    private static readonly string[] RequiredSections =
    [
        "## Executive conclusion",
        "## Evidence baseline",
        "## Requirement-by-requirement friction map",
        "## Repeated glue inventory",
        "## Asset vs Procurement comparison",
        "## Human discoverability findings",
        "## Agent discoverability findings",
        "## Closed-during-#85 incident review",
        "## Promote / Reject / Defer decisions",
        "## Remaining uncertainty",
        "## Closure statement"
    ];
    private static readonly string[] RequiredFrozenFields =
    [
        "Application files/code",
        "Descriptors",
        "Capabilities/handlers",
        "Manual registration",
        "Projection-specific code",
        "Permission/DataPermission wiring",
        "Persistence-specific code",
        "Serialization-specific code",
        "Framework glue",
        "Workarounds",
        "Framework modification?"
    ];
    private static readonly string[] RequiredFrictionLabels =
    [
        "Business requirement",
        "Observed construction work",
        "Evidence",
        "Semantic owner",
        "Primary classification",
        "Cross-domain repeated?",
        "Human discoverability",
        "Agent discoverability",
        "Workaround?",
        "Acceptance case",
        "Disposition",
        "Rationale"
    ];
    private static readonly string[] AllowedClassifications =
    [
        "Business-specific complexity",
        "Framework usability friction",
        "Framework capability gap",
        "Documentation / discoverability gap",
        "Accidental framework complexity"
    ];
    private static readonly string[] AllowedDispositions = ["Keep", "#87", "#88", "Defer", "Reject"];

    [Fact]
    public void Review_Should_Have_All_Required_Sections()
    {
        var review = ReadReview();

        foreach (var section in RequiredSections)
            review.Should().Contain(section);
    }

    [Fact]
    public void Review_Should_Cover_Every_Frozen_Phase10a_Friction_Field()
    {
        var review = ReadReview();

        foreach (var field in RequiredFrozenFields)
            review.Should().Contain($"| {field} |");

        Regex.Matches(review, "\\| (Application files/code|Descriptors|Capabilities/handlers|Manual registration|Projection-specific code|Permission/DataPermission wiring|Persistence-specific code|Serialization-specific code|Framework glue|Workarounds|Framework modification\\?) \\| F\\d{2} \\|")
            .Count.Should().Be(RequiredFrozenFields.Length);
    }

    [Fact]
    public void Every_Friction_Should_Have_A_BusinessRequirement_And_Evidence()
    {
        var entries = ReadFrictionEntries();

        entries.Should().HaveCount(11);
        foreach (var entry in entries)
        {
            foreach (var label in RequiredFrictionLabels)
                entry.Body.Should().Contain($"- {label}:");

            entry.Body.Should().MatchRegex("- Evidence:.*`[^`]+`");
            entry.Body.Should().NotContain("Business requirement: TBD");
            entry.Body.Should().NotContain("Evidence: TBD");
        }
    }

    [Fact]
    public void Every_Friction_Should_Have_Exactly_One_PrimaryClassification()
    {
        foreach (var entry in ReadFrictionEntries())
        {
            var classifications = AllowedClassifications
                .Where(classification => Regex.Matches(entry.Body, $"- Primary classification: {Regex.Escape(classification)}").Count > 0)
                .ToArray();

            classifications.Should().ContainSingle($"{entry.Id} must have one primary classification");
        }
    }

    [Fact]
    public void Every_Friction_Should_Declare_A_SemanticOwner()
    {
        foreach (var entry in ReadFrictionEntries())
            entry.Body.Should().MatchRegex("- Semantic owner: (?!TBD|None|Unknown).+");
    }

    [Fact]
    public void CapabilityGap_Should_Require_A_PreImplementation_AcceptanceCase()
    {
        foreach (var entry in ReadFrictionEntries().Where(entry => entry.Body.Contains("- Primary classification: Framework capability gap")))
        {
            entry.Body.Should().MatchRegex("- Acceptance case: .+");
            entry.Body.Should().NotContain("No acceptance case");
        }
    }

    [Fact]
    public void CapabilityGap_Should_Not_Be_Promoted_When_ExistingSemantics_Are_Sufficient()
    {
        var review = ReadReview();

        review.Should().Contain("The review found no new capability gap.");
        review.Should().MatchRegex("No new\\s+capability-gap candidate is promoted");
        ExtractSection(review, "### #88 candidates")
            .Should().NotContain("Classification: Framework capability gap");
    }

    [Fact]
    public void Review_Should_Separate_F11_Incident_Classifications()
    {
        var review = ReadReview();
        var entries = ReadFrictionEntries();

        entries.Single(entry => entry.Id == "F03").Body
            .Should().Contain("- Primary classification: Documentation / discoverability gap");
        entries.Single(entry => entry.Id == "F08").Body
            .Should().Contain("- Primary classification: Documentation / discoverability gap");
        entries.Single(entry => entry.Id == "F11").Body
            .Should().Contain("- Primary classification: Documentation / discoverability gap")
            .And.NotContain("- Primary classification: Framework capability gap");

        ExtractSection(review, "### I01 —")
            .Should().Contain("Incident classification: Framework capability gap");
        ExtractSection(review, "### I02 —")
            .Should().Contain("Incident classification: Framework contract correctness correction");
        ExtractSection(review, "### I03 —")
            .Should().Contain("Incident classification: Framework contract correctness correction");
    }

    [Fact]
    public void BusinessPolicy_Should_Not_Be_Promoted_As_FrameworkGap()
    {
        var review = ReadReview();

        review.Should().Contain("Asset lifecycle, assignment/return/transfer eligibility");
        review.Should().Contain("Organization visibility policy");
        review.Should().MatchRegex("No new\\s+capability-gap candidate is promoted");
        review.Should().NotContain("application policy as a #88 framework capability");
    }

    [Fact]
    public void RepeatedGlue_Should_Not_Imply_FrameworkOwnership_By_Itself()
    {
        var review = ReadReview();

        review.Should().Contain("Repetition is evidence, not ownership.");
        review.Should().Contain("different semantic contracts");
        review.Should().MatchRegex("provider, projection, security, or\\s+authority choice visible");
    }

    [Fact]
    public void CrossDomain_Repetition_Should_Link_Asset_And_Procurement_Evidence()
    {
        var review = ReadReview();
        var rows = review
            .Split("\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains("| Yes |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(7);
        foreach (var row in rows)
        {
            row.Should().Contain("Asset");
            row.Should().Contain("Procurement");
        }
    }

    [Fact]
    public void Discoverability_Findings_Should_Distinguish_Human_And_Agent_When_Necessary()
    {
        var review = ReadReview();

        review.Should().Contain("## Human discoverability findings");
        review.Should().Contain("## Agent discoverability findings");
        review.Should().Contain("Human discoverability differs from Agent discoverability");
        review.Should().Contain("Agent discoverability differs from human discoverability");
        review.Should().Contain("H01:");
        review.Should().Contain("A01:");
    }

    [Fact]
    public void ClosedPhase10a_FrameworkChanges_Should_Link_To_Their_FailingBusinessCases()
    {
        var review = ReadReview();

        foreach (var incident in new[] { "I01", "I02", "I03" })
        {
            var body = ExtractSection(review, $"### {incident} —");
            body.Should().Contain("Business acceptance case:");
            body.Should().Contain("Observed failure:");
            body.Should().Contain("Original owner:");
            body.Should().Contain("Why application workaround was invalid:");
            body.Should().Contain("Framework contract added/fixed:");
            body.Should().Contain("Tests proving the fix:");
            body.Should().Contain("Whether the lesson generalizes:");
        }
    }

    [Fact]
    public void ExtractedSections_Should_Not_Feed_Neighboring_Entries()
    {
        var review = ReadReview();

        ExtractSection(review, "### F01 —").Should().NotContain("### F02 —");
        ExtractSection(review, "### I01 —").Should().NotContain("I02 —");
        ExtractSection(review, "### I02 —").Should().NotContain("I03 —");
        ExtractSection(review, "### I03 —").Should().NotContain("## Promote / Reject / Defer decisions");
    }

    [Fact]
    public void Phase10b_FrameworkCandidates_Should_Target_Issue88()
    {
        var review = ExtractSection(ReadReview(), "### #88 candidates");

        review.Should().Contain("#### C88-01");
        review.Should().Contain("C88-02 — Contract/host JSON resolver composition");
        review.Should().Contain("Disposition: #88");
        review.Should().Contain("Disposition: Reject");
        review.Should().Contain("false-positive framework candidate");
        review.Should().Contain("JsonTypeInfoResolver.Combine(...)");
        review.Should().Contain("Pre-implementation failing acceptance case:");
        review.Should().Contain("both binaries linked successfully but failed at runtime");
        review.Should().MatchRegex("No new\\s+capability-gap candidate is promoted");
    }

    [Fact]
    public void Phase10b_AiEvolutionInputs_Should_Target_Issue87()
    {
        var review = ExtractSection(ReadReview(), "### #87 input list");

        review.Should().Contain("87-01");
        review.Should().Contain("87-02");
        review.Should().Contain("87-03");
        review.Should().Contain("87-04");
        review.Should().Contain("#87 must exercise these as business evolution scenarios");
    }

    [Fact]
    public void Review_Should_Have_No_Unclassified_Major_Friction()
    {
        var entries = ReadFrictionEntries();

        entries.Select(entry => entry.Id).Should().BeEquivalentTo(Enumerable.Range(1, 11).Select(index => $"F{index:00}"));
        foreach (var entry in entries)
        {
            var disposition = Regex.Match(entry.Body, "- Disposition: (?<value>[^.\\n]+)").Groups["value"].Value.Trim();
            AllowedDispositions.Should().Contain(disposition, $"{entry.Id} must have an explicit disposition");
        }
    }

    [Fact]
    public void Phase10b_Should_Not_Introduce_ProductionRuntimeChanges()
    {
        var review = ReadReview();

        review.Should().Contain("No production Runtime feature implemented in #86");
        review.Should().Contain("only review documentation, contract tests, and CI wiring");

        var repositoryRoot = FindRepositoryRoot();
        var baseRevision = Environment.GetEnvironmentVariable("PHASE10B_BASE_SHA") ?? "HEAD^";
        foreach (var arguments in new[]
        {
            new[] { "diff", baseRevision, "HEAD", "--", "src/Runtime" },
            new[] { "diff", "--", "src/Runtime" },
            new[] { "diff", "--cached", "--", "src/Runtime" }
        })
        {
            var result = RunGit(repositoryRoot, arguments);
            result.ExitCode.Should().Be(0, result.StandardError);
            result.StandardOutput.Trim().Should().BeEmpty("the Phase 10b change must not modify production Runtime files");
        }
    }

    private static IReadOnlyList<ReviewEntry> ReadFrictionEntries()
    {
        var review = ReadReview();
        return Regex.Matches(review, "(?ms)^### (?<id>F\\d{2}) —.*?(?=^### F\\d{2} —|^## )")
            .Select(match => new ReviewEntry(match.Groups["id"].Value, match.Value))
            .ToArray();
    }

    private static string ReadReview()
    {
        var path = Path.Combine(FindRepositoryRoot(), ReviewRelativePath);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CrestCreates repository root.");
    }

    private static string ExtractSection(string review, string heading)
    {
        var start = review.IndexOf(heading, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"section {heading} should exist");
        var contentStart = start + heading.Length;
        var nextHeading = Regex.Match(review[contentStart..], "(?m)^#{2,3} ");
        var end = nextHeading.Success ? contentStart + nextHeading.Index : review.Length;
        return review[start..end];
    }

    private static GitResult RunGit(string repositoryRoot, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record ReviewEntry(string Id, string Body);
    private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);
}
