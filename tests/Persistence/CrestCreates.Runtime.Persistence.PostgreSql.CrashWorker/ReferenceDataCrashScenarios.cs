using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker;

internal static class ReferenceDataCrashScenarios
{
    public static async Task<int> RunAsync(
        PostgreSqlRuntimePersistenceOptions options,
        string scenario,
        string applicationName)
    {
        var parts = scenario.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || parts[0] != "reference")
            throw new ArgumentException($"Invalid reference-data crash scenario '{scenario}'.", nameof(scenario));

        var surface = string.Join('-', parts[1..^2]);
        var window = $"{parts[^2]}-{parts[^1]}";
        if (window == "before-commit" || window == "after-commit" || window == "commit-unknown")
        {
            // handled below
        }
        else
        {
            throw new ArgumentException($"Invalid reference-data crash window '{window}'.", nameof(scenario));
        }

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();

        var marker = $"REFERENCE_{surface.ToUpperInvariant().Replace('-', '_')}_{window.ToUpperInvariant().Replace('-', '_')}";
        IDisposable? beforeCommit = null;
        IDisposable? afterCommit = null;
        if (window == "before-commit")
        {
            beforeCommit = PostgreSqlRuntimeTestHooks.BlockBeforeCommit(_ => WaitAfterMarkerAsync(marker));
        }
        else if (window == "commit-unknown")
        {
            afterCommit = PostgreSqlRuntimeTestHooks.BlockAfterCommit(() =>
                throw new IOException("Simulated lost COMMIT acknowledgement."));
        }

        try
        {
            await SaveAsync(provider, surface);
            if (window == "after-commit")
                await WaitAfterMarkerAsync(marker);
        }
        catch (RuntimeTransactionCommitUnknownException) when (window == "commit-unknown")
        {
            Console.WriteLine(marker);
            Console.Out.Flush();
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
        finally
        {
            beforeCommit?.Dispose();
            afterCommit?.Dispose();
        }

        return 0;
    }

    private static async ValueTask WaitAfterMarkerAsync(string marker)
    {
        Console.WriteLine(marker);
        Console.Out.Flush();
        await Task.Delay(TimeSpan.FromMinutes(5));
    }

    private static async Task SaveAsync(ServiceProvider provider, string surface)
    {
        switch (surface)
        {
            case "draft":
                await provider.GetRequiredService<IDescriptorDraftStore>().SaveAsync(new Draft
                {
                    TenantId = "crash",
                    DraftId = "reference-draft",
                    DescriptorKind = DescriptorKind.Schema,
                    DescriptorId = "reference-schema",
                    Operation = DescriptorDraftOperation.Create,
                    AuthorKind = DescriptorDraftAuthorKind.System,
                    AuthorId = "crash",
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
                    {
                        Id = "reference-schema",
                        Name = "Reference Schema"
                    })
                });
                break;
            case "organization-unit":
                await provider.GetRequiredService<IOrganizationStore>().SaveOrganizationUnitAsync(new OrganizationUnit
                {
                    Id = "reference-unit",
                    TenantId = "crash",
                    Name = "Reference Unit"
                });
                break;
            case "position":
                await provider.GetRequiredService<IOrganizationStore>().SavePositionAsync(new Position
                {
                    Id = "reference-position",
                    TenantId = "crash",
                    Name = "Reference Position"
                });
                break;
            case "membership":
                await provider.GetRequiredService<IOrganizationStore>().SaveMembershipAsync(new UserOrganizationMembership
                {
                    Id = "reference-membership",
                    TenantId = "crash",
                    UserId = "reference-user",
                    OrganizationUnitId = "reference-unit"
                });
                break;
            case "role-assignment":
                await provider.GetRequiredService<IOrganizationStore>().SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment
                {
                    Id = "reference-role-assignment",
                    TenantId = "crash",
                    UserId = "reference-user",
                    RoleId = "reference-role"
                });
                break;
            case "rule":
                await provider.GetRequiredService<IDataPermissionScopeRuleStore>().SaveRuleAsync(new DataPermissionScopeRule
                {
                    Resource = "reference-resource",
                    Action = "read",
                    Permission = "view",
                    TenantId = "crash",
                    ScopeKind = DataPermissionScopeKind.Self
                });
                break;
            default:
                throw new ArgumentException($"Unknown reference-data surface '{surface}'.", nameof(surface));
        }
    }
}
