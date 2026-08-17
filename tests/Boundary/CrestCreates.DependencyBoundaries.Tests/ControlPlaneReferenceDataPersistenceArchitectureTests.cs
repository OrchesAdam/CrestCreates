using System.Xml.Linq;
using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class ControlPlaneReferenceDataPersistenceArchitectureTests
{
    [Fact]
    public void SharedTestingProject_Should_NotReferenceNpgsqlOrProvider()
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var csprojPath = Path.Combine(repoRoot.FullName,
            "tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing.csproj");

        File.Exists(csprojPath).Should().BeTrue("the shared testing project must exist");

        var doc = XDocument.Load(csprojPath);
        var references = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference" || e.Name.LocalName == "PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .ToList();

        references.Should().NotContain(r => r.Contains("Npgsql", StringComparison.OrdinalIgnoreCase),
            "the shared testing project must not reference Npgsql");
        references.Should().NotContain(r => r.Contains("PostgreSql", StringComparison.OrdinalIgnoreCase),
            "the shared testing project must not reference PostgreSQL provider");
        references.Should().NotContain(r => r.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase),
            "the shared testing project must not reference Testcontainers");
        references.Should().NotContain(r => r.Contains("xunit", StringComparison.OrdinalIgnoreCase),
            "the shared testing project must not reference xUnit (it is not a test project)");
    }

    [Fact]
    public void SharedTestingProject_Should_OnlyReferenceAbstractions()
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var csprojPath = Path.Combine(repoRoot.FullName,
            "tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing.csproj");

        var doc = XDocument.Load(csprojPath);
        var projectRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .ToList();

        projectRefs.Should().HaveCount(2, "the shared kit references exactly two abstraction projects");
        projectRefs.Should().Contain(r => r.Contains("DescriptorDraft.Abstractions"),
            "must reference DescriptorDraft.Abstractions");
        projectRefs.Should().Contain(r => r.Contains("Organization.Abstractions"),
            "must reference Organization.Abstractions");
    }

    [Fact]
    public void SharedTestingProject_IsNotTestProject()
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var csprojPath = Path.Combine(repoRoot.FullName,
            "tests/Shared/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing/CrestCreates.ControlPlane.ReferenceData.Persistence.Testing.csproj");

        var doc = XDocument.Load(csprojPath);
        var isTestProject = doc.Descendants()
            .Where(e => e.Name.LocalName == "IsTestProject")
            .Select(e => e.Value)
            .FirstOrDefault();

        isTestProject.Should().Be("false", "the shared kit is a testing contract library, not a test project");
    }
}
