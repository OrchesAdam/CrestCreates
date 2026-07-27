using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

public static class ConsumerProjectBuilder
{
    public static async Task<string> CreateProjectAsync(
        string projectsDirectory,
        ConsumerSpec spec,
        string repositoryRoot,
        string? feedDirectory = null,
        string? packageVersion = null)
    {
        var projectId = Guid.NewGuid().ToString("N")[..8];
        var projectDir = Path.Combine(projectsDirectory, projectId);
        Directory.CreateDirectory(projectDir);

        var csproj = new StringBuilder();
        csproj.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        csproj.AppendLine("  <PropertyGroup>");
        if (spec.TargetFramework.Contains(';', StringComparison.Ordinal))
            csproj.AppendLine($"    <TargetFrameworks>{spec.TargetFramework}</TargetFrameworks>");
        else
            csproj.AppendLine($"    <TargetFramework>{spec.TargetFramework}</TargetFramework>");
        csproj.AppendLine($"    <ImplicitUsings>{spec.ImplicitUsings}</ImplicitUsings>");
        csproj.AppendLine($"    <Nullable>{spec.Nullable}</Nullable>");
        csproj.AppendLine($"    <LangVersion>{spec.LangVersion}</LangVersion>");
        csproj.AppendLine($"    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        if (spec.ManifestAccessibility != "Internal")
            csproj.AppendLine($"    <CrestCreatesJsonContractManifestAccessibility>{spec.ManifestAccessibility}</CrestCreatesJsonContractManifestAccessibility>");
        if (spec.GeneratedFile is not null)
            csproj.AppendLine($"    <CrestCreatesJsonContractGeneratedFile>{spec.GeneratedFile}</CrestCreatesJsonContractGeneratedFile>");
        if (spec.InputManifest is not null)
            csproj.AppendLine($"    <CrestCreatesJsonContractInputManifest>{spec.InputManifest}</CrestCreatesJsonContractInputManifest>");
        if (spec.GenerationStamp is not null)
            csproj.AppendLine($"    <CrestCreatesJsonContractGenerationStamp>{spec.GenerationStamp}</CrestCreatesJsonContractGenerationStamp>");
        if (spec.TemporaryDirectory is not null)
            csproj.AppendLine($"    <CrestCreatesJsonContractTemporaryDirectory>{spec.TemporaryDirectory}</CrestCreatesJsonContractTemporaryDirectory>");
        csproj.AppendLine("  </PropertyGroup>");

        csproj.AppendLine("  <ItemGroup>");
        for (int i = 0; i < spec.SourceFiles.Length; i++)
            csproj.AppendLine($"    <Compile Include=\"Source{i}.cs\" />");
        csproj.AppendLine("  </ItemGroup>");

        if (spec.Transport == "Package" && feedDirectory != null)
        {
            csproj.AppendLine("  <ItemGroup>");
            csproj.AppendLine("    <PackageReference Include=\"CrestCreates.Core.Abstractions\" Version=\"1.0.0\" />");
            csproj.AppendLine($"    <PackageReference Include=\"CrestCreates.JsonContracts.Build\" Version=\"{packageVersion ?? "1.0.0"}\" PrivateAssets=\"all\" />");
            csproj.AppendLine("  </ItemGroup>");

            var nugetConfig = new StringBuilder();
            nugetConfig.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            nugetConfig.AppendLine("<configuration>");
            nugetConfig.AppendLine("  <config>");
            nugetConfig.AppendLine($"    <add key=\"globalPackagesFolder\" value=\"{Path.Combine(Path.GetDirectoryName(feedDirectory)!, "packages")}\" />");
            nugetConfig.AppendLine("  </config>");
            nugetConfig.AppendLine("  <packageSources>");
            nugetConfig.AppendLine("    <clear />");
            nugetConfig.AppendLine($"    <add key=\"local-feed\" value=\"{feedDirectory}\" />");
            nugetConfig.AppendLine("  </packageSources>");
            nugetConfig.AppendLine("</configuration>");
            await File.WriteAllTextAsync(Path.Combine(projectDir, "NuGet.Config"), nugetConfig.ToString());
        }
        else if (spec.Transport == "Repository")
        {
            var taskProjectDir = Path.Combine(repositoryRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks");
            var buildDir = Path.Combine(taskProjectDir, "build");

            csproj.AppendLine($"  <Import Project=\"{Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Repository.props")}\" />");

            if (spec.EarlierTarget != null)
                csproj.AppendLine($"  <Import Project=\"{spec.EarlierTarget}\" />");

            if (spec.DuplicateImport)
                csproj.AppendLine($"  <Import Project=\"{Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Repository.props")}\" />");

            csproj.AppendLine($"  <Import Project=\"{Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Repository.targets")}\" />");

            if (spec.DuplicateImport)
                csproj.AppendLine($"  <Import Project=\"{Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Repository.targets")}\" />");
        }

        csproj.AppendLine("</Project>");

        var csprojPath = Path.Combine(projectDir, $"{projectId}.csproj");
        await File.WriteAllTextAsync(csprojPath, csproj.ToString());

        foreach (var (fileName, content) in spec.SourceFiles.Select((f, i) => ($"Source{i}.cs", f)))
        {
            await File.WriteAllTextAsync(Path.Combine(projectDir, fileName), content);
        }

        return projectDir;
    }

    public static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
