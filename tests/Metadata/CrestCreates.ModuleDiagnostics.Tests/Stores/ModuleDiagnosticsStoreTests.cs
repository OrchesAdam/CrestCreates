using System;
using System.Linq;
using CrestCreates.ModuleDiagnostics.Stores;
using FluentAssertions;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.Stores;

public class ModuleDiagnosticsStoreTests
{
    private readonly ModuleDiagnosticsStore _store = new();

    private ModulePhaseDiagnostic CreateSuccess(string moduleName, string phase)
    {
        return new ModulePhaseDiagnostic(moduleName, phase, ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null);
    }

    private ModulePhaseDiagnostic CreateFailure(string moduleName, string phase, string error)
    {
        return new ModulePhaseDiagnostic(moduleName, phase, ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(1), error);
    }

    [Fact]
    public void Record_ShouldStoreDiagnostic()
    {
        var diagnostic = CreateSuccess("TestModule", "Init");

        _store.Record(diagnostic);

        _store.GetAll().Should().ContainSingle()
            .Which.Should().Be(diagnostic);
    }

    [Fact]
    public void Record_MultiplePhases_ShouldIncreaseTotalCount()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M1", "Init"));
        _store.Record(CreateSuccess("M2", "PreInit"));

        _store.TotalCount.Should().Be(3);
    }

    [Fact]
    public void GetByModule_ShouldReturnAllPhasesForModule()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M1", "Init"));
        _store.Record(CreateSuccess("M2", "PreInit"));

        var m1Phases = _store.GetByModule("M1");

        m1Phases.Should().HaveCount(2);
        m1Phases.Select(p => p.Phase).Should().Contain(new[] { "PreInit", "Init" });
    }

    [Fact]
    public void GetByModule_UnknownModule_ShouldReturnEmpty()
    {
        _store.GetByModule("NonExistent").Should().BeEmpty();
    }

    [Fact]
    public void GetFailed_ShouldReturnOnlyFailures()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M1", "Init", "error 1"));
        _store.Record(CreateSuccess("M2", "PreInit"));
        _store.Record(CreateFailure("M2", "ConfigureServices", "error 2"));

        var failed = _store.GetFailed();

        failed.Should().HaveCount(2);
        failed.All(f => f.Status == ModulePhaseStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void Record_FailedPhase_ShouldSetHasFailures()
    {
        _store.HasFailures.Should().BeFalse();

        _store.Record(CreateFailure("M1", "Init", "error"));

        _store.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void Record_OnlySuccess_ShouldKeepHasFailuresFalse()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateSuccess("M2", "Init"));

        _store.HasFailures.Should().BeFalse();
    }

    [Fact]
    public void GetByModule_WithPartialFailure_ShouldReturnAllPhases()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M1", "Init", "failed"));

        var m1Phases = _store.GetByModule("M1");

        m1Phases.Should().HaveCount(2);
        m1Phases.Select(p => p.Status).Should().Contain(new[] { ModulePhaseStatus.Success, ModulePhaseStatus.Failed });
    }

    [Fact]
    public void GetAll_AfterMixedResults_ShouldContainBothSuccessAndFailure()
    {
        _store.Record(CreateSuccess("M1", "PreInit"));
        _store.Record(CreateFailure("M2", "Init", "failed"));

        var all = _store.GetAll();

        all.Should().HaveCount(2);
        all.Select(r => r.Status).Should().Contain(new[] { ModulePhaseStatus.Success, ModulePhaseStatus.Failed });
    }

    [Fact]
    public void EmptyStore_TotalCountShouldBeZero()
    {
        _store.TotalCount.Should().Be(0);
        _store.HasFailures.Should().BeFalse();
        _store.GetAll().Should().BeEmpty();
        _store.GetFailed().Should().BeEmpty();
    }
}