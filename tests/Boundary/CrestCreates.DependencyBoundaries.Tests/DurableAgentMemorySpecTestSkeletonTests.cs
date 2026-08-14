using System.Text.RegularExpressions;
using CrestCreates.Agent.Memory.Persistence.Testing.Manifest;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Proves the frozen Spec §18 skeleton exactly equals the approved Design's
/// §18.1/§18.2 fenced name blocks: 31 shared methods, 6 PostgreSQL groups,
/// 7 PostgreSQL methods, and the 44-name union. The parser reads only the
/// Spec markdown and must not derive names from this Plan's §17 tables.
/// </summary>
public class DurableAgentMemorySpecTestSkeletonTests
{
    private static readonly string SpecPath = Path.Combine(
        DependencyBoundaryTestsHelpers.FindRepoRoot().FullName,
        "docs/superpowers/specs/2026-08-13-phase-9bplus-durable-agent-memory-store-provider-design.md");

    [Fact]
    public void SkeletonSharedMethods_Should_ExactlyMatchSpec18_1()
    {
        var names = ParseFencedBlocks(ExtractSection(SpecPath, "### 18.1", "### 18.2"))
            .SelectMany(block => block)
            .Where(name => name.StartsWith("Conversation_", StringComparison.Ordinal)
                || name.StartsWith("Task_", StringComparison.Ordinal)
                || name.StartsWith("Concurrent_", StringComparison.Ordinal)
                || name.StartsWith("TaskAppend_", StringComparison.Ordinal)
                || name.StartsWith("CompressedContext_", StringComparison.Ordinal)
                || name.StartsWith("BlockIdentity_", StringComparison.Ordinal)
                || name.StartsWith("ReplacingContext_", StringComparison.Ordinal)
                || name.StartsWith("Candidate_", StringComparison.Ordinal)
                || name.StartsWith("Memory_", StringComparison.Ordinal)
                || name.StartsWith("SaveMemory_", StringComparison.Ordinal)
                || name.StartsWith("ListMemories_", StringComparison.Ordinal)
                || name.StartsWith("ListStores_", StringComparison.Ordinal)
                || name.StartsWith("Promote_", StringComparison.Ordinal)
                || name.StartsWith("Reject_", StringComparison.Ordinal)
                || name.StartsWith("Supersede_", StringComparison.Ordinal)
                || name.StartsWith("Archive_", StringComparison.Ordinal)
                || name.StartsWith("ConcurrentPromote_", StringComparison.Ordinal)
                || name.StartsWith("ConcurrentArchive_", StringComparison.Ordinal)
                || name.StartsWith("CurationCapabilities_", StringComparison.Ordinal)
                || name.StartsWith("PromotionPreparation_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var expected = DurableAgentMemorySpecTestSkeleton.SharedRequiredMethodNames
            .Select(entry => entry.Name)
            .ToArray();

        Assert.True(
            names.Length == expected.Length
            && names.SequenceEqual(expected, StringComparer.Ordinal),
            $"Spec §18.1 must define exactly the 31 frozen shared skeleton names.{Environment.NewLine}"
            + $"Spec names ({names.Length}): {string.Join(", ", names)}{Environment.NewLine}"
            + $"Skeleton names ({expected.Length}): {string.Join(", ", expected)}");
    }

    [Fact]
    public void SkeletonPostgreSqlGroups_Should_ExactlyMatchSpec18_2()
    {
        var blocks = ParseFencedBlocks(ExtractSection(SpecPath, "### 18.2", "## 19."));
        Assert.True(blocks.Count >= 2, "Spec §18.2 must contain group and method fenced blocks.");

        var groups = blocks[0];
        var expected = DurableAgentMemorySpecTestSkeleton.PostgreSqlRequiredGroupNames
            .Select(entry => entry.Name)
            .ToArray();

        Assert.True(
            groups.Length == expected.Length
            && groups.SequenceEqual(expected, StringComparer.Ordinal),
            $"Spec §18.2 must define exactly the 6 frozen PostgreSQL group names.{Environment.NewLine}"
            + $"Spec groups: {string.Join(", ", groups)}{Environment.NewLine}"
            + $"Skeleton groups: {string.Join(", ", expected)}");
    }

    [Fact]
    public void SkeletonPostgreSqlMethods_Should_ExactlyMatchSpec18_2()
    {
        var blocks = ParseFencedBlocks(ExtractSection(SpecPath, "### 18.2", "## 19."));
        Assert.True(blocks.Count >= 2, "Spec §18.2 must contain group and method fenced blocks.");

        var methods = blocks[1];
        var expected = DurableAgentMemorySpecTestSkeleton.PostgreSqlRequiredMethodNames
            .Select(entry => entry.Name)
            .ToArray();

        Assert.True(
            methods.Length == expected.Length
            && methods.SequenceEqual(expected, StringComparer.Ordinal),
            $"Spec §18.2 must define exactly the 7 frozen PostgreSQL method names.{Environment.NewLine}"
            + $"Spec methods: {string.Join(", ", methods)}{Environment.NewLine}"
            + $"Skeleton methods: {string.Join(", ", expected)}");
    }

    [Fact]
    public void SkeletonUnion_Should_ContainExactly44UniqueNames()
    {
        var names = DurableAgentMemorySpecTestSkeleton.SpecRequiredTestNames;
        Assert.True(names.Count == 44, $"Spec skeleton union must contain 44 names, got {names.Count}.");
        Assert.True(
            names.Distinct(StringComparer.Ordinal).Count() == 44,
            "Spec skeleton union must not contain duplicate names.");
        Assert.True(
            DurableAgentMemorySpecTestSkeleton.OwningSliceByName.Keys.Count() == 44,
            "Every skeleton name must record an owning Slice.");
    }

    [Fact]
    public void SkeletonOwningSlices_Should_BeWithin2To11()
    {
        var invalid = DurableAgentMemorySpecTestSkeleton.OwningSliceByName
            .Where(pair => pair.Value is < 2 or > 11)
            .Select(pair => $"{pair.Key}@{pair.Value}")
            .ToArray();
        Assert.True(invalid.Length == 0, $"Skeleton owning Slices must be within 2-11: {string.Join(", ", invalid)}");
    }

    private static string ExtractSection(string path, string startHeading, string endHeading)
    {
        Assert.True(File.Exists(path), $"Spec not found: {path}");
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, line => line.StartsWith(startHeading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"Heading not found in Spec: {startHeading}");
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith(endHeading, StringComparison.Ordinal));
        if (end < 0) end = lines.Length;
        return string.Join(Environment.NewLine, lines.Skip(start).Take(end - start));
    }

    private static IReadOnlyList<string[]> ParseFencedBlocks(string section)
    {
        var blocks = new List<string[]>();
        var fence = new Regex(@"^```\w*$");
        var inBlock = false;
        var current = new List<string>();
        foreach (var line in section.Split(Environment.NewLine))
        {
            if (fence.IsMatch(line.Trim()))
            {
                if (inBlock)
                {
                    blocks.Add(current.ToArray());
                    current = new List<string>();
                }
                inBlock = !inBlock;
                continue;
            }
            if (inBlock && !string.IsNullOrWhiteSpace(line))
                current.Add(line.Trim());
        }
        return blocks;
    }
}
