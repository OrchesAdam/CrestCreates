using System.Diagnostics;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlControlPlaneReferenceDataCrashTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    public static IEnumerable<object[]> Surfaces()
    {
        foreach (var surface in new[] { "draft", "organization-unit", "position", "membership", "role-assignment", "rule" })
            yield return new object[] { surface };
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task SaveSurface_CrashBeforeCommit_Should_NotExposePartialSnapshot(string surface)
    {
        await RunCrashScenarioAsync(surface, "before-commit", shouldExist: false);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task SaveSurface_CrashAfterCommit_Should_ExposeCompleteSnapshot(string surface)
    {
        await RunCrashScenarioAsync(surface, "after-commit", shouldExist: true);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task SaveSurface_CommitUnknown_Should_NotBeReportedAsDeterministicFailure(string surface)
    {
        await RunCrashScenarioAsync(surface, "commit-unknown", shouldExist: true);
    }

    private async Task RunCrashScenarioAsync(string surface, string window, bool shouldExist)
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var root = FindRepositoryRoot();
        var worker = Path.Combine(root,
            "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/bin/Debug/net10.0/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.dll");
        File.Exists(worker).Should().BeTrue("the CrashWorker must be built by the test project reference");

        var applicationName = $"phase9b-reference-{Guid.NewGuid():N}";
        var connectionBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = applicationName
        };
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{worker}\" \"{connectionBuilder.ConnectionString}\" {lease.Options.Schema} reference {applicationName} reference-{surface}-{window}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.Start();
        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var marker = await process.StandardOutput.ReadLineAsync(readyTimeout.Token);
        marker.Should().Be($"REFERENCE_{surface.ToUpperInvariant().Replace('-', '_')}_{window.ToUpperInvariant().Replace('-', '_')}");

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await WaitForBackendExitAsync(applicationName, fixture.ConnectionString);

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();
        await AssertSurfaceAsync(provider, surface, shouldExist);
    }

    private static async Task AssertSurfaceAsync(ServiceProvider provider, string surface, bool shouldExist)
    {
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        switch (surface)
        {
            case "draft":
                (await services.GetRequiredService<IDescriptorDraftStore>()
                    .GetAsync("crash", "reference-draft") is not null).Should().Be(shouldExist);
                break;
            case "organization-unit":
                (await services.GetRequiredService<IOrganizationStore>()
                    .GetOrganizationUnitByIdAsync("reference-unit", "crash") is not null).Should().Be(shouldExist);
                break;
            case "position":
                (await services.GetRequiredService<IOrganizationStore>()
                    .GetPositionByIdAsync("reference-position", "crash") is not null).Should().Be(shouldExist);
                break;
            case "membership":
                (await services.GetRequiredService<IOrganizationStore>()
                    .GetMembershipsByUserAsync("reference-user", "crash")).Any().Should().Be(shouldExist);
                break;
            case "role-assignment":
                (await services.GetRequiredService<IOrganizationStore>()
                    .GetRoleAssignmentsByUserAsync("reference-user", "crash")).Any().Should().Be(shouldExist);
                break;
            case "rule":
                (await services.GetRequiredService<IDataPermissionScopeRuleStore>()
                    .GetScopeKindAsync("reference-resource", "read", "view", "crash")).Should()
                    .Be(shouldExist ? DataPermissionScopeKind.Self : null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, null);
        }
    }

    private static async Task WaitForBackendExitAsync(string applicationName, string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "select count(*) from pg_stat_activity where application_name=@application;", connection);
            command.Parameters.AddWithValue("application", applicationName);
            if ((long)(await command.ExecuteScalarAsync())! == 0)
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException("The reference-data crash worker PostgreSQL backend did not exit.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
