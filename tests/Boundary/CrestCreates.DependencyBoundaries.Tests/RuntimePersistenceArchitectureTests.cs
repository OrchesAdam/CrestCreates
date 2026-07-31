using System.Xml.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public sealed class RuntimePersistenceArchitectureTests
{
    private const string AbstractionsProject =
        "src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions/"
        + "CrestCreates.Runtime.Persistence.Abstractions.csproj";

    private const string RuntimeProject =
        "src/Runtime/Persistence/CrestCreates.Runtime.Persistence/"
        + "CrestCreates.Runtime.Persistence.csproj";

    private const string TestingProject =
        "tests/Shared/CrestCreates.Runtime.Persistence.Testing/"
        + "CrestCreates.Runtime.Persistence.Testing.csproj";

    [Fact]
    public void RuntimeAbstractions_Should_Not_ExposeProviderTypes()
    {
        var project = LoadRequiredProject(AbstractionsProject);
        var references = ProjectReferences(project);
        var source = ProductionSources("src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions");

        Assert.DoesNotContain(references, IsProviderReference);
        Assert.DoesNotContain(source, ContainsProviderType);
    }

    [Fact]
    public void RuntimeProjects_Should_Not_ReferencePostgreSqlProvider()
    {
        foreach (var projectPath in ProjectFiles("src/Runtime"))
        {
            Assert.DoesNotContain(
                ProjectReferences(XDocument.Load(projectPath)),
                reference => reference.EndsWith(
                    "CrestCreates.Runtime.Persistence.PostgreSql.csproj",
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void RuntimePersistenceAbstractions_DoNotReferenceWorkflowHumanTaskOrProviders()
    {
        var references = ProjectReferences(LoadRequiredProject(AbstractionsProject));

        Assert.DoesNotContain(references, reference =>
            reference.Contains("Workflow", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("HumanTask", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Accountability", StringComparison.OrdinalIgnoreCase)
            || IsProviderReference(reference));
    }

    [Fact]
    public void PersistenceProjects_MayReferenceRuntimeAbstractionsButNotConcreteRuntimes()
    {
        foreach (var projectPath in ProjectFiles("src/Persistence"))
        {
            Assert.DoesNotContain(
                ProjectReferences(XDocument.Load(projectPath)),
                reference =>
                    EndsWithProject(reference, "CrestCreates.Workflow")
                    || EndsWithProject(reference, "CrestCreates.HumanTask")
                    || EndsWithProject(reference, "CrestCreates.Agent.Runtime"));
        }
    }

    [Fact]
    public void RuntimePersistenceTesting_ShouldBeRunnerFree()
    {
        var project = LoadRequiredProject(TestingProject);
        var packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        var isTestProject = project.Descendants("IsTestProject")
            .Select(element => element.Value)
            .SingleOrDefault();

        Assert.Equal("false", isTestProject);
        Assert.DoesNotContain(packages, package =>
            package.Contains("xunit", StringComparison.OrdinalIgnoreCase)
            || package.Contains("NUnit", StringComparison.OrdinalIgnoreCase)
            || package.Contains("Test.Sdk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimePersistenceTesting_DoesNotReferenceRuntimePersistenceConcrete()
    {
        var references = ProjectReferences(LoadRequiredProject(TestingProject));

        Assert.DoesNotContain(references, reference =>
            EndsWithProject(reference, "CrestCreates.Runtime.Persistence")
            || reference.Contains("Runtime.Persistence.InMemory", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Runtime.Persistence.PostgreSql", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkflowRuntime_ShouldNotReferenceRuntimePersistenceConcrete()
        => AssertProjectDoesNotReferenceRuntimePersistenceConcrete(
            "src/Runtime/Workflow/CrestCreates.Workflow/CrestCreates.Workflow.csproj");

    [Fact]
    public void HumanTaskRuntime_ShouldNotReferenceRuntimePersistenceConcrete()
        => AssertProjectDoesNotReferenceRuntimePersistenceConcrete(
            "src/Runtime/HumanTask/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj");

    [Fact]
    public void RuntimePersistenceConcrete_ShouldReferenceOnlyApprovedAbstractions()
    {
        var references = ProjectReferences(LoadRequiredProject(RuntimeProject));

        Assert.DoesNotContain(references, reference =>
            reference.Contains("Workflow", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("HumanTask", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Accountability", StringComparison.OrdinalIgnoreCase)
            || IsProviderReference(reference));
    }

    private static void AssertProjectDoesNotReferenceRuntimePersistenceConcrete(string relativePath)
    {
        var references = ProjectReferences(LoadRequiredProject(relativePath));

        Assert.DoesNotContain(references, reference =>
            EndsWithProject(reference, "CrestCreates.Runtime.Persistence")
            || reference.Contains("Runtime.Persistence.InMemory", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Runtime.Persistence.PostgreSql", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsProviderType(string source) =>
        source.Contains("using Npgsql", StringComparison.Ordinal)
        || source.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || source.Contains("DbConnection", StringComparison.Ordinal)
        || source.Contains("DbTransaction", StringComparison.Ordinal)
        || source.Contains("NpgsqlConnection", StringComparison.Ordinal)
        || source.Contains("NpgsqlTransaction", StringComparison.Ordinal);

    private static bool IsProviderReference(string reference) =>
        reference.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
        || reference.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
        || reference.Contains("Runtime.Persistence.InMemory", StringComparison.OrdinalIgnoreCase)
        || reference.Contains("Runtime.Persistence.PostgreSql", StringComparison.OrdinalIgnoreCase);

    private static bool EndsWithProject(string reference, string projectName) =>
        reference.EndsWith($"/{projectName}/{projectName}.csproj", StringComparison.OrdinalIgnoreCase)
        || reference.EndsWith($"\\{projectName}\\{projectName}.csproj", StringComparison.OrdinalIgnoreCase);

    private static XDocument LoadRequiredProject(string relativePath)
    {
        var fullPath = RepositoryPath(relativePath);
        Assert.True(File.Exists(fullPath), $"Required project does not exist: {relativePath}");
        return XDocument.Load(fullPath);
    }

    private static IReadOnlyList<string> ProjectReferences(XDocument project) =>
        project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

    private static IReadOnlyList<string> ProjectFiles(string relativeRoot)
    {
        var root = RepositoryPath(relativeRoot);
        Assert.True(Directory.Exists(root), $"Required project root does not exist: {relativeRoot}");
        return Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).ToArray();
    }

    private static IReadOnlyList<string> ProductionSources(string relativeRoot)
    {
        var root = RepositoryPath(relativeRoot);
        Assert.True(Directory.Exists(root), $"Required source root does not exist: {relativeRoot}");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
    }

    private static string RepositoryPath(string relativePath) =>
        Path.Combine(FindRepositoryRoot().FullName, relativePath);

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, "solutions")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
