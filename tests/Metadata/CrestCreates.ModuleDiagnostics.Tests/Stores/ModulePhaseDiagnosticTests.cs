using System;
using CrestCreates.ModuleDiagnostics.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.Stores;

public class ModulePhaseDiagnosticTests
{
    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        var elapsed = TimeSpan.FromMilliseconds(42);

        var diagnostic = new ModulePhaseDiagnostic(
            "TestModule",
            "Init",
            ModulePhaseStatus.Success,
            elapsed,
            null);

        diagnostic.ModuleName.Should().Be("TestModule");
        diagnostic.Phase.Should().Be("Init");
        diagnostic.Status.Should().Be(ModulePhaseStatus.Success);
        diagnostic.Elapsed.Should().Be(elapsed);
        diagnostic.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithError_ShouldSetErrorMessage()
    {
        var diagnostic = new ModulePhaseDiagnostic(
            "FailingModule",
            "ConfigureServices",
            ModulePhaseStatus.Failed,
            TimeSpan.FromMilliseconds(5),
            "DI resolution failed");

        diagnostic.Status.Should().Be(ModulePhaseStatus.Failed);
        diagnostic.ErrorMessage.Should().Be("DI resolution failed");
    }
}