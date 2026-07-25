using CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;
using FluentAssertions;

namespace CrestCreates.JsonContracts.Build.PackageTests.Failure;

public class TransportConflictContractTests(JsonContractBuildFixture fixture) : JsonContractContractTestBase(fixture)
{
    [Fact]
    public async Task Build_RepositoryAndPackageTransportConflictFailsBeforeGeneration()
    {
        var taskProjectDir = Path.Combine(Fixture.RepositoryRoot, "src", "Tooling", "CrestCreates.JsonContracts.BuildTasks");
        var buildDir = Path.Combine(taskProjectDir, "build");

        var spec = new ConsumerSpec(
            Transport: "Repository",
            SourceFiles:
            [
                """
                using System.Text.Json;
                using System.Text.Json.Serialization;
                using CrestCreates.Core.Abstractions.Serialization;

                [JsonContractSurface(typeof(ITestService))]
                public partial class TestContext : JsonSerializerContext { }
                """,
                """
                using System.Threading;
                using System.Threading.Tasks;

                public interface ITestService
                {
                    Task<string> GetAsync(string id, CancellationToken ct = default);
                }
                """
            ],
            EarlierTarget: Path.Combine(buildDir, "CrestCreates.JsonContracts.Build.props"));

        var project = await CreateRepositoryConsumerAsync(spec);
        var result = await BuildAsync(project);

        result.ExitCode.Should().NotBe(0, "conflicting transports should fail the build");
        (result.StandardOutput + result.StandardError).Should().Contain("CJC014");
    }
}
