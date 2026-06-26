using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.Extensions.Logging;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Default evidence rechecker that compares all binding snapshot hashes
/// against current descriptor/draft/artifact state.
/// Recomputes ContractHash and DefinitionHash from the materialized descriptor,
/// and resolves review/package/evidence hashes via IActivationBindingArtifactResolver.
/// </summary>
public sealed class DefaultActivationEvidenceRechecker : IActivationEvidenceRechecker
{
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly DraftAbstractions.IDescriptorDraftStore _draftStore;
    private readonly IActivationBindingArtifactResolver _artifactResolver;
    private readonly ActivationBindingHashValidator _bindingHashValidator;
    private readonly ILogger<DefaultActivationEvidenceRechecker> _logger;

    public DefaultActivationEvidenceRechecker(
        IDescriptorStableHashBuilder hashBuilder,
        DraftAbstractions.IDescriptorDraftStore draftStore,
        IActivationBindingArtifactResolver artifactResolver,
        ActivationBindingHashValidator bindingHashValidator,
        ILogger<DefaultActivationEvidenceRechecker> logger)
    {
        _hashBuilder = hashBuilder;
        _draftStore = draftStore;
        _artifactResolver = artifactResolver;
        _bindingHashValidator = bindingHashValidator;
        _logger = logger;
    }

    public async Task<ActivationEvidenceRecheckResult> RecheckAsync(
        string tenantId, ActivationBindingSnapshot bindingSnapshot, CancellationToken ct = default)
    {
        var drifts = new List<ActivationEvidenceDrift>();

        // Validate binding hashes — if malformed, treat as evidence of drift
        var hashIssues = _bindingHashValidator.Validate(bindingSnapshot.Hashes);
        var hashErrors = hashIssues.Where(i => i.Severity == BindingHashValidationSeverity.Error).ToList();
        if (hashErrors.Count > 0)
        {
            foreach (var issue in hashErrors)
            {
                _logger.LogWarning("Evidence drift: binding hash validation failed for '{Slot}': {Description}", issue.Slot, issue.Description);
                drifts.Add(new ActivationEvidenceDrift
                {
                    FieldName = $"BindingHash.{issue.Slot}",
                    BoundHashValue = "<bound>",
                    CurrentHashValue = $"<validation-error: {issue.Description}>"
                });
            }
            return new ActivationEvidenceRecheckResult { IsStale = true, Drifts = drifts };
        }

        // Log warnings without adding drift
        var hashWarnings = hashIssues.Where(i => i.Severity == BindingHashValidationSeverity.Warning).ToList();
        foreach (var w in hashWarnings)
        {
            _logger.LogWarning("Binding hash warning at slot '{Slot}': {Description}", w.Slot, w.Description);
        }

        // 1. Check draft existence and version drift
        var draft = await _draftStore.GetAsync(tenantId, bindingSnapshot.DraftId, ct);
        if (draft is null)
        {
            _logger.LogWarning("Evidence drift: draft {DraftId} not found", bindingSnapshot.DraftId);
            drifts.Add(new ActivationEvidenceDrift
            {
                FieldName = "Draft",
                BoundHashValue = bindingSnapshot.DraftId,
                CurrentHashValue = "<not-found>"
            });
            // Can't continue hash comparison without draft
            return new ActivationEvidenceRecheckResult { IsStale = true, Drifts = drifts };
        }

        // Check draft version drift
        if (draft.ProposedVersion is not null
            && draft.ProposedVersion != bindingSnapshot.DraftVersion.ToString())
        {
            _logger.LogWarning(
                "Evidence drift: draft {DraftId} version changed from {BoundVersion} to {CurrentVersion}",
                bindingSnapshot.DraftId, bindingSnapshot.DraftVersion, draft.ProposedVersion);
            drifts.Add(new ActivationEvidenceDrift
            {
                FieldName = "DraftVersion",
                BoundHashValue = bindingSnapshot.DraftVersion.ToString(),
                CurrentHashValue = draft.ProposedVersion
            });
        }

        // 2. Recompute descriptor hashes from materialized descriptor (via Payload)
        var materializedDescriptor = draft.Payload.GetDescriptor();
        if (materializedDescriptor is not null)
        {
            var currentHashes = _hashBuilder.Build(materializedDescriptor);

            CompareHash(drifts, "ContractHash",
                bindingSnapshot.Hashes.ContractHash, currentHashes.ContractHash);
            CompareHash(drifts, "DefinitionHash",
                bindingSnapshot.Hashes.DefinitionHash, currentHashes.DefinitionHash);
        }
        else
        {
            // No materialized descriptor — can't verify ContractHash/DefinitionHash
            _logger.LogWarning(
                "Evidence drift: draft {DraftId} has no materialized descriptor for hash recheck",
                bindingSnapshot.DraftId);
            drifts.Add(new ActivationEvidenceDrift
            {
                FieldName = "ContractHash",
                BoundHashValue = bindingSnapshot.Hashes.ContractHash.Value,
                CurrentHashValue = "<no-materialized-descriptor>"
            });
        }

        // 3. Resolve and compare artifact hashes (review, package, evidence)
        var resolvedArtifacts = await _artifactResolver.ResolveAsync(tenantId, bindingSnapshot, ct);

        CompareHash(drifts, "SourceReviewHash",
            bindingSnapshot.Hashes.SourceReviewHash, resolvedArtifacts.CurrentSourceReviewHash);
        CompareHash(drifts, "ReviewManifestHash",
            bindingSnapshot.Hashes.ReviewManifestHash, resolvedArtifacts.CurrentReviewManifestHash);

        // Compare package manifest hash from package preview
        if (resolvedArtifacts.CurrentPackageHashes is not null)
        {
            CompareHash(drifts, "PackageManifestHash",
                bindingSnapshot.Hashes.PackageManifestHash,
                resolvedArtifacts.CurrentPackageHashes.PackageManifestHash);
        }
        else
        {
            _logger.LogWarning("Evidence drift: PackageHashes artifact no longer exists");
            drifts.Add(new ActivationEvidenceDrift
            {
                FieldName = "PackageHashes",
                BoundHashValue = bindingSnapshot.Hashes.PackageHashes.ToString(),
                CurrentHashValue = "<not-found>"
            });
        }

        // Compare evidence hashes from evidence preview
        if (resolvedArtifacts.CurrentEvidenceHashes is not null)
        {
            CompareHash(drifts, "PackageEvidenceHash",
                bindingSnapshot.Hashes.PackageEvidenceHash,
                resolvedArtifacts.CurrentEvidenceHashes.PackageEvidenceHash);
            CompareHash(drifts, "PackageEvidenceEnvelopeHash",
                bindingSnapshot.Hashes.PackageEvidenceEnvelopeHash,
                resolvedArtifacts.CurrentEvidenceHashes.PackageEvidenceEnvelopeHash);
        }
        else
        {
            _logger.LogWarning("Evidence drift: EvidenceHashes artifact no longer exists");
            drifts.Add(new ActivationEvidenceDrift
            {
                FieldName = "EvidenceHashes",
                BoundHashValue = "<evidence>",
                CurrentHashValue = "<not-found>"
            });
        }

        return new ActivationEvidenceRecheckResult
        {
            IsStale = drifts.Count > 0,
            Drifts = drifts
        };
    }

    private void CompareHash(
        List<ActivationEvidenceDrift> drifts,
        string fieldName,
        CanonicalHash boundHash,
        CanonicalHash? currentHash)
    {
        if (currentHash is null)
        {
            _logger.LogWarning("Evidence drift: {FieldName} artifact no longer exists", fieldName);
            drifts.Add(new ActivationEvidenceDrift
            {
                FieldName = fieldName,
                BoundHashValue = boundHash.ToString(),
                CurrentHashValue = "<not-found>"
            });
            return;
        }

        if (boundHash != currentHash)
        {
            _logger.LogWarning(
                "Evidence drift: {FieldName} changed from {BoundValue} to {CurrentValue}",
                fieldName, boundHash.ToString(), currentHash.ToString());
            drifts.Add(new ActivationEvidenceDrift
            {
                FieldName = fieldName,
                BoundHashValue = boundHash.ToString(),
                CurrentHashValue = currentHash.ToString()
            });
        }
    }
}
