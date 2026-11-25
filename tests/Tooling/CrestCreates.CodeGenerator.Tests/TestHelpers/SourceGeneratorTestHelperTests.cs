using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.TestHelpers;

public sealed class SourceGeneratorTestHelperTests
{
    private const string Source = "public sealed class Sample { public int Value { get; set; } }";

    [Fact]
    public void Identical_inputs_reuse_the_immutable_compilation()
    {
        var first = SourceGeneratorTestHelper.CreateAnalysisCompilation("CacheProbe", [Source]);
        var second = SourceGeneratorTestHelper.CreateAnalysisCompilation("CacheProbe", [Source]);

        Assert.Same(first, second);
    }

    [Fact]
    public void Changed_source_or_additional_sources_create_a_distinct_compilation()
    {
        var baseline = SourceGeneratorTestHelper.CreateAnalysisCompilation("CacheProbe", [Source]);
        var changed = SourceGeneratorTestHelper.CreateAnalysisCompilation("CacheProbe", [Source + "\npublic sealed class Other { }"]);
        var additional = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "CacheProbe", [Source], additionalSources: ["public sealed class Additional { }"]);

        Assert.NotSame(baseline, changed);
        Assert.NotSame(baseline, additional);
        Assert.NotSame(changed, additional);
    }

    [Fact]
    public void Distinct_metadata_objects_with_the_same_display_do_not_share_a_cache_key()
    {
        var path = typeof(GeneratedSource).Assembly.Location;
        var firstReference = MetadataReference.CreateFromFile(path);
        var secondReference = MetadataReference.CreateFromFile(path);
        var first = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "CacheProbe", [Source], additionalMetadataReferences: [firstReference]);
        var second = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "CacheProbe", [Source], additionalMetadataReferences: [secondReference]);
        var repeated = SourceGeneratorTestHelper.CreateAnalysisCompilation(
            "CacheProbe", [Source], additionalMetadataReferences: [firstReference]);

        Assert.NotSame(first, second);
        Assert.Same(first, repeated);
    }
}
