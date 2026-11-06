#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using static CrestCreates.Tooling.CrudGeneration.CrudArtifactWriter;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// #128 S2: turns the frozen <see cref="CrudGenerationModel"/> into the materialized CRUD surface —
/// contracts DTOs, permission definitions, mapping seeds, service contract, service implementation and
/// the capability input envelopes. Every emitted name, namespace and member list is read from the model,
/// so the emitter owns no second naming rule, and the analyzer plus the build-time tool compile this same
/// implementation.
/// The surface deliberately contains no HTTP projection, no registry activation and no runtime
/// reflection: it must compile as ordinary input inside the same CoreCompile as the official STJ
/// generator and the sibling generators.
/// Imports are emitted inside the namespace block, so one artifact stays compilable alone and the whole
/// artifact set stays compilable when concatenated into a single bundle file — a file-level import after
/// the first namespace declaration would break the bundle.
/// </summary>
public static class CrudArtifactEmitter
{
    /// <summary>Owner key the outputs manifest records for the one assembly-level activation artifact.</summary>
    public const string AssemblyActivationOwnerKey = CrudHostActivationEmitter.OwnerKey;

    /// <summary>Materializes every artifact of one declared CRUD surface, in model order.</summary>
    public static IReadOnlyList<CrudArtifact> Emit(CrudGenerationModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var artifacts = new List<CrudArtifact>
        {
            CrudDtoEmitter.EmitReadDto(model),
            CrudDtoEmitter.EmitCreateDto(model),
            CrudDtoEmitter.EmitUpdateDto(model),
            CrudDtoEmitter.EmitListRequestDto(model),
            CrudPermissionEmitter.EmitPermissions(model),
            CrudMappingEmitter.EmitObjectMappingSeeds(model),
            CrudServiceContractEmitter.EmitServiceContract(model),
            CrudServiceImplementationEmitter.EmitServiceImplementation(model),
            CrudTypedQueryAdapterEmitter.EmitTypedQueryAdapter(model),
        };

        foreach (var action in model.Actions.Where(a => a.UsesCapabilityInputEnvelope))
        {
            artifacts.Add(CrudCapabilityEmitter.EmitCapabilityInputEnvelope(model, action));
        }

        // §7.2: the ExplicitHost projection of this service — descriptors, typed bindings, invokers and
        // typed result envelopes. One concrete contribution type per service.
        artifacts.Add(CrudCapabilityEmitter.EmitCapabilityContribution(model));

        return artifacts;
    }

    /// <summary>
    /// Every artifact of one assembly bundle: the per-model artifacts followed by the single
    /// assembly-level activation artifact. The precompile stage and the JSON reconciliation both consume
    /// this one list, so the outputs manifest and the JSON-side ownership check cannot disagree.
    /// </summary>
    public static IReadOnlyList<CrudOwnedArtifact> EmitBundleArtifacts(
        IReadOnlyList<CrudGenerationModel> models)
    {
        if (models is null)
        {
            throw new ArgumentNullException(nameof(models));
        }

        // Canonical order, so the bundle text, the outputs manifest and the JSON-side reconciliation
        // observe the same bytes regardless of the order the caller discovered the declarations in.
        var ordered = CanonicalOrder(models);
        var artifacts = new List<CrudOwnedArtifact>();
        foreach (var model in ordered)
        {
            foreach (var artifact in Emit(model))
            {
                artifacts.Add(new CrudOwnedArtifact(model.ServiceFullName, artifact));
            }
        }

        if (ordered.Count > 0)
        {
            artifacts.Add(new CrudOwnedArtifact(AssemblyActivationOwnerKey, EmitHostActivation(ordered)));
        }

        return artifacts;
    }

    /// <summary>
    /// The single bundle text the build stage writes as one <c>CrestCreates.Crud.g.cs</c> Compile input.
    /// </summary>
    public static string EmitBundle(CrudGenerationModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var builder = StartDocument(model);
        foreach (var artifact in Emit(model))
        {
            Line(builder, ArtifactSeparator + artifact.HintName + " ----");
            builder.Append(artifact.Source);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The plain fully-qualified type names one artifact declares, read back from the emitted source so
    /// the outputs manifest records what the producer actually declared rather than a name the emitter
    /// merely intended. The owner identity (assembly + service full name) is a sibling field of the
    /// manifest entry, never part of these names: §5.1 keeps ownership out of the type identity so a
    /// consumer can ask one question — "is this exact type already owned?" — by fully-qualified name.
    /// This is the only place the rule exists; the precompile stage and the ownership tests read it here.
    /// </summary>
    public static IReadOnlyList<string> DeclaredTypeFullNames(string source)
        => CrestCreates.Tooling.HttpProjection.HttpProjectionShared.DeclaredTypeFullNames(source);

    /// <summary>
    /// The one assembly-level activation artifact: a public <c>AddGeneratedCrud(this IServiceCollection)</c>
    /// extension in the assembly-qualified generated namespace (§5.2). It registers every contributed
    /// service — the scoped AppService (interface and concrete resolving to the same instance) and the
    /// ExplicitHost contribution — through <c>AddCrestCapabilityEndpointContribution</c>. Emitted once per
    /// bundle, never once per model; repeated calls are idempotent by construction and nothing is written
    /// to process-global state.
    /// </summary>
    public static CrudArtifact EmitHostActivation(IReadOnlyList<CrudGenerationModel> models)
        => CrudHostActivationEmitter.Emit(models);
}
