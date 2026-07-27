namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

public sealed record ConsumerSpec(
    string Transport,
    string[] SourceFiles,
    string TargetFramework = "net10.0",
    string ImplicitUsings = "enable",
    string Nullable = "enable",
    string LangVersion = "latest",
    string ManifestAccessibility = "Internal",
    string? EarlierTarget = null,
    bool HasTestSourceGenerator = false,
    bool DuplicateImport = false,
    string ExpectedOutputMarker = "",
    string? GeneratedFile = null,
    string? InputManifest = null,
    string? GenerationStamp = null,
    string? TemporaryDirectory = null);
