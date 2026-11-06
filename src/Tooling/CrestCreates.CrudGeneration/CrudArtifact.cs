#nullable enable
namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// One materialized CRUD source file: a stable, unique file-like name plus the ordinary C# text the
/// precompile stage feeds to the compiler as Compile input.
/// </summary>
public sealed record CrudArtifact(string HintName, string FileName, string Source);

/// <summary>
/// One artifact of an assembly bundle together with the owner identity the outputs manifest records for
/// it. Per-service artifacts carry the declaring service full name; the assembly-level activation
/// artifact is owned by the assembly (see <see cref="CrudArtifactEmitter.AssemblyActivationOwnerKey"/>).
/// </summary>
public sealed record CrudOwnedArtifact(string OwnerServiceFullName, CrudArtifact Artifact);
