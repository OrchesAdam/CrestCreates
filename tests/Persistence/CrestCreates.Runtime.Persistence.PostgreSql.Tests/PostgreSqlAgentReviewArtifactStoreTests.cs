using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentReviewArtifactStoreTests(PostgreSqlRuntimeCollectionFixture fixture)
{
    [Fact]
    public async Task Artifact_Should_RecoverAcrossProviderInstances_WithOriginalInputsAndDeterministicLatest()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        var firstProvider = BuildProvider(lease.Options);
        var store = firstProvider.GetRequiredService<IAgentReviewArtifactStore>();
        var older = Artifact("tenant-a", "draft-a", "review-a", DateTimeOffset.UnixEpoch);
        var tiedLowerId = Artifact("tenant-a", "draft-a", "review-b", DateTimeOffset.UnixEpoch.AddSeconds(1));
        var tiedHigherId = Artifact("tenant-a", "draft-a", "review-z", DateTimeOffset.UnixEpoch.AddSeconds(1));
        var otherDraft = Artifact("tenant-a", "draft-b", "review-other", DateTimeOffset.UnixEpoch.AddSeconds(2));
        var hashService = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var fixedTime = new DateTimeOffset(2026, 9, 24, 3, 4, 5, TimeSpan.Zero);
        var reportBuilder = new DefaultDescriptorReviewReportBuilder(
            new DefaultDescriptorReviewMessageTemplateCatalog(), hashService, new FixedTimeProvider(fixedTime));
        var expectedReport = reportBuilder.Build(tiedHigherId.ReportInput);
        var expectedSourceHash = hashService.ComputeSourceReviewHash(tiedHigherId.OriginalHashInput);
        var expectedManifestHash = hashService.ComputeReviewManifestHash(tiedHigherId.OriginalHashInput);
        expectedSourceHash.Should().NotBe(hashService.ComputeSourceReviewHash(tiedHigherId.ReportInput.ToReviewHashInput()),
            "original review hashes are retained independently from projected report facts");

        await store.InsertAsync(older);
        await store.InsertAsync(tiedLowerId);
        await store.InsertAsync(tiedHigherId);
        await store.InsertAsync(otherDraft);
        await firstProvider.DisposeAsync();

        await using var recoveredProvider = BuildProvider(lease.Options);
        var recovered = recoveredProvider.GetRequiredService<IAgentReviewArtifactStore>();
        var loaded = await recovered.GetAsync("tenant-a", "review-z");

        loaded.Should().BeEquivalentTo(tiedHigherId);
        loaded!.OriginalHashInput.Should().BeEquivalentTo(tiedHigherId.OriginalHashInput);
        loaded.ReportInput.Should().BeEquivalentTo(tiedHigherId.ReportInput);
        hashService.ComputeSourceReviewHash(loaded.OriginalHashInput).Should().Be(expectedSourceHash);
        hashService.ComputeReviewManifestHash(loaded.OriginalHashInput).Should().Be(expectedManifestHash);
        reportBuilder.Build(loaded.ReportInput).Should().BeEquivalentTo(expectedReport);
        (await recovered.GetLatestAsync("tenant-a", "draft-a"))!.ReviewResultId.Should().Be("review-z");
        (await recovered.ListAsync("tenant-a", "draft-a")).Select(item => item.ReviewResultId)
            .Should().Equal("review-a", "review-b", "review-z");
        (await recovered.ListAsync("tenant-a")).Should().HaveCount(4);
    }

    [Fact]
    public async Task Artifact_Should_IsolateTenantsAndRejectDuplicateTenantReviewKey()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var provider = BuildProvider(lease.Options);
        var store = provider.GetRequiredService<IAgentReviewArtifactStore>();
        await store.InsertAsync(Artifact("tenant-a", "draft-a", "same-id", DateTimeOffset.UnixEpoch));
        await store.InsertAsync(Artifact("tenant-b", "draft-a", "same-id", DateTimeOffset.UnixEpoch));

        (await store.GetAsync("tenant-a", "same-id"))!.TenantId.Should().Be("tenant-a");
        (await store.GetAsync("tenant-b", "same-id"))!.TenantId.Should().Be("tenant-b");
        (await store.ListAsync("tenant-a")).Should().ContainSingle();

        var act = () => store.InsertAsync(Artifact("tenant-a", "draft-other", "same-id", DateTimeOffset.UnixEpoch));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        (await store.GetAsync("tenant-a", "same-id"))!.DraftId.Should().Be("draft-a");
    }

    [Theory]
    [InlineData("draft_id")]
    [InlineData("draft_id_column")]
    [InlineData("created_at_payload")]
    [InlineData("created_at_column")]
    [InlineData("version_payload")]
    [InlineData("version_column")]
    [InlineData("null_payload")]
    public async Task Artifact_Should_FailClosed_WhenStoredColumnsDisagreeWithEnvelope(string corruption)
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var provider = BuildProvider(lease.Options);
        var store = provider.GetRequiredService<IAgentReviewArtifactStore>();
        await store.InsertAsync(Artifact("tenant-a", "draft-a", "review-a", DateTimeOffset.UnixEpoch));

        if (corruption is "draft_id_column" or "created_at_column" or "version_column")
        {
            await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
            await connection.OpenAsync();
            if (corruption == "version_column")
            {
                await using var dropConstraint = new NpgsqlCommand(
                    $"alter table \"{lease.Options.Schema}\".agent_review_artifacts drop constraint ck_agent_review_artifact_version;", connection);
                await dropConstraint.ExecuteNonQueryAsync();
            }
            var updateSql = corruption switch
            {
                "draft_id_column" => $"update \"{lease.Options.Schema}\".agent_review_artifacts set draft_id='different-draft' where tenant_id='tenant-a' and review_result_id='review-a';",
                "created_at_column" => $"update \"{lease.Options.Schema}\".agent_review_artifacts set created_at=created_at + interval '1 second' where tenant_id='tenant-a' and review_result_id='review-a';",
                "version_column" => $"update \"{lease.Options.Schema}\".agent_review_artifacts set artifact_version=2 where tenant_id='tenant-a' and review_result_id='review-a';",
                _ => throw new ArgumentOutOfRangeException(nameof(corruption))
            };
            await using var command = new NpgsqlCommand(updateSql, connection);
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            var jsonPath = corruption switch
            {
                "draft_id" => "'{projectedReview,draftId}'",
                "created_at_payload" => "'{createdAt}'",
                "version_payload" => "'{version}'",
                _ => null
            };
            var jsonValue = corruption switch
            {
                "draft_id" => "'\"different-draft\"'::jsonb",
                "created_at_payload" => "'\"1970-01-01T00:00:01+00:00\"'::jsonb",
                "version_payload" => "'2'::jsonb",
                _ => null
            };
            await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
            await connection.OpenAsync();
            var update = corruption == "null_payload"
                ? $"update \"{lease.Options.Schema}\".agent_review_artifacts set state_json='null'::jsonb where tenant_id='tenant-a' and review_result_id='review-a';"
                : $"update \"{lease.Options.Schema}\".agent_review_artifacts set state_json=jsonb_set(state_json, {jsonPath}, {jsonValue}) where tenant_id='tenant-a' and review_result_id='review-a';";
            await using var command = new NpgsqlCommand(update, connection);
            await command.ExecuteNonQueryAsync();
        }

        var act = () => store.GetAsync("tenant-a", "review-a");
        await act.Should().ThrowAsync<RuntimePersistenceContractException>()
            .Where(error => error.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
        if (corruption == "draft_id_column")
        {
            var latestAct = () => store.GetLatestAsync("tenant-a", "different-draft");
            await latestAct.Should().ThrowAsync<RuntimePersistenceContractException>()
                .Where(error => error.Code == RuntimePersistenceContractErrorCode.PersistedInvariantViolation);
        }
    }

    [Fact]
    public async Task Artifact_Should_SurfaceProviderFailureWithoutMemoryFallback()
    {
        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1;Command Timeout=1",
            Schema = $"itest_{Guid.NewGuid():N}"
        };
        await using var provider = new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddCrestCreatesPostgreSqlReviewArtifactStore()
            .BuildServiceProvider();

        var store = provider.GetRequiredService<IAgentReviewArtifactStore>();
        var act = () => store.InsertAsync(Artifact("tenant-a", "draft-a", "review-a", DateTimeOffset.UnixEpoch));
        await act.Should().ThrowAsync<RuntimePersistenceUnavailableException>();
        store.Should().BeOfType<PostgreSqlAgentReviewArtifactStore>();
    }

    [Fact]
    public void PostgreSqlReviewArtifactRegistration_Should_RequireKernelAndWinAgainstMemoryRegistrationInEitherOrder()
    {
        var missingKernel = new ServiceCollection();
        var missingKernelAct = () => missingKernel.AddCrestCreatesPostgreSqlReviewArtifactStore();
        missingKernelAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*complete base PostgreSQL Runtime persistence provider kernel*");

        var options = new PostgreSqlRuntimePersistenceOptions
        {
            ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused",
            Schema = $"itest_{Guid.NewGuid():N}"
        };
        foreach (var pgFirst in new[] { false, true })
        {
            var services = new ServiceCollection();
            if (pgFirst)
            {
                services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
                services.AddCrestCreatesPostgreSqlReviewArtifactStore();
                services.AddAgentControlPlaneInMemoryStubs();
            }
            else
            {
                services.AddAgentControlPlaneInMemoryStubs();
                services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
                services.AddCrestCreatesPostgreSqlReviewArtifactStore();
            }

            services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IAgentReviewArtifactStore))
                .Which.ImplementationType.Should().Be(typeof(PostgreSqlAgentReviewArtifactStore));
        }
    }

    [Fact]
    public async Task V014Migration_Should_AddReviewArtifactTableAfterV013()
    {
        await using var lease = await fixture.CreateSchemaLeaseAsync();
        await using var connection = new NpgsqlConnection(lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select version from \"{lease.Options.Schema}\".crest_runtime_schema_migrations order by version collate \"C\";",
            connection);
        var versions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            versions.Add(reader.GetString(0));

        versions.TakeLast(3).Should().Equal("V012", "V013", "V014");
        await AssertTableAsync(lease.Options, "agent_review_artifacts");
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddCrestCreatesPostgreSqlReviewArtifactStore()
            .BuildServiceProvider();

    private static async Task AssertTableAsync(PostgreSqlRuntimePersistenceOptions options, string table)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema=@schema and table_name=@table;",
            connection);
        command.Parameters.AddWithValue("schema", options.Schema);
        command.Parameters.AddWithValue("table", table);
        Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(1);
    }

    private static AgentReviewArtifactEnvelope Artifact(string tenantId, string draftId, string reviewId, DateTimeOffset createdAt)
    {
        var diagnostics = Array.Empty<DescriptorDraftDiagnostic>();
        var projected = new AgentReviewResultDto
        {
            TenantId = tenantId,
            DraftId = draftId,
            ValidationResult = DescriptorDraftValidationResult.Success(),
            Diagnostics = diagnostics,
            IsActivationEligible = true
        };
        var reportInput = new DescriptorReviewReportInputSnapshot
        {
            Version = DescriptorReviewReportInputSnapshot.CurrentVersion,
            TenantId = tenantId,
            DraftId = draftId,
            IsActivationEligible = true,
            IsValid = true,
            ValidationDiagnostics = diagnostics,
            ReviewDiagnostics = diagnostics,
            Owner = new DescriptorReviewReportOwnerInput
            {
                TenantId = tenantId,
                DraftId = draftId,
                DescriptorId = "descriptor-a",
                DescriptorKind = DescriptorKind.Schema,
                Operation = DescriptorDraftOperation.Create,
                AuthorKind = DescriptorDraftAuthorKind.System,
                AuthorId = "system",
                Status = DescriptorDraftStatus.Created
            },
            Materialization = null,
            Topology = null,
            Impact = null,
            CompatibilityFindings = null,
            GovernanceMaxDecision = null,
            GovernanceFirstDecision = null,
            GovernanceFirstTransition = null,
            GovernancePackageFindingSubjectIds = null,
            PackagePreview = null,
            StableHashes = null
        };
        var originalHashInput = new DescriptorDraftReviewHashInput
        {
            Version = DescriptorDraftReviewHashInput.CurrentVersion,
            SourceBinding = new ReviewResultSourceBindingProjection
            {
                TenantId = tenantId,
                DraftId = draftId,
                IsActivationEligible = false,
                IsValid = false,
                Diagnostics = [new ReviewDiagnosticProjection { Code = "ORIGINAL_ONLY", Severity = "Warning" }]
            }
        };
        var artifact = new AgentReviewArtifactEnvelope
        {
            Version = AgentReviewArtifactEnvelope.CurrentVersion,
            TenantId = tenantId,
            ReviewResultId = reviewId,
            CreatedAt = createdAt,
            ScopeFingerprint = "scope-fingerprint",
            ProjectedReview = projected,
            ReportInput = reportInput,
            OriginalHashInput = originalHashInput
        };
        artifact.Validate();
        return artifact;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
