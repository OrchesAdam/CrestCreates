using System.Xml.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public sealed class AccountabilityArchitectureTests
{
    [Fact]
    public void AccountabilityAbstractions_DoNotReferenceForbiddenRuntimeLayers()
    {
        var project = LoadProject("src/Runtime/Audit/CrestCreates.Accountability.Abstractions/CrestCreates.Accountability.Abstractions.csproj");
        var references = ProjectReferences(project);

        Assert.DoesNotContain(references, reference =>
            reference.Contains("AuditLogging", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Capability", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Workflow", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("HumanTask", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Agent", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("AspNet", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Platform", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Persistence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AccountabilityTestingReferencesNoConcreteAccountabilityRuntime()
    {
        var project = LoadProject("tests/Shared/CrestCreates.Accountability.Testing/CrestCreates.Accountability.Testing.csproj");
        var references = ProjectReferences(project);

        Assert.DoesNotContain(references, reference =>
            reference.EndsWith("CrestCreates.Accountability.csproj", StringComparison.Ordinal));
        Assert.Contains(references, reference =>
            reference.EndsWith("CrestCreates.Accountability.Abstractions.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void AccountabilityTestingReferencesNoTestRunnerPackage()
    {
        var project = LoadProject("tests/Shared/CrestCreates.Accountability.Testing/CrestCreates.Accountability.Testing.csproj");
        var packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(packages, package =>
            package.Contains("Test", StringComparison.OrdinalIgnoreCase)
            || package.Contains("xunit", StringComparison.OrdinalIgnoreCase)
            || package.Contains("NUnit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AccountabilityTestingIsNotATestProject()
    {
        var project = LoadProject("tests/Shared/CrestCreates.Accountability.Testing/CrestCreates.Accountability.Testing.csproj");
        var isTestProject = project.Descendants("IsTestProject").FirstOrDefault()?.Value;

        Assert.Equal("false", isTestProject);
    }

    [Fact]
    public void EnvelopeDoesNotContainObjectPayloadOrMutableCollections()
    {
        var envelopePath = RepositoryPath("src/Runtime/Audit/CrestCreates.Accountability.Abstractions/Contracts/AuditEnvelope.cs");
        var source = File.ReadAllText(envelopePath);

        Assert.DoesNotContain("object", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IReadOnlyList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IReadOnlyDictionary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("List<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<string, object", source, StringComparison.Ordinal);
    }

    private static XDocument LoadProject(string relativePath)
        => XDocument.Load(RepositoryPath(relativePath));

    private static string[] ProjectReferences(XDocument project)
        => project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

    private static string RepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CrestCreates.slnx");
            if (File.Exists(candidate))
                return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
