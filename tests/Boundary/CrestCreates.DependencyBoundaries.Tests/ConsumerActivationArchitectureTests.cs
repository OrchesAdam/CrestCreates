using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public sealed class ConsumerActivationArchitectureTests
{
    private static readonly string[] DeliverySourceRoots = new[]
    {
        "src/Runtime/Eventing/CrestCreates.Runtime.Delivery",
        "src/Runtime/Eventing/CrestCreates.Runtime.Delivery.Abstractions",
    };

    private static readonly string[] GeneratorSourceRoots = new[]
    {
        "src/Tooling/CrestCreates.CodeGenerator/OutboxConsumerActivationGenerator",
    };

    private static readonly string[] ForbiddenReflectionTokens = new[]
    {
        "Activator.CreateInstance",
        "ActivatorUtilities.CreateInstance",
        "ActivatorUtilities.GetServiceOrCreateInstance",
        "GetConstructors(",
        "GetConstructor(",
        "ConstructorInfo",
    };

    private static readonly string[] ForbiddenPluginTokens = new[]
    {
        "Assembly.Load(",
        "Assembly.LoadFrom(",
        "Assembly.GetExecutingAssembly(",
        "FactoryTable",
    };

    [Fact]
    public void ConsumerActivation_Should_Not_Use_RuntimeReflectionFallback()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var root in DeliverySourceRoots.Concat(GeneratorSourceRoots))
        {
            var fullPath = Path.Combine(repoRoot, root);
            if (!Directory.Exists(fullPath)) continue;

            var files = Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

            foreach (var file in files)
            {
                var lines = File.ReadLines(file).ToArray();
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (ForbiddenReflectionTokens.Any(token => line.Contains(token, StringComparison.Ordinal)))
                    {
                        var relativePath = Path.GetRelativePath(repoRoot, file);
                        violations.Add($"{relativePath}:{i + 1}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Consumer activation sources contain forbidden runtime reflection fallbacks:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ConsumerActivation_Should_Not_Create_A_GenericPluginBoundary()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var root in DeliverySourceRoots.Concat(GeneratorSourceRoots))
        {
            var fullPath = Path.Combine(repoRoot, root);
            if (!Directory.Exists(fullPath)) continue;

            var files = Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

            foreach (var file in files)
            {
                var lines = File.ReadLines(file).ToArray();
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (ForbiddenPluginTokens.Any(token => line.Contains(token, StringComparison.Ordinal)))
                    {
                        var relativePath = Path.GetRelativePath(repoRoot, file);
                        violations.Add($"{relativePath}:{i + 1}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Consumer activation sources contain forbidden plugin-boundary patterns:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ConsumerRegistration_Should_UseStaticFactoryDelegate()
    {
        var repoRoot = FindRepoRoot();
        var extensionsFile = Path.Combine(
            repoRoot,
            "src/Runtime/Eventing/CrestCreates.Runtime.Delivery/DeliveryServiceCollectionExtensions.cs");

        Assert.True(File.Exists(extensionsFile), $"Expected file at {extensionsFile}");

        var content = File.ReadAllText(extensionsFile);

        Assert.DoesNotContain("services.AddScoped<TConsumer>()", content, StringComparison.Ordinal);
        Assert.Contains("CreateOutboxConsumer", content, StringComparison.Ordinal);
    }

    [Fact]
    public void HostProjects_Should_Not_Contain_ConsumerConcreteFactories()
    {
        var repoRoot = FindRepoRoot();
        var hostPrograms = new[]
        {
            "samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Program.cs",
            "samples/ProcurementApproval/src/CrestCreates.Sample.ProcurementApproval.Host/Program.cs",
        };

        var violations = new List<string>();

        foreach (var programPath in hostPrograms)
        {
            var fullPath = Path.Combine(repoRoot, programPath);
            if (!File.Exists(fullPath))
            {
                var altPath = programPath.Replace(
                    "CrestCreates.Sample.ProcurementApproval.Host",
                    "CrestCreates.Sample.Procurement.Host");
                fullPath = Path.Combine(repoRoot, altPath);
                if (!File.Exists(fullPath)) continue;
            }

            var lines = File.ReadLines(fullPath).ToArray();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Detect hand-written consumer/handler factory replacements like:
                // builder.Services.Replace(ServiceDescriptor.Scoped<ProcurementHumanTaskDecisionHandler>(sp => ...
                // The pattern is Replace( + ServiceDescriptor.Scoped<SingleType> (one type param, not two)
                // Legitimate replacements like Replace(ServiceDescriptor.Singleton<IService, Impl>()) have two type params
                if (line.Contains("Replace(", StringComparison.Ordinal) &&
                    line.Contains("ServiceDescriptor.Scoped<", StringComparison.Ordinal))
                {
                    // Check if it's a single-type parameter (consumer factory) vs two-type parameter (legitimate)
                    var scopedStart = line.IndexOf("ServiceDescriptor.Scoped<", StringComparison.Ordinal);
                    if (scopedStart >= 0)
                    {
                        var afterScoped = line.Substring(scopedStart + "ServiceDescriptor.Scoped<".Length);
                        var commaPos = afterScoped.IndexOf(',');
                        var closePos = afterScoped.IndexOf('>');
                        // If close comes before comma (or no comma), it's a single-type parameter
                        if (closePos >= 0 && (commaPos < 0 || closePos < commaPos))
                        {
                            violations.Add($"{programPath}:{i + 1}: {line.Trim()}");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Host Program.cs files still contain consumer concrete factory Replace patches:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void HostFactoryDetector_Should_Catch_ProcurementHandlerPattern()
    {
        // Verify the detector would catch the actual Procurement pattern that was removed
        var procurementPattern = "builder.Services.Replace(ServiceDescriptor.Scoped<ProcurementHumanTaskDecisionHandler>(sp =>";
        Assert.True(procurementPattern.Contains("Replace(", StringComparison.Ordinal), "pattern must contain Replace(");
        Assert.True(procurementPattern.Contains("ServiceDescriptor.Scoped<", StringComparison.Ordinal), "pattern must contain ServiceDescriptor.Scoped<");

        // Verify it's a single-type parameter (not two-type like IService, Impl)
        var scopedStart = procurementPattern.IndexOf("ServiceDescriptor.Scoped<", StringComparison.Ordinal);
        var afterScoped = procurementPattern.Substring(scopedStart + "ServiceDescriptor.Scoped<".Length);
        var commaPos = afterScoped.IndexOf(',');
        var closePos = afterScoped.IndexOf('>');
        Assert.True(closePos >= 0 && (commaPos < 0 || closePos < commaPos),
            "detector must identify single-type ServiceDescriptor.Scoped pattern even when type name lacks 'Consumer' suffix");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(current.FullName, "solutions")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root could not be found.");
    }
}
