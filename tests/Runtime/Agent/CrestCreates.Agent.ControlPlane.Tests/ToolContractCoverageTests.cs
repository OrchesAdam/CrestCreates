using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Coverage tests that enforce set equality across three sets:
/// 1. Static manifest tool names (from <see cref="StaticAgentToolManifestProvider"/>)
/// 2. Request/result contract types registered in <see cref="AgentControlPlaneToolJsonSerializerContext"/>
/// 3. Source-generated <see cref="JsonTypeInfo"/> entries available in the context.
///
/// These tests are tool-kind-aware: facade tools require <see cref="AgentToolResult{T}"/>
/// coverage; manifest query tools do not.
/// </summary>
public class ToolContractCoverageTests
{
    // ── Shared helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns all tool names from the static manifest provider.
    /// This is the ground-truth set for coverage comparisons.
    /// </summary>
    private static HashSet<string> GetAllManifestToolNames()
    {
        var provider = new StaticAgentToolManifestProvider();
        return provider.GetAllTools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns tool names that are manifest query tools
    /// (category == AgentToolCategory.Manifest).
    /// These do NOT use AgentToolResult&lt;T&gt; wrappers.
    /// </summary>
    private static HashSet<string> GetManifestQueryToolNames()
    {
        var provider = new StaticAgentToolManifestProvider();
        return provider.GetAllTools()
            .Where(t => t.Category == AgentToolCategory.Manifest)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds a map from facade tool name to its result type T
    /// by reflecting on <see cref="IAgentControlPlaneToolService"/>.
    /// Each method returning Task&lt;AgentToolResult&lt;T&gt;&gt; maps to a tool.
    /// </summary>
    private static Dictionary<string, Type> BuildFacadeToolResultMap()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        var serviceType = typeof(IAgentControlPlaneToolService);

        foreach (var method in serviceType.GetMethods())
        {
            var toolName = StripAsyncSuffix(method.Name);
            var returnType = method.ReturnType; // Task<AgentToolResult<T>>

            // Extract T from Task<AgentToolResult<T>>
            var taskType = returnType.GetGenericArguments()[0]; // AgentToolResult<T>
            var resultType = taskType.GetGenericArguments()[0]; // T

            map[toolName] = resultType;
        }

        return map;
    }

    /// <summary>
    /// Builds a map from facade tool name to its request DTO type (if any).
    /// A request DTO is a non-string, non-CancellationToken parameter type
    /// that is a CrestCreates type (not a primitive or BCL type).
    /// All such types MUST be registered in the JSON serializer context,
    /// regardless of whether they currently are — this is the coverage gate.
    /// </summary>
    private static Dictionary<string, Type> BuildFacadeToolRequestMap()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        var serviceType = typeof(IAgentControlPlaneToolService);

        foreach (var method in serviceType.GetMethods())
        {
            var toolName = StripAsyncSuffix(method.Name);
            var parameters = method.GetParameters();

            // Find the first parameter after context that is not CancellationToken
            var requestParam = parameters
                .Skip(1)
                .FirstOrDefault(p => p.ParameterType != typeof(CancellationToken));

            if (requestParam is null)
                continue;

            var paramType = requestParam.ParameterType;

            // Include all CrestCreates request types — they MUST be registered
            // (string, DescriptorRef, and other non-CrestCreates types don't need registration)
            if (IsCrestCreatesType(paramType))
            {
                map[toolName] = paramType;
            }
        }

        return map;
    }

    /// <summary>
    /// Returns the set of result types for manifest query tools.
    /// These are not wrapped in AgentToolResult&lt;T&gt;.
    /// </summary>
    private static Dictionary<string, Type> BuildManifestToolTypeMap()
    {
        return new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["ListAgentTools"] = typeof(IReadOnlyList<AgentToolDescriptor>),
            ["GetAgentToolDescriptor"] = typeof(AgentToolDescriptor),
        };
    }

    /// <summary>
    /// Returns all types registered via [JsonSerializable] attributes
    /// on <see cref="AgentControlPlaneToolJsonSerializerContext"/>.
    /// Uses CustomAttributeData to avoid dependency on property name
    /// variance across SDK versions.
    /// </summary>
    private static HashSet<Type> GetAllJsonSerializableTypes()
    {
        var contextType = typeof(AgentControlPlaneToolJsonSerializerContext);
        return contextType.GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(JsonSerializableAttribute))
            .Select(d => (Type)d.ConstructorArguments[0].Value!)
            .ToHashSet();
    }

    /// <summary>
    /// Returns all types that have corresponding JsonTypeInfo&lt;T&gt; properties
    /// on <see cref="AgentControlPlaneToolJsonSerializerContext"/>.
    /// These are the types that the source generator produced type info for.
    /// </summary>
    private static HashSet<Type> GetAllJsonTypeInfoTypes()
    {
        var contextType = typeof(AgentControlPlaneToolJsonSerializerContext);

        // The generated context has properties like:
        //   public JsonTypeInfo<FooType> FooType { get; }
        // We extract FooType from each such property.
        return contextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();
    }

    /// <summary>
    /// Recursively collects all types reachable from <paramref name="rootTypes"/>
    /// via public instance properties (including through generic arguments).
    /// Skips system types, arrays of primitives, and already-visited types.
    /// Used to determine which supporting types are "referenced" by the contract DTOs.
    /// </summary>
    private static HashSet<Type> CollectReferencedTypes(IEnumerable<Type> rootTypes)
    {
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>(rootTypes);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();

            if (!visited.Add(type))
                continue;

            // Skip non-CrestCreates types and system primitives
            if (!IsCrestCreatesType(type))
                continue;

            // Recurse through generic arguments
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    if (!arg.IsGenericParameter)
                        queue.Enqueue(arg);
                }
            }

            // Recurse through properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0)
                    continue;

                var propType = prop.PropertyType;

                // Unwrap Nullable<T>
                if (Nullable.GetUnderlyingType(propType) is { } underlying)
                {
                    queue.Enqueue(underlying);
                    continue;
                }

                queue.Enqueue(propType);

                // Also enqueue generic arguments of property types
                if (propType.IsGenericType && !propType.IsGenericTypeDefinition)
                {
                    foreach (var arg in propType.GetGenericArguments())
                    {
                        if (!arg.IsGenericParameter)
                            queue.Enqueue(arg);
                    }
                }
            }
        }

        return visited;
    }

    private static bool IsCrestCreatesType(Type type)
    {
        var ns = type.Namespace ?? "";
        return ns.StartsWith("CrestCreates.", StringComparison.Ordinal);
    }

    private static string StripAsyncSuffix(string methodName)
    {
        return methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName[..^5]
            : methodName;
    }

    // ── Test 1 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that every tool in the static manifest has its contract types
    /// registered in <see cref="AgentControlPlaneToolJsonSerializerContext"/>.
    ///
    /// For facade tools: both <c>AgentToolResult&lt;TResult&gt;</c> and (when applicable)
    /// the request DTO must be present in the [JsonSerializable] registrations.
    /// For manifest query tools: the type itself must be registered (no wrapper).
    /// </summary>
    [Fact]
    public void ManifestToolNames_Match_ContractRegistrations()
    {
        var manifestToolNames = GetAllManifestToolNames();
        var manifestQueryNames = GetManifestQueryToolNames();
        var facadeResultMap = BuildFacadeToolResultMap();
        var facadeRequestMap = BuildFacadeToolRequestMap();
        var manifestTypeMap = BuildManifestToolTypeMap();
        var serializableTypes = GetAllJsonSerializableTypes();

        var errors = new List<string>();

        // Set equality: manifest tool names = facade methods ∪ manifest query tools
        var facadeToolNames = facadeResultMap.Keys.ToHashSet(StringComparer.Ordinal);
        var allContractTools = facadeToolNames
            .Union(manifestTypeMap.Keys, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var inManifestNotInContract = manifestToolNames.Except(allContractTools).ToList();
        var inContractNotInManifest = allContractTools.Except(manifestToolNames).ToList();

        if (inManifestNotInContract.Any())
        {
            errors.Add(
                $"Manifest tools without contract coverage: [{string.Join(", ", inManifestNotInContract)}]");
        }
        if (inContractNotInManifest.Any())
        {
            errors.Add(
                $"Contract tools without manifest entry: [{string.Join(", ", inContractNotInManifest)}]");
        }

        // Tools returning AgentToolResult<string> (BCL type) don't need JsonSerializable registration
        var bclResultTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "RenderDescriptorReviewReport", // returns AgentToolResult<string>
        };

        // Check facade tools: AgentToolResult<TResult> and request DTO must be registered
        foreach (var (toolName, resultType) in facadeResultMap)
        {
            // Skip BCL-result tools (e.g., AgentToolResult<string> doesn't need registration)
            if (bclResultTools.Contains(toolName))
                continue;

            // AgentToolResult<TResult> must be in the serializable set
            var wrappedType = typeof(AgentToolResult<>).MakeGenericType(resultType);
            if (!serializableTypes.Contains(wrappedType))
            {
                errors.Add(
                    $"Tool '{toolName}': missing AgentToolResult<{resultType.Name}> registration");
            }

            // If this tool has a request DTO, it must be in the serializable set
            if (facadeRequestMap.TryGetValue(toolName, out var requestType))
            {
                if (!serializableTypes.Contains(requestType))
                {
                    errors.Add(
                        $"Tool '{toolName}': missing request type '{requestType.Name}' registration");
                }
            }
        }

        // Check manifest query tools
        foreach (var (toolName, toolType) in manifestTypeMap)
        {
            if (!serializableTypes.Contains(toolType))
            {
                errors.Add(
                    $"Tool '{toolName}': missing '{toolType.Name}' registration");
            }
        }

        errors.Should().BeEmpty(
            "manifest tool names must equal contract tool names, and every tool must have its contract types registered. " +
            $"Errors: {string.Join("; ", errors)}");
    }

    // ── Test 2 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that every [JsonSerializable] contract type has a corresponding
    /// <see cref="JsonTypeInfo"/> generated by the source generator.
    ///
    /// STJ automatically generates JsonTypeInfo for transitively referenced types
    /// (primitives, collections, Nullable, etc.) beyond explicit [JsonSerializable]
    /// registrations. We only verify the forward direction: every registered type
    /// must have a JsonTypeInfo. The reverse direction is not enforced because
    /// auto-generated type infos are expected and correct.
    /// </summary>
    [Fact]
    public void ContractRegistrations_Match_JsonTypeInfoSet()
    {
        var serializableTypes = GetAllJsonSerializableTypes();
        var jsonTypeInfoTypes = GetAllJsonTypeInfoTypes();

        // Verify forward direction only: every registered type must have a JsonTypeInfo
        var missingFromJsonTypeInfo = serializableTypes.Except(jsonTypeInfoTypes).ToList();

        missingFromJsonTypeInfo.Should().BeEmpty(
            "every [JsonSerializable] type must have a corresponding JsonTypeInfo<T> property. " +
            $"Missing JsonTypeInfo for: {string.Join(", ", missingFromJsonTypeInfo.Select(t => t.Name))}");
    }

    // ── Test 3 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that for every facade tool, <c>AgentToolResult&lt;TResult&gt;</c>
    /// has a corresponding <see cref="JsonTypeInfo"/> in the context.
    ///
    /// Manifest query tools are excluded because they return plain types
    /// (not wrapped in AgentToolResult).
    /// </summary>
    [Fact]
    public void FacadeTools_Have_AgentToolResult_JsonTypeInfo()
    {
        var facadeResultMap = BuildFacadeToolResultMap();
        var jsonTypeInfoTypes = GetAllJsonTypeInfoTypes();

        // Tools returning AgentToolResult<string> (BCL type) don't have a specific JsonTypeInfo
        var bclResultTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "RenderDescriptorReviewReport",
        };

        var missingTypeInfos = new List<string>();

        foreach (var (toolName, resultType) in facadeResultMap)
        {
            if (bclResultTools.Contains(toolName))
                continue;

            var wrappedType = typeof(AgentToolResult<>).MakeGenericType(resultType);
            if (!jsonTypeInfoTypes.Contains(wrappedType))
            {
                missingTypeInfos.Add(
                    $"Tool '{toolName}': missing JsonTypeInfo for AgentToolResult<{resultType.Name}>");
            }
        }

        missingTypeInfos.Should().BeEmpty(
            "every facade tool must have AgentToolResult<TResult> covered by a JsonTypeInfo. " +
            $"Missing: {string.Join("; ", missingTypeInfos)}");
    }

    // ── Test 3b ────────────────────────────────────────────────────────

    /// <summary>
    /// Helper to detect whether a type is a compiler-generated record.
    /// Records have a compiler-generated &lt;Clone&gt;$ method.
    /// </summary>
    private static bool IsRecordType(Type type)
    {
        return type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public) is not null;
    }

    /// <summary>
    /// Verifies that every public sealed record DTO type in the ControlPlane.Abstractions
    /// assembly has a corresponding <see cref="JsonTypeInfo"/> property in
    /// <see cref="AgentControlPlaneToolJsonSerializerContext"/>.
    ///
    /// This catches DTOs that should have a <see cref="JsonTypeInfo"/> but were
    /// missed by the source generator (e.g., a new DTO added without a corresponding
    /// <see cref="JsonSerializableAttribute"/>).
    /// </summary>
    [Fact]
    public void AllPublicToolContractDtos_Have_JsonTypeInfo()
    {
        var abstractionsAssembly = typeof(AgentControlPlaneToolJsonSerializerContext).Assembly;

        var dtoTypes = abstractionsAssembly.GetTypes()
            .Where(t => t.IsPublic && t.IsSealed && !t.IsValueType)
            .Where(t => !t.IsGenericTypeDefinition) // Exclude open generics (concrete instantiations are checked)
            .Where(t => t.Namespace?.StartsWith("CrestCreates.Agent.ControlPlane.Abstractions") == true)
            .Where(t => IsRecordType(t))
            // Exclude known non-serialization types (authorization config/result, etc.)
            .Where(t => t != typeof(AgentToolAuthorizationOptions)
                     && t != typeof(AgentToolAuthorizationResult))
            .ToList();

        var jsonTypeInfoTypes = typeof(AgentControlPlaneToolJsonSerializerContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();

        var missing = dtoTypes.Where(t => !jsonTypeInfoTypes.Contains(t)).ToList();

        missing.Should().BeEmpty(
            "all public sealed record DTOs in ControlPlane.Abstractions must have a JsonTypeInfo registration. " +
            $"Missing: {string.Join(", ", missing.Select(t => t.Name))}");
    }

    // ── Test 4 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that no DTO types registered in the context exist without
    /// a corresponding manifest tool. A type is "accounted for" if it is:
    /// - A result type or request type of any tool (facade or manifest), or
    /// - A supporting type transitively referenced by those types.
    ///
    /// Types from non-ControlPlane assemblies (e.g., Metadata.Abstractions,
    /// ContextPack.Abstractions) that serve as tool result/request types
    /// are also considered accounted for.
    /// </summary>
    [Fact]
    public void No_Orphan_ContractTypes_Without_ManifestTool()
    {
        var facadeResultMap = BuildFacadeToolResultMap();
        var facadeRequestMap = BuildFacadeToolRequestMap();
        var manifestTypeMap = BuildManifestToolTypeMap();
        var serializableTypes = GetAllJsonSerializableTypes();

        // Build the root set: all types directly referenced as tool result or request types
        var rootContractTypes = new HashSet<Type>();

        // Facade result types + AgentToolResult<TResult> wrappers
        foreach (var resultType in facadeResultMap.Values)
        {
            rootContractTypes.Add(resultType);
            rootContractTypes.Add(typeof(AgentToolResult<>).MakeGenericType(resultType));
        }

        // Facade request types
        foreach (var requestType in facadeRequestMap.Values)
        {
            rootContractTypes.Add(requestType);
        }

        // Manifest tool types
        foreach (var toolType in manifestTypeMap.Values)
        {
            rootContractTypes.Add(toolType);
        }

        // Collect all transitively referenced types
        var referencedTypes = CollectReferencedTypes(rootContractTypes);

        // Also include root types and their AgentToolResult wrappers explicitly
        // (the queue walk may not add the root types themselves since they start in the queue)
        foreach (var rootType in rootContractTypes)
        {
            referencedTypes.Add(rootType);
        }

        // Find orphan types: registered serializable types that are not referenced
        // and are not explicitly registered supporting types.
        // Some types like AgentToolAuthorizationMode are registered for authorization
        // serialization but aren't directly referenced by tool DTOs — these are
        // legitimate supporting registrations, not orphans.
        var knownSupportingTypes = new HashSet<Type>
        {
            typeof(AgentToolAuthorizationMode),
            typeof(DescriptorReviewReportBuildRequest), // internal builder input, not adapter request
            typeof(DescriptorReviewReportFormat),        // enum parameter for render tool

            // Phase 7e — Activation supporting types (used internally by activation infrastructure)
            typeof(DescriptorActivationAuditRecord),
            typeof(DescriptorActivationDecision),
            typeof(DescriptorActivationPolicy),
            typeof(DescriptorActivationReviewDecision),
            typeof(DescriptorActivationReviewOutcome),
            typeof(DescriptorActivationReviewTaskInput),
            typeof(ActivationEvidenceRecheckResult),
            typeof(ActivationEvidenceDrift),
            typeof(RuntimeActivationGateResult),
            typeof(ResolvedBindingArtifacts),
        };

        var orphanTypes = serializableTypes
            .Where(t => IsCrestCreatesType(t) && !referencedTypes.Contains(t) && !knownSupportingTypes.Contains(t))
            .ToList();

        orphanTypes.Should().BeEmpty(
            "no [JsonSerializable] contract types should exist without being referenced by at least one manifest tool. " +
            $"Orphan types: {string.Join(", ", orphanTypes.Select(t => t.FullName))}");
    }
}
