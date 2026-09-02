namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

public enum DurableScenario
{
    BeforeCommit,
    AfterCommit,
    CommitUnknown
}

public sealed record ProcessScenarioResult(
    bool Success,
    string? Surface,
    string? Scenario,
    string? LogicalId,
    string? Error);

public sealed record CaseManifestEntry(
    string CaseId,
    string Surface,
    string Variant,
    EvidenceVectorKey EvidenceVectorKey,
    RequiredRunner Runner,
    OwningSlice OwningSlice,
    string NormativeTestName);

public sealed record EvidenceTuple(
    string CaseId,
    string Surface,
    string Variant,
    EvidenceVectorKey Key,
    RequiredRunner Runner,
    string NormativeTestName);

public static class CaseId
{
    public const string D01 = "D01";
    public const string D02 = "D02";
    public const string D03 = "D03";
    public const string D04 = "D04";
    public const string D05 = "D05";
    public const string D06 = "D06";
    public const string D07 = "D07";
    public const string D08 = "D08";
    public const string D09 = "D09";
    public const string D10 = "D10";
    public const string D11 = "D11";
    public const string D12 = "D12";
    public const string D13 = "D13";

    public const string O01 = "O01";
    public const string O02 = "O02";
    public const string O03 = "O03";
    public const string O04 = "O04";
    public const string O05 = "O05";
    public const string O06 = "O06";
    public const string O07 = "O07";
    public const string O08 = "O08";
    public const string O09 = "O09";
    public const string O10 = "O10";
    public const string O11 = "O11";
    public const string O12 = "O12";
    public const string O13 = "O13";
    public const string O14 = "O14";
    public const string O15 = "O15";
    public const string O16 = "O16";
    public const string O17 = "O17";
    public const string O18 = "O18";
    public const string O19 = "O19";
    public const string O20 = "O20";
    public const string O21 = "O21";
    public const string O22 = "O22";

    public const string P01 = "P01";
    public const string P02 = "P02";
    public const string P03 = "P03";
    public const string P04 = "P04";
    public const string P05 = "P05";
    public const string P06 = "P06";
    public const string P07 = "P07";
    public const string P08 = "P08";
    public const string P09 = "P09";
    public const string P10 = "P10";
    public const string P11 = "P11";
    public const string P12 = "P12";
    public const string P13 = "P13";

    public const string V01 = "V01";
    public const string V02 = "V02";
    public const string V03 = "V03";
    public const string V04 = "V04";
    public const string V05 = "V05";

    public const string F01 = "F01";
    public const string F02 = "F02";
    public const string F03 = "F03";
    public const string F04 = "F04";
    public const string F05 = "F05";
    public const string F06 = "F06";
    public const string F07 = "F07";
    public const string F08 = "F08";
    public const string F09 = "F09";

    public const string C01 = "C01";
    public const string C02 = "C02";
    public const string C03 = "C03";
    public const string C04 = "C04";
    public const string C05 = "C05";
    public const string C06 = "C06";
    public const string C07 = "C07";
    public const string C08 = "C08";
    public const string C09 = "C09";
    public const string C10 = "C10";
    public const string C11 = "C11";
    public const string C12 = "C12";
    public const string C13 = "C13";
    public const string C14 = "C14";
    public const string C15 = "C15";

    public const string OVG01 = "OVG01";
    public const string OVG02 = "OVG02";
    public const string OVG03 = "OVG03";
    public const string OVG04 = "OVG04";
    public const string OVG05 = "OVG05";
    public const string OVG06 = "OVG06";
    public const string OVG07 = "OVG07";
    public const string OVG08 = "OVG08";
    public const string OVG09 = "OVG09";
    public const string OVG10 = "OVG10";
    public const string OVG11 = "OVG11";
    public const string OVG12 = "OVG12";
    public const string OHC01 = "OHC01";
    public const string OHC02 = "OHC02";
    public const string OHC03 = "OHC03";
    public const string OHC04 = "OHC04";
    public const string OHC05 = "OHC05";
    public const string OHC06 = "OHC06";
    public const string OHC07 = "OHC07";
    public const string OHC08 = "OHC08";
    public const string OHC09 = "OHC09";
    public const string OHC10 = "OHC10";
    public const string OHC11 = "OHC11";
    public const string OHC12 = "OHC12";
    public const string OHC13 = "OHC13";
    public const string OHC14 = "OHC14";
    public const string OHC15 = "OHC15";
    public const string OHC16 = "OHC16";
    public const string OHC17 = "OHC17";
    public const string OHC18 = "OHC18";
    public const string OHC19 = "OHC19";
    public const string OHC20 = "OHC20";
    public const string OHC21 = "OHC21";
    public const string OHC22 = "OHC22";
    public const string OHC23 = "OHC23";
    public const string OHC24 = "OHC24";
    public const string OMI01 = "OMI01";
    public const string OMI02 = "OMI02";
    public const string PSC01 = "PSC01";
    public const string PSC02 = "PSC02";
    public const string PSC03 = "PSC03";
    public const string PSC04 = "PSC04";
    public const string PSC05 = "PSC05";
    public const string PSC06 = "PSC06";
    public const string PSC07 = "PSC07";
    public const string PSC08 = "PSC08";
    public const string AOT01 = "AOT01";
    public const string H301 = "H301";
}
