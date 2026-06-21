using System.Reflection;
using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane.Projections;
using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Event.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using AgentDraftContractErrorCodes = CrestCreates.Agent.DraftContracts.Dto.AgentDraftContractErrorCodes;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests that all Tool Contract DTOs satisfy the boundary constraints
/// defined in the Phase 7c Tool DTO JSON Contract spec.
///
/// Key invariants:
/// - No DTO may expose IDescriptor, IServiceProvider, or runtime handler types.
/// - No DTO may use object/dynamic/JsonElement escape hatches.
/// - Request DTOs must use AgentDraftPayloadDto, never DescriptorDraftPayload.
/// - DescriptorPackagePreview must not reintroduce projected-away unsafe types.
/// </summary>
public class ToolDtoBoundaryConstraintTests
{
    private static readonly Assembly AbstractionsAssembly =
        typeof(AgentControlPlaneToolJsonSerializerContext).Assembly;

    // ── Helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Recursively enumerates all property types reachable from <paramref name="type"/>,
    /// including through generic arguments (IReadOnlyList&lt;T&gt;, IReadOnlyDictionary&lt;TK,TV&gt;, etc.),
    /// Nullable&lt;T&gt; unwrapping, and nested record/class properties.
    /// </summary>
    private static IEnumerable<Type> GetAllPropertyTypes(Type type, HashSet<Type>? visited = null)
    {
        visited ??= new HashSet<Type>();

        if (!visited.Add(type))
            yield break;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip indexers
            if (prop.GetIndexParameters().Length > 0)
                continue;

            var propType = prop.PropertyType;

            yield return propType;

            // Unwrap Nullable<T>
            if (Nullable.GetUnderlyingType(propType) is { } underlying)
            {
                yield return underlying;
                foreach (var nested in GetAllPropertyTypes(underlying, visited))
                    yield return nested;
                continue;
            }

            // Unwrap generic type arguments (IReadOnlyList<T>, IReadOnlyDictionary<TK,TV>, etc.)
            if (propType.IsGenericType)
            {
                foreach (var arg in propType.GetGenericArguments())
                {
                    // Skip generic parameters from open generic types
                    if (arg.IsGenericParameter)
                        continue;

                    yield return arg;

                    // Recurse into the argument type if it's a class
                    if (arg.IsClass && arg != typeof(string) && !arg.IsArray)
                    {
                        foreach (var nested in GetAllPropertyTypes(arg, visited))
                            yield return nested;
                    }
                }
            }

            // Recurse into non-primitive class types (skip string, arrays, enums, System types)
            if (propType.IsClass && propType != typeof(string) && !propType.IsArray)
            {
                foreach (var nested in GetAllPropertyTypes(propType, visited))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Returns types from the assembly whose namespace contains "ToolDtos"
    /// or whose name ends with "Request" or "Result", or are explicitly
    /// named DTO types that don't follow the standard naming convention.
    /// </summary>
    private static IEnumerable<Type> GetDtoTypesForScan()
    {
        return AbstractionsAssembly.GetExportedTypes()
            .Where(t => t.IsClass && t.IsSealed && !t.IsGenericTypeDefinition)
            .Where(t =>
            {
                var ns = t.Namespace ?? "";
                var name = t.Name;

                // ToolDtos namespace
                if (ns.Contains(".ToolDtos") || ns.EndsWith(".ToolDtos"))
                    return true;

                // Request/result naming convention
                if (name.EndsWith("Request") || name.EndsWith("Result"))
                    return true;

                // Explicit result types that don't end with "Result"
                if (name is "PackageEvidencePreview" or "ActivationReadinessPreview"
                    or "ActivationRequest" or "DescriptorInfo"
                    or "FixProposal" or "DiagnosticExplanation")
                    return true;

                return false;
            });
    }

    /// <summary>
    /// Returns all property types (recursively resolved) across all target DTO types.
    /// </summary>
    private static IEnumerable<Type> GetAllDtoPropertyTypes()
    {
        var visited = new HashSet<Type>();
        foreach (var dtoType in GetDtoTypesForScan())
        {
            foreach (var propType in GetAllPropertyTypes(dtoType, visited))
                yield return propType;
        }
    }

    // ── Test 1: No IDescriptor exposure ───────────────────────────────

    [Fact]
    public void ToolDtos_Do_Not_Expose_IDescriptor()
    {
        var violations = new List<(Type DtoType, Type PropertyType, string PropertyName)>();

        foreach (var dtoType in GetDtoTypesForScan())
        {
            var visited = new HashSet<Type>();
            foreach (var propType in GetAllPropertyTypes(dtoType, visited))
            {
                if (typeof(IDescriptor).IsAssignableFrom(propType) && propType != typeof(IDescriptor))
                {
                    // Track the owning DTO type for better diagnostics
                    violations.Add((dtoType, propType, ""));
                }
                else if (propType == typeof(IDescriptor))
                {
                    violations.Add((dtoType, propType, ""));
                }
            }
        }

        violations.Should().BeEmpty(
            $"no DTO should expose IDescriptor or any type implementing IDescriptor directly or recursively. " +
            $"Violations: {string.Join("; ", violations.Select(v => $"{v.DtoType.Name} → {v.PropertyType.Name}"))}");
    }

    // ── Test 2: No IServiceProvider or runtime handler types ──────────

    [Fact]
    public void ToolDtos_Do_Not_Expose_IServiceProvider_Or_RuntimeTypes()
    {
        var forbiddenSuffixes = new[] { "Registry", "Handler", "Executor", "Scanner" };
        var controlPlaneNsPrefix = "CrestCreates.Agent.ControlPlane";
        var violations = new List<string>();

        foreach (var propType in GetAllDtoPropertyTypes())
        {
            // Check IServiceProvider
            if (propType == typeof(IServiceProvider) || typeof(IServiceProvider).IsAssignableFrom(propType))
            {
                violations.Add($"IServiceProvider: {propType.FullName}");
                continue;
            }

            // Check runtime handler/scanner/executor/registry types from ControlPlane namespace
            var typeName = propType.Name;
            var typeNs = propType.Namespace ?? "";

            if (forbiddenSuffixes.Any(suffix => typeName.EndsWith(suffix, StringComparison.Ordinal))
                && typeNs.StartsWith(controlPlaneNsPrefix, StringComparison.Ordinal))
            {
                violations.Add($"Runtime type: {propType.FullName}");
            }
        }

        violations.Should().BeEmpty(
            "no DTO should expose IServiceProvider, Registry, Handler, Executor, or Scanner types " +
            $"from the ControlPlane namespace. Violations: {string.Join("; ", violations)}");
    }

    // ── Test 3: No object/dynamic/JsonElement/JsonDocument ───────────

    [Fact]
    public void ToolDtos_Do_Not_Use_Object_Or_Dynamic_EscapeHatches()
    {
        // Exact-type matches only: object (covers dynamic), JsonElement, JsonDocument.
        // NOTE: We do NOT use IsAssignableFrom for object because that matches every reference type.
        var forbiddenTypes = new[]
        {
            typeof(object),
            typeof(JsonElement),
            typeof(JsonDocument),
        };

        var violations = new List<string>();

        foreach (var propType in GetAllDtoPropertyTypes())
        {
            foreach (var forbidden in forbiddenTypes)
            {
                if (propType == forbidden)
                {
                    violations.Add(propType.FullName ?? propType.Name);
                }
            }
        }

        violations.Should().BeEmpty(
            "no DTO should use object, dynamic, JsonElement, or JsonDocument. " +
            $"Violations: {string.Join("; ", violations)}");
    }

    // ── Test 4: DraftComparisonResult.CurrentActiveDescriptor type ───

    [Fact]
    public void DraftComparisonResult_Does_Not_Expose_IDescriptor()
    {
        var currentActiveProp = typeof(DraftComparisonResult)
            .GetProperty(nameof(DraftComparisonResult.CurrentActiveDescriptor));

        currentActiveProp.Should().NotBeNull("DraftComparisonResult must have CurrentActiveDescriptor property");
        currentActiveProp!.PropertyType.Should().Be(
            typeof(DescriptorSummaryDto),
            "DraftComparisonResult.CurrentActiveDescriptor must be DescriptorSummaryDto, not IDescriptor");
        // Also verify it's nullable (the ? annotation)
        var nullableContext = new NullabilityInfoContext();
        var nullabilityInfo = nullableContext.Create(currentActiveProp);
        nullabilityInfo.ReadState.Should().Be(
            NullabilityState.Nullable,
            "DraftComparisonResult.CurrentActiveDescriptor must be nullable (DescriptorSummaryDto?)");
    }

    // ── Test 5: CreateDescriptorDraftRequest.Payload type ─────────────

    [Fact]
    public void CreateDescriptorDraftRequest_Does_Not_Expose_DescriptorDraftPayload()
    {
        var payloadProp = typeof(CreateDescriptorDraftRequest)
            .GetProperty(nameof(CreateDescriptorDraftRequest.Payload));

        payloadProp.Should().NotBeNull("CreateDescriptorDraftRequest must have Payload property");
        payloadProp!.PropertyType.Should().Be(
            typeof(AgentDraftPayloadDto),
            "CreateDescriptorDraftRequest.Payload must be AgentDraftPayloadDto, not DescriptorDraftPayload");
    }

    // ── Test 6: UpdateDescriptorDraftRequest.Payload type ─────────────

    [Fact]
    public void UpdateDescriptorDraftRequest_Does_Not_Expose_DescriptorDraftPayload()
    {
        var payloadProp = typeof(UpdateDescriptorDraftRequest)
            .GetProperty(nameof(UpdateDescriptorDraftRequest.Payload));

        payloadProp.Should().NotBeNull("UpdateDescriptorDraftRequest must have Payload property");
        payloadProp!.PropertyType.Should().Be(
            typeof(AgentDraftPayloadPatchDto),
            "UpdateDescriptorDraftRequest.Payload must be AgentDraftPayloadPatchDto, not DescriptorDraftPayload or AgentDraftPayloadDto");
        // Verify nullable annotation
        var nullableContext = new NullabilityInfoContext();
        var nullabilityInfo = nullableContext.Create(payloadProp);
        nullabilityInfo.ReadState.Should().Be(
            NullabilityState.Nullable,
            "UpdateDescriptorDraftRequest.Payload must be nullable (AgentDraftPayloadPatchDto?)");
    }

    // ── Test 7: DescriptorPackagePreview unsafe type check ────────────

    [Fact]
    public void DescriptorPackagePreview_Does_Not_Reintroduce_ProjectedUnsafeTypes()
    {
        var packagePreviewType = typeof(DraftAbstractions.DescriptorPackagePreview);

        // Exact-type matches (not IsAssignableFrom for object, which matches every ref type).
        var unsafeExactTypes = new[]
        {
            typeof(DraftAbstractions.DescriptorDraft),
            typeof(DraftAbstractions.DescriptorDraftReviewResult),
            typeof(IDescriptor),
            typeof(DraftAbstractions.DescriptorDraftMaterializationResult),
            typeof(object),
            typeof(JsonElement),
        };

        // Type + assignable check (for interface IDescriptor)
        var unsafeAssignableTypes = new[]
        {
            typeof(IDescriptor),
        };

        var visited = new HashSet<Type>();
        var violations = new List<Type>();

        foreach (var propType in GetAllPropertyTypes(packagePreviewType, visited))
        {
            foreach (var unsafeType in unsafeExactTypes)
            {
                if (propType == unsafeType)
                {
                    violations.Add(propType);
                }
            }

            foreach (var unsafeType in unsafeAssignableTypes)
            {
                if (propType != unsafeType && unsafeType.IsAssignableFrom(propType))
                {
                    violations.Add(propType);
                }
            }
        }

        // Additional check: DescriptorTopologySnapshot is in a sub-namespace
        var topologySnapshotType = typeof(CrestCreates.Metadata.Abstractions.DescriptorTopology.DescriptorTopologySnapshot);
        if (GetAllPropertyTypes(packagePreviewType, new HashSet<Type>()).Any(t => t == topologySnapshotType))
        {
            violations.Add(topologySnapshotType);
        }

        violations.Should().BeEmpty(
            "DescriptorPackagePreview must not reintroduce DescriptorDraft, " +
            "DescriptorDraftReviewResult, IDescriptor, DescriptorTopologySnapshot, " +
            "DescriptorDraftMaterializationResult, object, dynamic, or JsonElement. " +
            $"Found unsafe types: {string.Join(", ", violations.Select(t => t.Name))}");
    }

    // ── Test 8: PackageEvidencePreview unsafe type check ──────────────

    [Fact]
    public void PackageEvidencePreview_Does_Not_Reintroduce_DescriptorDraft_Or_ReviewResult()
    {
        var evidencePreviewType = typeof(PackageEvidencePreview);

        var unsafeTypes = new[]
        {
            typeof(DraftAbstractions.DescriptorDraft),
            typeof(DraftAbstractions.DescriptorDraftReviewResult),
        };

        var visited = new HashSet<Type>();
        var violations = new List<Type>();

        foreach (var propType in GetAllPropertyTypes(evidencePreviewType, visited))
        {
            foreach (var unsafeType in unsafeTypes)
            {
                if (propType == unsafeType || unsafeType.IsAssignableFrom(propType))
                {
                    violations.Add(propType);
                }
            }
        }

        violations.Should().BeEmpty(
            "PackageEvidencePreview must not reintroduce DescriptorDraft or " +
            "DescriptorDraftReviewResult. " +
            $"Found unsafe types: {string.Join(", ", violations.Select(t => t.Name))}");
    }

    // ── Test 9: AgentDraftPayloadDto discriminator invariants ─────────

    [Fact]
    public void AgentDraftPayloadDto_Discriminator_Allows_Only_KindSpecific_FieldSet()
    {
        // Valid: each discriminator kind with only its matching sub-record non-null
        AssertValidDiscriminator(DescriptorKind.Capability,
            cap => cap with { Capability = new AgentCapabilityDraftPayloadDto { CapabilityKind = CapabilityKind.Command, RiskLevel = CapabilityRiskLevel.Low, State = DescriptorState.Active } });
        AssertValidDiscriminator(DescriptorKind.Workflow,
            wf => wf with { Workflow = new AgentWorkflowDraftPayloadDto { State = DescriptorState.Active } });
        AssertValidDiscriminator(DescriptorKind.HumanTask,
            ht => ht with { HumanTask = new AgentHumanTaskDraftPayloadDto { State = DescriptorState.Active, AssigneeStrategy = AssigneeStrategy.SingleUser } });
        AssertValidDiscriminator(DescriptorKind.Form,
            f => f with { Form = new AgentFormDraftPayloadDto { State = DescriptorState.Active } });
        AssertValidDiscriminator(DescriptorKind.Event,
            ev => ev with { Event = new AgentEventDraftPayloadDto { State = DescriptorState.Active, Category = EventCategory.Domain, Semantic = EventSemantic.Fact, Importance = EventImportance.Operational, ChangeKind = SchemaChangeKind.Additive } });
        AssertValidDiscriminator(DescriptorKind.Schema,
            s => s with { Schema = new AgentSchemaDraftPayloadDto { State = DescriptorState.Active, ChangeKind = SchemaChangeKind.Additive } });
    }

    [Fact]
    public void AgentDraftPayloadDto_MixedKindPayloads_Are_Rejected()
    {
        // Capability discriminator but Workflow sub-record populated
        var mixedPayload = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto { CapabilityKind = CapabilityKind.Command, RiskLevel = CapabilityRiskLevel.Low, State = DescriptorState.Active },
            Workflow = new AgentWorkflowDraftPayloadDto { State = DescriptorState.Active },
        };

        var (isValid1, error1) = AgentDraftPayloadProjection.TryValidatePayload(mixedPayload);
        isValid1.Should().BeFalse(
            "mixed-kind payload (Capability + Workflow populated) must be rejected");

        // Workflow discriminator but Capability sub-record populated
        var mixedPayload2 = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Workflow,
            Workflow = new AgentWorkflowDraftPayloadDto { State = DescriptorState.Active },
            HumanTask = new AgentHumanTaskDraftPayloadDto { State = DescriptorState.Active, AssigneeStrategy = AssigneeStrategy.SingleUser },
        };

        var (isValid2, error2) = AgentDraftPayloadProjection.TryValidatePayload(mixedPayload2);
        isValid2.Should().BeFalse(
            "mixed-kind payload (Workflow + HumanTask populated) must be rejected");

        // Event discriminator with no matching sub-record populated.
        // Discriminator-aware validation now catches this at TryValidatePayload,
        // so Create returns failure instead of NRE.
        var mismatchedPayload = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Event,
            Capability = new AgentCapabilityDraftPayloadDto { CapabilityKind = CapabilityKind.Command, RiskLevel = CapabilityRiskLevel.Low, State = DescriptorState.Active },
        };

        var result3 = AgentDraftPayloadProjection.Create(mismatchedPayload);
        result3.IsSuccess.Should().BeFalse(
            "mismatched payload (Event discriminator but Capability populated) must be rejected");
        result3.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }

    private static void AssertValidDiscriminator(
        DescriptorKind kind,
        Func<AgentDraftPayloadDto, AgentDraftPayloadDto> configure)
    {
        var basePayload = new AgentDraftPayloadDto { Discriminator = kind };
        var payload = configure(basePayload);

        // Property-level invariant: only the matching sub-record is non-null
        var kindFieldMap = new Dictionary<DescriptorKind, Func<AgentDraftPayloadDto, object?>>
        {
            [DescriptorKind.Capability] = p => p.Capability,
            [DescriptorKind.Workflow] = p => p.Workflow,
            [DescriptorKind.HumanTask] = p => p.HumanTask,
            [DescriptorKind.Form] = p => p.Form,
            [DescriptorKind.Event] = p => p.Event,
            [DescriptorKind.Schema] = p => p.Schema,
        };

        foreach (var (mapKind, accessor) in kindFieldMap)
        {
            var value = accessor(payload);
            if (mapKind == kind)
            {
                value.Should().NotBeNull(
                    $"sub-record for {kind} must be non-null when Discriminator = {kind}");
            }
            else
            {
                value.Should().BeNull(
                    $"sub-record for {mapKind} must be null when Discriminator = {kind}");
            }
        }
    }

    // ── Test 10: Request-side payloads are adapter-safe ───────────────

    [Fact]
    public void RequestSide_DraftPayloads_Are_AdapterSafe()
    {
        var requestTypes = AbstractionsAssembly.GetExportedTypes()
            .Where(t => t.IsClass && t.IsSealed && !t.IsGenericTypeDefinition)
            .Where(t => t.Name.EndsWith("Request"))
            .ToList();

        requestTypes.Should().NotBeEmpty("there should be request DTOs in the assembly");

        foreach (var requestType in requestTypes)
        {
            var payloadProps = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(DraftAbstractions.DescriptorDraftPayload)
                         || (Nullable.GetUnderlyingType(p.PropertyType) == typeof(DraftAbstractions.DescriptorDraftPayload)));

            payloadProps.Should().BeEmpty(
                $"request DTO {requestType.Name} must not expose DescriptorDraftPayload. " +
                "All payload fields must use AgentDraftPayloadDto or AgentDraftPayloadDto? instead.");
        }
    }
}
