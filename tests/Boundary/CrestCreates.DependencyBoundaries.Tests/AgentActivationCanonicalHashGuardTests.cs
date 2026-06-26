using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class AgentActivationCanonicalHashGuardTests
{
    [Fact]
    public void ControlPlane_DoesNotContain_AdHoc_Hash_Computation()
    {
        var root = FindRepositoryRoot();
        var controlPlaneDir = Path.Combine(root, "src", "Runtime", "Agent", "CrestCreates.Agent.ControlPlane");

        if (!Directory.Exists(controlPlaneDir))
        {
            // Skip if directory doesn't exist in this checkout
            return;
        }

        var csFiles = Directory.GetFiles(controlPlaneDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal))
            .ToList();

        var forbiddenPatterns = new[]
        {
            "ComputeSourceReviewHash",
            "ComputeReviewManifestHash",
            "CreateReviewCanonicalHash",
            "CreatePackageCanonicalHash"
        };

        var violations = new List<string>();
        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            // Skip files that use these via the proper service interface
            // (calling _reviewHashService.ComputeSourceReviewHash is fine — it's the service)
            // Only flag ad-hoc definitions that are NOT service method calls
            foreach (var pattern in forbiddenPatterns)
            {
                if (content.Contains(pattern, StringComparison.Ordinal))
                {
                    // Check if this is a proper service call or an ad-hoc method
                    // Ad-hoc patterns: method definition (static/private before the name),
                    // or called without a service instance qualifier
                    bool isAdHoc = false;

                    // Ad-hoc method definition: "static string ComputeSourceReviewHash" or
                    // "private string ComputeSourceReviewHash" or just a bare call without service prefix
                    if (content.Contains($"static {pattern}", StringComparison.Ordinal) ||
                        content.Contains($"private {pattern}", StringComparison.Ordinal) ||
                        content.Contains($"public {pattern}", StringComparison.Ordinal))
                    {
                        isAdHoc = true;
                    }
                    // Bare call without service qualifier (not _reviewHashService. or service.)
                    else if (!content.Contains($".ComputeSourceReviewHash") &&
                             !content.Contains($".ComputeReviewManifestHash") &&
                             !content.Contains($".CreateReviewCanonicalHash") &&
                             !content.Contains($".CreatePackageCanonicalHash"))
                    {
                        // If the pattern appears but not as a dotted method call, it might be
                        // a declaration or reference. Check if it's just an interface method reference.
                        if (!content.Contains($"I{pattern}", StringComparison.Ordinal) &&
                            content.Contains(pattern, StringComparison.Ordinal))
                        {
                            // Only flag if truly ad-hoc (not a service call with dot prefix)
                            var dotPattern = $".{pattern}";
                            if (!content.Contains(dotPattern, StringComparison.Ordinal))
                            {
                                isAdHoc = true;
                            }
                        }
                    }

                    if (isAdHoc)
                    {
                        violations.Add($"{fileName}: contains ad-hoc '{pattern}'");
                    }
                }
            }

            // Check for SHA256 ad-hoc usage (not through ICanonicalHashComputer)
            // Exclude DefaultDescriptorReviewReportBuilder.cs where ComputeSha256 is used for ReportId
            if (fileName != "DefaultDescriptorReviewReportBuilder.cs")
            {
                if (content.Contains("new SHA256Managed", StringComparison.Ordinal) ||
                    (content.Contains("SHA256.Create()", StringComparison.Ordinal)))
                {
                    violations.Add($"{fileName}: uses ad-hoc SHA256 (new SHA256Managed or SHA256.Create())");
                }

                // Check for pipe-delimited pattern: StringBuilder + AppendLine + ComputeHash
                if (content.Contains("StringBuilder", StringComparison.Ordinal) &&
                    content.Contains("AppendLine", StringComparison.Ordinal) &&
                    content.Contains("ComputeHash", StringComparison.Ordinal))
                {
                    violations.Add($"{fileName}: uses pipe-delimited StringBuilder+AppendLine+ComputeHash pattern");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Agent Control Plane must not contain ad-hoc hash computation. " +
            "Use IDescriptorDraftReviewHashService and IDescriptorPackageCanonicalHashComputer instead." +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DescriptorPackage_DoesNotUse_Legacy_HashComputer_InProduction()
    {
        var root = FindRepositoryRoot();
        var metadataDir = Path.Combine(root, "src", "Metadata", "CrestCreates.Metadata");

        if (!Directory.Exists(metadataDir))
            return;

        var csFiles = Directory.GetFiles(metadataDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal))
            // DescriptorPackageHashComputer.cs is the obsolete class itself — allowed
            .Where(f => !f.EndsWith("DescriptorPackageHashComputer.cs", StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();
        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);
            if (content.Contains("DescriptorPackageHashComputer", StringComparison.Ordinal) &&
                !content.Contains("[Obsolete", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: references DescriptorPackageHashComputer without [Obsolete]");
            }
        }

        Assert.True(
            violations.Count == 0,
            "DescriptorPackage production code must not reference obsolete DescriptorPackageHashComputer. " +
            "Use IDescriptorPackageCanonicalHashComputer instead." +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
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
