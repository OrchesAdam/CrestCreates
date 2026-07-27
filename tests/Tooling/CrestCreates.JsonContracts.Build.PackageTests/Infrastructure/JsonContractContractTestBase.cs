using Xunit;

namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

[CollectionDefinition("JsonContractBuild")]
public class JsonContractBuildCollection : ICollectionFixture<JsonContractBuildFixture>
{
}

[Collection("JsonContractBuild")]
public class JsonContractContractTestBase
{
    protected JsonContractBuildFixture Fixture { get; }

    public JsonContractContractTestBase(JsonContractBuildFixture fixture)
    {
        Fixture = fixture;
    }

    protected async Task<ProjectLayout> CreateRepositoryConsumerAsync(ConsumerSpec spec)
    {
        var projectDir = await ConsumerProjectBuilder.CreateProjectAsync(
            Fixture.ProjectsDirectory, spec, Fixture.RepositoryRoot);
        return new ProjectLayout(projectDir, Directory.GetFiles(projectDir, "*.csproj").First(), Fixture.RepositoryRoot);
    }

    protected async Task<ProjectLayout> CreatePackageConsumerAsync(ConsumerSpec spec)
    {
        await Fixture.PackAsync();
        var projectDir = await ConsumerProjectBuilder.CreateProjectAsync(
            Fixture.ProjectsDirectory, spec with { Transport = "Package" }, Fixture.RepositoryRoot,
            feedDirectory: Fixture.FeedDirectory, packageVersion: "1.0.0");
        return new ProjectLayout(projectDir, Directory.GetFiles(projectDir, "*.csproj").First(), Fixture.RepositoryRoot);
    }

    protected async Task<DotNetProcessResult> BuildAsync(ProjectLayout project, params string[] args)
    {
        var arguments = $"build \"{project.ProjectFile}\" --disable-build-servers";
        if (args.Length > 0)
            arguments += " " + string.Join(" ", args);
        return await DotNetProcess.RunAsync(project.ProjectDirectory, arguments, timeout: TimeSpan.FromMinutes(2));
    }

    protected async Task<DotNetProcessResult> RebuildAsync(ProjectLayout project, params string[] args)
    {
        var arguments = $"build \"{project.ProjectFile}\" --no-incremental --disable-build-servers";
        if (args.Length > 0)
            arguments += " " + string.Join(" ", args);
        return await DotNetProcess.RunAsync(project.ProjectDirectory, arguments, timeout: TimeSpan.FromMinutes(2));
    }

    protected async Task<DotNetProcessResult> CleanAsync(ProjectLayout project)
    {
        return await DotNetProcess.RunAsync(project.ProjectDirectory, $"clean \"{project.ProjectFile}\" --disable-build-servers", timeout: TimeSpan.FromMinutes(1));
    }

    protected async Task<DotNetProcessResult> PublishAsync(ProjectLayout project, string output)
    {
        return await DotNetProcess.RunAsync(project.ProjectDirectory, $"publish \"{project.ProjectFile}\" --disable-build-servers -o \"{output}\"", timeout: TimeSpan.FromMinutes(2));
    }

    protected string ReadGeneratedSource(ProjectLayout project, string tfm = "net10.0", string configuration = "Debug")
    {
        var path = Path.Combine(project.ProjectDirectory, "obj", configuration, tfm, "CrestCreates.JsonContracts.g.cs");
        return File.Exists(path) ? File.ReadAllText(path) : throw new FileNotFoundException($"Generated source not found: {path}");
    }

    protected string ReadInputManifest(ProjectLayout project, string tfm = "net10.0", string configuration = "Debug")
    {
        var path = Path.Combine(project.ProjectDirectory, "obj", configuration, tfm, "CrestCreates.JsonContracts.inputs.json");
        return File.Exists(path) ? File.ReadAllText(path) : throw new FileNotFoundException($"Input manifest not found: {path}");
    }

    protected FileSnapshot SnapshotGeneratedFile(ProjectLayout project, string tfm = "net10.0")
    {
        var path = Path.Combine(project.ProjectDirectory, "obj", "Debug", tfm, "CrestCreates.JsonContracts.g.cs");
        if (!File.Exists(path))
            return new FileSnapshot(path, DateTime.MinValue, []);
        return new FileSnapshot(path, File.GetLastWriteTimeUtc(path), File.ReadAllBytes(path));
    }

    protected void AssertNoTaskAssemblies(string directory)
    {
        PackageLayoutAssertions.AssertNoTaskAssemblies(directory);
    }
}
