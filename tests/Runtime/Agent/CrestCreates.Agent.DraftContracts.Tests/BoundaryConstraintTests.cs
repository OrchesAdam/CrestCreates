using System.Reflection;
using CrestCreates.Agent.DraftContracts;
using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.DraftContracts.Tests;

/// <summary>
/// DTO boundary constraint tests for the Draft Payload Contract system.
///
/// Ensures that all DTO types follow the strict contract rules:
/// - All DTO types are sealed records
/// - No DTO property exposes IDescriptor
/// - No DTO property exposes object or dynamic
/// - All DTO properties are immutable (init-only)
/// </summary>
public class BoundaryConstraintTests
{
    /// <summary>
    /// Returns all DTO types from the generated manifest (the source of truth for contract types).
    /// This avoids false positives on static utility classes like AgentDraftContractErrorCodes.
    /// </summary>
    private static IEnumerable<Type> GetDtoTypesForScan()
    {
        return GeneratedAgentDraftPayloadContractManifest.ContractTypes;
    }

    /// <summary>
    /// Recursively enumerates all property types reachable from <paramref name="type"/>,
    /// including through generic arguments and nested record/class properties.
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

            // Unwrap generic type arguments (IReadOnlyList<T>, etc.)
            if (propType.IsGenericType)
            {
                foreach (var arg in propType.GetGenericArguments())
                {
                    if (arg.IsGenericParameter)
                        continue;

                    yield return arg;

                    if (arg.IsClass && arg != typeof(string) && !arg.IsArray)
                    {
                        foreach (var nested in GetAllPropertyTypes(arg, visited))
                            yield return nested;
                    }
                }
            }

            // Recurse into non-primitive class/struct types
            if ((propType.IsClass || IsUserStruct(propType)) && propType != typeof(string) && !propType.IsArray)
            {
                foreach (var nested in GetAllPropertyTypes(propType, visited))
                    yield return nested;
            }
        }
    }

    private static bool IsUserStruct(Type type) =>
        type.IsValueType && !type.IsPrimitive && !type.IsEnum && type.Namespace != "System";

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

    // ── Test 1: All DTO types are sealed records ──────────────────

    [Fact]
    public void All_DtoTypes_Are_Sealed_Records()
    {
        var violations = new List<string>();

        foreach (var dtoType in GetDtoTypesForScan())
        {
            // Enum types (like ChangedField flags enums) are not records but are valid contract types
            if (dtoType.IsEnum)
                continue;

            bool isRecord = dtoType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(m => m.Name == "<Clone>$");

            if (!isRecord)
                violations.Add($"{dtoType.Name} is not a record type.");

            if (!dtoType.IsSealed)
                violations.Add($"{dtoType.Name} is not sealed.");
        }

        violations.Should().BeEmpty();
    }

    // ── Test 2: No DTO property exposes IDescriptor ───────────────

    [Fact]
    public void No_DtoProperty_Exposes_IDescriptor()
    {
        var violations = new List<(Type DtoType, Type PropertyType)>();

        foreach (var dtoType in GetDtoTypesForScan())
        {
            var visited = new HashSet<Type>();
            foreach (var propType in GetAllPropertyTypes(dtoType, visited))
            {
                if (typeof(IDescriptor).IsAssignableFrom(propType))
                {
                    violations.Add((dtoType, propType));
                }
            }
        }

        violations.Should().BeEmpty(
            "DTO contract types must not expose IDescriptor; found {0} violations: {1}",
            violations.Count,
            string.Join(", ", violations.Select(v => $"{v.DtoType.Name}→{v.PropertyType.Name}")));
    }

    // ── Test 3: No DTO property exposes object or dynamic ─────────

    [Fact]
    public void No_DtoProperty_Exposes_Object_Or_Dynamic()
    {
        var forbidden = new HashSet<Type> { typeof(object), typeof(System.Dynamic.ExpandoObject) };
        var violations = new List<(Type DtoType, Type PropertyType)>();

        foreach (var dtoType in GetDtoTypesForScan())
        {
            var visited = new HashSet<Type>();
            foreach (var propType in GetAllPropertyTypes(dtoType, visited))
            {
                if (forbidden.Contains(propType))
                {
                    violations.Add((dtoType, propType));
                }

                // Catch dynamic via System.Object or any escape-hatch
                if (propType.FullName == "System.Object" && propType != typeof(object))
                {
                    violations.Add((dtoType, propType));
                }
            }
        }

        violations.Should().BeEmpty(
            "DTO contract types must not expose object or dynamic; found {0} violations: {1}",
            violations.Count,
            string.Join(", ", violations.Select(v => $"{v.DtoType.Name}→{v.PropertyType.Name}")));
    }

    // ── Test 4: All DTO properties are immutable ──────────────────

    [Fact]
    public void All_DtoProperties_Are_Immutable()
    {
        var violations = new List<string>();

        foreach (var dtoType in GetDtoTypesForScan())
        {
            foreach (var prop in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0)
                    continue;

                // Check if property has a setter that is not init-only
                var setMethod = prop.SetMethod;
                if (setMethod != null)
                {
                    // init-only properties return true for IsInitOnly in .NET 5+
                    bool isInitOnly = setMethod.ReturnParameter.GetRequiredCustomModifiers()
                        .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));

                    if (!isInitOnly)
                    {
                        violations.Add($"{dtoType.Name}.{prop.Name} has a mutable setter (not init-only).");
                    }
                }
                // Properties without any setter are fine (computed/get-only)
            }
        }

        violations.Should().BeEmpty();
    }
}
