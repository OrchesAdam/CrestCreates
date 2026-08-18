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

        // Find the window token(s) after the surface.
        // Two-token windows: before-commit, after-commit, commit-unknown
        // Three-token window: save-and-exit
        int windowTokenCount = scenario.EndsWith("-save-and-exit") ? 3 : 2;
        var window = string.Join('-', parts[^windowTokenCount..]);
        var surface = string.Join('-', parts[1..^windowTokenCount]);

        if (window is not ("before-commit" or "after-commit" or "commit-unknown" or "save-and-exit"))
            throw new ArgumentException($"Invalid reference-data crash window '{window}'.", nameof(scenario));

        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider();

        var marker = $"REFERENCE_{surface.ToUpperInvariant().Replace('-', '_')}_{window.ToUpperInvariant().Replace('-', '_')}";

        if (window == "save-and-exit")
        {
            await SaveAsync(provider, surface);
            Console.WriteLine(marker);
            Console.Out.Flush();
            return 0;
        }

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
            case "hierarchy":
                var orgStore = provider.GetRequiredService<IOrganizationStore>();
                await orgStore.SaveOrganizationUnitAsync(new OrganizationUnit
                {
                    Id = "restart-parent-unit",
                    TenantId = "restart",
                    Name = "Restart Parent",
                    Code = "R-P",
                    ParentId = null,
                    SortOrder = 1,
                    CreatedAt = DateTimeOffset.UnixEpoch
                });
                await orgStore.SaveOrganizationUnitAsync(new OrganizationUnit
                {
                    Id = "restart-child-unit",
                    TenantId = "restart",
                    Name = "Restart Child",
                    Code = "R-C",
                    ParentId = "restart-parent-unit",
                    SortOrder = 2,
                    CreatedAt = DateTimeOffset.UnixEpoch
                });
                break;
            case "all-org-surfaces":
                var store = provider.GetRequiredService<IOrganizationStore>();
                await store.SaveOrganizationUnitAsync(new OrganizationUnit
                {
                    Id = "restart-unit",
                    TenantId = "restart",
                    Name = "Restart Unit",
                    Code = "R-U",
                    SortOrder = 1,
                    CreatedAt = DateTimeOffset.UnixEpoch
                });
                await store.SavePositionAsync(new Position
                {
                    Id = "restart-position",
                    TenantId = "restart",
                    Name = "Restart Position"
                });
                await store.SaveMembershipAsync(new UserOrganizationMembership
                {
                    Id = "restart-membership",
                    TenantId = "restart",
                    UserId = "restart-user",
                    OrganizationUnitId = "restart-unit",
                    PositionId = "restart-position",
                    IsPrimary = true,
                    CreatedAt = DateTimeOffset.UnixEpoch
                });
                await store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment
                {
                    Id = "restart-role-assignment",
                    TenantId = "restart",
                    UserId = "restart-user",
                    RoleId = "restart-role"
                });
                break;
            default:
                throw new ArgumentException($"Unknown reference-data surface '{surface}'.", nameof(surface));
        }
    }
}
