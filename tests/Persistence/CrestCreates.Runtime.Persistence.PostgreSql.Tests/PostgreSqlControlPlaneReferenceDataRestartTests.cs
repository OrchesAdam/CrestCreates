using System.Diagnostics;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// Restart durability tests for Control Plane and Reference Data stores.
/// D09: provider restart (in-process), D10/O16-O18/P09: process restart via CrashWorker.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlControlPlaneReferenceDataRestartTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    // D09: DescriptorDraft_Should_SurviveProviderRestart
    [Fact]
    public async Task DescriptorDraft_Should_SurviveProviderRestart()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        await using (var provider = services.BuildServiceProvider())
        {
            var drafts = provider.GetRequiredService<IDescriptorDraftStore>();
            await drafts.SaveAsync(CreateRestartDraft("restart-draft"));
        }

        await using var rebuilt = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();

        var read = await rebuilt.GetRequiredService<IDescriptorDraftStore>()
            .GetAsync("restart", "restart-draft");
        read.Should().NotBeNull();
        read!.DraftId.Should().Be("restart-draft");
        read.DescriptorId.Should().Be("restart-schema");
        read.Operation.Should().Be(DescriptorDraftOperation.Create);
    }

    // D10: DescriptorDraft_Should_SurviveProcessRestart
    [Fact]
    public async Task DescriptorDraft_Should_SurviveProcessRestart()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await RunSaveAndExitAsync("reference-draft-save-and-exit", lease.Options.Schema);

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();

        var read = await provider.GetRequiredService<IDescriptorDraftStore>()
            .GetAsync("crash", "reference-draft");
        read.Should().NotBeNull();
        read!.DraftId.Should().Be("reference-draft");
        read.DescriptorId.Should().Be("reference-schema");
    }

    // O16: OrganizationEntitySurface_Should_SurviveProcessRestart
    [Fact]
    public async Task OrganizationEntitySurface_Should_SurviveProcessRestart()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await RunSaveAndExitAsync("reference-all-org-surfaces-save-and-exit", lease.Options.Schema);

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();
        var org = provider.GetRequiredService<IOrganizationStore>();

        (await org.GetOrganizationUnitByIdAsync("restart-unit", "restart")).Should().NotBeNull();
        (await org.GetPositionByIdAsync("restart-position", "restart")).Should().NotBeNull();
        (await org.GetMembershipsByUserAsync("restart-user", "restart")).Should().NotBeEmpty();
        (await org.GetRoleAssignmentsByUserAsync("restart-user", "restart")).Should().NotBeEmpty();
    }

    // O17: OrganizationHierarchy_Should_RemainStableAfterRestart
    [Fact]
    public async Task OrganizationHierarchy_Should_RemainStableAfterRestart()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await RunSaveAndExitAsync("reference-hierarchy-save-and-exit", lease.Options.Schema);

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .AddOrganizationKernel()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var hierarchy = scope.ServiceProvider.GetRequiredService<IOrganizationHierarchyService>();
        var descendants = await hierarchy.GetDescendantsAsync("restart-parent-unit", "restart");
        descendants.Should().HaveCount(1);
        descendants[0].Id.Should().Be("restart-child-unit");
    }

    // O18: OrganizationIdentity_Should_RemainStableAfterRestart
    [Fact]
    public async Task OrganizationIdentity_Should_RemainStableAfterRestart()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await RunSaveAndExitAsync("reference-all-org-surfaces-save-and-exit", lease.Options.Schema);

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .AddOrganizationKernel()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IOrganizationIdentityService>();
        var context = await identity.GetContextAsync("restart-user", "restart");
        context.Should().NotBeNull();
        context.PrimaryOrganizationUnitId.Should().Be("restart-unit");
        context.PositionIds.Should().Contain("restart-position");
        context.RoleIds.Should().Contain("restart-role");
    }

    // P09: DataPermissionRule_Should_SurviveProcessRestart
    [Fact]
    public async Task DataPermissionRule_Should_SurviveProcessRestart()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await RunSaveAndExitAsync("reference-rule-save-and-exit", lease.Options.Schema);

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();

        var scopeKind = await provider.GetRequiredService<IDataPermissionScopeRuleStore>()
            .GetScopeKindAsync("reference-resource", "read", "view", "crash");
        scopeKind.Should().Be(DataPermissionScopeKind.Self);
    }

    private async Task RunSaveAndExitAsync(string scenario, string schema)
    {
        var root = FindRepositoryRoot();
        var worker = Path.Combine(root,
            "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/bin/Debug/net10.0/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.dll");
        File.Exists(worker).Should().BeTrue("the CrashWorker must be built by the test project reference");

        var applicationName = $"phase9b-restart-{Guid.NewGuid():N}";
        var connectionBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = applicationName
        };
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{worker}\" \"{connectionBuilder.ConnectionString}\" {schema} reference {applicationName} {scenario}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var marker = await process.StandardOutput.ReadLineAsync(timeout.Token);
        marker.Should().StartWith("REFERENCE_");

        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(exitTimeout.Token);
        process.ExitCode.Should().Be(0, "save-and-exit should complete cleanly");
        await WaitForBackendExitAsync(applicationName, fixture.ConnectionString);
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

        throw new TimeoutException("The restart worker PostgreSQL backend did not exit.");
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

    private static Draft CreateRestartDraft(string draftId)
        => new()
        {
            TenantId = "restart",
            DraftId = draftId,
            DescriptorKind = DescriptorKind.Schema,
            DescriptorId = "restart-schema",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.System,
            AuthorId = "restart",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
            {
                Id = "restart-schema",
                Name = "Restart Schema"
            })
        };
}
