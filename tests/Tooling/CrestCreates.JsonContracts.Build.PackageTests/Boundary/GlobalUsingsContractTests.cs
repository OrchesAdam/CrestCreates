/// <summary>Case IDs: B12, B14, B15, B16, B17, B18</summary>
public class GlobalUsingsContractTests
{
    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_ImplicitUsingsOnlySurfaceBindsAfterGenerateGlobalUsings() { }
}

/// <summary>Case IDs: H01, B13, B14, B15, F15</summary>
public class IncrementalContractTests
{
    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_AddMethodThenRebuildUpdatesManifest() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_RemoveMethodThenRebuildRemovesRoot() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_SourceDeletionInvalidatesGeneration() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_UnchangedSemanticOutputDoesNotRewriteTimestamp() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_MissingGeneratedSourceInvalidatesStamp() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_DesignTimeReusesExistingGeneratedFile() { }
}

/// <summary>Case IDs: B15, C08</summary>
public class MultiTargetingContractTests
{
    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_MultiTargetingProducesIndependentOutputs() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_DebugAndReleaseOutputsAreIsolated() { }
}

/// <summary>Case IDs: B17, B18</summary>
public class ManifestAccessibilityContractTests
{
    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_InternalManifestAccessibility() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_InternalManifestRemainsAssemblyScoped() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_PublicManifestAccessibility() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_PublicManifestSetsAreImmutable() { }

    [Fact(Skip = AcceptanceSkeleton.Pending)]
    public void Build_PublicManifestIsConsumableFromSeparateAssembly() { }
}
