using System.Xml.Linq;
using Xunit;

namespace CrestCreates.Agent.Memory.Accountability.Tests.Architecture;

/// <summary>
/// Design §15.5 — structural guards that keep the Agent Memory Accountability
/// bridge a thin compile-time mainline and prevent dependency leakage into
/// read-runtime primitives, the Accountability runtime, or reflection-heavy
/// serialization fallbacks.
/// </summary>
public sealed class AgentMemoryAccountabilityArchitectureTests
{
    [Fact]
    public void Store_Should_NotReferenceAccountability()
    {
        var project = LoadProject("src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj");
        var references = ProjectReferences(project);
        var store = File.ReadAllText(RepositoryPath(
            "src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentMemoryStore.cs"));

        Assert.DoesNotContain(references, reference =>
            reference.Contains("Accountability", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Accountability", store, StringComparison.Ordinal);
    }

    [Fact]
    public void Retriever_Should_NotReferenceAccountabilityProducerOrRecorder()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Agent/CrestCreates.Agent.Memory/Recall/DefaultAgentMemoryRetriever.cs"));

        Assert.DoesNotContain("AgentMemoryAccountabilityProducer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IAuditRecorder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Accountability", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expander_Should_NotReferenceAccountabilityProducerOrRecorder()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src/Runtime/Agent/CrestCreates.Agent.Memory/Recall/DefaultAgentContextSourceExpander.cs"));

        Assert.DoesNotContain("AgentMemoryAccountabilityProducer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IAuditRecorder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Accountability", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Producer_Should_NotReferenceIAuditSink()
    {
        foreach (var file in BridgeSourceFiles())
            Assert.DoesNotContain("IAuditSink", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Accountability_Should_NotReferenceAgentMemory()
    {
        var projects = new[]
        {
            "src/Runtime/Audit/CrestCreates.Accountability/CrestCreates.Accountability.csproj",
            "src/Runtime/Audit/CrestCreates.Accountability.Abstractions/CrestCreates.Accountability.Abstractions.csproj"
        };

        foreach (var projectPath in projects)
        {
            var references = ProjectReferences(LoadProject(projectPath));
            Assert.DoesNotContain(references, reference =>
                reference.Contains("Agent", StringComparison.OrdinalIgnoreCase)
                || reference.Contains("Memory", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Bridge_Should_NotReferenceAgentToolsMcpOrPostgreSql()
    {
        var project = LoadProject(
            "src/Runtime/Agent/CrestCreates.Agent.Memory.Accountability/CrestCreates.Agent.Memory.Accountability.csproj");
        var references = ProjectReferences(project);

        Assert.DoesNotContain(references, reference =>
            reference.Contains("Tools", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Mcp", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("PostgreSql", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Postgres", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Platform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Payloads_Should_HaveGeneratedJsonContracts()
    {
        var context = File.ReadAllText(RepositoryPath(
            "src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Json/AgentMemoryAccountabilityJsonSerializerContext.cs"));
        var payloads = File.ReadAllText(RepositoryPath(
            "src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Accountability/AgentMemoryAccountabilityPayloads.cs"));

        Assert.Contains("[JsonSerializable(typeof(AgentMemoryRecallAccountabilityPayload))]", context, StringComparison.Ordinal);
        Assert.Contains("[JsonSerializable(typeof(AgentMemoryCurationAccountabilityPayload))]", context, StringComparison.Ordinal);
        Assert.Contains("[JsonSerializable(typeof(AgentMemorySourceExpansionAccountabilityPayload))]", context, StringComparison.Ordinal);
        Assert.Contains("JsonSourceGenerationMode.Metadata", context, StringComparison.Ordinal);
        Assert.Contains("JsonSerializerContext", context, StringComparison.Ordinal);

        Assert.DoesNotContain("JsonSerializer.Serialize(", payloads, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize(", payloads, StringComparison.Ordinal);
    }

    [Fact]
    public void NoReflectionSerializationFallback_Should_Exist()
    {
        var bridgeRoot = RepositoryPath("src/Runtime/Agent/CrestCreates.Agent.Memory.Accountability");
        var abstractionsRoot = RepositoryPath("src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions");
        var files = Directory.EnumerateFiles(bridgeRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(abstractionsRoot, "Accountability"), "*.cs", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(Path.Combine(abstractionsRoot, "Json"), "*.cs", SearchOption.TopDirectoryOnly))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("JsonSerializer.Serialize(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JsonSerializer.Deserialize(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IL2026", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RequiresUnreferencedCode", source, StringComparison.Ordinal);
        }
    }

    private static XDocument LoadProject(string relativePath)
        => XDocument.Load(RepositoryPath(relativePath));

    private static string[] ProjectReferences(XDocument project)
        => project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

    private static IEnumerable<string> BridgeSourceFiles()
        => Directory.EnumerateFiles(
                RepositoryPath("src/Runtime/Agent/CrestCreates.Agent.Memory.Accountability"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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
