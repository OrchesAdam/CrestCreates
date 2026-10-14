using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Architecture guards for the typed LocalEvent mainline: one dispatch index, one replay core,
/// no reflection bridges, no dynamic-JSON overloads and no runtime scanning on the formal paths.
/// Behavioral coverage stays in the execution suites; these guards only lock the shape.
/// </summary>
public sealed class LocalEventTypedDispatchArchitectureTests
{
    private static readonly string[] FormalPathRoots =
    [
        "src/Runtime/Eventing/CrestCreates.EventBus.Abstractions",
        "src/Runtime/Eventing/CrestCreates.EventBus.Local",
        "src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel",
        "src/Tooling/CrestCreates.CodeGenerator/LocalEventGenerator",
    ];

    private static readonly string[] ForbiddenReflectionTokens =
    [
        "MakeGenericMethod(",
        "MakeGenericType(",
        "MethodInfo",
        "GetMethod(",
        "Type.GetType(",
        "Assembly.Load(",
        "Assembly.GetExecutingAssembly(",
        "AppDomain",
        "Activator.CreateInstance(",
        "Expression.Compile(",
        "ConstructorInfo",
        "DefaultJsonTypeInfoResolver",
    ];

    [Fact]
    public void LocalEventMainline_Should_Not_Contain_Reflection_Bridges()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateFormalPathFiles(repoRoot))
        {
            var lines = File.ReadLines(file).ToArray();
            for (var i = 0; i < lines.Length; i++)
            {
                if (ForbiddenReflectionTokens.Any(token => lines[i].Contains(token, StringComparison.Ordinal)))
                {
                    violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "LocalEvent formal paths contain forbidden reflection or dynamic-JSON patterns:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void LocalEventMainline_Should_Keep_Single_Index_And_Single_Replay_Core()
    {
        var repoRoot = FindRepoRoot();
        var indexImplementations = new List<string>();
        var replayCoreImplementations = new List<string>();
        var recorderImplementations = new List<string>();

        var sourceRoot = Path.Combine(repoRoot, "src");
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(IsNotBuildOutput))
        {
            var text = File.ReadAllText(file);
            if (text.Contains(": ILocalEventDispatchRegistry", StringComparison.Ordinal))
                indexImplementations.Add(Path.GetRelativePath(repoRoot, file));
            if (text.Contains(": ILocalDeadLetterReplayExecutor", StringComparison.Ordinal))
                replayCoreImplementations.Add(Path.GetRelativePath(repoRoot, file));
            if (text.Contains(": ILocalEventFailureRecorder", StringComparison.Ordinal))
                recorderImplementations.Add(Path.GetRelativePath(repoRoot, file));
        }

        Assert.True(indexImplementations.Count == 1,
            "Exactly one frozen dispatch index implementation is allowed: " + string.Join(", ", indexImplementations));
        Assert.True(replayCoreImplementations.Count == 1,
            "Exactly one replay core implementation is allowed: " + string.Join(", ", replayCoreImplementations));
        Assert.True(recorderImplementations.Count == 1,
            "Exactly one failure recorder implementation is allowed: " + string.Join(", ", recorderImplementations));
    }

    [Fact]
    public void LocalEventEntries_Should_Reference_The_Unified_Index_And_Replay_Core()
    {
        var repoRoot = FindRepoRoot();
        var expectations = new Dictionary<string, string[]>
        {
            [@"src/Runtime/Eventing/CrestCreates.EventBus.Local/DefaultLocalEventDispatcher.cs"] =
                ["ILocalEventDispatchRegistry", "UnknownLocalEventException", "LocalEventAdmission", "LocalEventExecution"],
            [@"src/Runtime/Eventing/CrestCreates.EventBus.Local/DefaultLocalEventBus.cs"] =
                ["ILocalEventDispatchRegistry", "ILocalEventFailureRecorder", "LocalEventAdmission", "LocalEventExecution"],
            [@"src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel/BackgroundChannelLocalEventBus.cs"] =
                ["ILocalEventDispatchRegistry", "UnknownLocalEventException", "LocalEventAdmission"],
            [@"src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel/BackgroundChannelLocalEventBusConsumer.cs"] =
                ["ILocalEventDispatchRegistry", "ILocalEventFailureRecorder", "LocalEventAdmission", "LocalEventExecution"],
            [@"src/Runtime/Eventing/CrestCreates.EventBus.Local/DefaultLocalDeadLetterManager.cs"] =
                ["ILocalDeadLetterReplayExecutor"],
            [@"src/Runtime/Eventing/CrestCreates.EventBus.Local.Channel/LocalDeadLetterBackgroundService.cs"] =
                ["ILocalDeadLetterReplayExecutor"],
        };

        var violations = new List<string>();
        foreach (var (relativePath, expectedTokens) in expectations)
        {
            var path = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(path))
            {
                violations.Add($"{relativePath}: file missing");
                continue;
            }

            var text = File.ReadAllText(path);
            foreach (var token in expectedTokens)
            {
                if (!text.Contains(token, StringComparison.Ordinal))
                    violations.Add($"{relativePath}: missing '{token}'");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> EnumerateFormalPathFiles(string repoRoot)
    {
        foreach (var root in FormalPathRoots)
        {
            var fullPath = Path.Combine(repoRoot, root);
            if (!Directory.Exists(fullPath)) continue;

            foreach (var file in Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                         .Where(IsNotBuildOutput))
            {
                yield return file;
            }
        }
    }

    private static bool IsNotBuildOutput(string file)
        => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
           && !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
