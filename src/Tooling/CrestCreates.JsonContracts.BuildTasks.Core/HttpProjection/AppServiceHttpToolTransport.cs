using System.Collections.Generic;
using CrestCreates.JsonContracts.BuildTasks.Tool;

namespace CrestCreates.JsonContracts.BuildTasks.HttpProjection;

/// <summary>Transport shapes for the <c>prepare-appservice-http</c> command (source-generated STJ).</summary>
internal sealed class AppServiceHttpPrepareRequest
{
    public string? AssemblyName { get; set; }

    public List<SourceDto>? Sources { get; set; }

    public List<string>? ReferencePaths { get; set; }

    public string? LangVersion { get; set; }

    public List<string>? DefineConstants { get; set; }

    public string? Nullable { get; set; }

    public bool AllowUnsafeBlocks { get; set; }

    public bool ValidateOnly { get; set; }
}

internal sealed class AppServiceHttpPrepareResponse
{
    public List<ToolDiagnosticDto>? Diagnostics { get; set; }

    public byte[]? BundleBytes { get; set; }

    public AppServiceHttpOutputManifestDocument? Outputs { get; set; }

    public int DeclarationCount { get; set; }

    public string? ModelSchemaVersion { get; set; }
}
