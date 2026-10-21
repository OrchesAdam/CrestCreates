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
    public void Self_contract_scope_and_host_composition_are_frozen()
    {
        var scopes = Fixture.RootElement.GetProperty("declarationScopes");
        var interfaceContract = scopes.GetProperty("interfaceContract");
        interfaceContract.GetProperty("methods").GetString().Should().Contain("contract and its inherited interfaces");
        interfaceContract.GetProperty("anchorMethods").GetString().Should().Be("never enumerated");

        var selfContract = scopes.GetProperty("selfContract");
        selfContract.GetProperty("contractType").GetString().Should().Contain("projection class itself");
        selfContract.GetProperty("methods").GetString().Should().Contain("[AppServiceHttpAction]");
        selfContract.GetProperty("helperMethods").GetString().Should().Be("never exposed");
        selfContract.GetProperty("businessRegistration").GetString().Should().Contain("owner registers the class once");

        var composition = scopes.GetProperty("hostComposition");
        composition.GetProperty("missingRegistrationDiagnostic").GetString().Should().Contain("CCHTTP007");
        composition.GetProperty("zeroRoutesOnFailure").GetBoolean().Should().BeTrue();
        composition.GetProperty("requestTime500Forbidden").GetBoolean().Should().BeTrue();
    }

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
        inputs.Should().Contain("httpOwnerAssemblyName");
        inputs.Should().Contain("contractAssemblyName");
        forbidden.Should().Contain("action short name alone");

        var fields = Fixture.RootElement.GetProperty("manifest").GetProperty("artifactEntry").GetProperty("fields")
            .EnumerateArray().Select(value => value.GetString()!).ToList();
        fields.Should().BeEquivalentTo(new[]
        {
            "OwnerAssemblyName", "OwnerServiceFullName", "ArtifactHintName", "DeclaredTypeFullNames",
            "SourceLocation", "ModelSchemaVersion", "ContentHash", "ContractAssemblyName",
            "ReferencedTypeFullNames",
        }, "the #128 artifact entry shape is reused; contract assembly and referenced types are separate fields");
    }

    [Fact]
    public void Legacy_projects_are_fully_retired_from_the_build_graph()
    {
        var legacy = Fixture.RootElement.GetProperty("legacyProjects");
        legacy.GetProperty("retained").EnumerateArray().Should().BeEmpty(
            "S4 exit requires the three Legacy projects to be archived, not retained behind an allowlist");

        legacy.GetProperty("edges").GetProperty("formalToLegacy").GetString().Should().Be("forbidden");
        legacy.GetProperty("edges").GetProperty("defaultAnalyzerOrPackageIncludesLegacy").GetBoolean().Should().BeFalse();
        legacy.GetProperty("edges").GetProperty("sampleEndpointOwnership").GetString()
            .Should().Be("formal ExplicitHost contributions only");
        legacy.GetProperty("exitEvidence").EnumerateArray().Select(value => value.GetString())
            .Should().Contain(evidence => evidence!.Contains("no Legacy project", StringComparison.Ordinal));

        var legacyNames = new[] { "CrestCreates.DynamicApi.Legacy", "CrestCreates.CodeGenerator.Legacy", "CrestCreates.Web.Legacy" };
        var roots = new[] { "src", "samples", "tests" };
        foreach (var root in roots)
        {
            foreach (var projectFile in Directory.GetFiles(Path.Combine(RepoRoot(), root), "*.csproj", SearchOption.AllDirectories))
            {
                if (projectFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || projectFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(projectFile);
                foreach (var legacyName in legacyNames)
                {
                    text.Should().NotContain(legacyName,
                        $"{Path.GetRelativePath(RepoRoot(), projectFile)} must not reference the archived legacy package");
                }
            }
        }
    }

    [Fact]
    public void Referenced_declaration_scope_is_frozen_with_probes_and_forbidden_shapes()
    {
        var referenced = Fixture.RootElement.GetProperty("referencedAssembly");
        referenced.GetProperty("decision").GetString().Should().Contain("referenced assembly");

        referenced.GetProperty("anchor").GetString().Should().Contain("local marker class");

        var rules = referenced.GetProperty("rules").EnumerateArray().Select(v => v.GetString()!).ToList();
        rules.Should().Contain(rule => rule.Contains("startup CCHTTP007", StringComparison.Ordinal));
        rules.Should().Contain(rule => rule.Contains("contractAssemblyName", StringComparison.Ordinal));

        var forbidden = referenced.GetProperty("forbidden").EnumerateArray().Select(v => v.GetString()!).ToList();
        forbidden.Should().Contain(shape => shape.Contains("assembly-level", StringComparison.Ordinal));
        forbidden.Should().Contain(shape => shape.Contains("scanning referenced assemblies", StringComparison.Ordinal));
        forbidden.Should().Contain(shape => shape.Contains("reflection", StringComparison.Ordinal));

        var probes = referenced.GetProperty("probes");
        foreach (var id in new[] { "R-01", "R-02", "R-03", "R-04", "R-05", "R-06a", "R-06b", "R-06c" })
        {
            probes.TryGetProperty(id, out _).Should().BeTrue($"probe {id} must stay in the frozen plan");
        }

        probes.TryGetProperty("R-06", out _).Should().BeFalse(
            "R-06 was split so each cancellation class has its own contract");
        foreach (var id in new[] { "R-06d", "R-06e", "R-06f" })
        {
            probes.TryGetProperty(id, out _).Should().BeTrue(
                $"probe {id} locks original-source classification and the binding-stage rule");
        }

        var addendum = Path.Combine(RepoRoot(), "docs", "superpowers", "specs",
            "2026-10-17-issue-129-referenced-declaration-scope-addendum.md");
        File.Exists(addendum).Should().BeTrue("the addendum is the design gate for S2");
        var text = File.ReadAllText(addendum);
        text.Should().Contain("ContractType = typeof(",
            "the addendum must show the concrete declaration form, not only prose");
        text.Should().Contain("CancellationToken",
            "the addendum freezes cancellation handling together with the declaration scope");
        text.Should().Contain("程序集级 Attribute",
            "the addendum must name what it deliberately does not add");
    }

    [Fact]
    public void Referenced_types_are_recorded_outside_declared_ownership()
    {
        var ownership = Fixture.RootElement.GetProperty("manifest").GetProperty("ownership");
        ownership.GetProperty("declaredOnly").GetBoolean().Should().BeTrue();
        ownership.GetProperty("declaredTypesField").GetString().Should().Be("DeclaredTypeFullNames");
        ownership.GetProperty("referencedTypesField").GetString().Should().Be("ReferencedTypeFullNames");

        var forbidden = ownership.GetProperty("forbidden").EnumerateArray().Select(v => v.GetString()!).ToList();
        forbidden.Should().Contain(shape => shape.Contains("DeclaredTypeFullNames", StringComparison.Ordinal));

        ownership.GetProperty("authority").GetString().Should().Contain("CrudJsonSurfaceDiscovery.cs",
            "the rule must point at the CCCRUD008 reconciliation that enforces it");
        ownership.GetProperty("evidence").GetString().Should().Contain("CCCRUD008");
    }

    [Fact]
    public void Cancellation_contract_freezes_all_three_classes_and_the_acceptance_delta()
    {
        var cancellation = Fixture.RootElement.GetProperty("failureBridge").GetProperty("cancellation");
        cancellation.GetProperty("principle").GetString().Should().Contain("original cancellation source");
        cancellation.GetProperty("principle").GetString().Should().Contain("never by whether L is canceled");

        var cases = cancellation.GetProperty("cases").EnumerateArray().ToList();
        cases.Select(c => c.GetProperty("kind").GetString()).Should().BeEquivalentTo(
            "callerAbort", "managedDeadline", "nonCallerOperationCanceled", "cancelAfterCommitOrResponseFailure");

        var tokens = cancellation.GetProperty("tokens");
        tokens.GetProperty("R").GetString().Should().Contain("RequestAborted");
        tokens.GetProperty("K").GetString().Should().Contain("deadline");
        tokens.GetProperty("L").GetString().Should().Contain("linked");

        var classification = cancellation.GetProperty("classification").EnumerateArray()
            .Select(v => v.GetString()!).ToList();
        classification.First().Should().Contain("default/None").And.Contain("NonCaller")
            .And.Contain("before any equality matching",
                "None must be handled first, otherwise a default OCE matches a None-valued R and suppresses a live response");
        classification.Should().Contain(step => step.Contains("OCE.Token == R", StringComparison.Ordinal)
                                               && step.Contains("R is canceled", StringComparison.Ordinal),
            "an identity match alone is not enough; the source must actually be canceled");
        classification.Should().Contain(step => step.Contains("OCE.Token == L", StringComparison.Ordinal)
                                               && step.Contains("R canceled", StringComparison.Ordinal),
            "the linked token must be resolved back to R or K, otherwise deadline cancels it too and the rule conflates sources");
        classification.Should().Contain(step => step.Contains("K canceled", StringComparison.Ordinal)
                                               && step.Contains("NonCaller", StringComparison.Ordinal),
            "L with neither source canceled is NonCaller");
        classification.Should().Contain(step => step.Contains("third token", StringComparison.Ordinal)
                                               && step.Contains("NonCaller", StringComparison.Ordinal));

        var edgeRules = cancellation.GetProperty("edgeRules").EnumerateArray().Select(v => v.GetString()!).ToList();
        edgeRules.Should().Contain(rule => rule.Contains("None is handled before identity matching", StringComparison.Ordinal));
        edgeRules.Should().Contain(rule => rule.Contains("IsCancellationRequested", StringComparison.Ordinal));
        edgeRules.Should().Contain(rule => rule.Contains("never a default ManagedDeadline", StringComparison.Ordinal));

        cancellation.GetProperty("precedence").GetString().Should().Contain("CallerAbort wins");
        cancellation.GetProperty("precedence").GetString().Should().Contain("both");
        cancellation.GetProperty("responseSuppression").GetString().Should().Contain("suppress the body")
            .And.Contain("regardless of classification");

        var deadline = cases.Single(c => c.GetProperty("kind").GetString() == "managedDeadline");
        deadline.GetProperty("http").GetString().Should().Contain("Crest.InternalError");
        deadline.GetProperty("http").GetString().Should().Contain("sanitized");
        deadline.GetProperty("forbidden").EnumerateArray().Select(v => v.GetString()!)
            .Should().Contain(rule => rule.Contains("re-execute", StringComparison.Ordinal));

        var nonCaller = cases.Single(c => c.GetProperty("kind").GetString() == "nonCallerOperationCanceled");
        nonCaller.GetProperty("detect").GetString().Should().Contain("third token");
        nonCaller.GetProperty("detect").GetString().Should().Contain("None");
        nonCaller.GetProperty("forbidden").EnumerateArray().Select(v => v.GetString()!)
            .Should().Contain(rule => rule.Contains("caller cancellation", StringComparison.Ordinal));

        cancellation.GetProperty("bindingStage").GetString().Should().Contain("same classifier");
        cancellation.GetProperty("bindingStage").GetString().Should().Contain("OCE(R)");
        cancellation.GetProperty("bindingStage").GetString().Should().Contain("OCE(K)");

        var delta = Fixture.RootElement.GetProperty("acceptanceMatrixDelta");
        foreach (var row in new[] { "H02", "H07", "N01/N02", "D01" })
        {
            delta.TryGetProperty(row, out _).Should().BeTrue($"acceptance row {row} must be extended explicitly");
        }

        var addendum = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "superpowers", "specs",
            "2026-10-17-issue-129-referenced-declaration-scope-addendum.md"));
        addendum.Should().Contain("ReferencedTypeFullNames");
        addendum.Should().Contain("非调用方 OCE");
        addendum.Should().Contain("500 `Crest.InternalError`");
        addendum.Should().Contain("不写 JSON 错误体");
        addendum.Should().Contain("OCE.Token == L",
            "the addendum must resolve the linked token back to its original source");
        addendum.Should().Contain("CallerAbort 优先",
            "the both-canceled precedence must be explicit");
        addendum.Should().Contain("第三 token",
            "the binding-stage rule must not blanket-map to caller abort");
        addendum.Should().Contain("None 先处理",
            "the addendum must establish identity absence before equality matching");
        addendum.Should().Contain("L 的来源都未取消",
            "the linked token must not default to a deadline when neither source is canceled");
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
