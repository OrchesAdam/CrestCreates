using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.ContextPack;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class ProjectionScopeNativeAotFixture
{
    private const string TenantId = "projection-scope-aot-tenant";
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 24, 5, 0, 0, TimeSpan.Zero);

    public static bool Run()
    {
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var catalog = new FixtureDescriptorCatalog(
            [
                new SchemaDescriptor
                {
                    Id = "aot-event-schema",
                    Name = "AOT event schema",
                    Version = 1,
                    ChangeKind = SchemaChangeKind.Additive
                }
            ]);
            services.AddSingleton<IDescriptorCatalog>(catalog);
            services.AddRelationshipKernel();
            services.AddTopologyKernel();
            services.AddMetadataContextPack();
            services.AddDescriptorImpactAnalysis();
            services.AddDescriptorCompatibilityAnalysis();
            services.AddDescriptorLifecycleGovernance();
            services.AddDescriptorPackaging();
            services.AddDescriptorDrafts();
            services.AddRuntimePersistence();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));

            var broadOptions = AgentToolAuthorizationOptions.DevelopmentDefaults;
            services.AddAgentControlPlane(broadOptions).AddAgentControlPlaneInMemoryStubs();

            using var provider = services.BuildServiceProvider(validateScopes: true);
            var currentOptions = broadOptions;
            var controlPlane = CreateService(provider, broadOptions, () => currentOptions);
            var draftContext = Context("CreateDescriptorDraft");
            var created = controlPlane.CreateDescriptorDraftAsync(draftContext, new CreateDescriptorDraftRequest
            {
                DescriptorKind = DescriptorKind.Event,
                DescriptorId = "aot-projection-scope-event",
                Operation = DescriptorDraftOperation.Create,
                Payload = new AgentDraftPayloadDto
                {
                    Discriminator = DescriptorKind.Event,
                    Event = new AgentEventDraftPayloadDto
                    {
                        Name = "AOT projection scope event",
                        Version = 1,
                        State = DescriptorState.Active,
                        Category = EventCategory.Domain,
                        Semantic = EventSemantic.Fact,
                        Importance = EventImportance.Operational,
                        ChangeKind = SchemaChangeKind.Additive,
                        PayloadSchema = new DescriptorRef("schema", "aot-event-schema", 1)
                    }
                },
                ProposedVersion = "1",
                Intent = "Exercise cached review projection visibility"
            }).GetAwaiter().GetResult();

            if (created.Status != AgentToolResultStatus.Success || created.Value is null)
                return Fail("creating the fixture draft failed");

            var draftId = created.Value.DraftId;
            var reviewed = controlPlane.ReviewDescriptorDraftAsync(Context("ReviewDescriptorDraft"), draftId)
                .GetAwaiter().GetResult();
            if (reviewed.Status != AgentToolResultStatus.Success || reviewed.Value?.TopologySummary is null
                || !reviewed.Value.TopologySummary.NodeCountsByKind.TryGetValue(DescriptorKind.Schema, out var schemaNodes)
                || schemaNodes == 0)
                return Fail($"the broad review did not retain the nested Schema projection (status={reviewed.Status}, diagnostics={FormatToolDiagnostics(reviewed.Diagnostics)}, validation={FormatDraftDiagnostics(reviewed.Value?.ValidationResult.Diagnostics ?? Array.Empty<DescriptorDraftDiagnostic>())})");

            var reviewId = reviewed.AuditRecord?.TouchedReviewResultIds?.SingleOrDefault();
            if (string.IsNullOrWhiteSpace(reviewId))
                return Fail("the broad review did not return its stored review identity");

            var reviewStore = provider.GetRequiredService<IAgentReviewArtifactStore>();
            var storedArtifact = reviewStore.GetAsync(TenantId, reviewId).GetAwaiter().GetResult();
            if (storedArtifact is null || storedArtifact.CreatedAt == default
                || !ReviewArtifactStoreNativeAotFixture.Run(storedArtifact))
                return Fail("the in-memory store did not retain a source-generated JSON review artifact");

            var packagePreview = controlPlane.PreviewDescriptorPackageAsync(
                Context("PreviewDescriptorPackage"), draftId).GetAwaiter().GetResult();
            var packagePreviewId = packagePreview.AuditRecord?.TouchedPackagePreviewIds?.SingleOrDefault();
            if (packagePreview.Status != AgentToolResultStatus.Success || string.IsNullOrWhiteSpace(packagePreviewId))
                return Fail("the broad package preview was not stored");

            var firstBuild = controlPlane.BuildPackageEvidencePreviewAsync(
                Context("BuildPackageEvidencePreview"), draftId).GetAwaiter().GetResult();
            if (firstBuild.Status != AgentToolResultStatus.Success || firstBuild.Value is null
                || string.IsNullOrWhiteSpace(firstBuild.Value.PackagePreviewId)
                || string.IsNullOrWhiteSpace(firstBuild.Value.EvidencePreviewId))
                return Fail("the first package/evidence build did not return its exact stored identities");

            // A catalog change forces a second immutable package build for the same
            // draft, version, and authorization scope. Its evidence must remain linked
            // to that second package even when callers later mix the two builds.
            catalog.Add(new SchemaDescriptor
            {
                Id = "aot-event-schema-v2",
                Name = "AOT event schema v2",
                Version = 1,
                ChangeKind = SchemaChangeKind.Additive
            });
            var secondBuild = controlPlane.BuildPackageEvidencePreviewAsync(
                Context("BuildPackageEvidencePreview"), draftId).GetAwaiter().GetResult();
            if (secondBuild.Status != AgentToolResultStatus.Success || secondBuild.Value is null
                || string.IsNullOrWhiteSpace(secondBuild.Value.PackagePreviewId)
                || string.IsNullOrWhiteSpace(secondBuild.Value.EvidencePreviewId)
                || StringComparer.Ordinal.Equals(firstBuild.Value.PackagePreviewId, secondBuild.Value.PackagePreviewId)
                || StringComparer.Ordinal.Equals(firstBuild.Value.EvidencePreviewId, secondBuild.Value.EvidencePreviewId))
                return Fail("the changed visible catalog did not produce a distinct package/evidence build");

            var packageArtifactStore = provider.GetRequiredService<IAgentPackageArtifactStore>();
            var packageArtifactValidator = provider.GetRequiredService<IAgentPackageArtifactValidator>();
            var packageArtifact = packageArtifactStore.GetPackageAsync(
                new AgentPackageArtifactKey(TenantId, firstBuild.Value.PackagePreviewId)).GetAwaiter().GetResult();
            var secondPackageArtifact = packageArtifactStore.GetPackageAsync(
                new AgentPackageArtifactKey(TenantId, secondBuild.Value.PackagePreviewId)).GetAwaiter().GetResult();
            var evidenceArtifact = packageArtifactStore.GetEvidenceAsync(
                new AgentEvidenceArtifactKey(TenantId, secondBuild.Value.EvidencePreviewId)).GetAwaiter().GetResult();
            if (packageArtifact is null || secondPackageArtifact is null || evidenceArtifact is null
                || !StringComparer.Ordinal.Equals(evidenceArtifact.PackagePreviewId, secondBuild.Value.PackagePreviewId))
                return Fail("the artifact store did not retain the exact package/evidence identities");

            if (!PackageEvidenceArtifactNativeAotFixture.Run(
                    packageArtifact, secondPackageArtifact, evidenceArtifact, packageArtifactValidator,
                    provider.GetRequiredService<IDescriptorPackageSerializer>()))
                return false;

            var firstReport = controlPlane.BuildDescriptorReviewReportAsync(
                Context("BuildDescriptorReviewReport"), draftId).GetAwaiter().GetResult();
            if (firstReport.Status != AgentToolResultStatus.Success || firstReport.Value is null
                || firstReport.Value.GeneratedAt != FixedNow)
                return Fail("the fixed-clock report was not available from the stored review artifact");
            var reportTypeInfo = (JsonTypeInfo<DescriptorReviewReportDto>?)
                AgentControlPlaneToolJsonSerializerContext.Default.GetTypeInfo(typeof(DescriptorReviewReportDto));
            if (reportTypeInfo is null)
                return Fail("source-generated report DTO metadata was not available");
            var firstReportJson = JsonSerializer.Serialize(firstReport.Value, reportTypeInfo);

            // Construct a fresh tool service over the same explicit in-memory store.
            // This checks the service/store contract after service recreation; it is
            // deliberately not evidence of native PostgreSQL persistence.
            var recreatedControlPlane = CreateService(provider, broadOptions, () => currentOptions);
            if (!ReferenceEquals(reviewStore, provider.GetRequiredService<IAgentReviewArtifactStore>()))
                return Fail("the recreated tool service did not resolve the configured review store");
            if (!ReferenceEquals(packageArtifactStore, provider.GetRequiredService<IAgentPackageArtifactStore>()))
                return Fail("the recreated tool service did not resolve the configured package/evidence store");
            var recreatedPackage = recreatedControlPlane.GetPackagePreviewAsync(
                Context("GetPackagePreview"), firstBuild.Value.PackagePreviewId).GetAwaiter().GetResult();
            var recreatedEvidence = recreatedControlPlane.BuildPackageEvidencePreviewAsync(
                Context("BuildPackageEvidencePreview"), draftId).GetAwaiter().GetResult();
            if (recreatedPackage.Status != AgentToolResultStatus.Success
                || recreatedEvidence.Status != AgentToolResultStatus.Success || recreatedEvidence.Value is null
                || !StringComparer.Ordinal.Equals(recreatedEvidence.Value.PackagePreviewId, secondBuild.Value.PackagePreviewId)
                || StringComparer.Ordinal.Equals(recreatedEvidence.Value.EvidencePreviewId, secondBuild.Value.EvidencePreviewId))
                return Fail("a recreated tool service did not read the stored package and reuse its exact parent package");
            var recreatedEvidenceArtifact = packageArtifactStore.GetEvidenceAsync(
                new AgentEvidenceArtifactKey(TenantId, recreatedEvidence.Value.EvidencePreviewId)).GetAwaiter().GetResult();
            if (recreatedEvidenceArtifact is null
                || !StringComparer.Ordinal.Equals(recreatedEvidenceArtifact.PackagePreviewId, secondBuild.Value.PackagePreviewId))
                return Fail("the recreated service did not persist evidence linked to the exact reused package artifact");

            var bindingSnapshotForHashes = new ActivationBindingSnapshot
            {
                TenantId = TenantId,
                DraftId = draftId,
                DraftVersion = 1,
                ReviewResultId = reviewId,
                PackagePreviewId = firstBuild.Value.PackagePreviewId,
                EvidencePreviewId = firstBuild.Value.EvidencePreviewId,
                Hashes = null!,
                CreatedAt = FixedNow
            };
            var resolvedBindingHashes = provider.GetRequiredService<IActivationBindingArtifactResolver>()
                .ResolveAsync(TenantId, bindingSnapshotForHashes).GetAwaiter().GetResult();
            if (resolvedBindingHashes.CurrentSourceReviewHash is null
                || resolvedBindingHashes.CurrentReviewManifestHash is null
                || resolvedBindingHashes.CurrentPackageHashes is null
                || reviewed.Value.StableHashes is null)
                return Fail("the exact first build did not resolve its review and package hashes");

            var activationAuditor = provider.GetRequiredService<IDescriptorActivationAuditor>() as InMemoryDescriptorActivationAuditor;
            if (activationAuditor is null)
                return Fail("the fixture did not resolve its explicit in-memory activation auditor");
            var activationAuditCountBeforeMismatch = activationAuditor.GetAllRecords().Count;
            var mismatchedSubmit = recreatedControlPlane.SubmitActivationRequestAsync(
                Context("SubmitActivationRequest"),
                new SubmitActivationRequestRequest
                {
                    DraftId = draftId,
                    BindingSnapshot = bindingSnapshotForHashes with
                    {
                        EvidencePreviewId = recreatedEvidence.Value.EvidencePreviewId,
                        Hashes = new BindingHashes
                        {
                            SourceReviewHash = resolvedBindingHashes.CurrentSourceReviewHash,
                            ReviewManifestHash = resolvedBindingHashes.CurrentReviewManifestHash,
                            PackageManifestHash = resolvedBindingHashes.CurrentPackageHashes.PackageManifestHash,
                            PackageEvidenceHash = resolvedBindingHashes.CurrentPackageHashes.PackageEvidenceHash,
                            PackageEvidenceEnvelopeHash = resolvedBindingHashes.CurrentPackageHashes.PackageEvidenceEnvelopeHash,
                            ContractHash = reviewed.Value.StableHashes.ContractHash,
                            DefinitionHash = reviewed.Value.StableHashes.DefinitionHash
                        }
                    }
                }).GetAwaiter().GetResult();
            if (mismatchedSubmit.Status != AgentToolResultStatus.InvalidRequest
                || mismatchedSubmit.Diagnostics.All(d => d.Code != DescriptorActivationDiagnosticCodes.EvidencePackageMismatch)
                || activationAuditor.GetAllRecords().Count != activationAuditCountBeforeMismatch
                || mismatchedSubmit.AuditRecord?.TouchedActivationRequestIds is not null)
                return Fail("cross-build evidence was not rejected before activation request or review-task creation");
            var recreatedGet = recreatedControlPlane.GetDraftReviewResultAsync(
                Context("GetDraftReviewResult"), reviewId).GetAwaiter().GetResult();
            var recreatedReport = recreatedControlPlane.BuildDescriptorReviewReportAsync(
                Context("BuildDescriptorReviewReport"), draftId).GetAwaiter().GetResult();
            if (recreatedGet.Status != AgentToolResultStatus.Success
                || recreatedReport.Status != AgentToolResultStatus.Success || recreatedReport.Value is null
                || !StringComparer.Ordinal.Equals(firstReportJson, JsonSerializer.Serialize(recreatedReport.Value, reportTypeInfo)))
                return Fail("recreated tool service did not recover the review and fixed-clock report from the same store");

            currentOptions = broadOptions with { DeniedDescriptorKinds = ["Schema"] };
            var narrowedGet = controlPlane.GetDraftReviewResultAsync(Context("GetDraftReviewResult"), reviewId)
                .GetAwaiter().GetResult();
            if (narrowedGet.Status != AgentToolResultStatus.Denied
                || narrowedGet.Diagnostics.All(d => d.Code != AgentToolDiagnosticCodes.ReviewResultScopeMismatch))
                return Fail("Get returned a review captured under a broader scope");

            var narrowedPackage = controlPlane.GetPackagePreviewAsync(
                Context("GetPackagePreview"), packagePreviewId).GetAwaiter().GetResult();
            if (narrowedPackage.Status != AgentToolResultStatus.Denied
                || narrowedPackage.Diagnostics.All(d => d.Code != AgentToolDiagnosticCodes.PackagePreviewScopeMismatch))
                return Fail("GetPackagePreview returned an artifact captured under a broader scope");

            var narrowedList = controlPlane.ListDraftReviewResultsAsync(Context("ListDraftReviewResults"), draftId)
                .GetAwaiter().GetResult();
            if (narrowedList.Status != AgentToolResultStatus.Success || narrowedList.Value?.Results.Count != 0
                || narrowedList.Diagnostics.Count == 0)
                return Fail("List did not trim the review captured under a broader scope");

            var narrowedReport = controlPlane.BuildDescriptorReviewReportAsync(Context("BuildDescriptorReviewReport"), draftId)
                .GetAwaiter().GetResult();
            if (narrowedReport.Status != AgentToolResultStatus.Failed
                || narrowedReport.Diagnostics.All(d => d.Code != AgentToolDiagnosticCodes.ReviewResultScopeMismatch))
                return Fail("report building reused a review captured under a broader scope");

            // Use a distinct scope that leaves this draft's Schema references visible,
            // so the test can capture a second immutable review instead of correctly
            // rejecting a package projection that contains a hidden Schema.
            currentOptions = broadOptions with { DeniedDescriptorKinds = ["Workflow"] };
            var narrowReview = controlPlane.ReviewDescriptorDraftAsync(Context("ReviewDescriptorDraft"), draftId)
                .GetAwaiter().GetResult();
            var narrowReviewId = narrowReview.AuditRecord?.TouchedReviewResultIds?.SingleOrDefault();
            if (narrowReview.Status != AgentToolResultStatus.Success || string.IsNullOrWhiteSpace(narrowReviewId))
                return Fail("the narrower visibility scope did not capture a latest review artifact");
            var narrowLatestReport = controlPlane.BuildDescriptorReviewReportAsync(
                Context("BuildDescriptorReviewReport"), draftId).GetAwaiter().GetResult();
            if (narrowLatestReport.Status != AgentToolResultStatus.Success)
                return Fail("the latest narrow-scope review could not build its own report");

            currentOptions = broadOptions;
            var wrongScopeLatestReport = recreatedControlPlane.BuildDescriptorReviewReportAsync(
                Context("BuildDescriptorReviewReport"), draftId).GetAwaiter().GetResult();
            if (wrongScopeLatestReport.Status != AgentToolResultStatus.Failed
                || wrongScopeLatestReport.Diagnostics.All(d => d.Code != AgentToolDiagnosticCodes.ReviewResultScopeMismatch))
                return Fail("latest report fell back to an older broad-scope review after a newer narrow-scope review");

            // Restore the original scope and capture a fresh review. This proves that
            // review execution and subsequent reads still work on the real service path
            // after the narrower-scope rejection, without changing the fixture inventory.
            var reReviewed = controlPlane.ReviewDescriptorDraftAsync(Context("ReviewDescriptorDraft"), draftId)
                .GetAwaiter().GetResult();
            if (reReviewed.Status != AgentToolResultStatus.Success || reReviewed.Value?.TopologySummary is null
                || !reReviewed.Value.TopologySummary.NodeCountsByKind.ContainsKey(DescriptorKind.Schema))
                return Fail("same-scope re-review did not preserve the nested Schema projection");

            var recoveredReviewId = reReviewed.AuditRecord?.TouchedReviewResultIds?.SingleOrDefault();
            if (string.IsNullOrWhiteSpace(recoveredReviewId))
                return Fail("same-scope re-review did not return its stored review identity");
            var recoveredGet = controlPlane.GetDraftReviewResultAsync(Context("GetDraftReviewResult"), recoveredReviewId)
                .GetAwaiter().GetResult();
            var recoveredPackage = controlPlane.GetPackagePreviewAsync(Context("GetPackagePreview"), packagePreviewId)
                .GetAwaiter().GetResult();
            var recoveredList = controlPlane.ListDraftReviewResultsAsync(Context("ListDraftReviewResults"), draftId)
                .GetAwaiter().GetResult();
            var recoveredReport = controlPlane.BuildDescriptorReviewReportAsync(Context("BuildDescriptorReviewReport"), draftId)
                .GetAwaiter().GetResult();
            if (recoveredGet.Status != AgentToolResultStatus.Success || recoveredPackage.Status != AgentToolResultStatus.Success
                || recoveredList.Status != AgentToolResultStatus.Success || recoveredList.Value is null
                || recoveredList.Value.Results.Count != 2
                || recoveredList.Diagnostics.All(d => d.Code != AgentToolDiagnosticCodes.ResultsSecurityTrimmed)
                || recoveredReport.Status != AgentToolResultStatus.Success)
                return Fail("same-scope re-review did not restore review, package, list, and report reads");

            Console.WriteLine("CONTROL_PLANE_PROJECTION_SCOPE_NATIVEAOT_OK");
            Console.WriteLine("CONTROL_PLANE_PACKAGE_EVIDENCE_ARTIFACT_MEMORY_NATIVEAOT_OK");
            return true;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static DefaultAgentControlPlaneToolService CreateService(
        IServiceProvider provider,
        AgentToolAuthorizationOptions initialOptions,
        Func<AgentToolAuthorizationOptions> optionsFactory) => new(
        provider.GetRequiredService<IAgentToolManifestProvider>(),
        provider.GetRequiredService<IAgentToolAuthorizationService>(),
        provider.GetRequiredService<IAgentToolInvocationAuditor>(),
        provider.GetRequiredService<IDescriptorDraftStore>(),
        provider.GetRequiredService<IDescriptorDraftValidator>(),
        provider.GetRequiredService<IDescriptorDraftReviewService>(),
        provider.GetRequiredService<IDescriptorDraftMaterializer>(),
        provider.GetRequiredService<CrestCreates.Metadata.ContextPack.Abstractions.IMetadataContextPackBuilder>(),
        provider.GetRequiredService<IDescriptorCatalog>(),
        provider.GetRequiredService<IDescriptorRelationshipProvider>(),
        provider.GetRequiredService<CrestCreates.Metadata.Abstractions.IDescriptorTopologyBuilder>(),
        provider.GetRequiredService<CrestCreates.Metadata.Abstractions.DescriptorPackage.IDescriptorPackageBuilder>(),
        provider.GetRequiredService<ILogger<DefaultAgentControlPlaneToolService>>(),
        provider.GetRequiredService<CrestCreates.Metadata.Abstractions.CanonicalHashing.IDescriptorStableHashBuilder>(),
        provider.GetRequiredService<CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing.IDescriptorDraftReviewHashService>(),
        provider.GetRequiredService<IDescriptorReviewReportBuilder>(),
        provider.GetRequiredService<IDescriptorReviewReportRenderer>(),
        provider.GetRequiredService<IDescriptorActivationRequestService>(),
        provider.GetRequiredService<IActivationReviewOrchestrator>(),
        provider.GetRequiredService<IAgentReviewArtifactStore>(),
        provider.GetRequiredService<IAgentPackageArtifactStore>(),
        provider.GetRequiredService<IAgentPackageArtifactFactory>(),
        provider.GetRequiredService<TimeProvider>(),
        initialOptions,
        optionsFactory);

    private static AgentToolInvocationContext Context(string toolName) => new()
    {
        TenantId = TenantId,
        ActorId = "projection-scope-aot-actor",
        ActorKind = AgentToolActorKind.Agent,
        CorrelationId = "projection-scope-aot-correlation",
        ToolName = toolName,
        InvocationSource = AgentToolInvocationSource.Direct
    };

    private static bool Fail(string message)
    {
        Console.Error.WriteLine($"FAIL [ProjectionScopeNativeAot]: {message}");
        return false;
    }

    private static string FormatToolDiagnostics(IReadOnlyList<AgentToolDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code.Value}: {d.Message}"));

    private static string FormatDraftDiagnostics(IReadOnlyList<DescriptorDraftDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code.Value}: {d.Message}"));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixtureDescriptorCatalog(IReadOnlyList<IDescriptor> initialDescriptors) : IDescriptorCatalog
    {
        private readonly List<IDescriptor> _descriptors = initialDescriptors.ToList();

        public void Add(IDescriptor descriptor) => _descriptors.Add(descriptor);

        public IDescriptor? Get(string id) => _descriptors.LastOrDefault(d => d.Id == id);
        public IEnumerable<IDescriptor> GetAll() => _descriptors;
        public IEnumerable<IDescriptor> FindByKind(DescriptorKind kind) => _descriptors.Where(d => d.Kind == kind);
        public IEnumerable<IDescriptor> FindByPackage(string packageId) => Array.Empty<IDescriptor>();
        public IEnumerable<IDescriptor> FindDependents(string descriptorId) => Array.Empty<IDescriptor>();
        public IEnumerable<IDescriptor> FindDependencies(string descriptorId) => Array.Empty<IDescriptor>();
        public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion) => new()
        {
            DescriptorId = descriptorId,
            FromVersion = fromVersion,
            ToVersion = toVersion
        };
    }
}
