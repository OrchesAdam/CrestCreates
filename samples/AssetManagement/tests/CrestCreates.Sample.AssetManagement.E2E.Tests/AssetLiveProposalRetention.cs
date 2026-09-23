using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

using DescriptorDraftModel = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Test-only opt-in retention for the live authoring probe. It deliberately
/// persists only a draft which the caller has already accepted; it has no
/// approval, activation, or registry surface.
/// </summary>
internal sealed class AssetLiveProposalRetention
{
    private const string EnableVariable = "CREST_ASSET_LIVE_RETAIN_PROPOSAL";
    private const string SchemaVariable = "CREST_ASSET_LIVE_RETAIN_SCHEMA";
    private const string ConnectionVariable = "ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING";

    private AssetLiveProposalRetention(bool enabled, string? connectionString, string? schema, string preflightCode)
    {
        Enabled = enabled;
        ConnectionString = connectionString;
        Schema = schema;
        PreflightCode = preflightCode;
    }

    public bool Enabled { get; }
    public string? ConnectionString { get; }
    public string? Schema { get; }
    public string PreflightCode { get; }

    public static AssetLiveProposalRetention FromEnvironment() => Create(
        Environment.GetEnvironmentVariable(EnableVariable),
        Environment.GetEnvironmentVariable(ConnectionVariable),
        Environment.GetEnvironmentVariable(SchemaVariable));

    internal static AssetLiveProposalRetention Create(string? requested, string? connectionString, string? schema)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.Equals(requested, "0", StringComparison.Ordinal))
            return new(false, null, null, "RETENTION_DISABLED");

        if (!string.Equals(requested, "1", StringComparison.Ordinal))
            return new(true, null, null, "RETENTION_ENABLEMENT_INVALID");

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(schema))
            return new(true, connectionString, schema, "RETENTION_CONFIGURATION_INVALID");

        return new(true, connectionString, schema, "RETENTION_REQUESTED");
    }

    /// <summary>
    /// Runs before authoring so an invalid provider configuration, unavailable
    /// database, or incompatible schema cannot spend a live model request.
    /// </summary>
    public async Task<AssetLiveRetentionPreflight> PreflightAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled)
            return new(true, PreflightCode);
        if (PreflightCode != "RETENTION_REQUESTED")
            return new(false, PreflightCode);

        try
        {
            var options = CreateOptions();
            await using (var provider = BuildProvider(options))
            {
                // Registration is the formal options-validation boundary used
                // by the real provider, before any schema work or HTTP call.
            }
            await new PostgreSqlRuntimeMigrationRunner(options).ApplyAsync(
                new PostgreSqlRuntimeMigrationOptions { ApplyMigrations = true }, cancellationToken);
            return new(true, "RETENTION_READY");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            return new(false, "RETENTION_CONFIGURATION_INVALID");
        }
        catch (Exception)
        {
            // A public run artifact must not disclose a connection, provider
            // response, or exception details.
            return new(false, "RETENTION_STORAGE_UNAVAILABLE");
        }
    }

    public async Task<AssetLiveRetentionResult> RetainAndReplayAsync(
        string runId,
        DescriptorDraftModel draft,
        IReadOnlyList<IDescriptor> baseline,
        string authorId,
        string? promptInputHash,
        string? promptOutputHash,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled || PreflightCode != "RETENTION_REQUESTED")
            return AssetLiveRetentionResult.Failure("RETENTION_NOT_READY", "retention");

        try
        {
            var options = CreateOptions();
            await using (var initialProvider = BuildProvider(options))
            {
                await initialProvider.GetRequiredService<IDescriptorDraftStore>()
                    .SaveAsync(draft, cancellationToken);
            }

            DescriptorDraftModel reloaded;
            await using (var restartedProvider = BuildProvider(options))
            {
                reloaded = await restartedProvider.GetRequiredService<IDescriptorDraftStore>()
                    .GetAsync(draft.TenantId, draft.DraftId, cancellationToken)
                    ?? throw new AssetLiveRetentionFailure("RETENTION_READBACK_MISSING", "readback");
            }

            var originalHashes = BuildHashes(draft);
            var reloadedHashes = BuildHashes(reloaded);
            if (!SameEnvelope(draft, reloaded) || originalHashes != reloadedHashes)
                return AssetLiveRetentionResult.Failure("RETENTION_READBACK_MISMATCH", "readback");

            await using var replayHarness = await AssetControlPlaneApprovalHarness.CreateAsync(draft.TenantId, authorId);
            var review = await replayHarness.Services.GetRequiredService<IDescriptorDraftReviewService>()
                .ReviewAsync(reloaded, baseline, cancellationToken);
            if (!ReviewPassed(review))
                return AssetLiveRetentionResult.Failure("RETENTION_REPLAY_REVIEW_FAILED", "replay");

            return AssetLiveRetentionResult.Success(new AssetLiveProposalLocator(
                runId,
                draft.TenantId,
                draft.DraftId,
                Schema!,
                originalHashes,
                promptInputHash,
                promptOutputHash));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AssetLiveRetentionFailure failure)
        {
            return AssetLiveRetentionResult.Failure(failure.Code, failure.Stage);
        }
        catch (Exception)
        {
            return AssetLiveRetentionResult.Failure("RETENTION_PERSISTENCE_FAILED", "persistence");
        }
    }

    private PostgreSqlRuntimePersistenceOptions CreateOptions() => new()
    {
        ConnectionString = ConnectionString!,
        Schema = Schema!,
        ApplyMigrations = true
    };

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options) => new ServiceCollection()
        .AddCrestCreatesPostgreSqlRuntimePersistence(options)
        .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static DescriptorStableHashes BuildHashes(DescriptorDraftModel draft) =>
        new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer())
            .Build(draft.Payload.GetDescriptor());

    private static bool SameEnvelope(DescriptorDraftModel expected, DescriptorDraftModel actual) =>
        expected.TenantId == actual.TenantId
        && expected.DraftId == actual.DraftId
        && expected.DescriptorKind == actual.DescriptorKind
        && expected.DescriptorId == actual.DescriptorId
        && expected.Operation == actual.Operation
        && expected.AuthorKind == actual.AuthorKind
        && expected.AuthorId == actual.AuthorId
        && expected.CreatedAt == actual.CreatedAt
        && expected.BaseVersion == actual.BaseVersion
        && expected.ProposedVersion == actual.ProposedVersion
        && expected.Intent == actual.Intent
        && expected.Rationale == actual.Rationale
        && expected.CorrelationId == actual.CorrelationId
        && expected.Source == actual.Source
        && expected.Status == actual.Status
        && expected.Payload.GetType() == actual.Payload.GetType()
        && SameMetadata(expected.Metadata, actual.Metadata);

    private static bool SameMetadata(IReadOnlyDictionary<string, string>? expected, IReadOnlyDictionary<string, string>? actual)
    {
        if (expected is null || actual is null)
            return expected is null && actual is null;
        return expected.Count == actual.Count
            && expected.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    private static bool ReviewPassed(DescriptorDraftReviewResult review)
    {
        var hasBlockingDiagnostics = review.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == SeverityLevel.Blocker || diagnostic.Severity == SeverityLevel.Error);
        return review.ValidationResult.IsValid
            && review.MaterializationResult?.IsMaterialized == true
            && !hasBlockingDiagnostics
            && review.GovernanceDecision?.MaxDecision
                != CrestCreates.Metadata.Abstractions.DescriptorLifecycle.DescriptorLifecycleDecisionKind.Blocked;
    }

    private sealed class AssetLiveRetentionFailure(string code, string stage) : Exception
    {
        public string Code { get; } = code;
        public string Stage { get; } = stage;
    }
}

internal sealed record AssetLiveRetentionPreflight(bool Succeeded, string Code);

internal sealed record AssetLiveRetentionResult(
    bool Succeeded,
    string? FailureCode,
    string? FailureStage,
    AssetLiveProposalLocator? Locator)
{
    public static AssetLiveRetentionResult Success(AssetLiveProposalLocator locator) => new(true, null, null, locator);
    public static AssetLiveRetentionResult Failure(string code, string stage) => new(false, code, stage, null);
}

internal sealed record AssetLiveProposalLocator(
    string RunId,
    string TenantId,
    string DraftId,
    string Schema,
    DescriptorStableHashes Hashes,
    string? PromptInputHash,
    string? PromptOutputHash);
