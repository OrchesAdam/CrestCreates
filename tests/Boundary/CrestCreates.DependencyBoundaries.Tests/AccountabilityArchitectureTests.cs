using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public sealed class AccountabilityArchitectureTests
{
    [Fact]
    public void AbstractionsDoNotReferenceProducerOrAspNetCoreAssemblies()
        => AccountabilityAbstractions_DoNotReferenceForbiddenRuntimeLayers();

    [Fact]
    public void ProducersReferenceOnlyAccountabilityAbstractions()
    {
        var projects = new[]
        {
            "src/Runtime/Audit/CrestCreates.AuditLogging/CrestCreates.AuditLogging.csproj",
            "src/Runtime/Capability/CrestCreates.Capability/CrestCreates.Capability.csproj",
            "src/Runtime/Workflow/CrestCreates.Workflow/CrestCreates.Workflow.csproj"
        };

        foreach (var projectPath in projects)
        {
            var references = ProjectReferences(LoadProject(projectPath));
            Assert.Contains(references, reference =>
                reference.EndsWith("CrestCreates.Accountability.Abstractions.csproj", StringComparison.Ordinal));
            Assert.DoesNotContain(references, reference =>
                reference.EndsWith("CrestCreates.Accountability.csproj", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ProducersDoNotReferenceIAuditSink()
    {
        foreach (var file in ProducerSourceFiles())
            Assert.DoesNotContain("IAuditSink", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void EnvelopeContainsNoObjectOrMutableCollectionPayload()
        => EnvelopeDoesNotContainObjectPayloadOrMutableCollections();

    [Fact]
    public void AccountabilityUsesJsonContractBuildTasks()
    {
        var project = File.ReadAllText(RepositoryPath(
            "src/Runtime/Audit/CrestCreates.Accountability.Abstractions/CrestCreates.Accountability.Abstractions.csproj"));

        Assert.Contains("CrestCreates.JsonContracts.BuildTasks", project, StringComparison.Ordinal);
        Assert.Contains("CrestCreates.JsonContracts.Build.Repository.targets", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountabilityUsesCanonicalHashRuntime()
    {
        var project = ProjectReferences(LoadProject(
            "src/Runtime/Audit/CrestCreates.Accountability/CrestCreates.Accountability.csproj"));
        var hasher = File.ReadAllText(RepositoryPath(
            "src/Runtime/Audit/CrestCreates.Accountability/CanonicalHashing/DefaultAuditIntegrityHasher.cs"));

        Assert.Contains(project, reference =>
            reference.EndsWith("CrestCreates.Metadata.csproj", StringComparison.Ordinal));
        Assert.Contains("ICanonicalHashComputer", hasher, StringComparison.Ordinal);
    }

    [Fact]
    public void NoAccountabilityReflectionJsonOrIL2026Suppression()
    {
        var root = RepositoryPath("src/Runtime/Audit");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("Accountability", StringComparison.Ordinal));

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("JsonSerializer.Serialize(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JsonSerializer.Deserialize(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IL2026", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RequiresUnreferencedCode", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CapabilityMiddlewareDoesNotUseICapabilityAuditStore()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Capability/CrestCreates.Capability/Middleware/AuditMiddleware.cs"));

        Assert.Contains("IAuditRecorder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ICapabilityAuditStore", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyAppendOnlyStoreIsNotRegisteredAsAuditSink()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Capability/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs"));

        Assert.DoesNotContain("IAuditSink", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyCapabilityAuditStoreSink", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityMappingDoesNotCountAsAcceptedRecord()
    {
        var writer = File.ReadAllText(RepositoryPath(
            "src/Runtime/Audit/CrestCreates.AuditLogging/Services/AuditLogWriter.cs"));

        Assert.Contains("IAuditRecorder", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("IAuditSink", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAccepted = true", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityAuditRecordIdComesOnlyFromContractCompliantSink()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Capability/CrestCreates.Capability/Middleware/AuditMiddleware.cs"));

        Assert.Contains("record.IsAccepted ? record.AuditId : null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoSideDictionaryPretendsToProvideDurableIdempotency()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Capability/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs"));

        Assert.DoesNotContain("LegacyCapabilityAuditStoreSink", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowLifecycleEventContainsNoObjectPayload()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Workflow/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs"));

        Assert.DoesNotContain("object", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentGovernanceAuditorRemainsIndependent()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs"));

        Assert.Contains("IAgentToolInvocationAuditor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IAuditRecorder", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewRuntimeSpecificPrimaryAuditStoresAreForbidden()
    {
        foreach (var file in ProducerSourceFiles())
            Assert.DoesNotMatch(new Regex(@"\:\s*IAuditSink\b", RegexOptions.CultureInvariant), File.ReadAllText(file));
    }

    [Fact]
    public void SpecSection18AcceptanceTestLedgerIsComplete()
    {
        var spec = File.ReadAllLines(RepositoryPath(
            "docs/superpowers/specs/2026-07-28-phase-9a-accountability-runtime-foundation-design.md"));
        var required = spec
            .SkipWhile(line => line != "### 18.1 Contract and validation")
            .TakeWhile(line => line != "## 19. Implementation Slices")
            .Select(line => Regex.Match(line, @"^  (?<name>[A-Z][A-Za-z0-9]*)$"))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourceRoots = new[] { RepositoryPath("tests"), RepositoryPath("samples/ProcurementApproval/tests") };
        var testSources = sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Select(File.ReadAllText)
            .ToArray();
        var missing = required
            .Where(name => !testSources.Any(source => Regex.IsMatch(source,
                $@"\[(?:Fact|Theory)\][\s\S]{{0,800}}?public\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|void)\s+{Regex.Escape(name)}\s*\(",
                RegexOptions.CultureInvariant)))
            .ToArray();

        Assert.True(required.Length > 100, "Spec §18 acceptance ledger unexpectedly contains too few tests.");
        Assert.Empty(missing);
    }

    [Fact]
    public void ProcurementMainlineAndNativeAotAcceptanceTestsAreGuarded()
    {
        var required = new[]
        {
            "PlatformHttpCapabilitySharesAccountabilityCorrelation",
            "NativeAotBinary_RunsGoldenScenarioAndExits",
            "NativeAotBinary_PrintsSuccessSentinel",
            "NativeAot_HttpSubmitAndGet_UseRealEndpoint"
        };
        var testSources = Directory
            .EnumerateFiles(RepositoryPath("samples/ProcurementApproval/tests"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var name in required)
        {
            Assert.Contains(testSources, source => Regex.IsMatch(source,
                $@"\[(?:Fact|Theory)\][\s\S]{{0,800}}?public\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|void)\s+{Regex.Escape(name)}\s*\(",
                RegexOptions.CultureInvariant));
        }
    }

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

    [Fact]
    public void UseCrestWebUsesAccountabilityHttpMainline()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Platform/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs"));

        Assert.Contains("app.UseAccountabilityHttpTerminalObserver();", source, StringComparison.Ordinal);
        Assert.Contains("app.UseAccountabilityHttpOperationScope();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("app.UseAuditLogging();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UseCrestWebPreservesGlobalExceptionCoverageAndAuthenticatedOperationScope()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Platform/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs"));

        Assert.True(
            source.IndexOf("app.UseAccountabilityHttpTerminalObserver();", StringComparison.Ordinal)
            < source.IndexOf("app.UseExceptionHandling();", StringComparison.Ordinal));
        Assert.True(
            source.IndexOf("app.UseExceptionHandling();", StringComparison.Ordinal)
            < source.IndexOf("app.UseRouting();", StringComparison.Ordinal));
        Assert.True(
            source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal)
            < source.IndexOf("app.UseAccountabilityHttpOperationScope();", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyAuditLoggingIsNotEnabledByDefault()
    {
        var source = File.ReadAllText(RepositoryPath(
            "samples/LibraryManagement/LibraryManagement.Web/Program.cs"));

        Assert.Contains("app.UseAccountabilityHttpTerminalObserver();", source, StringComparison.Ordinal);
        Assert.Contains("app.UseAccountabilityHttpOperationScope();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("app.UseAuditLogging();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryManagementPreservesGlobalExceptionCoverageAndAuthenticatedOperationScope()
    {
        var source = File.ReadAllText(RepositoryPath(
            "samples/LibraryManagement/LibraryManagement.Web/Program.cs"));

        Assert.True(
            source.IndexOf("app.UseAccountabilityHttpTerminalObserver();", StringComparison.Ordinal)
            < source.IndexOf("app.UseExceptionHandling();", StringComparison.Ordinal));
        Assert.True(
            source.IndexOf("app.UseExceptionHandling();", StringComparison.Ordinal)
            < source.IndexOf("app.UseRouting();", StringComparison.Ordinal));
        Assert.True(
            source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal)
            < source.IndexOf("app.UseAccountabilityHttpOperationScope();", StringComparison.Ordinal));
    }

    [Fact]
    public void AuditLogWriterDoesNotDirectlyPersistLegacyAuditLog()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Audit/CrestCreates.AuditLogging/Services/AuditLogWriter.cs"));

        Assert.Contains("IAuditRecorder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IAuditLogService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToAuditLog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFirstPartyHostUsesLegacyHttpAuditMainline()
    {
        var root = Path.GetDirectoryName(RepositoryPath("CrestCreates.slnx"))!;
        var firstPartyFiles = Directory.EnumerateFiles(Path.Combine(root, "samples"), "*.cs", SearchOption.AllDirectories)
            .Append(RepositoryPath("src/Platform/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs"));

        foreach (var file in firstPartyFiles)
            Assert.DoesNotContain(".UseAuditLogging();", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void AccountabilityJsonContextHasNoHandwrittenTransitiveRootLedger()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Audit/CrestCreates.Accountability.Abstractions/Json/AccountabilityJsonSerializerContext.cs"));

        Assert.DoesNotContain("JsonContractExplicitRoot", source, StringComparison.Ordinal);
        Assert.Contains("JsonContractSurface", source, StringComparison.Ordinal);
        Assert.Contains("ExcludedParameterTypes", source, StringComparison.Ordinal);
    }

    private static XDocument LoadProject(string relativePath)
        => XDocument.Load(RepositoryPath(relativePath));

    private static string[] ProjectReferences(XDocument project)
        => project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

    private static IEnumerable<string> ProducerSourceFiles()
    {
        var roots = new[]
        {
            "src/Runtime/Audit/CrestCreates.AuditLogging",
            "src/Runtime/Capability/CrestCreates.Capability",
            "src/Runtime/Workflow/CrestCreates.Workflow"
        };
        return roots.SelectMany(root => Directory.EnumerateFiles(
            RepositoryPath(root), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

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
