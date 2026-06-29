using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Xunit;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Tests.DescriptorImpact;

public class DescriptorImpactAnalyzerTests
{
    private readonly DescriptorImpactAnalyzer _analyzer = new();

    private static (
        DescriptorTopologySnapshot Snapshot,
        Dictionary<DescriptorRef, DescriptorNode> NodeMap)
        BuildTopology(
            (DescriptorRef Ref, DescriptorKind Kind, string Name, DescriptorState State)[] nodeDefs,
            (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
             string? Role, RelationshipStrength Strength, bool IsRuntimeBinding)[] edgeDefs)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        foreach (var def in nodeDefs)
        {
            nodes[def.Ref] = new DescriptorNode
            {
                Ref = def.Ref,
                Kind = def.Kind,
                Name = def.Name,
                State = def.State,
                OutgoingEdgeIndices = new HashSet<int>(),
                IncomingEdgeIndices = new HashSet<int>()
            };
        }

        var edges = new List<DescriptorEdge>();
        foreach (var def in edgeDefs)
        {
            var edge = new DescriptorEdge
            {
                Index = def.Index,
                From = def.From,
                To = def.To,
                Kind = def.Kind,
                Role = def.Role,
                SourcePath = def.Role,
                Strength = def.Strength,
                IsRuntimeBinding = def.IsRuntimeBinding
            };
            edges.Add(edge);

            if (nodes.TryGetValue(def.From, out var fn))
                ((HashSet<int>)fn.OutgoingEdgeIndices).Add(def.Index);
            if (nodes.TryGetValue(def.To, out var tn))
                ((HashSet<int>)tn.IncomingEdgeIndices).Add(def.Index);
        }

        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        var snapshot = new DescriptorTopologySnapshot(
            nodes, edges,
            new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
            new(), new(), new(), DateTimeOffset.UtcNow);

        return (snapshot, nodes);
    }

    [Fact]
    public void DirectStrongConsumer_IsReported()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);

        report.AffectedDescriptors.Should().ContainSingle()
            .Which.Ref.Should().Be(form);
        report.AffectedDescriptors[0].Severity.Should().Be(DescriptorImpactSeverity.High);
        report.AffectedDescriptors[0].Paths.Should().ContainSingle()
            .Which.Segments.Should().ContainSingle()
            .Which.Role.Should().Be("Schema");
    }

    [Fact]
    public void TransitiveConsumer_IsReported_WithAttenuatedSeverity()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, cap, form, RelationshipKind.Consumes, "InputSchema", RelationshipStrength.Strong, true)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Deprecated } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);

        report.AffectedDescriptors.Should().HaveCount(2);
        var formEntry = report.AffectedDescriptors.First(a => a.Ref == form);
        var capEntry = report.AffectedDescriptors.First(a => a.Ref == cap);

        // form: depth 1, Deprecated + Strong Descriptor → Medium (no boost, not runtime)
        formEntry.Severity.Should().Be(DescriptorImpactSeverity.Medium);
        // cap: depth 2, Deprecated + Strong Runtime → High base, attenuated Medium
        capEntry.Severity.Should().Be(DescriptorImpactSeverity.Medium);
    }

    [Fact]
    public void RuntimeBinding_TerminalSegment_BoostsSeverity()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Updated } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors[0].Severity.Should().Be(DescriptorImpactSeverity.High);
        report.AffectedDescriptors[0].RuntimeAreas.Should().Contain(DescriptorImpactRuntimeArea.RuntimeBinding);
    }

    [Fact]
    public void RuntimeBinding_NonTerminalSegment_DoesNotBoostDownstream()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);
        var wf = new DescriptorRef("wf", "OrderWorkflow", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active),
                (wf, DescriptorKind.Workflow, "OrderWorkflow", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (1, wf, cap, RelationshipKind.Uses, "VariableSchema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);

        var capEntry = report.AffectedDescriptors.First(a => a.Ref == cap);
        // Removed + Strong Runtime → Critical (no boost, already Runtime-aware)
        capEntry.Severity.Should().Be(DescriptorImpactSeverity.Critical);
        capEntry.RuntimeAreas.Should().Contain(DescriptorImpactRuntimeArea.RuntimeBinding);

        var wfEntry = report.AffectedDescriptors.First(a => a.Ref == wf);
        wfEntry.Severity.Should().Be(DescriptorImpactSeverity.Medium);
        wfEntry.RuntimeAreas.Should().Contain(DescriptorImpactRuntimeArea.RuntimeBinding);
    }

    [Fact]
    public void RuntimeBinding_Area_Added_PathWide()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);
        var wf = new DescriptorRef("wf", "OrderWorkflow", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active),
                (wf, DescriptorKind.Workflow, "OrderWorkflow", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (1, wf, cap, RelationshipKind.Uses, "VariableSchema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        var wfEntry = report.AffectedDescriptors.First(a => a.Ref == wf);
        wfEntry.RuntimeAreas.Should().Contain(DescriptorImpactRuntimeArea.RuntimeBinding);
    }

    [Fact]
    public void WeakPath_Included_ByDefault()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Produces, "OutputSchema", RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().ContainSingle().Which.Severity.Should().Be(DescriptorImpactSeverity.Medium);
    }

    [Fact]
    public void WeakPath_Excluded_WhenFalse()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var cap = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (cap, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, cap, schema, RelationshipKind.Produces, "OutputSchema", RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var opts = new DescriptorImpactAnalysisOptions { IncludeWeakRelationships = false };
        var report = _analyzer.Analyze(snapshot, changeSet, opts);
        report.AffectedDescriptors.Should().BeEmpty();
    }

    [Fact]
    public void AdvisoryPath_Skipped_WhenFalse_WithDiagnostic()
    {
        var capA = new DescriptorRef("cap", "A", 2);
        var capB = new DescriptorRef("cap", "B", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (capA, DescriptorKind.Capability, "A", DescriptorState.Active),
                (capB, DescriptorKind.Capability, "B", DescriptorState.Active)
            },
            new[] {
                (0, capB, capA, RelationshipKind.DependsOn, RelationshipRoles.SupersededBy, RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = capA, Kind = DescriptorChangeKind.Removed } }
        };

        var opts = new DescriptorImpactAnalysisOptions { IncludeAdvisoryRelationships = false };
        var report = _analyzer.Analyze(snapshot, changeSet, opts);

        report.AffectedDescriptors.Should().BeEmpty();
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_SKIPPED_WEAK_PATH");
    }

    [Fact]
    public void DepthLimit_Truncates_WithDiagnostic()
    {
        var s = new DescriptorRef("schema", "S", 1);
        var f = new DescriptorRef("form", "F", 1);
        var c = new DescriptorRef("cap", "C", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (s, DescriptorKind.Schema, "S", DescriptorState.Active),
                (f, DescriptorKind.Form, "F", DescriptorState.Active),
                (c, DescriptorKind.Capability, "C", DescriptorState.Active)
            },
            new[] {
                (0, f, s, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, c, f, RelationshipKind.Consumes, "InputSchema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = s, Kind = DescriptorChangeKind.Removed } }
        };

        var opts = new DescriptorImpactAnalysisOptions { MaxDepth = 1 };
        var report = _analyzer.Analyze(snapshot, changeSet, opts);

        report.AffectedDescriptors.Should().ContainSingle().Which.Ref.Should().Be(f);
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_PATH_TRUNCATED");
    }

    [Fact]
    public void ChangedDescriptor_NotInTopology_ReturnsEmpty()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] { (form, DescriptorKind.Form, "Checkout", DescriptorState.Active) },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)>());

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().BeEmpty();
        report.MaxSeverity.Should().Be(DescriptorImpactSeverity.None);
    }

    [Fact]
    public void UnpinnedConsumer_Included_ForExactChangedVersion()
    {
        var schemaV1 = new DescriptorRef("schema", "Order", 1);
        var schemaV2 = new DescriptorRef("schema", "Order", 2);
        var form = new DescriptorRef("form", "Checkout", 1);
        var unpinnedTo = new DescriptorRef("schema", "Order", null);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schemaV1, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, unpinnedTo, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schemaV1, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().ContainSingle().Which.Ref.Should().Be(form);
    }

    [Fact]
    public void UnpinnedRef_Ambiguous_FanOut_WithDiagnostic()
    {
        var schemaV1 = new DescriptorRef("schema", "Order", 1);
        // Two versioned consumers, edge's From is unpinned → resolves to both
        var capV1 = new DescriptorRef("cap", "ProcessOrder", 1);
        var capV2 = new DescriptorRef("cap", "ProcessOrder", 2);
        var unpinnedFrom = new DescriptorRef("cap", "ProcessOrder", null);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schemaV1, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (capV1, DescriptorKind.Capability, "ProcessOrderV1", DescriptorState.Active),
                (capV2, DescriptorKind.Capability, "ProcessOrderV2", DescriptorState.Active)
            },
            new[] {
                (0, unpinnedFrom, schemaV1, RelationshipKind.Consumes, "InputSchema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schemaV1, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().HaveCount(2);
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_AMBIGUOUS_UNPINNED_TARGET");
    }

    [Fact]
    public void UnpinnedRef_Unresolved_EmitsDiagnostic()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var capExact = new DescriptorRef("cap", "ProcessOrder", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (capExact, DescriptorKind.Capability, "ProcessOrder", DescriptorState.Active)
            },
            new[] {
                (0, new DescriptorRef("cap", "Ghost", null), schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_UNRESOLVED_CONSUMER");
    }

    [Fact]
    public void FanOut_PreservesVersionBranchPaths_ButDedupesAffected()
    {
        var schemaV1 = new DescriptorRef("schema", "Order", 1);
        var schemaV2 = new DescriptorRef("schema", "Order", 2);
        var form = new DescriptorRef("form", "Checkout", null);
        var unpinnedTo = new DescriptorRef("schema", "Order", null);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schemaV1, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (schemaV2, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, unpinnedTo, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[]
            {
                new DescriptorChange { Ref = schemaV1, Kind = DescriptorChangeKind.Removed },
                new DescriptorChange { Ref = schemaV2, Kind = DescriptorChangeKind.Removed }
            }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().ContainSingle();
        report.AffectedDescriptors[0].Paths.Should().HaveCount(2);
    }

    [Fact]
    public void MultipleChangeKinds_MultipleAffected_AllReported()
    {
        var s1 = new DescriptorRef("schema", "S1", 1);
        var s2 = new DescriptorRef("schema", "S2", 1);
        var f1 = new DescriptorRef("form", "F1", 1);
        var f2 = new DescriptorRef("form", "F2", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (s1, DescriptorKind.Schema, "S1", DescriptorState.Active),
                (s2, DescriptorKind.Schema, "S2", DescriptorState.Active),
                (f1, DescriptorKind.Form, "F1", DescriptorState.Active),
                (f2, DescriptorKind.Form, "F2", DescriptorState.Active)
            },
            new[] {
                (0, f1, s1, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, f2, s1, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (2, f2, s2, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[]
            {
                new DescriptorChange { Ref = s1, Kind = DescriptorChangeKind.Removed },
                new DescriptorChange { Ref = s2, Kind = DescriptorChangeKind.Deprecated }
            }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().HaveCount(2);
        report.AffectedDescriptors.Select(a => a.Ref.Id).Should().Contain(new[] { "F1", "F2" });
    }

    [Fact]
    public void Severity_IsMaxAcrossAllPaths()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false),
                (1, form, schema, RelationshipKind.Produces, "OutputSchema", RelationshipStrength.Weak, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors[0].Severity.Should().Be(DescriptorImpactSeverity.High);
    }

    [Fact]
    public void Path_ContainsRole_And_SourcePath()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        var seg = report.AffectedDescriptors[0].Paths[0].Segments[0];
        seg.Role.Should().Be("Schema");
        seg.SourcePath.Should().Be("Schema");
    }

    [Fact]
    public void TopologyDiagnostic_OnPath_ReExported()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var topoDiag = new DescriptorTopologyDiagnostic(
            SeverityLevel.Error,
            new DiagnosticCode("MISSING_TARGET"),
            "Missing target: X",
            form, null);

        var nodes = snapshot.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value);
        var edges = snapshot.Edges.ToList();
        var diags = new DescriptorTopologyDiagnostics { All = new[] { topoDiag } };
        var fixedSnapshot = new DescriptorTopologySnapshot(
            nodes, edges, diags, new(), new(), new(), DateTimeOffset.UtcNow);

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(fixedSnapshot, changeSet);
        report.Diagnostics.Should().Contain(d => d.Code == "IMPACT_TOPOLOGY_MISSING_TARGET");
    }

    [Fact]
    public void TopologyDiagnostic_OffPath_NotExported()
    {
        var schema = new DescriptorRef("schema", "Order", 1);
        var form = new DescriptorRef("form", "Checkout", 1);
        var unrelated = new DescriptorRef("form", "Unrelated", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (schema, DescriptorKind.Schema, "Order", DescriptorState.Active),
                (form, DescriptorKind.Form, "Checkout", DescriptorState.Active),
                (unrelated, DescriptorKind.Form, "Unrelated", DescriptorState.Active)
            },
            new[] {
                (0, form, schema, RelationshipKind.Uses, "Schema", RelationshipStrength.Strong, false)
            });

        var topoDiag = new DescriptorTopologyDiagnostic(
            SeverityLevel.Error,
            new DiagnosticCode("MISSING_TARGET"),
            "Missing target: off-path",
            unrelated, null);
        var diags = new DescriptorTopologyDiagnostics { All = new[] { topoDiag } };
        var fixedSnapshot = new DescriptorTopologySnapshot(
            snapshot.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value),
            snapshot.Edges.ToList(), diags, new(), new(), new(), DateTimeOffset.UtcNow);

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = schema, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(fixedSnapshot, changeSet);
        report.Diagnostics.Should().NotContain(d => d.Code == "IMPACT_TOPOLOGY_MISSING_TARGET");
    }

    [Fact]
    public void Cycle_DoesNotLoop_Infinite()
    {
        var a = new DescriptorRef("test", "A", 1);
        var b = new DescriptorRef("test", "B", 1);

        var (snapshot, _) = BuildTopology(
            new[] {
                (a, DescriptorKind.Capability, "A", DescriptorState.Active),
                (b, DescriptorKind.Capability, "B", DescriptorState.Active)
            },
            new[] {
                (0, a, b, RelationshipKind.Uses, "Dep", RelationshipStrength.Strong, false),
                (1, b, a, RelationshipKind.Uses, "Dep", RelationshipStrength.Strong, false)
            });

        var changeSet = new DescriptorChangeSet
        {
            Changes = new[] { new DescriptorChange { Ref = a, Kind = DescriptorChangeKind.Removed } }
        };

        var report = _analyzer.Analyze(snapshot, changeSet);
        report.AffectedDescriptors.Should().NotBeEmpty();
    }
}
