using System.Diagnostics;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class GoldenScenarioProcessTests
{
    [Fact]
    public async Task IndependentHostProcess_RunsGoldenScenarioAndPrintsSentinel()
    {
        var hostAssembly = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostAssembly)!
        };
        startInfo.ArgumentList.Add(hostAssembly);
        startInfo.ArgumentList.Add("--golden-scenario");
        startInfo.Environment["ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING"] = Environment.GetEnvironmentVariable("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING")
            ?? throw new InvalidOperationException("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING must point at the durable PostgreSQL test service.");
        startInfo.Environment["ASSET_MANAGEMENT_RUNTIME_SCHEMA"] = $"crest_asset_runtime_e2e_{Guid.NewGuid():N}";
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Asset Management Host process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        var output = string.Concat(await stdout, Environment.NewLine, await stderr);
        process.ExitCode.Should().Be(0, output);
        output.Should().Contain("CRESTCREATES_ASSET_MANAGEMENT_GOLDEN_OK");
    }
}
