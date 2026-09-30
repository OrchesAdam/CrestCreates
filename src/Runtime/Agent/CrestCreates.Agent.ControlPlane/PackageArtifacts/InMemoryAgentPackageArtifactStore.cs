using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;

namespace CrestCreates.Agent.ControlPlane.PackageArtifacts;

/// <summary>Explicit development provider for package artifacts; callers must opt into this store.</summary>
public sealed class InMemoryAgentPackageArtifactStore : IAgentPackageArtifactStore
{
    private readonly object _sync = new();
    private readonly Dictionary<AgentPackageArtifactKey, AgentPackageArtifactEnvelope> _packages = new();
    private readonly Dictionary<AgentEvidenceArtifactKey, AgentEvidenceArtifactEnvelope> _evidence = new();
    private readonly IAgentPackageArtifactValidator _validator;

    public InMemoryAgentPackageArtifactStore(IAgentPackageArtifactValidator validator)
        => _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public Task InsertPackageAsync(AgentPackageArtifactEnvelope package, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = new AgentPackageArtifactKey(package.TenantId, package.PackagePreviewId);
        var detached = Detach(package);
        _validator.ValidatePackage(detached);
        lock (_sync)
        {
            if (_packages.ContainsKey(key))
                throw new InvalidOperationException($"Package artifact '{key.PackagePreviewId}' already exists for tenant '{key.TenantId}'.");
            _packages.Add(key, detached);
        }
        return Task.CompletedTask;
    }

    public Task InsertPackageAndEvidenceAsync(
        AgentPackageArtifactEnvelope package,
        AgentEvidenceArtifactEnvelope evidence,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var packageKey = new AgentPackageArtifactKey(package.TenantId, package.PackagePreviewId);
        var evidenceKey = new AgentEvidenceArtifactKey(evidence.TenantId, evidence.EvidencePreviewId);
        var detachedPackage = Detach(package);
        var detachedEvidence = Detach(evidence);
        _validator.ValidatePackage(detachedPackage);
        _validator.ValidateEvidence(detachedEvidence, detachedPackage);
        lock (_sync)
        {
            if (_packages.ContainsKey(packageKey))
                throw new InvalidOperationException($"Package artifact '{packageKey.PackagePreviewId}' already exists for tenant '{packageKey.TenantId}'.");
            if (_evidence.ContainsKey(evidenceKey))
                throw new InvalidOperationException($"Evidence artifact '{evidenceKey.EvidencePreviewId}' already exists for tenant '{evidenceKey.TenantId}'.");

            _packages.Add(packageKey, detachedPackage);
            _evidence.Add(evidenceKey, detachedEvidence);
        }
        return Task.CompletedTask;
    }

    public Task InsertEvidenceAsync(AgentEvidenceArtifactEnvelope evidence, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var evidenceKey = new AgentEvidenceArtifactKey(evidence.TenantId, evidence.EvidencePreviewId);
        var packageKey = new AgentPackageArtifactKey(evidence.TenantId, evidence.PackagePreviewId);
        var detachedEvidence = Detach(evidence);
        detachedEvidence.ValidateBinding();
        lock (_sync)
        {
            if (_evidence.ContainsKey(evidenceKey))
                throw new InvalidOperationException($"Evidence artifact '{evidenceKey.EvidencePreviewId}' already exists for tenant '{evidenceKey.TenantId}'.");
            if (!_packages.TryGetValue(packageKey, out var parent))
                throw new InvalidOperationException("Evidence artifact parent package does not exist for this tenant.");
            _validator.ValidatePackage(parent);
            _validator.ValidateEvidence(detachedEvidence, parent);
            _evidence.Add(evidenceKey, detachedEvidence);
        }
        return Task.CompletedTask;
    }

    public Task<AgentPackageArtifactEnvelope?> GetPackageAsync(AgentPackageArtifactKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        lock (_sync)
        {
            if (!_packages.TryGetValue(key, out var package))
                return Task.FromResult<AgentPackageArtifactEnvelope?>(null);
            var detached = Detach(package);
            _validator.ValidatePackage(detached);
            return Task.FromResult<AgentPackageArtifactEnvelope?>(detached);
        }
    }

    public Task<AgentEvidenceArtifactEnvelope?> GetEvidenceAsync(AgentEvidenceArtifactKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        lock (_sync)
        {
            if (!_evidence.TryGetValue(key, out var evidence))
                return Task.FromResult<AgentEvidenceArtifactEnvelope?>(null);
            var parentKey = new AgentPackageArtifactKey(evidence.TenantId, evidence.PackagePreviewId);
            if (!_packages.TryGetValue(parentKey, out var parent))
                throw new InvalidOperationException("Evidence artifact parent package is missing.");
            var detachedParent = Detach(parent);
            var detachedEvidence = Detach(evidence);
            _validator.ValidatePackage(detachedParent);
            _validator.ValidateEvidence(detachedEvidence, detachedParent);
            return Task.FromResult<AgentEvidenceArtifactEnvelope?>(detachedEvidence);
        }
    }

    public Task<AgentPackageArtifactEnvelope?> GetLatestReusablePackageAsync(
        string tenantId,
        string draftId,
        string scopeFingerprint,
        string? capturedDraftVersion,
        string visibleCatalogFingerprint,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var candidate = _packages.Values
                .Where(item => StringComparer.Ordinal.Equals(item.TenantId, tenantId) &&
                    StringComparer.Ordinal.Equals(item.Owner.DraftId, draftId) &&
                    StringComparer.Ordinal.Equals(item.ScopeFingerprint, scopeFingerprint) &&
                    StringComparer.Ordinal.Equals(item.DraftVersion, capturedDraftVersion) &&
                    StringComparer.Ordinal.Equals(item.VisibleCatalogFingerprint, visibleCatalogFingerprint))
                .OrderByDescending(item => item.CapturedAt)
                .ThenByDescending(item => item.PackagePreviewId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is null)
                return Task.FromResult<AgentPackageArtifactEnvelope?>(null);
            var detached = Detach(candidate);
            _validator.ValidatePackage(detached);
            return Task.FromResult<AgentPackageArtifactEnvelope?>(detached);
        }
    }

    private static AgentPackageArtifactEnvelope Detach(AgentPackageArtifactEnvelope artifact)
    {
        var json = JsonSerializer.Serialize(artifact, AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope);
        return JsonSerializer.Deserialize(json, AgentPackageArtifactJsonSerializerContext.Default.AgentPackageArtifactEnvelope)
            ?? throw new InvalidOperationException("Could not detach package artifact snapshot.");
    }

    private static AgentEvidenceArtifactEnvelope Detach(AgentEvidenceArtifactEnvelope artifact)
    {
        var json = JsonSerializer.Serialize(artifact, AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope);
        return JsonSerializer.Deserialize(json, AgentPackageArtifactJsonSerializerContext.Default.AgentEvidenceArtifactEnvelope)
            ?? throw new InvalidOperationException("Could not detach evidence artifact snapshot.");
    }
}
