namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

public sealed class JsonContractBuildFixture : IAsyncLifetime
{
    public string RepositoryRoot { get; private set; } = string.Empty;
    public string FixtureRoot { get; private set; } = string.Empty;
    public string FeedDirectory { get; private set; } = string.Empty;
    public string PackagesDirectory { get; private set; } = string.Empty;
    public string ProjectsDirectory { get; private set; } = string.Empty;
    public string PublishDirectory { get; private set; } = string.Empty;
    public string LogsDirectory { get; private set; } = string.Empty;
    public string TaskAssemblyPath { get; private set; } = string.Empty;
    public string ToolAssemblyPath { get; private set; } = string.Empty;
    public string BuildPropsPath { get; private set; } = string.Empty;
    public string BuildTargetsPath { get; private set; } = string.Empty;
    public string RepositoryPropsPath { get; private set; } = string.Empty;
    public string RepositoryTargetsPath { get; private set; } = string.Empty;
    public string CommonPropsPath { get; private set; } = string.Empty;
    public string CommonTargetsPath { get; private set; } = string.Empty;
    public string CoreAbstractionsPath { get; private set; } = string.Empty;
    public string PackagePath { get; private set; } = string.Empty;
    public string CoreAbstractionsPackagePath { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        RepositoryRoot = FindRepositoryRoot();
        FixtureRoot = Path.Combine(Path.GetTempPath(), $"JsonContract_{Guid.NewGuid():N}");
        FeedDirectory = Path.Combine(FixtureRoot, "feed");
        PackagesDirectory = Path.Combine(FixtureRoot, "packages");
        ProjectsDirectory = Path.Combine(FixtureRoot, "projects");
        PublishDirectory = Path.Combine(FixtureRoot, "publish");
        LogsDirectory = Path.Combine(FixtureRoot, "logs");

        foreach (var dir in new[] { FeedDirectory, PackagesDirectory, ProjectsDirectory, PublishDirectory, LogsDirectory })
            Directory.CreateDirectory(dir);

        var coreAbstractionsProject = Path.Combine(RepositoryRoot, "src", "Core", "CrestCreates.Core.Abstractions");
        await BuildProjectAsync(coreAbstractionsProject, "Debug");
        await BuildProjectAsync(coreAbstractionsProject, "Release");

        CoreAbstractionsPath = Path.Combine(coreAbstractionsProject, "bin", "Debug", "net10.0", "CrestCreates.Core.Abstractions.dll");

        var toolProjectDir = Path.Combine(RepositoryRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks.Core");
        await BuildProjectAsync(toolProjectDir, "Debug");
        await BuildProjectAsync(toolProjectDir, "Release");

        ToolAssemblyPath = Path.Combine(toolProjectDir, "bin", "Debug", "net10.0", "CrestCreates.JsonContracts.Tool.dll");

        var taskProjectDir = Path.Combine(RepositoryRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks");
        await BuildProjectAsync(taskProjectDir, "Debug");
        await BuildProjectAsync(taskProjectDir, "Release");

        TaskAssemblyPath = Path.Combine(taskProjectDir, "bin", "Debug", "net10.0", "CrestCreates.JsonContracts.BuildTasks.dll");
        if (!File.Exists(TaskAssemblyPath))
            throw new InvalidOperationException($"Task assembly not found at {TaskAssemblyPath}");

        var buildDir = Path.Combine(taskProjectDir, "build");
        BuildPropsPath = Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.props");        BuildTargetsPath = Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.targets");
        RepositoryPropsPath = Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Repository.props");
        RepositoryTargetsPath = Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Repository.targets");
        CommonPropsPath = Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Common.props");
        CommonTargetsPath = Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.Common.targets");
    }

    public async Task PackAsync()
    {
        if (File.Exists(PackagePath)) return;

        var coreAbstractionsProject = Path.Combine(RepositoryRoot, "src", "Core", "CrestCreates.Core.Abstractions");
        var corePack = await DotNetProcess.RunAsync(
            coreAbstractionsProject,
            $"pack --configuration Debug --no-build --disable-build-servers -p:PackageVersion=1.0.0 -p:SuppressDependenciesWhenPacking=true -o \"{FeedDirectory}\"",
            timeout: TimeSpan.FromMinutes(3));
        if (corePack.ExitCode != 0)
            throw new InvalidOperationException($"Failed to pack Core.Abstractions: {corePack.StandardError}{corePack.StandardOutput}");

        CoreAbstractionsPackagePath = Path.Combine(FeedDirectory, "CrestCreates.Core.Abstractions.1.0.0.nupkg");
        if (!File.Exists(CoreAbstractionsPackagePath))
            throw new InvalidOperationException($"Core.Abstractions package not found at {CoreAbstractionsPackagePath}");

        var taskProjectDir = Path.Combine(RepositoryRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks");
        var result = await DotNetProcess.RunAsync(taskProjectDir, "pack --configuration Debug --disable-build-servers", timeout: TimeSpan.FromMinutes(3));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to pack: {result.StandardError}");

        var nupkg = Path.Combine(taskProjectDir, "bin", "Debug", "CrestCreates.JsonContracts.Build.1.0.0.nupkg");
        if (!File.Exists(nupkg))
            throw new InvalidOperationException($"Package not found at {nupkg}");

        var destNupkg = Path.Combine(FeedDirectory, Path.GetFileName(nupkg));
        File.Copy(nupkg, destNupkg, overwrite: true);
        PackagePath = destNupkg;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(FixtureRoot))
                Directory.Delete(FixtureRoot, true);
        }
        catch
        {
        }
        return Task.CompletedTask;
    }

    private static async Task BuildProjectAsync(string projectDir, string configuration)
    {
        var result = await DotNetProcess.RunAsync(projectDir, $"build --configuration {configuration} --disable-build-servers", timeout: TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to build {projectDir} ({configuration}): {result.StandardError}");
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "CrestCreates.slnx")) || File.Exists(Path.Combine(dir, "global.json")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Cannot find repository root.");
    }
}
