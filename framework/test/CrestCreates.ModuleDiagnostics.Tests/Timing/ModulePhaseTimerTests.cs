using System;
using CrestCreates.ModuleDiagnostics.Stores;
using CrestCreates.ModuleDiagnostics.Timing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.Timing;

public class ModulePhaseTimerTests
{
    [Fact]
    public void StartNew_Stop_ShouldRecordNonZeroElapsedTime()
    {
        var timer = ModulePhaseTimer.StartNew("TestModule", "PreInit");

        var result = timer.Stop(ModulePhaseStatus.Success);

        result.Elapsed.Should().BePositive();
        result.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Stop_WithSuccess_ShouldSetStatusToSuccess()
    {
        var timer = ModulePhaseTimer.StartNew("TestModule", "Init");

        var result = timer.Stop(ModulePhaseStatus.Success);

        result.Status.Should().Be(ModulePhaseStatus.Success);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void StopFailed_ShouldCaptureErrorMessage()
    {
        var timer = ModulePhaseTimer.StartNew("TestModule", "ConfigureServices");
        var exception = new InvalidOperationException("Connection string missing");

        var result = timer.StopFailed(exception);

        result.Status.Should().Be(ModulePhaseStatus.Failed);
        result.ErrorMessage.Should().Be("Connection string missing");
    }

    [Fact]
    public void Stop_ShouldPreserveModuleNameAndPhase()
    {
        var timer = ModulePhaseTimer.StartNew("SecurityModule", "PostInit");

        var result = timer.Stop(ModulePhaseStatus.Success);

        result.ModuleName.Should().Be("SecurityModule");
        result.Phase.Should().Be("PostInit");
    }

    [Fact]
    public void StopFailed_ShouldPreserveModuleNameAndPhase()
    {
        var timer = ModulePhaseTimer.StartNew("DataCoreModule", "AppInit");
        var exception = new Exception("Failed");

        var result = timer.StopFailed(exception);

        result.ModuleName.Should().Be("DataCoreModule");
        result.Phase.Should().Be("AppInit");
    }
}