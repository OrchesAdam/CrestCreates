using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlControlPlaneReferenceDataStoreTests
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;

    public PostgreSqlControlPlaneReferenceDataStoreTests(PostgreSqlRuntimeCollectionFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Real_feature_stores_round_trip_generated_json_contracts()
    {
        await using var lease = await _fixture.CreateSchemaLeaseAsync();
        var services = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();

        await using var provider = services.BuildServiceProvider();
        var drafts = provider.GetRequiredService<IDescriptorDraftStore>();
        var organizations = provider.GetRequiredService<IOrganizationStore>();

        var draft = new Draft
        {
            TenantId = "tenant-1",
            DraftId = "draft-1",
            DescriptorKind = Metadata.Abstractions.DescriptorKind.Schema,
            DescriptorId = "schema-1",
            Operation = DescriptorDraftOperation.Create,
            AuthorKind = DescriptorDraftAuthorKind.System,
            AuthorId = "system",
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)),
            Payload = new SchemaDescriptorDraftPayload(new SchemaDescriptor
            {
                Id = "schema-1",
                Name = "Schema",
                Fields = new[]
                {
                    new SchemaFieldDescriptor { Name = "Name", FieldType = "string", IsRequired = true }
                }
            })
        };

        await drafts.SaveAsync(draft);
        (await drafts.GetAsync(draft.TenantId, draft.DraftId)).Should().BeEquivalentTo(draft);

        var organizationUnit = new OrganizationUnit
        {
            Id = "unit-1",
            TenantId = "tenant-1",
            Name = "Unit",
            Code = "U-1",
            ParentId = null,
            SortOrder = 1,
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
        };
        await organizations.SaveOrganizationUnitAsync(organizationUnit);

        (await organizations.GetOrganizationUnitByIdAsync(organizationUnit.Id, organizationUnit.TenantId))
            .Should().BeEquivalentTo(organizationUnit);

        var rules = provider.GetRequiredService<IDataPermissionScopeRuleStore>();
        await rules.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "reference-data",
            Action = "read",
            Permission = "view",
            TenantId = "tenant-1",
            ScopeKind = DataPermissionScopeKind.Self
        });
        (await rules.GetScopeKindAsync("reference-data", "read", "view", "tenant-1"))
            .Should().Be(DataPermissionScopeKind.Self);
    }
}
