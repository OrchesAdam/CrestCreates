using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.Procurement.E2E.Tests;

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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the procurement Host process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        var output = string.Concat(await stdout, Environment.NewLine, await stderr);

        process.ExitCode.Should().Be(0, output);
        output.Should().Contain("CRESTCREATES_PROCUREMENT_SAMPLE_OK");
    }
}
