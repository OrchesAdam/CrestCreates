using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Agent.ControlPlane.PackageArtifacts;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using CrestCreates.Metadata;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using DescriptorDraftModel = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentPackageArtifactStoreTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task Artifacts_ShouldRecoverAcrossInstancesAndSelectLatestByExactTenantScopeVersionAndCatalog()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var fixtureArtifacts = CreateArtifacts();
        await using (var firstProvider = BuildProvider(lease.Options, fixtureArtifacts.Validator))
        {
            var store = firstProvider.GetRequiredService<IAgentPackageArtifactStore>();
            var timestamp = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
            var tiedLower = fixtureArtifacts.Create("tenant-a", "draft-a", "package-a", "evidence-a", timestamp, "scope-a", "1", "catalog-a");
            var tiedHigher = fixtureArtifacts.Create("tenant-a", "draft-a", "package-z", "evidence-z", timestamp, "scope-a", "1", "catalog-a");
            var otherVersion = fixtureArtifacts.Create("tenant-a", "draft-a", "package-v2", "evidence-v2", timestamp.AddSeconds(1), "scope-a", "2", "catalog-a");
            var otherScope = fixtureArtifacts.Create("tenant-a", "draft-a", "package-scope-b", "evidence-scope-b", timestamp.AddSeconds(2), "scope-b", "1", "catalog-a");
            var otherCatalog = fixtureArtifacts.Create("tenant-a", "draft-a", "package-catalog-b", "evidence-catalog-b", timestamp.AddSeconds(3), "scope-a", "1", "catalog-b");
            var nullVersion = fixtureArtifacts.Create("tenant-a", "draft-null-version", "package-null-version", "evidence-null-version", timestamp, "scope-a", null, "catalog-a");

            await store.InsertPackageAndEvidenceAsync(tiedLower.Package, tiedLower.Evidence);
            await store.InsertPackageAndEvidenceAsync(tiedHigher.Package, tiedHigher.Evidence);
            await store.InsertPackageAsync(otherVersion.Package);
            await store.InsertPackageAsync(otherScope.Package);
            await store.InsertPackageAsync(otherCatalog.Package);
            await store.InsertPackageAsync(nullVersion.Package);
        }

        await using var recoveredProvider = BuildProvider(lease.Options, fixtureArtifacts.Validator);
        var recovered = recoveredProvider.GetRequiredService<IAgentPackageArtifactStore>();
        var loadedPackage = await recovered.GetPackageAsync(new AgentPackageArtifactKey("tenant-a", "package-z"));
        loadedPackage.Should().BeEquivalentTo(fixtureArtifacts.Create("tenant-a", "draft-a", "package-z", "evidence-z",
            new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero), "scope-a", "1", "catalog-a").Package);
        var loadedEvidence = await recovered.GetEvidenceAsync(new AgentEvidenceArtifactKey("tenant-a", "evidence-z"));
        loadedEvidence!.PackagePreviewId.Should().Be("package-z");
        (await recovered.GetPackageAsync(new AgentPackageArtifactKey("tenant-b", "package-z"))).Should().BeNull();
        (await recovered.GetLatestReusablePackageAsync("tenant-a", "draft-a", "scope-a", "1", "catalog-a"))!.PackagePreviewId.Should().Be("package-z");
        (await recovered.GetLatestReusablePackageAsync("tenant-a", "draft-a", "scope-a", "2", "catalog-a"))!.PackagePreviewId.Should().Be("package-v2");
        (await recovered.GetLatestReusablePackageAsync("tenant-a", "draft-null-version", "scope-a", null, "catalog-a"))!.PackagePreviewId.Should().Be("package-null-version");
        (await recovered.GetLatestReusablePackageAsync("tenant-b", "draft-a", "scope-a", "1", "catalog-a")).Should().BeNull();
    }

    [Fact]
    public async Task AtomicPairInsert_ShouldRollbackPackageWhenEvidenceIdentityCollides()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var artifacts = CreateArtifacts();
        await using var provider = BuildProvider(lease.Options, artifacts.Validator);
        var store = provider.GetRequiredService<IAgentPackageArtifactStore>();
        var existing = artifacts.Create("tenant-a", "draft-a", "package-existing", "evidence-reused", DateTimeOffset.UnixEpoch, "scope", "1", "catalog");
        var colliding = artifacts.Create("tenant-a", "draft-a", "package-new", "evidence-reused", DateTimeOffset.UnixEpoch.AddSeconds(1), "scope", "1", "catalog");
        await store.InsertPackageAndEvidenceAsync(existing.Package, existing.Evidence);

        var act = () => store.InsertPackageAndEvidenceAsync(colliding.Package, colliding.Evidence);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        (await store.GetPackageAsync(new AgentPackageArtifactKey("tenant-a", "package-new"))).Should().BeNull();
        (await store.GetEvidenceAsync(new AgentEvidenceArtifactKey("tenant-a", "evidence-reused")))!.PackagePreviewId.Should().Be("package-existing");
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("snapshot_relationships")]
    [InlineData("diagnostics")]
    [InlineData("structured_scope")]
    [InlineData("integrity_metadata")]
    [InlineData("evidence_scope_column")]
    [InlineData("evidence_parent_column")]
    public async Task Read_ShouldFailClosedForTamperedRetainedContentOrStructuredColumns(string corruption)
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var artifacts = CreateArtifacts();
        await using var provider = BuildProvider(lease.Options, artifacts.Validator);
        var store = provider.GetRequiredService<IAgentPackageArtifactStore>();
        var artifact = artifacts.Create("tenant-a", "draft-a", "package-a", "evidence-a", DateTimeOffset.UnixEpoch, "scope-a", "1", "catalog-a");
        await store.InsertPackageAndEvidenceAsync(artifact.Package, artifact.Evidence);
        if (corruption == "evidence_parent_column")
        {
            var second = artifacts.Create("tenant-a", "draft-a", "package-b", "evidence-b", DateTimeOffset.UnixEpoch.AddSeconds(1), "scope-a", "1", "catalog-a");
            await store.InsertPackageAsync(second.Package);
        }

        await using (var connection = new NpgsqlConnection(lease.Options.ConnectionString))
        {
            await connection.OpenAsync();
            if (corruption == "structured_scope")
            {
                await using var command = new NpgsqlCommand(
                    $"update \"{lease.Options.Schema}\".agent_package_artifacts set scope_fingerprint='scope-tampered' where tenant_id='tenant-a' and package_preview_id='package-a';", connection);
                await command.ExecuteNonQueryAsync();
            }
            else if (corruption is "evidence_scope_column" or "evidence_parent_column")
            {
                var column = corruption == "evidence_scope_column" ? "scope_fingerprint" : "package_preview_id";
                var replacement = corruption == "evidence_scope_column" ? "scope-tampered" : "package-b";
                await using var command = new NpgsqlCommand(
                    $"update \"{lease.Options.Schema}\".agent_evidence_artifacts set {column}=@replacement where tenant_id='tenant-a' and evidence_preview_id='evidence-a';", connection);
                command.Parameters.AddWithValue("replacement", replacement);
                await command.ExecuteNonQueryAsync();
            }
            else if (corruption == "integrity_metadata")
            {
                await using var command = new NpgsqlCommand(
                    $"update \"{lease.Options.Schema}\".agent_package_artifacts set state_json=jsonb_set(state_json, '{{contentIntegrityHash,canonicalShapeVersion}}', to_jsonb('tampered'::text)) where tenant_id='tenant-a' and package_preview_id='package-a';", connection);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                var packageJson = JsonNode.Parse(artifact.Package.PackageJson)!;
                if (corruption == "snapshot")
                    packageJson["snapshotData"]!["descriptors"]![0]!["supersededById"] = "substituted-schema";
                else if (corruption == "snapshot_relationships")
                {
                    var relationships = packageJson["snapshotData"]!["relationships"]!.AsArray();
                    relationships.Add(new JsonObject
                    {
                        ["from"] = new JsonObject { ["namespace"] = "schema", ["id"] = "schema-a", ["version"] = 1 },
                        ["to"] = new JsonObject { ["namespace"] = "schema", ["id"] = "other-schema", ["version"] = 1 },
                        ["kind"] = (int)RelationshipKind.DependsOn,
                        ["role"] = "tampered",
                        ["sourcePath"] = "snapshot.relationships",
                        ["strength"] = (int)RelationshipStrength.Strong,
                        ["isRuntimeBinding"] = true
                    });
                    var changedPackage = artifacts.Serializer.Deserialize(packageJson.ToJsonString());
                    var envelope = changedPackage.EvidenceEnvelope!;
                    var recomputedLegacyHashes = artifacts.PackageHashComputer.ComputeHashSet(changedPackage.Manifest, changedPackage.Evidence,
                        new DescriptorPackageEvidenceEnvelopeMetadata
                        {
                            PackageId = envelope.PackageId,
                            PackageVersion = envelope.PackageVersion,
                            CreatedAt = envelope.CreatedAt,
                            CreatedBy = envelope.CreatedBy,
                            Source = envelope.Source
                        });
                    recomputedLegacyHashes.Should().BeEquivalentTo(changedPackage.Hashes,
                        "snapshot relationship data is outside the existing three package hash inputs");
                }
                else
                    packageJson["diagnostics"]!.AsArray().Add(JsonSerializer.SerializeToNode(new DescriptorPackageDiagnostic
                    {
                        Code = new CrestCreates.Core.Abstractions.Identity.DiagnosticCode("TEST.TAMPER"),
                        Severity = DescriptorPackageDiagnosticSeverity.Warning,
                        Message = "tampered diagnostics"
                    }));

                var changedPackageJson = packageJson.ToJsonString();
                await using var command = new NpgsqlCommand(
                    $"update \"{lease.Options.Schema}\".agent_package_artifacts set state_json=jsonb_set(state_json, '{{packageJson}}', to_jsonb(@packageJson::text)) where tenant_id='tenant-a' and package_preview_id='package-a';", connection);
                command.Parameters.AddWithValue("packageJson", changedPackageJson);
                await command.ExecuteNonQueryAsync();
            }
        }

        Func<Task> read = corruption is "evidence_scope_column" or "evidence_parent_column"
            ? async () => { await store.GetEvidenceAsync(new AgentEvidenceArtifactKey("tenant-a", "evidence-a")); }
            : async () => { await store.GetPackageAsync(new AgentPackageArtifactKey("tenant-a", "package-a")); };
        await read.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(exception => exception.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
    }

    [Fact]
    public async Task EvidenceInsert_ShouldRequireExactTenantKeyedParent()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var artifacts = CreateArtifacts();
        await using var provider = BuildProvider(lease.Options, artifacts.Validator);
        var store = provider.GetRequiredService<IAgentPackageArtifactStore>();
        var pair = artifacts.Create("tenant-a", "draft-a", "package-a", "evidence-a", DateTimeOffset.UnixEpoch, "scope-a", "1", "catalog-a");
        var missingParent = pair.Evidence with { PackagePreviewId = "other-package", ProjectedEvidence = pair.Evidence.ProjectedEvidence with { PackagePreviewId = "other-package" } };

        var act = () => store.InsertEvidenceAsync(missingParent);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*parent package does not exist*");
        (await store.GetEvidenceAsync(new AgentEvidenceArtifactKey("tenant-a", "evidence-a"))).Should().BeNull();
    }

    [Fact]
    public async Task EvidenceInsert_ShouldRejectAnExistingParentWithDifferentCapturedScope()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var artifacts = CreateArtifacts();
        await using var provider = BuildProvider(lease.Options, artifacts.Validator);
        var store = provider.GetRequiredService<IAgentPackageArtifactStore>();
        var parent = artifacts.Create("tenant-a", "draft-a", "package-a", "unused-a", DateTimeOffset.UnixEpoch, "scope-a", "1", "catalog-a");
        var unrelated = artifacts.Create("tenant-a", "draft-a", "package-b", "evidence-b", DateTimeOffset.UnixEpoch.AddSeconds(1), "scope-b", "1", "catalog-a");
        await store.InsertPackageAsync(parent.Package);
        await store.InsertPackageAsync(unrelated.Package);
        var misboundEvidence = unrelated.Evidence with
        {
            PackagePreviewId = parent.Package.PackagePreviewId,
            ProjectedEvidence = unrelated.Evidence.ProjectedEvidence with { PackagePreviewId = parent.Package.PackagePreviewId }
        };

        var act = () => store.InsertEvidenceAsync(misboundEvidence);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*owner, scope, and draft version*");
        (await store.GetEvidenceAsync(new AgentEvidenceArtifactKey("tenant-a", "evidence-b"))).Should().BeNull();
    }

    [Fact]
    public async Task V015Migration_ShouldAddPackageAndEvidenceTablesAfterV014()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using (var history = new NpgsqlCommand(
            $"select version from \"{lease.Options.Schema}\".crest_runtime_schema_migrations order by version collate \"C\";", connection))
        {
            var versions = new List<string>();
            await using var reader = await history.ExecuteReaderAsync();
            while (await reader.ReadAsync()) versions.Add(reader.GetString(0));
            versions.TakeLast(2).Should().Equal("V014", "V015");
        }
        foreach (var table in new[] { "agent_package_artifacts", "agent_evidence_artifacts" })
        {
            await using var tableCheck = new NpgsqlCommand(
                "select exists(select 1 from information_schema.tables where table_schema=@schema and table_name=@table);", connection);
            tableCheck.Parameters.AddWithValue("schema", lease.Options.Schema);
            tableCheck.Parameters.AddWithValue("table", table);
            ((bool)(await tableCheck.ExecuteScalarAsync())!).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ProviderFailure_ShouldNotFallBackToMemoryStoreAndRegistrationRequiresKernel()
    {
        var artifacts = CreateArtifacts();
        var missingKernel = new ServiceCollection();
        var registration = () => missingKernel.AddCrestCreatesPostgreSqlPackageArtifactStore();
        registration.Should().Throw<InvalidOperationException>().WithMessage("*complete base PostgreSQL Runtime persistence provider kernel*");

        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1;Command Timeout=1",
            Schema = $"itest_{Guid.NewGuid():N}"
        };
        await using var provider = BuildProvider(options, artifacts.Validator);
        var store = provider.GetRequiredService<IAgentPackageArtifactStore>();
        store.Should().BeOfType<PostgreSqlAgentPackageArtifactStore>();
        var artifact = artifacts.Create("tenant-a", "draft-a", "package-a", "evidence-a", DateTimeOffset.UnixEpoch, "scope-a", "1", "catalog-a");
        var act = () => store.InsertPackageAsync(artifact.Package);
        await act.Should().ThrowAsync<RuntimePersistenceUnavailableException>();
    }

    [Fact]
    public void PostgreSqlRegistration_ShouldRequireKernelAndReplaceExplicitMemoryStoreInEitherOrder()
    {
        var missingKernel = new ServiceCollection();
        var missingKernelAct = () => missingKernel.AddCrestCreatesPostgreSqlPackageArtifactStore();
        missingKernelAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*complete base PostgreSQL Runtime persistence provider kernel*");

        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused",
            Schema = $"itest_{Guid.NewGuid():N}"
        };
        foreach (var postgresFirst in new[] { false, true })
        {
            var services = new ServiceCollection();
            services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
            if (postgresFirst)
            {
                services.AddCrestCreatesPostgreSqlPackageArtifactStore();
                services.AddAgentControlPlaneInMemoryStubs();
            }
            else
            {
                services.AddAgentControlPlaneInMemoryStubs();
                services.AddCrestCreatesPostgreSqlPackageArtifactStore();
            }
            services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IAgentPackageArtifactStore))
                .Which.ImplementationType.Should().Be(typeof(PostgreSqlAgentPackageArtifactStore));
        }
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options, IAgentPackageArtifactValidator validator)
        => new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddSingleton(validator)
            .AddCrestCreatesPostgreSqlPackageArtifactStore()
            .BuildServiceProvider();

    private static ArtifactFactory CreateArtifacts()
    {
        var canonical = new DefaultCanonicalHashComputer();
        var packageHashes = new DefaultDescriptorPackageCanonicalHashComputer(canonical);
        var serializer = new DescriptorPackageSerializer();
        var validator = new AgentPackageArtifactValidator(serializer, packageHashes, canonical);
        var factory = new AgentPackageArtifactFactory(serializer, validator, new UnusedTopologyBuilder());
        var builder = new DefaultDescriptorPackageBuilder(new DescriptorStableHashBuilder(canonical), packageHashes);
        return new ArtifactFactory(validator, serializer, packageHashes, (tenant, draftId, packageId, evidenceId, capturedAt, scope, version, catalog) =>
        {
            var descriptor = new SchemaDescriptor { Id = "schema-a", Name = "Schema A", Version = 1, State = DescriptorState.Active };
            var owner = new DescriptorDraftModel
            {
                TenantId = tenant,
                DraftId = draftId,
                DescriptorKind = DescriptorKind.Schema,
                DescriptorId = descriptor.Id,
                Operation = DescriptorDraftOperation.Create,
                AuthorKind = DescriptorDraftAuthorKind.System,
                AuthorId = "system",
                CreatedAt = capturedAt,
                ProposedVersion = version,
                Payload = new SchemaDescriptorDraftPayload(descriptor)
            };
            var package = builder.Build(new DescriptorPackageBuildRequest
            {
                PackageId = draftId,
                PackageVersion = version ?? "1",
                CreatedBy = owner.AuthorId,
                Source = "package-artifact-test",
                CreatedAt = capturedAt,
                Descriptors = new IDescriptor[] { descriptor }
            });
            var preview = new DescriptorPackagePreview
            {
                DescriptorIds = [descriptor.Id],
                PackageManifestHash = package.Hashes!.PackageManifestHash,
                PackageEvidenceHash = package.Hashes.PackageEvidenceHash,
                PackageEvidenceEnvelopeHash = package.Hashes.PackageEvidenceEnvelopeHash
            };
            var packageArtifact = factory.CreatePackage(packageId, capturedAt, owner, scope, catalog, preview, package);
            var evidenceProjection = new PackageEvidencePreview
            {
                DraftId = draftId,
                TenantId = tenant,
                PackagePreview = preview,
                Evidence = package.Evidence,
                Diagnostics = Array.Empty<AgentToolDiagnostic>()
            };
            var evidenceArtifact = factory.CreateEvidence(evidenceId, capturedAt, packageArtifact, evidenceProjection);
            return (packageArtifact, evidenceArtifact);
        });
    }

    private sealed record ArtifactFactory(
        IAgentPackageArtifactValidator Validator,
        IDescriptorPackageSerializer Serializer,
        IDescriptorPackageCanonicalHashComputer PackageHashComputer,
        Func<string, string, string, string, DateTimeOffset, string, string?, string, (AgentPackageArtifactEnvelope Package, AgentEvidenceArtifactEnvelope Evidence)> Create);

    private sealed class UnusedTopologyBuilder : IDescriptorTopologyBuilder
    {
        public DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors) =>
            throw new NotSupportedException("Package artifact store fixtures do not invoke topology projection.");
    }
}
