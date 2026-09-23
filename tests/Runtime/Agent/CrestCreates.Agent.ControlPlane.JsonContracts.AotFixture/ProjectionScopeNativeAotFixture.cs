using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
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

            var packagePreview = controlPlane.PreviewDescriptorPackageAsync(
                Context("PreviewDescriptorPackage"), draftId).GetAwaiter().GetResult();
            var packagePreviewId = packagePreview.AuditRecord?.TouchedPackagePreviewIds?.SingleOrDefault();
            if (packagePreview.Status != AgentToolResultStatus.Success || string.IsNullOrWhiteSpace(packagePreviewId))
                return Fail("the broad package preview was not stored");

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

            // Restore the original scope and capture a fresh review. This proves that
            // review execution and subsequent reads still work on the real service path
            // after the narrower-scope rejection, without changing the fixture inventory.
            currentOptions = broadOptions;
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
                || recoveredList.Status != AgentToolResultStatus.Success || recoveredList.Value?.Results.Count != 2
                || recoveredReport.Status != AgentToolResultStatus.Success)
                return Fail("same-scope re-review did not restore review, package, list, and report reads");

            Console.WriteLine("CONTROL_PLANE_PROJECTION_SCOPE_NATIVEAOT_OK");
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
        provider.GetRequiredService<IActivationBindingArtifactResolver>(),
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

    private sealed class FixtureDescriptorCatalog(IReadOnlyList<IDescriptor> initialDescriptors) : IDescriptorCatalog
    {
        private IReadOnlyList<IDescriptor> _descriptors = initialDescriptors;

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
