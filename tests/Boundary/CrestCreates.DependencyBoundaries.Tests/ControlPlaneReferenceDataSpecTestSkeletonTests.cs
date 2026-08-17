using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class ControlPlaneReferenceDataSpecTestSkeletonTests
{
    [Fact]
    public void AllCaseIds_Should_BeUnique()
    {
        var allCaseIds = ControlPlaneReferenceDataCaseManifest.AllCases.Select(c => c.CaseId).ToList();
        allCaseIds.Should().OnlyHaveUniqueItems("every Case ID must be unique across the manifest");
    }

    [Fact]
    public void AllCaseIds_Should_MatchFrozenSpecSection14()
    {
        var expectedCaseIds = new HashSet<string>
        {
            // Descriptor Draft D01–D13
            CaseId.D01, CaseId.D02, CaseId.D03, CaseId.D04, CaseId.D05,
            CaseId.D06, CaseId.D07, CaseId.D08, CaseId.D09, CaseId.D10,
            CaseId.D11, CaseId.D12, CaseId.D13,
            // Organization O01–O22
            CaseId.O01, CaseId.O02, CaseId.O03, CaseId.O04, CaseId.O05,
            CaseId.O06, CaseId.O07, CaseId.O08, CaseId.O09, CaseId.O10,
            CaseId.O11, CaseId.O12, CaseId.O13, CaseId.O14, CaseId.O15,
            CaseId.O16, CaseId.O17, CaseId.O18, CaseId.O19, CaseId.O20,
            CaseId.O21, CaseId.O22,
            // DataPermission P01–P13
            CaseId.P01, CaseId.P02, CaseId.P03, CaseId.P04, CaseId.P05,
            CaseId.P06, CaseId.P07, CaseId.P08, CaseId.P09, CaseId.P10,
            CaseId.P11, CaseId.P12, CaseId.P13,
            // Validation V01–V05
            CaseId.V01, CaseId.V02, CaseId.V03, CaseId.V04, CaseId.V05,
            // Failure F01–F09
            CaseId.F01, CaseId.F02, CaseId.F03, CaseId.F04, CaseId.F05,
            CaseId.F06, CaseId.F07, CaseId.F08, CaseId.F09,
            // Composition C01–C15
            CaseId.C01, CaseId.C02, CaseId.C03, CaseId.C04, CaseId.C05,
            CaseId.C06, CaseId.C07, CaseId.C08, CaseId.C09, CaseId.C10,
            CaseId.C11, CaseId.C12, CaseId.C13, CaseId.C14, CaseId.C15,
        };

        var actualCaseIds = new HashSet<string>(ControlPlaneReferenceDataCaseManifest.AllCases.Select(c => c.CaseId));

        actualCaseIds.Should().BeEquivalentTo(expectedCaseIds,
            "the manifest must contain exactly the 77 Case IDs frozen in Spec §14");
    }

    [Fact]
    public void EveryCase_Should_HaveValidRunner()
    {
        foreach (var entry in ControlPlaneReferenceDataCaseManifest.AllCases)
        {
            Enum.IsDefined(entry.Runner).Should().BeTrue(
                $"Case {entry.CaseId} has an invalid Runner value: {entry.Runner}");
        }
    }

    [Fact]
    public void EveryCase_Should_HaveValidOwningSlice()
    {
        foreach (var entry in ControlPlaneReferenceDataCaseManifest.AllCases)
        {
            Enum.IsDefined(entry.OwningSlice).Should().BeTrue(
                $"Case {entry.CaseId} has an invalid OwningSlice value: {entry.OwningSlice}");
        }
    }

    [Fact]
    public void EveryExpandedVector_Should_BeDefinedInEvidenceVectorKey()
    {
        foreach (var expansion in ControlPlaneReferenceDataCaseManifest.EvidenceVectorExpansion)
        {
            foreach (var key in expansion.Value)
            {
                Enum.IsDefined(key).Should().BeTrue(
                    $"EvidenceVectorKey {key} for ({expansion.Key.CaseId}, {expansion.Key.Variant}) is not defined");
            }
        }
    }

    [Fact]
    public void PgOnlyCases_Should_HavePostgreSqlOrArchitectureOrAotRunner()
    {
        var pgOnlyIds = new HashSet<string>
        {
            CaseId.D09, CaseId.D10, CaseId.O15, CaseId.O16, CaseId.O17, CaseId.O18,
            CaseId.P08, CaseId.P09, CaseId.P13,
            CaseId.C01, CaseId.C02, CaseId.C03, CaseId.C04, CaseId.C05, CaseId.C06,
            CaseId.C07, CaseId.C10, CaseId.C11, CaseId.C12, CaseId.C13,
            CaseId.F03, CaseId.F04, CaseId.F05, CaseId.F06, CaseId.F07, CaseId.F08, CaseId.F09,
        };

        var pgCases = ControlPlaneReferenceDataCaseManifest.AllCases
            .Where(c => pgOnlyIds.Contains(c.CaseId));

        foreach (var entry in pgCases)
        {
            entry.Runner.Should().BeOneOf(new[] {
                RequiredRunner.PostgreSql, RequiredRunner.Architecture, RequiredRunner.Aot },
                $"PG-only Case {entry.CaseId} must have PostgreSql, Architecture, or Aot runner, got {entry.Runner}");
        }
    }

    [Fact]
    public void ManifestEntryCount_Should_Be77()
    {
        ControlPlaneReferenceDataCaseManifest.AllCases.Should().HaveCount(77,
            "Spec §14 defines exactly 77 Case IDs");
    }
}
