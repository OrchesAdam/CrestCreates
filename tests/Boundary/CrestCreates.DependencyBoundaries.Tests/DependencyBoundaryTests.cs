using System.Xml.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class DependencyBoundaryTests
{
    [Fact]
    public void CoreProjects_DoNotReferenceUpperLayers()
    {
        AssertNoDirectProjectReferences(
            "src/Core",
            "Core projects must not reference upper layers.",
            new[] { "src/Framework", "src/Metadata", "src/Runtime", "src/Persistence", "src/Platform" });
    }

    [Fact]
    public void MetadataAbstractions_DoesNotReferenceUpperLayers()
    {
        AssertNoDirectProjectReferences(
            "src/Metadata/CrestCreates.Metadata.Abstractions",
            "Metadata.Abstractions must remain descriptor contracts only — no Framework, Runtime, Persistence, or Platform.",
            new[] { "src/Framework", "src/Runtime", "src/Persistence", "src/Platform" });
    }

    [Fact]
    public void RuntimeProjects_DoNotReferenceFrameworkApiWebOrPlatform()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime",
            "Runtime projects must not reference API/Web framework packages or Platform composition.",
            new[]
            {
                "src/Framework/Api/CrestCreates.DynamicApi",
                "src/Framework/Api/CrestCreates.OpenApi",
                "src/Framework/Web/CrestCreates.AspNetCore",
                "src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict",
                "src/Framework/Web/CrestCreates.HealthCheck",
                "src/Framework/Web/CrestCreates.HealthCheck.AspNetCore",
                "src/Framework/Web/CrestCreates.HealthCheck.Mvc",
                "src/Platform"
            });
    }

    [Fact]
    public void RuntimeProjects_DoNotReferenceConcreteBusinessOrmProviders()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime",
            "Runtime projects must not reference concrete business ORM providers.",
            new[] { "src/Persistence/CrestCreates.Data.FreeSql", "src/Persistence/CrestCreates.Data.SqlSugar" });
    }

    [Fact]
    public void PersistenceProjects_DoNotReferenceRuntimeWorkflowAgentOrHumanTask()
    {
        AssertNoDirectProjectReferences(
            "src/Persistence",
            "Persistence projects must not own runtime store contracts.",
            new[]
            {
                "src/Runtime/Workflow/CrestCreates.Workflow",
                "src/Runtime/Agent/CrestCreates.Agent.Runtime",
                "src/Runtime/HumanTask/CrestCreates.HumanTask"
            });
    }

    [Fact]
    public void ToolingProjects_DoNotReferenceConcreteRuntimeImplementations()
    {
        AssertNoDirectProjectReferences(
            "src/Tooling",
            "Tooling may reference abstractions but must not reference concrete runtime implementations.",
            new[]
            {
                "src/Runtime/Capability/CrestCreates.Capability",
                "src/Runtime/Workflow/CrestCreates.Workflow",
                "src/Runtime/HumanTask/CrestCreates.HumanTask",
                "src/Runtime/Eventing/CrestCreates.EventBus.Local",
                "src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel",
                "src/Runtime/Eventing/CrestCreates.EventBus.Kafka",
                "src/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ",
                "src/Runtime/Audit/CrestCreates.AuditLogging"
            },
            allowMissingRoot: true);
    }

    [Fact]
    public void PlatformProjects_AreAllowedToComposeFrameworkRuntimeAndPersistence()
    {
        var repoRoot = FindRepoRoot();
        var platformRoot = repoRoot.Combine("src/Platform");

        Assert.True(platformRoot.Exists, "Platform root should exist when Platform projects are part of the layout.");
    }

    [Fact]
    public void AgentMemoryAbstractions_DoesNotReferenceControlPlaneAbstractions()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions",
            "Agent Memory abstractions must remain runtime-context contracts and must not depend on ControlPlane contracts.",
            new[] { "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions" });
    }

    [Fact]
    public void AgentMemoryProjects_DoNotReferenceForbiddenRuntimeOrPlatformLayers()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Memory",
            "Agent Memory runtime must not depend on ControlPlane, Framework Api/Web, Platform, or persistence providers.",
            new[]
            {
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
                "src/Framework/Api",
                "src/Framework/Web",
                "src/Platform",
                "src/Persistence/CrestCreates.Data.FreeSql",
                "src/Persistence/CrestCreates.Data.SqlSugar"
            });
    }

    [Fact]
    public void ControlPlaneAbstractions_MayReferenceHumanTaskAbstractions_ButNotFrameworkOrWeb()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
            "ControlPlane.Abstractions may reference HumanTask.Abstractions (Runtime→Runtime) but must not reference Framework or Web layer types.",
            new[]
            {
                "src/Framework",
                "src/Platform",
                "src/Runtime/Workflow/CrestCreates.Workflow/",
                "src/Runtime/HumanTask/CrestCreates.HumanTask/",
                "src/Runtime/Agent/CrestCreates.Agent.Runtime/",
                "src/Runtime/Capability/CrestCreates.Capability/",
            });
    }

    [Fact]
    public void AgentAuthoringAbstractions_DoesNotReferenceControlPlaneOrDraftContractsOrHttp()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Authoring.Abstractions",
            "Authoring abstractions must remain authoring contracts only — no ControlPlane, DraftContracts, or HTTP provider.",
            new[]
            {
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
                "src/Runtime/Agent/CrestCreates.Agent.DraftContracts",
                "src/Runtime/Agent/CrestCreates.Agent.Authoring.Http"
            });
    }

    [Fact]
    public void AgentAuthoringRuntime_DoesNotReferenceControlPlaneOrDraftContractsOrHttpOrPlatform()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Authoring",
            "Authoring runtime must not depend on ControlPlane, DraftContracts, HTTP provider, or Platform.",
            new[]
            {
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
                "src/Runtime/Agent/CrestCreates.Agent.DraftContracts",
                "src/Runtime/Agent/CrestCreates.Agent.Authoring.Http",
                "src/Platform"
            });
    }

    [Fact]
    public void AgentAuthoringHttp_DoesNotReferenceControlPlaneOrDraftContractsOrPlatform()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Authoring.Http",
            "Authoring HTTP provider must not depend on ControlPlane, DraftContracts, or Platform.",
            new[]
            {
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
                "src/Runtime/Agent/CrestCreates.Agent.DraftContracts",
                "src/Platform"
            });
    }

    [Fact]
    public void AgentPromptingAbstractions_DoesNotReferenceControlPlaneDraftContractsAuthoringHttpOrPlatform()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Prompting.Abstractions",
            "Prompting abstractions must remain prompt evidence contracts only.",
            new[]
            {
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
                "src/Runtime/Agent/CrestCreates.Agent.DraftContracts",
                "src/Runtime/Agent/CrestCreates.Agent.Authoring.Http",
                "src/Platform"
            });
    }

    [Fact]
    public void AgentPromptingRuntime_DoesNotReferenceControlPlaneDraftContractsAuthoringHttpOrPlatform()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Prompting",
            "Prompting runtime must not own model execution, provider integration, governance, or activation.",
            new[]
            {
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane",
                "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions",
                "src/Runtime/Agent/CrestCreates.Agent.DraftContracts",
                "src/Runtime/Agent/CrestCreates.Agent.Authoring.Http",
                "src/Platform"
            });
    }

    [Fact]
    public void AgentPrompting_DoesNotExposePromptExecutorModelClientOrCompletionService()
    {
        var repoRoot = FindRepoRoot();
        var files = Directory.EnumerateFiles(
            Path.Combine(repoRoot.FullName, "src/Runtime/Agent"),
            "*.cs",
            SearchOption.AllDirectories)
            .Where(path => path.Contains("CrestCreates.Agent.Prompting", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var forbidden = files
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => new { file, line, index }))
            .Where(x =>
                x.line.Contains("IAgentPromptExecutor", StringComparison.Ordinal) ||
                x.line.Contains("IAgentPromptModelClient", StringComparison.Ordinal) ||
                x.line.Contains("IAgentPromptCompletionService", StringComparison.Ordinal))
            .Select(x => $"{Path.GetRelativePath(repoRoot.FullName, x.file)}:{x.index + 1}: {x.line.Trim()}")
            .ToArray();

        Assert.True(forbidden.Length == 0, "Prompting must not expose executor/model client/completion service interfaces." + Environment.NewLine + string.Join(Environment.NewLine, forbidden));
    }

    private static void AssertNoDirectProjectReferences(
        string projectRootRelativePath,
        string reason,
        IReadOnlyCollection<string> forbiddenFragments,
        bool allowMissingRoot = false)
    {
        var repoRoot = FindRepoRoot();
        var projectRoot = repoRoot.Combine(projectRootRelativePath);
        if (!projectRoot.Exists && allowMissingRoot)
        {
            return;
        }

        Assert.True(projectRoot.Exists, $"Project root not found: {projectRootRelativePath}");

        var violations = Directory
            .EnumerateFiles(projectRoot.FullName, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => ReadProjectReferences(project)
                .Select(reference => new
                {
                    Project = Path.GetRelativePath(repoRoot.FullName, project),
                    Reference = Normalize(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference))),
                }))
            .Where(edge => forbiddenFragments.Any(fragment => edge.Reference.Contains(Normalize(fragment), StringComparison.OrdinalIgnoreCase)))
            .Select(edge => $"{edge.Project} -> {Path.GetRelativePath(repoRoot.FullName, edge.Reference)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            reason + Environment.NewLine + "Forbidden project references:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(current.FullName, "solutions")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private static string Normalize(string value) =>
        value.Replace('\\', '/');
}

internal static class DirectoryInfoExtensions
{
    public static DirectoryInfo Combine(this DirectoryInfo directory, string relativePath) =>
        new(Path.Combine(directory.FullName, relativePath));
}
