using System.Text.RegularExpressions;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class SemanticStringGuardTests
{
    private static readonly Regex[] ForbiddenPatterns =
    [
        new("\"ACTIVATION_[A-Z0-9_]+\"", RegexOptions.Compiled),
        new("\"CCHASH[0-9]{3}\"", RegexOptions.Compiled),
        new("\"OM[0-9]{2,3}\"", RegexOptions.Compiled),
        new("\"FIELD_REQUIRED\"", RegexOptions.Compiled),
        new("\"descriptor-activation-review\"", RegexOptions.Compiled),
        new("\"agent\\.[a-z0-9_.-]+\"", RegexOptions.Compiled)
    ];

    [Fact]
    public void OfficialSemanticStrings_Are_Not_Inlined_Outside_Definition_Files()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => IsScannedSource(root, path))
            .ToArray();

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            if (text.Contains("semantic-string-guard: allow", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var pattern in ForbiddenPatterns)
            {
                if (pattern.IsMatch(text))
                {
                    violations.Add(relative);
                    break;
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Inline semantic string literals found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsScannedSource(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

        if (!relative.StartsWith("src/", StringComparison.Ordinal) &&
            !relative.StartsWith("tests/", StringComparison.Ordinal))
        {
            return false;
        }

        if (relative.Contains("/bin/", StringComparison.Ordinal) ||
            relative.Contains("/obj/", StringComparison.Ordinal) ||
            relative.Contains("/Generated/", StringComparison.Ordinal) ||
            relative.Contains("/Snapshots/", StringComparison.Ordinal) ||
            relative.Contains("/Migrations/", StringComparison.Ordinal))
        {
            return false;
        }

        return !IsDefinitionFile(relative);
    }

    private static bool IsDefinitionFile(string relative)
    {
        var name = Path.GetFileName(relative);
        return name.EndsWith("ErrorCodes.cs", StringComparison.Ordinal) ||
               name.EndsWith("DiagnosticCodes.cs", StringComparison.Ordinal) ||
               name.EndsWith("EventNames.cs", StringComparison.Ordinal) ||
               name.EndsWith("PermissionNames.cs", StringComparison.Ordinal) ||
               name.EndsWith("PermissionName.cs", StringComparison.Ordinal) ||
               name.EndsWith("PolicyNames.cs", StringComparison.Ordinal) ||
               name.EndsWith("CapabilityIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("WorkflowIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("HumanTaskIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("DescriptorIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("VersionKeys.cs", StringComparison.Ordinal) ||
               name.EndsWith("MessageTemplateIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("ValidationErrorCodes.cs", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
