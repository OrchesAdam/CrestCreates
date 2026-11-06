using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Dynamic API mainline is compile-time generated: the runtime assemblies only consume frozen
/// registries and typed bindings produced by the source generators. Endpoint discovery or execution
/// by reflection would silently re-open the retired runtime-scanner track, so the shape is locked here.
/// Behavioural coverage stays in <c>tests/Framework/Api/CrestCreates.DynamicApi.Tests</c>.
/// </summary>
public sealed class DynamicApiCompileTimeOnlyArchitectureTests
{
    private static readonly string[] FormalPathRoots =
    [
        "src/Framework/Api/CrestCreates.DynamicApi",
        "src/Framework/Api/CrestCreates.DynamicApi.Abstractions",
    ];

    private static readonly string[] ForbiddenReflectionTokens =
    [
        "MakeGenericMethod(",
        "MakeGenericType(",
        "GetMethod(",
        "GetMethods(",
        "GetProperty(",
        "GetProperties(",
        "GetConstructor(",
        "GetInterfaces(",
        "GetCustomAttribute",
        "Type.GetType(",
        "Assembly.Load(",
        "Assembly.GetExecutingAssembly(",
        "Assembly.GetReferencedAssemblies(",
        "Activator.CreateInstance(",
        "DynamicInvoke(",
        "InvokeMember(",
    ];

    /// <summary>
    /// Diagnostics may print <c>GetType().FullName</c>; discovering members by reflection may not.
    /// </summary>
    private static readonly string[] AllowedReflectionUsageTokens =
    [
        "GetType().FullName",
        "GetType().Name",
        "GetType().Assembly",
    ];

    [Fact]
    public void DynamicApiMainline_Should_Not_DiscoverOrExecuteEndpointsByReflection()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateFormalPathFiles(repoRoot))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    // Prose may name a forbidden pattern; only compilable lines bind the shape.
                    continue;
                }

                if (AllowedReflectionUsageTokens.Any(token => line.Contains(token, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (ForbiddenReflectionTokens.Any(token => line.Contains(token, StringComparison.Ordinal)))
                {
                    violations.Add($"{ArchitecturePath.Relative(repoRoot, file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "The Dynamic API formal path contains runtime reflection discovery or dynamic execution:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations)
            + Environment.NewLine + "Endpoint binding must come from the compile-time generated registries; "
            + "the runtime scanner/executor track was retired with #129 S4 and must not return.");
    }

    /// <summary>
    /// Coverage control: a guard whose scan set went empty would pass vacuously.
    /// </summary>
    [Fact]
    public void Scan_Still_ObservesTheFormalDynamicApiAssemblies()
    {
        var repoRoot = FindRepoRoot();
        var observed = EnumerateFormalPathFiles(repoRoot)
            .Select(file => ArchitecturePath.Relative(repoRoot, file))
            .ToList();

        foreach (var expected in new[]
                 {
                     "src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRegistry.cs",
                     "src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointMapper.cs",
                     "src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointHostActivation.cs",
                 })
        {
            Assert.True(
                observed.Contains(expected, StringComparer.Ordinal),
                $"The Dynamic API compile-time guard no longer scans '{expected}'. "
                + observed.Count + " files were scanned; update the guard instead of letting it pass vacuously.");
        }
    }

    private static IEnumerable<string> EnumerateFormalPathFiles(string repoRoot)
    {
        return FormalPathRoots
            .Select(root => Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !ArchitecturePath.Relative(repoRoot, file).Split('/').Contains("obj", StringComparer.Ordinal))
            .Where(file => !ArchitecturePath.Relative(repoRoot, file).Split('/').Contains("bin", StringComparer.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
