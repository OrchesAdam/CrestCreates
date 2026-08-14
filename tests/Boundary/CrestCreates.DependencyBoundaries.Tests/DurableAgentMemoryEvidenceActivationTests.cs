using System.Text.RegularExpressions;
using CrestCreates.Agent.Memory.Persistence.Testing.Manifest;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Static/metadata discovery guards for the Phase 9b+ evidence ledger. They
/// prove that every skeleton name and evidence tuple with a given owning Slice
/// is activated as a concrete test in the owning project. They never launch
/// tests recursively and never claim that another test assembly passed —
/// execution evidence is owned by the concrete runner projects.
/// </summary>
public abstract class DurableAgentMemoryEvidenceActivationGuard
{
    protected const string InMemoryRunnerProject = "tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests";
    protected const string PostgreSqlRunnerProject = "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests";
    protected const string AotFixtureProject = "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests";

    protected abstract int Slice { get; }

    protected virtual bool CheckBothRunnersForSharedNames => false;

    [Fact]
    public void SliceSkeletonEntries_Should_BeDiscoverable()
    {
        var missing = new List<string>();
        foreach (var entry in DurableAgentMemorySpecTestSkeleton.SharedRequiredMethodNames.Where(e => e.OwningSlice == Slice))
        {
            if (!DurableAgentMemorySourceDiscovery.HasMethodInRunner(
                    InMemoryRunnerProject, "CrestCreates.Agent.Memory.Tests.Persistence", entry.Name))
                missing.Add($"shared method {entry.Name}");
        }
        foreach (var entry in DurableAgentMemorySpecTestSkeleton.PostgreSqlRequiredGroupNames.Where(e => e.OwningSlice == Slice))
        {
            if (!DurableAgentMemorySourceDiscovery.HasClassInRunner(
                    PostgreSqlRunnerProject, "CrestCreates.Runtime.Persistence.PostgreSql.Tests", entry.Name))
                missing.Add($"PostgreSQL group {entry.Name}");
        }
        foreach (var entry in DurableAgentMemorySpecTestSkeleton.PostgreSqlRequiredMethodNames.Where(e => e.OwningSlice == Slice))
        {
            if (!DurableAgentMemorySourceDiscovery.HasMethodInRunner(
                    PostgreSqlRunnerProject, "CrestCreates.Runtime.Persistence.PostgreSql.Tests", entry.Name))
                missing.Add($"PostgreSQL method {entry.Name}");
        }

        Assert.True(
            missing.Count == 0,
            $"Slice {Slice} skeleton entries must be activated:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void SliceEvidenceTuples_Should_BeDiscoverable()
    {
        var missing = new List<string>();
        foreach (var tuple in DurableAgentMemoryCaseManifest.EvidenceTuples.Where(t => t.OwningSlice == Slice))
        {
            if (!IsDiscoverable(tuple.ExactFullyQualifiedTestName))
                missing.Add($"{tuple.CaseId}/{tuple.Kind}: {tuple.ExactFullyQualifiedTestName}");
        }

        Assert.True(
            missing.Count == 0,
            $"Slice {Slice} evidence tuples must be activated:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    protected bool IsDiscoverable(string fullyQualifiedTestName)
    {
        var dot = fullyQualifiedTestName.LastIndexOf('.');
        if (dot <= 0) return false;
        var methodName = fullyQualifiedTestName[(dot + 1)..];
        var typeName = fullyQualifiedTestName[..dot];
        var namespaceDot = typeName.LastIndexOf('.');
        var className = typeName[(namespaceDot + 1)..];
        var namespaceName = typeName[..namespaceDot];

        var project = namespaceName switch
        {
            "CrestCreates.Agent.Memory.Tests.Persistence" => InMemoryRunnerProject,
            "CrestCreates.Runtime.Persistence.PostgreSql.Tests" => PostgreSqlRunnerProject,
            "CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests" => AotFixtureProject,
            "CrestCreates.DependencyBoundaries.Tests" => "tests/Boundary/CrestCreates.DependencyBoundaries.Tests",
            _ => null
        };

        return project is not null
            && DurableAgentMemorySourceDiscovery.HasMethodInRunner(project, namespaceName, className, methodName);
    }
}

/// <summary>Slice 2 — InMemory semantic alignment + shared projection surfaces.</summary>
public sealed class Slice2EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 2;
}

/// <summary>Slice 3 — V010 schema, JSON roots, and explicit DI selection.</summary>
public sealed class Slice3EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 3;
}
/// <summary>Slice 4 — durable Conversation and Task stores.</summary>
public sealed class Slice4EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 4;
}

/// <summary>Slice 5 — durable Context and Block projection.</summary>
public sealed class Slice5EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 5;
}

/// <summary>Slice 6 — Candidate/Memory base store and query parity.</summary>
public sealed class Slice6EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 6;
}

/// <summary>Slice 7 — atomic Promote and Reject.</summary>
public sealed class Slice7EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 7;
}

/// <summary>Slice 8 — atomic Supersede/Archive + truthful capability.</summary>
public sealed class Slice8EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 8;
}

/// <summary>Slice 9 — concurrency, failure injection, and real crash evidence.</summary>
public sealed class Slice9EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 9;
}

/// <summary>Slice 10 — restart Recall/Source Expansion and composition parity.</summary>
public sealed class Slice10EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 10;
}

/// <summary>Slice 11 union guard — every skeleton name and evidence tuple must
/// be discoverable, including shared names in both runners.</summary>
public sealed class Slice11EvidenceActivationTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 11;

    [Fact]
    public void SharedSkeletonNames_Should_BeDiscoverableInBothRunners()
    {
        var missing = new List<string>();
        foreach (var entry in DurableAgentMemorySpecTestSkeleton.SharedRequiredMethodNames)
        {
            if (!DurableAgentMemorySourceDiscovery.HasMethodInRunner(
                    InMemoryRunnerProject, "CrestCreates.Agent.Memory.Tests.Persistence", entry.Name))
                missing.Add($"InMemory shared method {entry.Name}");
            if (!DurableAgentMemorySourceDiscovery.HasMethodInRunner(
                    PostgreSqlRunnerProject, "CrestCreates.Runtime.Persistence.PostgreSql.Tests", entry.Name))
                missing.Add($"PostgreSQL shared method {entry.Name}");
        }

        Assert.True(
            missing.Count == 0,
            $"Shared skeleton names must exist in both runners:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }
}

/// <summary>Slice 11 discovery-completeness union over all 44 skeleton names
/// and all 98 evidence tuples.</summary>
public sealed class AllDurableAgentMemoryEvidenceTests : DurableAgentMemoryEvidenceActivationGuard
{
    protected override int Slice => 11;

    [Fact]
    public void EverySkeletonNameAndEvidenceTuple_Should_BeDiscoverable()
    {
        var missingSkeleton = new List<string>();
        foreach (var name in DurableAgentMemorySpecTestSkeleton.SpecRequiredTestNames)
        {
            if (DurableAgentMemorySpecTestSkeleton.SharedRequiredMethodNames.Any(e => e.Name == name))
            {
                if (!DurableAgentMemorySourceDiscovery.HasMethodInRunner(
                        InMemoryRunnerProject, "CrestCreates.Agent.Memory.Tests.Persistence", name)
                    || !DurableAgentMemorySourceDiscovery.HasMethodInRunner(
                        PostgreSqlRunnerProject, "CrestCreates.Runtime.Persistence.PostgreSql.Tests", name))
                {
                    missingSkeleton.Add($"shared method {name} (both runners)");
                }
            }
            else if (DurableAgentMemorySpecTestSkeleton.PostgreSqlRequiredGroupNames.Any(e => e.Name == name))
            {
                if (!DurableAgentMemorySourceDiscovery.HasClassInRunner(
                        PostgreSqlRunnerProject, "CrestCreates.Runtime.Persistence.PostgreSql.Tests", name))
                    missingSkeleton.Add($"PostgreSQL group {name}");
            }
            else if (!DurableAgentMemorySourceDiscovery.HasMethodInRunner(
                         PostgreSqlRunnerProject, "CrestCreates.Runtime.Persistence.PostgreSql.Tests", name))
            {
                missingSkeleton.Add($"PostgreSQL method {name}");
            }
        }

        var missingEvidence = DurableAgentMemoryCaseManifest.EvidenceTuples
            .Where(tuple => !IsDiscoverable(tuple.ExactFullyQualifiedTestName))
            .Select(tuple => $"{tuple.CaseId}/{tuple.Kind}: {tuple.ExactFullyQualifiedTestName}")
            .ToArray();

        Assert.True(
            missingSkeleton.Count == 0 && missingEvidence.Length == 0,
            $"All skeleton names and evidence tuples must be activated.{Environment.NewLine}"
            + $"Skeleton missing: {string.Join(Environment.NewLine, missingSkeleton)}{Environment.NewLine}"
            + $"Evidence missing: {string.Join(Environment.NewLine, missingEvidence)}");
    }
}

/// <summary>
/// Source-level static discovery: an exact namespace, class, and method
/// declaration must exist in the owning test project. Discovery is scoped to
/// the concrete runner projects so shared-kit case names cannot satisfy it.
/// </summary>
internal static class DurableAgentMemorySourceDiscovery
{
    public static bool HasClassInRunner(string projectRelativePath, string namespaceName, string className)
        => EnumerateSourceFiles(projectRelativePath).Any(file => ContainsClass(file, namespaceName, className));

    public static bool HasMethodInRunner(string projectRelativePath, string namespaceName, string methodName)
        => EnumerateSourceFiles(projectRelativePath).Any(file => ContainsMethod(file, namespaceName, methodName: methodName, className: null));

    public static bool HasMethodInRunner(string projectRelativePath, string namespaceName, string className, string methodName)
        => EnumerateSourceFiles(projectRelativePath).Any(file => ContainsMethod(file, namespaceName, methodName, className));

    private static IEnumerable<string> EnumerateSourceFiles(string projectRelativePath)
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var projectRoot = repoRoot.Combine(projectRelativePath);
        Assert.True(projectRoot.Exists, $"Test project root not found: {projectRelativePath}");
        return Directory
            .EnumerateFiles(projectRoot.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool ContainsClass(string file, string namespaceName, string className)
    {
        var content = File.ReadAllText(file);
        return content.Contains($"namespace {namespaceName}", StringComparison.Ordinal)
            && Regex.IsMatch(content, $@"\bclass\s+{Regex.Escape(className)}\b", RegexOptions.Compiled);
    }

    private static bool ContainsMethod(string file, string namespaceName, string methodName, string? className)
    {
        var content = File.ReadAllText(file);
        if (!content.Contains($"namespace {namespaceName}", StringComparison.Ordinal))
            return false;
        if (className is not null
            && !Regex.IsMatch(content, $@"\bclass\s+{Regex.Escape(className)}\b", RegexOptions.Compiled))
        {
            return false;
        }

        var declaration = new Regex(
            $@"^\s*(?:public|internal)\s+(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+|partial\s+)*(?:[\w<>\[\],\.\?]+\s+)+{Regex.Escape(methodName)}\s*\(",
            RegexOptions.Compiled | RegexOptions.Multiline);
        return declaration.IsMatch(content);
    }
}
