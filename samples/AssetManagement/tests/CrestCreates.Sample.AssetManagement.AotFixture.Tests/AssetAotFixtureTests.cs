using System.Diagnostics;
using System.Text.Json;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Contracts.Json;
using CrestCreates.Sample.AssetManagement.Host.Json;

namespace CrestCreates.Sample.AssetManagement.AotFixture.Tests;

public sealed class AssetAotFixtureTests
{
    private static readonly Lazy<Task<NativeAotRunResult>> NativeAotRun = new(RunNativeAotScenarioAsync);

    [Fact]
    public void JsonTypeInfo_ResolvesAssetContracts()
    {
        AssetJsonContext.Default.RegisterAssetInput.Should().NotBeNull();
        AssetJsonContext.Default.AssetResult.Should().NotBeNull();
        AssetHostJsonContext.Default.DynamicApiResponseObject.Should().NotBeNull();
    }

    [Fact]
    public void AssetQuery_RoundTripsViaSourceGenerator()
    {
        var input = new AssetQueryInput { AssetId = Guid.NewGuid(), Search = "laptop", Status = "Available" };
        var json = JsonSerializer.Serialize(input, AssetJsonContext.Default.AssetQueryInput);
        var roundTrip = JsonSerializer.Deserialize(json, AssetJsonContext.Default.AssetQueryInput);
        roundTrip.Should().BeEquivalentTo(input);
    }

    [Fact]
    public async Task NativeAotBinary_RunsGoldenScenarioAndExits()
    {
        var result = await NativeAotRun.Value;
        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("CRESTCREATES_ASSET_MANAGEMENT_GOLDEN_OK");
    }

    private static async Task<NativeAotRunResult> RunNativeAotScenarioAsync()
    {
        var root = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(Path.Combine(root, "samples", "AssetManagement", "scripts", "run-nativeaot-golden-scenario.sh"));
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start NativeAOT gate.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        await process.WaitForExitAsync(timeout.Token);
        return new NativeAotRunResult(process.ExitCode, string.Concat(await stdout, Environment.NewLine, await stderr));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
                return current.FullName;
        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed record NativeAotRunResult(int ExitCode, string Output);
}
