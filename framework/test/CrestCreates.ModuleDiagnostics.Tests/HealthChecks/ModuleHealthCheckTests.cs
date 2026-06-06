using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.ModuleDiagnostics.HealthChecks;
using CrestCreates.ModuleDiagnostics.Stores;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace CrestCreates.ModuleDiagnostics.Tests.HealthChecks;

public class ModuleHealthCheckTests
{
    [Fact]
    public async Task AllSuccess_ShouldReturnHealthy()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(2), null));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task EmptyStore_ShouldReturnHealthy()
    {
        var store = new ModuleDiagnosticsStore();
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task AnyFailure_ShouldReturnUnhealthy()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(2), "error"));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task AnyFailure_ShouldIncludeFailureDetailsInData()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("SecurityModule", "Init", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(5), "Unable to resolve IPasswordHasher"));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Data.Should().ContainKey("failedPhases");
        result.Data["failedPhases"].Should().Be(1);
        result.Data.Should().ContainKey("failedDetails");
        var details = result.Data["failedDetails"] as List<Dictionary<string, string>>;
        details.Should().NotBeNull();
        details!.Should().HaveCount(1);
        details![0]["module"].Should().Be("SecurityModule");
        details![0]["phase"].Should().Be("Init");
        details![0]["error"].Should().Be("Unable to resolve IPasswordHasher");
    }

    [Fact]
    public async Task MultipleModulesFailed_ShouldCountAll()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(1), "err1"));
        store.Record(new ModulePhaseDiagnostic("M2", "ConfigureServices", ModulePhaseStatus.Failed, TimeSpan.FromMilliseconds(1), "err2"));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Data["failedPhases"].Should().Be(2);
        var details = result.Data["failedDetails"] as List<Dictionary<string, string>>;
        details.Should().HaveCount(2);
    }

    [Fact]
    public async Task AllSuccess_ShouldIncludeTotalPhasesInData()
    {
        var store = new ModuleDiagnosticsStore();
        store.Record(new ModulePhaseDiagnostic("M1", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M1", "Init", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        store.Record(new ModulePhaseDiagnostic("M2", "PreInit", ModulePhaseStatus.Success, TimeSpan.FromMilliseconds(1), null));
        var healthCheck = new ModuleHealthCheck(store);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        result.Data["totalPhases"].Should().Be(3);
        result.Data["failedPhases"].Should().Be(0);
    }
}