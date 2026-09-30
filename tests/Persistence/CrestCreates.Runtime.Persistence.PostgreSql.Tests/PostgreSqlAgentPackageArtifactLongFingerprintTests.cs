using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Agent.ControlPlane.PackageArtifacts;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentPackageArtifactLongFingerprintTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task LongCatalogAndScopeFingerprints_ShouldPersistAndMatchExactlyDuringReuseLookup()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var artifacts = CreateArtifactFactory();
        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(lease.Options)
            .AddSingleton<IAgentPackageArtifactValidator>(artifacts.Validator)
            .AddCrestCreatesPostgreSqlPackageArtifactStore()
            .BuildServiceProvider();
        var store = provider.GetRequiredService<IAgentPackageArtifactStore>();

        var visibleCatalog = Enumerable.Range(0, 512)
            .Select(index => (IDescriptor)new SchemaDescriptor
            {
                Id = $"schema-{index:D4}",
                Name = $"Schema {index:D4}",
                Version = 1,
                State = DescriptorState.Active
            })
            .ToArray();
        var catalogFingerprint = AgentPackageArtifactFactory.ComputeVisibleCatalogFingerprint(visibleCatalog);
        catalogFingerprint.Length.Should().BeGreaterThan(8 * 1024);
        var scopeFingerprint = new string('s', 12 * 1024);
        var sameLengthDifferentCatalog = catalogFingerprint[..^1] + (catalogFingerprint[^1] == 'x' ? 'y' : 'x');
        var capturedAt = new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero);
        var exact = artifacts.Create("tenant-long", "draft-long", "package-long-a", capturedAt, scopeFingerprint, catalogFingerprint);
        var differentCatalog = artifacts.Create("tenant-long", "draft-long", "package-long-b", capturedAt.AddSeconds(1), scopeFingerprint, sameLengthDifferentCatalog);
        await store.InsertPackageAsync(exact);
        await store.InsertPackageAsync(differentCatalog);

        (await store.GetLatestReusablePackageAsync("tenant-long", "draft-long", scopeFingerprint, "1", catalogFingerprint))!
            .PackagePreviewId.Should().Be("package-long-a");
        (await store.GetLatestReusablePackageAsync("tenant-long", "draft-long", scopeFingerprint, "1", sameLengthDifferentCatalog))!
            .PackagePreviewId.Should().Be("package-long-b");
        (await store.GetLatestReusablePackageAsync("tenant-long", "draft-long", new string('s', 12 * 1024 - 1), "1", catalogFingerprint))
            .Should().BeNull();
    }

    private static LongFingerprintArtifactFactory CreateArtifactFactory()
    {
        var canonical = new DefaultCanonicalHashComputer();
        var packageHashes = new DefaultDescriptorPackageCanonicalHashComputer(canonical);
        var serializer = new DescriptorPackageSerializer();
        var validator = new AgentPackageArtifactValidator(serializer, packageHashes, canonical);
        var factory = new AgentPackageArtifactFactory(serializer, validator, new UnusedTopologyBuilder());
        var packageBuilder = new DefaultDescriptorPackageBuilder(new DescriptorStableHashBuilder(canonical), packageHashes);

        AgentPackageArtifactEnvelope Create(string tenantId, string draftId, string previewId, DateTimeOffset capturedAt, string scope, string catalog)
        {
            var descriptor = new SchemaDescriptor { Id = "schema-long", Name = "Schema", Version = 1, State = DescriptorState.Active };
            var draft = new CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft
            {
                TenantId = tenantId,
                DraftId = draftId,
                DescriptorKind = DescriptorKind.Schema,
                DescriptorId = descriptor.Id,
                Operation = DescriptorDraftOperation.Create,
                AuthorKind = DescriptorDraftAuthorKind.System,
                AuthorId = "system",
                CreatedAt = capturedAt,
                ProposedVersion = "1",
                Payload = new SchemaDescriptorDraftPayload(descriptor)
            };
            var package = packageBuilder.Build(new DescriptorPackageBuildRequest
            {
                PackageId = draftId,
                PackageVersion = "1",
                CreatedAt = capturedAt,
                CreatedBy = "system",
                Source = "long-fingerprint-test",
                Descriptors = [descriptor]
            });
            var preview = new DescriptorPackagePreview
            {
                DescriptorIds = [descriptor.Id],
                PackageManifestHash = package.Hashes!.PackageManifestHash,
                PackageEvidenceHash = package.Hashes.PackageEvidenceHash,
                PackageEvidenceEnvelopeHash = package.Hashes.PackageEvidenceEnvelopeHash
            };
            return factory.CreatePackage(previewId, capturedAt, draft, scope, catalog, preview, package);
        }

        return new LongFingerprintArtifactFactory(validator, Create);
    }

    private sealed record LongFingerprintArtifactFactory(
        IAgentPackageArtifactValidator Validator,
        Func<string, string, string, DateTimeOffset, string, string, AgentPackageArtifactEnvelope> Create);

    private sealed class UnusedTopologyBuilder : IDescriptorTopologyBuilder
    {
        public DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors) =>
            throw new NotSupportedException("Long-fingerprint persistence tests do not invoke topology projection.");
    }
}
