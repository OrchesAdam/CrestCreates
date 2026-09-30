using System.Collections.Frozen;
using System.Text.Json;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using Xunit;
using FluentAssertions;

namespace CrestCreates.Agent.Tools.Tests;

/// <summary>
/// #110 Agent Tool Exposure Activation Probe.
/// Tests the boundary between IAgentToolCatalog output and a constrained provider adapter.
/// No production runtime code is added; this is purely a test probe.
/// </summary>
public class AgentToolExposureActivationProbeFixture : IDisposable
{
    private const int DefaultProviderBudget = 128;
    private const string ProbeAgentRole = "probe-agent";

    private static readonly JsonDocument SharedSchemaDocument = JsonDocument.Parse("{}");
    private static readonly JsonElement SharedEmptySchema = SharedSchemaDocument.RootElement.Clone();

    private readonly List<JsonDocument> _allocatedDocuments = new();

    [Fact]
    public void AuthorizedTools_BelowProviderLimit_Should_Be_Consumable()
    {
        const int toolCount = 20;
        var context = CreateContext();
        var (entries, toolNames) = BuildProbeEntries(toolCount);
        var (catalog, snapshot) = BuildCatalog(entries, context);

        var authorizedTools = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
        var adapter = new ConstrainedProviderAdapter(DefaultProviderBudget);
        var result = adapter.TryConsume(authorizedTools);

        authorizedTools.Count.Should().Be(toolCount,
            "Catalog must return all authorized tools without truncation");
        result.IsOverflow.Should().BeFalse();
        result.SubmittedCount.Should().Be(toolCount);
        result.ProviderBudget.Should().Be(DefaultProviderBudget);
        result.OrderingFingerprint.Should().Be(ComputeFingerprint(toolNames));
    }

    [Fact]
    public void AuthorizedTools_AtProviderLimit_Should_Be_Consumable()
    {
        const int toolCount = 128;
        var context = CreateContext();
        var (entries, toolNames) = BuildProbeEntries(toolCount);
        var (catalog, _) = BuildCatalog(entries, context);

        var authorizedTools = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
        var adapter = new ConstrainedProviderAdapter(DefaultProviderBudget);
        var result = adapter.TryConsume(authorizedTools);

        authorizedTools.Count.Should().Be(toolCount,
            "Catalog must return exactly 128 authorized tools");
        result.IsOverflow.Should().BeFalse();
        result.SubmittedCount.Should().Be(toolCount);
        result.OrderingFingerprint.Should().Be(ComputeFingerprint(toolNames));
    }

    [Fact]
    public void AuthorizedTools_AboveProviderLimit_Should_Expose_CurrentFailure()
    {
        const int toolCount = 129;
        var context = CreateContext();
        var (entries, toolNames) = BuildProbeEntries(toolCount);
        var (catalog, _) = BuildCatalog(entries, context);

        var authorizedTools = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
        var adapter = new ConstrainedProviderAdapter(DefaultProviderBudget);
        var result = adapter.TryConsume(authorizedTools);

        authorizedTools.Count.Should().Be(toolCount,
            "Catalog must return all 129 authorized tools without truncation");
        result.IsOverflow.Should().BeTrue(
            "Provider adapter must report overflow when authorized count exceeds budget");
        result.SubmittedCount.Should().Be(0,
            "Adapter must not partially submit tools on overflow");
        result.FailureKind.Should().Be("ToolCountExceedsBudget");
        result.AuthorizedCount.Should().Be(toolCount);
        result.ProviderBudget.Should().Be(DefaultProviderBudget);
        result.OrderingFingerprint.Should().Be(ComputeFingerprint(toolNames));
    }

    [Fact]
    public void ProviderLimit_Should_Not_Change_AuthorizedToolSet()
    {
        const int toolCount = 20;
        var context = CreateContext();
        var (entries, toolNames) = BuildProbeEntries(toolCount);
        var (catalog, _) = BuildCatalog(entries, context);

        var adapter128 = new ConstrainedProviderAdapter(128);
        var adapter64 = new ConstrainedProviderAdapter(64);
        var adapter256 = new ConstrainedProviderAdapter(256);

        var authorizedTools = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
        var result128 = adapter128.TryConsume(authorizedTools);
        var result64 = adapter64.TryConsume(authorizedTools);
        var result256 = adapter256.TryConsume(authorizedTools);

        authorizedTools.Count.Should().Be(toolCount,
            "Catalog returns the same authorized set regardless of provider budget");

        result128.OrderingFingerprint.Should().Be(result64.OrderingFingerprint,
            "Ordering fingerprint must not change with provider budget");
        result128.OrderingFingerprint.Should().Be(result256.OrderingFingerprint,
            "Ordering fingerprint must not change with provider budget");
        result128.AuthorizedCount.Should().Be(result64.AuthorizedCount);
        result128.AuthorizedCount.Should().Be(result256.AuthorizedCount);

        result128.IsOverflow.Should().BeFalse();
        result64.IsOverflow.Should().BeFalse(
            "Budget 64 > 20 tools, so no overflow; authorization set is unchanged");
        result256.IsOverflow.Should().BeFalse();
    }

    [Fact]
    public void ProviderAdapter_Should_Not_Arbitrarily_Truncate_AuthorizedTools()
    {
        const int toolCount = 20;
        const int budget = 15;
        var context = CreateContext();
        var (entries, _) = BuildProbeEntries(toolCount);
        var (catalog, _) = BuildCatalog(entries, context);

        var authorizedTools = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
        var adapter = new ConstrainedProviderAdapter(budget);
        var result = adapter.TryConsume(authorizedTools);

        result.IsOverflow.Should().BeTrue();
        result.SubmittedCount.Should().Be(0,
            "Adapter must not silently truncate to fit budget");
        result.DroppedToolIds.Should().BeEmpty(
            "Adapter must not drop individual tools; overflow is an all-or-nothing failure");
        result.FailureKind.Should().Be("ToolCountExceedsBudget");
    }

    [Fact]
    public void RegistrationOrder_Should_Not_Change_ExposureProbeOutcome()
    {
        const int toolCount = 30;
        var context = CreateContext();

        var (entriesA, toolNamesA) = BuildProbeEntries(toolCount, seed: "probe", shuffle: false);
        var (entriesB, toolNamesB) = BuildProbeEntries(toolCount, seed: "probe", shuffle: true);

        var (catalogA, _) = BuildCatalog(entriesA, context);
        var (catalogB, _) = BuildCatalog(entriesB, context);

        var toolsA = catalogA.ListAsync().AsTask().GetAwaiter().GetResult();
        var toolsB = catalogB.ListAsync().AsTask().GetAwaiter().GetResult();

        var adapterA = new ConstrainedProviderAdapter(DefaultProviderBudget);
        var adapterB = new ConstrainedProviderAdapter(DefaultProviderBudget);
        var resultA = adapterA.TryConsume(toolsA);
        var resultB = adapterB.TryConsume(toolsB);

        toolsA.Count.Should().Be(toolsB.Count);
        resultA.OrderingFingerprint.Should().Be(resultB.OrderingFingerprint,
            "Probe outcome must not depend on registration order");

        var namesA = toolsA.Select(t => t.ToolName).ToList();
        var namesB = toolsB.Select(t => t.ToolName).ToList();
        namesA.Should().Equal(namesB,
            "Catalog sorts ordinally by ToolName, so output must be identical regardless of registration order");
    }

    [Fact]
    public void UnauthorizedTool_Should_Never_Become_ModelVisible()
    {
        var context = CreateContext();
        var authorizedEntries = BuildProbeEntries(5, role: ProbeAgentRole).Entries;
        var unauthorizedEntry = BuildSingleProbeEntry(
            index: 999, role: "unauthorized-role", seed: "unauth");

        var allEntries = authorizedEntries.Concat(new[] { unauthorizedEntry }).ToArray();
        var (catalog, _) = BuildCatalog(allEntries, context);

        var tools = catalog.ListAsync().AsTask().GetAwaiter().GetResult();

        tools.Count.Should().Be(5,
            "Only role-matching tools should be catalog-visible; unauthorized tool must be excluded");
        tools.Should().NotContain(t => t.ToolName == unauthorizedEntry.DiscoveryContract.ToolName,
            "Tool outside Agent authority must never become catalog-visible");
    }

    /// <summary>
    /// Verifies that catalog-visible tools carry governance metadata required for
    /// execution-time enforcement. This is a metadata projection check, not a runtime
    /// enforcement test. For execution-time governance denial evidence, see:
    /// - AgentToolInvokerTests.Invoke_RoleDeniedToolBehavesAsUnknownAndRecordsGovernanceDecision
    /// - GoldenSampleAcceptanceTests.BudgetDenied_DoesNotEnterDispatcher
    /// </summary>
    [Fact]
    public void CatalogVisibleTool_Should_Carry_GovernanceMetadata_ForExecutionTimeEnforcement()
    {
        const int toolCount = 10;
        var context = CreateContext();
        var (entries, _) = BuildProbeEntries(toolCount);
        var (catalog, _) = BuildCatalog(entries, context);

        var tools = catalog.ListAsync().AsTask().GetAwaiter().GetResult();

        tools.Should().AllSatisfy(tool =>
        {
            tool.Governance.Should().NotBeNull(
                "Every catalog-visible tool must carry governance metadata for downstream enforcement");
            tool.Governance.SelectionPolicy.Should().NotBe(AgentToolSelectionPolicy.Unknown,
                "Selection policy must be explicitly projected for execution-time filtering");
            tool.Governance.Budget.Should().NotBeNull(
                "Budget requirement must be projected for execution-time budget gate");
            tool.Governance.Budget.Category.Should().NotBeNullOrWhiteSpace(
                "Budget category must be identifiable");
            tool.Governance.EffectiveApprovalMode.Should().NotBe(AgentToolApprovalMode.Unknown,
                "Approval mode must be explicitly determined");
            tool.Governance.EffectiveAuditMode.Should().NotBe(AgentToolAuditMode.Unknown,
                "Audit mode must be explicitly determined");
        });
    }

    public void Dispose()
    {
        foreach (var doc in _allocatedDocuments)
            doc.Dispose();
        SharedSchemaDocument.Dispose();
    }

    #region Helpers

    private static AgentExecutionContext CreateContext()
        => new()
        {
            ExecutionId = "probe-execution",
            InvocationId = "probe-invocation",
            AgentId = "probe-agent",
            AgentRoles = new HashSet<string>(StringComparer.Ordinal) { ProbeAgentRole },
            CallOrigin = AgentToolCallOrigin.AutomaticSelection,
            CausationId = "probe-causation"
        };

    private static (AgentToolRuntimeEntry[] Entries, List<string> ToolNames) BuildProbeEntries(
        int count, string role = ProbeAgentRole, string seed = "probe", bool shuffle = false)
    {
        var indices = Enumerable.Range(0, count).ToList();
        if (shuffle)
        {
            var rng = new Random(42);
            indices = indices.OrderBy(_ => rng.Next()).ToList();
        }

        var entries = new List<AgentToolRuntimeEntry>(count);
        var toolNames = new List<string>(count);

        foreach (var i in indices)
        {
            var entry = BuildSingleProbeEntry(i, role, seed);
            entries.Add(entry);
            toolNames.Add(entry.DiscoveryContract.ToolName);
        }

        toolNames.Sort(StringComparer.Ordinal);
        return (entries.ToArray(), toolNames);
    }

    private static AgentToolRuntimeEntry BuildSingleProbeEntry(
        int index, string role, string seed = "probe")
    {
        var toolName = $"probe.{seed}.tool.{index:D4}";
        var toolId = $"agent-tool:probe.{seed}.{index:D4}";
        var capabilityId = $"probe.{seed}.capability.{index:D4}";

        var descriptor = new AgentCapabilityToolDescriptor
        {
            Id = toolId,
            Name = toolId,
            Version = 1,
            State = DescriptorState.Active,
            Capability = new CapabilityProjectionReference(
                capabilityId, 1, VersionSelectionMode.Exact, null),
            ToolName = toolName,
            Title = $"Probe tool {index}",
            Description = $"Probe tool {index} for activation testing.",
            SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed,
            SideEffectKind = AgentToolSideEffectKind.ReadOnly,
            ApprovalMode = AgentToolApprovalMode.None,
            Budget = new AgentToolBudgetRequirement
            {
                Category = "probe",
                CostUnits = 1,
                MaxCallsPerExecution = 100
            },
            AuditMode = AgentToolAuditMode.BestEffort,
            AllowedAgentRoles = new[] { role }
        };

        var governance = new AgentToolEffectiveGovernance(
            AgentToolSelectionPolicy.AutomaticAllowed,
            AgentToolSideEffectKind.ReadOnly,
            CapabilityRiskLevel.Low,
            AgentToolApprovalMode.None,
            new AgentToolBudgetRequirement
            {
                Category = "probe",
                CostUnits = 1,
                MaxCallsPerExecution = 100
            },
            AgentToolAuditMode.BestEffort);

        var discovery = new AgentToolDiscoveryContract
        {
            ToolName = toolName,
            Title = $"Probe tool {index}",
            Description = $"Probe tool {index} for activation testing.",
            InputSchema = SharedEmptySchema,
            OutputSchema = null,
            ToolContract = new AgentToolContractIdentity(toolId, 1, $"hash:probe:{toolId}:1"),
            CapabilityContract = new AgentToolContractIdentity(
                capabilityId, 1, $"hash:probe:{capabilityId}:1"),
            Governance = governance
        };

        return new AgentToolRuntimeEntry(
            descriptor,
            Capability: null!,
            InputSchema: null,
            OutputSchema: null,
            Binding: null!,
            DiscoveryContract: discovery,
            AllowedAgentRoles: new HashSet<string>(StringComparer.Ordinal) { role }.ToFrozenSet(),
            EffectiveRisk: CapabilityRiskLevel.Low,
            EffectiveSideEffectKind: AgentToolSideEffectKind.ReadOnly,
            Governance: governance,
            ToolContractHash: $"hash:probe:{toolId}:1",
            CapabilityContractHash: $"hash:probe:{capabilityId}:1",
            InputSchemaContractHash: null,
            OutputSchemaContractHash: null,
            PreparedOutcomeContract: null,
            OutputAuditProjection: null,
            OutputAuditProjector: null,
            OutputOutcomeCodeProjector: null);
    }

    private static (AgentToolCatalog Catalog, AgentToolRuntimeSnapshot Snapshot) BuildCatalog(
        AgentToolRuntimeEntry[] entries, AgentExecutionContext context)
    {
        var snapshot = new AgentToolRuntimeSnapshot(
            entries.ToFrozenDictionary(e => e.Descriptor.ToolName, StringComparer.Ordinal));
        var provider = new AgentToolRuntimeSnapshotProvider();
        provider.Publish(snapshot);
        var contextAccessor = new ProbeAgentExecutionContextAccessor(context);
        var catalog = new AgentToolCatalog(provider, contextAccessor);
        return (catalog, snapshot);
    }

    private static string ComputeFingerprint(IEnumerable<string> toolNames)
        => string.Join(",", toolNames.OrderBy(n => n, StringComparer.Ordinal));

    #endregion

    #region Constrained Provider Adapter Test Double

    /// <summary>
    /// Constrained provider adapter test double.
    /// Models only MaxToolCount = N. No semantic ranking, selection, or discovery.
    /// </summary>
    internal sealed class ConstrainedProviderAdapter
    {
        private readonly int _maxToolCount;

        public ConstrainedProviderAdapter(int maxToolCount)
            => _maxToolCount = maxToolCount;

        public ProviderAdapterResult TryConsume(
            IReadOnlyList<AgentToolDiscoveryContract> authorizedTools)
        {
            var names = authorizedTools.Select(t => t.ToolName).ToList();
            var fingerprint = string.Join(",", names);

            if (authorizedTools.Count > _maxToolCount)
            {
                return new ProviderAdapterResult
                {
                    AuthorizedCount = authorizedTools.Count,
                    ProviderBudget = _maxToolCount,
                    SubmittedCount = 0,
                    IsOverflow = true,
                    FailureKind = "ToolCountExceedsBudget",
                    OrderingFingerprint = fingerprint,
                    DroppedToolIds = Array.Empty<string>()
                };
            }

            return new ProviderAdapterResult
            {
                AuthorizedCount = authorizedTools.Count,
                ProviderBudget = _maxToolCount,
                SubmittedCount = authorizedTools.Count,
                IsOverflow = false,
                FailureKind = null,
                OrderingFingerprint = fingerprint,
                DroppedToolIds = Array.Empty<string>()
            };
        }
    }

    internal sealed class ProviderAdapterResult
    {
        public int AuthorizedCount { get; init; }
        public int ProviderBudget { get; init; }
        public int SubmittedCount { get; init; }
        public bool IsOverflow { get; init; }
        public string? FailureKind { get; init; }
        public string OrderingFingerprint { get; init; } = string.Empty;
        public IReadOnlyList<string> DroppedToolIds { get; init; } = Array.Empty<string>();
    }

    #endregion

    #region Test Infrastructure

    private sealed class ProbeAgentExecutionContextAccessor : IAgentExecutionContextAccessor
    {
        public ProbeAgentExecutionContextAccessor(AgentExecutionContext context)
            => Current = context;

        public AgentExecutionContext? Current { get; }
    }

    #endregion
}
