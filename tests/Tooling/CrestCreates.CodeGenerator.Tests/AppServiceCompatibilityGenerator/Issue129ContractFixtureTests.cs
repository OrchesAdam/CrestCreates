using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.AppServiceCompatibilityGenerator;

/// <summary>
/// Issue #129 rev.2 14.7.3: the frozen contract fixture is checked against the repository it claims to
/// describe, so a table that drifts from the real error codes or the real Ticket contract fails here
/// instead of in a later slice.
/// </summary>
public class Issue129ContractFixtureTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));

    [Fact]
    public void Failure_bridge_codes_exist_in_the_exception_converter_authority()
    {
        var errorCodes = Read("src/Framework/Ddd/CrestCreates.Domain.Shared/Exceptions/CrestErrorCodes.cs");
        var converter = Read("src/Framework/Web/CrestCreates.AspNetCore/Errors/DefaultCrestExceptionConverter.cs");
        var rows = Fixture.RootElement.GetProperty("failureBridge").GetProperty("codeStatusTable").EnumerateArray().ToList();

        rows.Should().HaveCount(9);
        foreach (var row in rows)
        {
            var code = row.GetProperty("code").GetString()!;
            errorCodes.Should().Contain($"\"{code}\"",
                "the bridge may only reuse an ErrorCode the Web authority already declares");
        }

        foreach (var authority in new[]
                 {
                     "UnauthorizedAccessException",
                     "CrestPermissionException",
                     "KeyNotFoundException",
                     "ValidationException",
                     "ArgumentException",
                     "InvalidOperationException",
                 })
        {
            converter.Should().Contain(authority,
                "the bridge must reuse these classifications rather than invent a Ticket-local switch");
        }

        errorCodes.Should().Contain("Crest.Concurrency.Conflict");
        errorCodes.Should().Contain("Crest.Concurrency.PreconditionRequired");
    }

    [Fact]
    public void Failure_bridge_never_leaks_transport_types_into_the_runtime_facts()
    {
        var forbidden = Fixture.RootElement.GetProperty("failureBridge").GetProperty("factsMustNotCarry").EnumerateArray()
            .Select(value => value.GetString()!).ToList();

        forbidden.Should().Contain("HttpStatusCode");
        forbidden.Should().Contain("stack trace");

        var runtimeProjects = Path.Combine(RepoRoot(), "src", "Runtime");
        Directory.Exists(runtimeProjects).Should().BeTrue();
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(runtimeProjects, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (text.Contains("CapabilityFailureKind", StringComparison.Ordinal)
                && (text.Contains("Microsoft.AspNetCore", StringComparison.Ordinal) || text.Contains("HttpStatusCode", StringComparison.Ordinal)))
            {
                offenders.Add(Path.GetRelativePath(RepoRoot(), file));
            }
        }

        offenders.Should().BeEmpty(
            "if the failure facts land in Runtime they must stay transport neutral");
    }

    [Fact]
    public void Permission_map_covers_exactly_the_ticket_contract_methods()
    {
        var contract = Read("samples/SaaSHelpdesk/SaaSHelpdesk.Application.Contracts/Interfaces/ITicketAppService.cs");
        var declared = contract.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Task", StringComparison.Ordinal) && line.Contains("Async(", StringComparison.Ordinal))
            .Select(line => line[..(line.IndexOf("Async(", StringComparison.Ordinal) + 5)].Split(' ').Last())
            .Select(name => name.Replace("(", string.Empty))
            .ToList();

        declared.Should().HaveCount(11, "the frozen Ticket contract is eleven methods");

        var map = Fixture.RootElement.GetProperty("permissions").GetProperty("actionToPermission");
        var mapped = map.EnumerateObject().Select(property => property.Name).ToList();

        mapped.Should().BeEquivalentTo(declared,
            "every contract action needs one frozen permission decision and no action may be added silently");

        var formal = map.EnumerateObject()
            .Select(property => property.Value.GetString()!)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        formal.Should().BeEquivalentTo(
            new[] { "Ticket.Create", "Ticket.Delete", "Ticket.Get", "Ticket.Search", "Ticket.Update" },
            "rev.2 freezes exactly five Ticket definitions and no sixth name such as Ticket.GetAll");

        map.GetProperty("GetAllAsync").GetString().Should().Be("Ticket.Search");
        foreach (var write in new[] { "AssignAsync", "ResolveAsync", "CloseAsync" })
        {
            map.GetProperty(write).GetString().Should().Be("Ticket.Update");
        }
    }

    [Fact]
    public void Manifest_identity_inputs_reject_the_short_name_only_shape()
    {
        var inputs = Fixture.RootElement.GetProperty("manifest").GetProperty("actionIdentity").GetProperty("inputs")
            .EnumerateArray().Select(value => value.GetString()!).ToList();
        var forbidden = Fixture.RootElement.GetProperty("manifest").GetProperty("actionIdentity").GetProperty("forbidden")
            .EnumerateArray().Select(value => value.GetString()!).ToList();

        inputs.Should().Contain("ownerAssemblyName");
        inputs.Should().Contain("ownerServiceFullMetadataName");
        inputs.Should().Contain("closedContractMethodSignature");
        forbidden.Should().Contain("action short name alone");

        var fields = Fixture.RootElement.GetProperty("manifest").GetProperty("artifactEntry").GetProperty("fields")
            .EnumerateArray().Select(value => value.GetString()!).ToList();
        fields.Should().BeEquivalentTo(new[]
        {
            "OwnerAssemblyName", "OwnerServiceFullName", "ArtifactHintName", "DeclaredTypeFullNames",
            "SourceLocation", "ModelSchemaVersion", "ContentHash",
        }, "the #128 artifact entry shape is reused, not reinvented");
    }

    [Fact]
    public void Legacy_dependency_fixture_has_no_formal_to_legacy_edge()
    {
        var legacy = Fixture.RootElement.GetProperty("legacyProjects");
        legacy.EnumerateObject().Select(property => property.Name)
            .Where(name => name != "edges")
            .Should().BeEquivalentTo(
            "CrestCreates.DynamicApi.Legacy", "CrestCreates.CodeGenerator.Legacy", "CrestCreates.Web.Legacy");

        legacy.GetProperty("edges").GetProperty("formalToLegacy").GetString().Should().Be("forbidden");
        legacy.GetProperty("edges").GetProperty("defaultAnalyzerOrPackageIncludesLegacy").GetBoolean().Should().BeFalse();

        var legacyNames = new[] { "CrestCreates.DynamicApi.Legacy", "CrestCreates.CodeGenerator.Legacy", "CrestCreates.Web.Legacy" };
        foreach (var projectFile in Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            if (legacyNames.Any(name => Path.GetFileNameWithoutExtension(projectFile).Equals(name, StringComparison.Ordinal)))
            {
                continue;
            }

            var text = File.ReadAllText(projectFile);
            foreach (var legacyName in legacyNames)
            {
                text.Should().NotContain(legacyName,
                    $"formal project {Path.GetRelativePath(RepoRoot(), projectFile)} must not depend on the isolated legacy package");
            }
        }
    }

    private static string FixturePath()
        => Path.Combine(RepoRoot(), "docs", "superpowers", "specs", "fixtures", "issue-129-projection-contracts.json");

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("global.json not found above the test output");
    }
}
