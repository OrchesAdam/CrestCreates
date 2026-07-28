using System.Diagnostics;
using System.Text.Json;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using CrestCreates.Sample.Procurement.Host.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.Procurement.AotFixture.Tests;

public class ProcurementAotFixtureTests
{
    private static readonly Lazy<Task<NativeAotRunResult>> NativeAotRun = new(RunNativeAotScenarioAsync);

    [Fact]
    public void JsonTypeInfo_ResolvesSubmitRequestInput()
    {
        var typeInfo = ProcurementJsonContext.Default.SubmitProcurementRequestInput;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for SubmitProcurementRequestInput");
    }

    [Fact]
    public void JsonTypeInfo_ResolvesSubmitRequestResult()
    {
        var typeInfo = ProcurementJsonContext.Default.SubmitProcurementRequestResult;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for SubmitProcurementRequestResult");
    }

    [Fact]
    public void JsonTypeInfo_ResolvesProcurementRequestResult()
    {
        var typeInfo = ProcurementJsonContext.Default.ProcurementRequestResult;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for ProcurementRequestResult");
    }

    [Fact]
    public void JsonTypeInfo_ResolvesDynamicApiResponse()
    {
        var typeInfo = ProcurementHostJsonContext.Default.DynamicApiResponseObject;
        typeInfo.Should().NotBeNull("STJ source generator should produce JsonTypeInfo for DynamicApiResponse<object>");
    }

    [Fact]
    public void SubmitRequestInput_RoundTripsViaSourceGenerator()
    {
        var input = new SubmitProcurementRequestInput
        {
            Title = "Office Supplies",
            Amount = 500m,
            Currency = "USD",
            Category = "General"
        };

        var json = JsonSerializer.Serialize(input, ProcurementJsonContext.Default.SubmitProcurementRequestInput);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.SubmitProcurementRequestInput);

        deserialized.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void SubmitRequestResult_RoundTripsViaSourceGenerator()
    {
        var result = new SubmitProcurementRequestResult
        {
            RequestId = Guid.NewGuid(),
            Status = "PendingApproval",
            Amount = 15000m,
            Currency = "USD",
            RequiresApproval = true
        };

        var json = JsonSerializer.Serialize(result, ProcurementJsonContext.Default.SubmitProcurementRequestResult);
        var deserialized = JsonSerializer.Deserialize(json, ProcurementJsonContext.Default.SubmitProcurementRequestResult);

        deserialized.Should().BeEquivalentTo(result);
    }

    [Fact]
    public async Task NativeAotBinary_RunsGoldenScenarioAndExits()
    {
        var result = await NativeAotRun.Value;

        result.ExitCode.Should().Be(0, result.Output);
    }

    [Fact]
    public async Task NativeAotBinary_PrintsSuccessSentinel()
    {
        var result = await NativeAotRun.Value;

        result.Output.Should().Contain("CRESTCREATES_PROCUREMENT_SAMPLE_OK");
    }

    private static async Task<NativeAotRunResult> RunNativeAotScenarioAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(
            repositoryRoot,
            "samples",
            "ProcurementApproval",
            "scripts",
            "run-nativeaot-golden-scenario.sh");
        var startInfo = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot
        };
        startInfo.ArgumentList.Add(scriptPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start NativeAOT gate.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("NativeAOT golden scenario exceeded the twelve-minute gate timeout.");
        }
        var output = string.Concat(await stdout, Environment.NewLine, await stderr);
        return new NativeAotRunResult(process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the CrestCreates repository root.");
    }

    private sealed record NativeAotRunResult(int ExitCode, string Output);
}
