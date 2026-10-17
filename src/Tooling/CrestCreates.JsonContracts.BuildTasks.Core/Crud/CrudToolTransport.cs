using System.Collections.Generic;
using CrestCreates.JsonContracts.BuildTasks.Tool;

namespace CrestCreates.JsonContracts.BuildTasks.Crud;

/// <summary>
/// Transport shapes for the <c>prepare-crud</c> command. They stay in the tool's own source-generated
/// JSON contract (reflection is disabled in this assembly), and only use arrays/lists/primitives so
/// the official STJ generator can close them without helpers.
/// </summary>
internal sealed class CrudPrepareRequest
{
    public string? AssemblyName { get; set; }

    public List<SourceDto>? Sources { get; set; }

    public List<string>? ReferencePaths { get; set; }

    public string? LangVersion { get; set; }

    public List<string>? DefineConstants { get; set; }

    public string? Nullable { get; set; }

    public bool AllowUnsafeBlocks { get; set; }

    /// <summary>Model schema version the caller expects; a mismatch fails the build rather than reusing output.</summary>
    public string? ExpectedModelSchemaVersion { get; set; }

    /// <summary>True when CRUD generation is disabled: formal declarations then become a build error.</summary>
    public bool ValidateOnly { get; set; }
}

internal sealed class CrudPrepareResponse
{
    public List<ToolDiagnosticDto>? Diagnostics { get; set; }

    public byte[]? BundleBytes { get; set; }

    public CrudOutputManifestDocument? Outputs { get; set; }

    public int DeclarationCount { get; set; }

    public string? ModelSchemaVersion { get; set; }
}

internal sealed class CrudOutputManifestDocument
{
    public string? SchemaVersion { get; set; }

    public string? ModelSchemaVersion { get; set; }

    public List<CrudOutputManifestEntryDto>? Entries { get; set; }
}

internal sealed class CrudOutputManifestEntryDto
{
    public string? OwnerAssemblyName { get; set; }

    public string? OwnerServiceFullName { get; set; }

    public string? ArtifactHintName { get; set; }

    public string[]? DeclaredTypeFullNames { get; set; }

    public string? SourceLocation { get; set; }

    public string? ModelSchemaVersion { get; set; }

    public string? ContentHash { get; set; }
}
