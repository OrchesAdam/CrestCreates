using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Metadata.ContextPack;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.ContextPack.Tests;

public class MetadataContextPackBuilderTests
{
    // ── Helpers ──

    private static readonly (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
        string? Role, RelationshipStrength Strength, bool IsRuntimeBinding)[] NoEdges = [];

    private static DescriptorTopologySnapshot CreateSnapshot(
        (DescriptorRef Ref, DescriptorKind Kind, string Name, DescriptorState State)[] nodeDefs,
        (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
         string? Role, RelationshipStrength Strength, bool IsRuntimeBinding)[] edgeDefs)
    {
        return CreateSnapshotWithState(nodeDefs, edgeDefs);
    }

    private static DescriptorTopologySnapshot CreateSnapshot(
        (DescriptorRef Ref, DescriptorKind Kind, string Name)[] nodeDefs,
        (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
         string? Role, RelationshipStrength Strength, bool IsRuntimeBinding)[] edgeDefs)
    {
        var withState = nodeDefs
            .Select(n => (n.Ref, n.Kind, n.Name, DescriptorState.Active))
            .ToArray();
        return CreateSnapshotWithState(withState, edgeDefs);
    }

    private static DescriptorTopologySnapshot CreateSnapshotWithState(
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
                SourcePath = null,
                Strength = def.Strength,
                IsRuntimeBinding = def.IsRuntimeBinding
            };
            edges.Add(edge);

            if (nodes.TryGetValue(def.From, out var fromNode))
                ((HashSet<int>)fromNode.OutgoingEdgeIndices).Add(def.Index);
            if (nodes.TryGetValue(def.To, out var toNode))
                ((HashSet<int>)toNode.IncomingEdgeIndices).Add(def.Index);
        }

        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        var diagnostics = new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() };
        return new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);
    }

    private static List<IDescriptor> CreateDescriptors(
        params (DescriptorRef Ref, DescriptorKind Kind, string Name, DescriptorState State)[] defs)
    {
        return defs.Select(d => new TestDescriptor(d.Ref, d.Kind, d.Name, d.State)).ToList<IDescriptor>();
    }

    private sealed class TestDescriptor : IDescriptor
    {
        private readonly DescriptorRef _ref;
        public TestDescriptor(DescriptorRef ref_, DescriptorKind kind, string name, DescriptorState state)
        {
            _ref = ref_; Kind = kind; Name = name; State = state;
        }
        public string Namespace => _ref.Namespace;
        public string Id => _ref.Id;
        public string Name { get; }
        public string FullId => $"{Namespace}.{Id}";
        public DescriptorKind Kind { get; }
        public DescriptorState State { get; }
        public string ContractHash => "";
        public string DefinitionHash => "";
        public string? SupersededById => null;
    }

    private readonly DefaultMetadataContextPackBuilder _builder = new();

    // ── A. Scope Traversal ──

    [Fact]
    public void FocusOnly_Returns_Only_Requested_Descriptors()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");
        var c = new DescriptorRef("ns", "C");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Event, "B"), (c, DescriptorKind.Schema, "C") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active),
            (c, DescriptorKind.Schema, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a, b, c }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Should().HaveCount(3);
        pack.Descriptors.Should().OnlyContain(d => d.IsFocus);
        pack.Relationships.Should().BeEmpty();
    }

    [Fact]
    public void DirectDependencies_Includes_Dependencies_And_Edges()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");
        var schema = new DescriptorRef("schema", "InputSchema");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap"), (schema, DescriptorKind.Schema, "InputSchema") },
            new[] { (0, cap, schema, RelationshipKind.Uses, "InputSchema", RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
            (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, schema });
        pack.Descriptors.First(d => d.Ref.Equals(cap)).IsFocus.Should().BeTrue();
        pack.Descriptors.First(d => d.Ref.Equals(schema)).IsFocus.Should().BeFalse();
        pack.Relationships.Should().ContainSingle();
        pack.Relationships[0].From.Should().Be(cap);
        pack.Relationships[0].To.Should().Be(schema);
    }

    [Fact]
    public void DirectDependents_Includes_Dependents_And_Edges()
    {
        var evt = new DescriptorRef("event", "ApprovedEvent");
        var cap = new DescriptorRef("capability", "ApproveCap");

        var topology = CreateSnapshot(
            new[] { (evt, DescriptorKind.Event, "ApprovedEvent"), (cap, DescriptorKind.Capability, "ApproveCap") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] { (0, cap, evt, RelationshipKind.Produces, null, RelationshipStrength.Weak, false) });

        var descriptors = CreateDescriptors(
            (evt, DescriptorKind.Event, "ApprovedEvent", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "ApproveCap", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependents,
            FocusDescriptors = new[] { evt }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { evt, cap });
        pack.Relationships.Should().ContainSingle();
        pack.Relationships[0].From.Should().Be(cap);
        pack.Relationships[0].To.Should().Be(evt);
    }

    [Fact]
    public void ImpactRadius_Respects_MaxTraversalDepth()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");
        var workflow = new DescriptorRef("workflow", "W");
        var humanTask = new DescriptorRef("humantask", "H");

        var topology = CreateSnapshot(
            new[] {
                (schema, DescriptorKind.Schema, "S"),
                (cap, DescriptorKind.Capability, "C"),
                (workflow, DescriptorKind.Workflow, "W"),
                (humanTask, DescriptorKind.HumanTask, "H")
            },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                (1, workflow, cap, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (2, humanTask, workflow, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
            });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active),
            (workflow, DescriptorKind.Workflow, "W", DescriptorState.Active),
            (humanTask, DescriptorKind.HumanTask, "H", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.ImpactRadius,
            FocusDescriptors = new[] { schema },
            MaxTraversalDepth = 2
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Schema (depth 0) → Cap (depth 1) → Workflow (depth 2). HumanTask at depth 3 excluded.
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { schema, cap, workflow });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);
    }

    [Fact]
    public void RuntimeScenario_Executes_Recipe_Steps()
    {
        var workflow = new DescriptorRef("workflow", "CompanyCert");
        var submitCap = new DescriptorRef("capability", "SubmitCap");
        var reviewHt = new DescriptorRef("humantask", "ReviewHt");
        var approveCap = new DescriptorRef("capability", "ApproveCap");
        var approvedEvt = new DescriptorRef("event", "ApprovedEvt");

        var topology = CreateSnapshot(
            new[] {
                (workflow, DescriptorKind.Workflow, "CompanyCert"),
                (submitCap, DescriptorKind.Capability, "SubmitCap"),
                (reviewHt, DescriptorKind.HumanTask, "ReviewHt"),
                (approveCap, DescriptorKind.Capability, "ApproveCap"),
                (approvedEvt, DescriptorKind.Event, "ApprovedEvt")
            },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, workflow, submitCap, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (1, workflow, reviewHt, RelationshipKind.Triggers, "HumanTaskStep", RelationshipStrength.Strong, true),
                (2, workflow, approveCap, RelationshipKind.Triggers, "CapabilityStep", RelationshipStrength.Strong, true),
                (3, reviewHt, approveCap, RelationshipKind.Triggers, "Outcome", RelationshipStrength.Strong, true),
                (4, approveCap, approvedEvt, RelationshipKind.Produces, null, RelationshipStrength.Weak, false)
            });

        var descriptors = CreateDescriptors(
            (workflow, DescriptorKind.Workflow, "CompanyCert", DescriptorState.Active),
            (submitCap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
            (reviewHt, DescriptorKind.HumanTask, "ReviewHt", DescriptorState.Active),
            (approveCap, DescriptorKind.Capability, "ApproveCap", DescriptorState.Active),
            (approvedEvt, DescriptorKind.Event, "ApprovedEvt", DescriptorState.Active));

        var recipe = new RuntimeScenarioRecipe
        {
            Name = "CompanyCertification",
            Steps = new[]
            {
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.Triggers,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    MaxDepth = 1
                },
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.Produces,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    TargetKind = DescriptorKind.Event,
                    MaxDepth = 1
                }
            }
        };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { workflow },
            ScenarioRecipe = recipe
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(
            new[] { workflow, submitCap, reviewHt, approveCap, approvedEvt });
    }

    // ── B. Bounds and Filters ──

    [Fact]
    public void MaxDescriptorCount_Truncates_And_Emits_Diagnostic()
    {
        var focus = new DescriptorRef("ns", "Focus");
        var others = Enumerable.Range(0, 10)
            .Select(i => new DescriptorRef("ns", $"D{i}"))
            .ToArray();

        var allNodes = new[] { (focus, DescriptorKind.Capability, "Focus") }
            .Concat(others.Select((r, i) => (r, DescriptorKind.Event, $"D{i}")))
            .ToArray();

        var allEdges = others.Select((r, i) =>
            (i, focus, r, RelationshipKind.Triggers, (string?)"Step" + i, RelationshipStrength.Strong, true))
            .ToArray();

        var topology = CreateSnapshot(allNodes, allEdges);

        var allDescriptors = new[] { (focus, DescriptorKind.Capability, "Focus", DescriptorState.Active) }
            .Concat(others.Select((r, i) => (r, DescriptorKind.Event, $"D{i}", DescriptorState.Active)))
            .ToArray();
        var descriptors = CreateDescriptors(allDescriptors);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { focus },
            MaxDescriptorCount = 5
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Focus + up to 4 non-focus (total 5)
        pack.Descriptors.Should().HaveCount(5);
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByCount);
        pack.Summary.TruncatedAtCount.Should().Be(5);
        pack.Descriptors.First(d => d.Ref.Equals(focus)).IsFocus.Should().BeTrue();
    }

    [Fact]
    public void IncludeKinds_Limits_Candidates()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (schema, DescriptorKind.Schema, "S"), (cap, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] { (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap },
            IncludeKinds = new[] { DescriptorKind.Schema }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Focus (Capability) always included, but non-focus Schema is included by IncludeKinds
        pack.Descriptors.Select(d => d.Kind).Should().BeEquivalentTo(new[] { DescriptorKind.Capability, DescriptorKind.Schema });
    }

    [Fact]
    public void ExcludeKinds_Removes_Matches()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (schema, DescriptorKind.Schema, "S"), (cap, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] { (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap },
            ExcludeKinds = new[] { DescriptorKind.Schema }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Schema excluded, only focus Capability remains
        pack.Descriptors.Select(d => d.Kind).Should().BeEquivalentTo(new[] { DescriptorKind.Capability });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.KindExcluded);
    }

    [Fact]
    public void Include_And_Exclude_Precedence()
    {
        var schema = new DescriptorRef("schema", "S");
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (schema, DescriptorKind.Schema, "S"), (cap, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] { (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptors = CreateDescriptors(
            (schema, DescriptorKind.Schema, "S", DescriptorState.Active),
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap },
            IncludeKinds = new[] { DescriptorKind.Schema, DescriptorKind.Capability },
            ExcludeKinds = new[] { DescriptorKind.Schema }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // IncludeKinds allows Schema+Capability, ExcludeKinds removes Schema → only Capability
        pack.Descriptors.Select(d => d.Kind).Should().BeEquivalentTo(new[] { DescriptorKind.Capability });
    }

    [Fact]
    public void Focus_Always_Included_Despite_Kind_Filters()
    {
        var cap = new DescriptorRef("capability", "C");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "C") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { cap },
            ExcludeKinds = new[] { DescriptorKind.Capability }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.FocusKindFiltered);
    }

    // ── C. Diagnostics ──

    [Fact]
    public void Unknown_Focus_Produces_Diagnostic_Not_Exception()
    {
        var missing = new DescriptorRef("ns", "Missing");

        var topology = CreateSnapshot(
            Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
            NoEdges);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { missing }
        };

        var act = () => _builder.Build(request, topology, Array.Empty<IDescriptor>());

        act.Should().NotThrow();
        var pack = act();
        pack.Descriptors.Should().BeEmpty();
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.FocusNotFound);
    }

    [Fact]
    public void Mixed_Known_And_Unknown_Focus_Continues_With_Known()
    {
        var known = new DescriptorRef("ns", "Known");
        var missing = new DescriptorRef("ns", "Missing");

        var topology = CreateSnapshot(
            new[] { (known, DescriptorKind.Capability, "Known") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (known, DescriptorKind.Capability, "Known", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { known, missing }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { known });
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.FocusNotFound);
    }

    [Fact]
    public void RuntimeScenario_Without_Recipe_Emits_Error()
    {
        var focus = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (focus, DescriptorKind.Capability, "A") },
            NoEdges);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { focus },
            ScenarioRecipe = null
        };

        var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.RecipeMissing &&
            d.Severity == MetadataContextPackDiagnosticSeverity.Error);
    }

    [Fact]
    public void Truncated_By_Depth_Only_When_Unvisited_Nodes_Exist()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] { (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.ImpactRadius,
            FocusDescriptors = new[] { a },
            MaxTraversalDepth = 10
        };

        var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

        // Graph is shallow (depth 1), MaxTraversalDepth=10 reaches everything → no truncation diagnostic
        pack.Diagnostics.Should().NotContain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);
    }

    [Fact]
    public void Hash_Builder_Missing_Emits_Warning()
    {
        var focus = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (focus, DescriptorKind.Capability, "A") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (focus, DescriptorKind.Capability, "A", DescriptorState.Active));

        var builder = new DefaultMetadataContextPackBuilder(hashBuilder: null);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { focus },
            IncludeStableHashes = true
        };

        var pack = builder.Build(request, topology, descriptors);

        pack.Descriptors.All(d => d.Hashes is null).Should().BeTrue();
        pack.Diagnostics.Should().Contain(d => d.Code == MetadataContextPackDiagnosticCodes.HashBuilderMissing);
    }

    // ── D. Determinism and Safety ──

    [Fact]
    public void Deterministic_Output_Ordering()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");
        var c = new DescriptorRef("ns", "C");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Event, "B"), (c, DescriptorKind.Schema, "C") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active),
            (c, DescriptorKind.Schema, "C", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a, b, c }
        };

        var pack1 = _builder.Build(request, topology, descriptors);
        var pack2 = _builder.Build(request, topology, descriptors);

        pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
        pack1.Relationships.Should().Equal(pack2.Relationships);
        pack1.Diagnostics.Select(d => d.Code).Should().Equal(pack2.Diagnostics.Select(d => d.Code).ToArray());
    }

    [Fact]
    public void Shuffled_Input_Still_Deterministic()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");
        var c = new DescriptorRef("ns", "C");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Event, "B"), (c, DescriptorKind.Schema, "C") },
            NoEdges);

        var descriptors1 = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active),
            (c, DescriptorKind.Schema, "C", DescriptorState.Active));

        var descriptors2 = CreateDescriptors(
            (c, DescriptorKind.Schema, "C", DescriptorState.Active),
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Event, "B", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a, b, c }
        };

        var pack1 = _builder.Build(request, topology, descriptors1);
        var pack2 = _builder.Build(request, topology, descriptors2);

        pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
    }

    [Fact]
    public void Self_Cycle_Terminates()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] { (0, a, a, RelationshipKind.References, null, RelationshipStrength.Weak, false) });

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active));

        var recipe = new RuntimeScenarioRecipe
        {
            Name = "SelfLoop",
            Steps = new[]
            {
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.References,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    MaxDepth = 5
                }
            }
        };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { a },
            ScenarioRecipe = recipe
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Should().ContainSingle();
        pack.Descriptors[0].Ref.Should().Be(a);
    }

    [Fact]
    public void Builder_Is_Read_Only()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Schema, "B") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] { (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false) });

        var descriptorList = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Schema, "B", DescriptorState.Active));

        var nodeCountBefore = topology.NodeCount;
        var descCountBefore = descriptorList.Count;

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { a }
        };

        _builder.Build(request, topology, descriptorList);

        topology.NodeCount.Should().Be(nodeCountBefore);
        descriptorList.Count.Should().Be(descCountBefore);
    }

    [Fact]
    public void Request_Collections_Are_Snapshotted()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            NoEdges);

        var focusList = new List<DescriptorRef> { a };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = focusList
        };

        var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

        focusList.Add(new DescriptorRef("ns", "B"));

        pack.Request.FocusDescriptors.Should().HaveCount(1);
        pack.Request.FocusDescriptors[0].Should().Be(a);
    }

    // ── E. Optional Enrichment ──

    [Fact]
    public void Stable_Hashes_Omitted_By_Default()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            IncludeStableHashes = false
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.All(d => d.Hashes is null).Should().BeTrue();
    }

    [Fact]
    public void Stable_Hashes_Included_When_Requested()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            NoEdges);

        var testDesc = new TestDescriptor(a, DescriptorKind.Capability, "A", DescriptorState.Active);
        var descriptors = new List<IDescriptor> { testDesc };

        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes("contract", "definition"));

        var builder = new DefaultMetadataContextPackBuilder(hashBuilderMock.Object);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            IncludeStableHashes = true
        };

        var pack = builder.Build(request, topology, descriptors);

        pack.Descriptors.All(d => d.Hashes is not null).Should().BeTrue();
    }

    [Fact]
    public void Stable_Hashes_Not_Computed_When_Not_Requested()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            NoEdges);

        var testDesc = new TestDescriptor(a, DescriptorKind.Capability, "A", DescriptorState.Active);
        var descriptors = new List<IDescriptor> { testDesc };

        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes("contract", "definition"));

        var builder = new DefaultMetadataContextPackBuilder(hashBuilderMock.Object);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            IncludeStableHashes = false
        };

        _ = builder.Build(request, topology, descriptors);

        hashBuilderMock.Verify(h => h.Build(It.IsAny<IDescriptor>()), Times.Never);
    }

    [Fact]
    public void Governance_State_From_Descriptor_State_Only()
    {
        var active = new DescriptorRef("ns", "Active");
        var draft = new DescriptorRef("ns", "Draft");

        var topology = CreateSnapshot(
            new[] { (active, DescriptorKind.Capability, "Active", DescriptorState.Active), (draft, DescriptorKind.Capability, "Draft", DescriptorState.Draft) },
            NoEdges);

        var descriptors = CreateDescriptors(
            (active, DescriptorKind.Capability, "Active", DescriptorState.Active),
            (draft, DescriptorKind.Capability, "Draft", DescriptorState.Draft));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { active, draft },
            IncludeGovernanceState = true
        };

        var pack = _builder.Build(request, topology, descriptors);

        var activeEntry = pack.Descriptors.First(d => d.Ref.Equals(active));
        var draftEntry = pack.Descriptors.First(d => d.Ref.Equals(draft));

        activeEntry.Governance.Should().NotBeNull();
        activeEntry.Governance!.State.Should().Be(DescriptorState.Active);
        activeEntry.Governance.RequiresReview.Should().BeFalse();

        draftEntry.Governance.Should().NotBeNull();
        draftEntry.Governance!.State.Should().Be(DescriptorState.Draft);
        draftEntry.Governance.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Intent_Is_Ignored_In_Phase7b()
    {
        var a = new DescriptorRef("ns", "A");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active));

        var request1 = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            Intent = "I want to understand the capability"
        };

        var request2 = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { a },
            Intent = "Completely different intent"
        };

        var pack1 = _builder.Build(request1, topology, descriptors);
        var pack2 = _builder.Build(request2, topology, descriptors);

        pack1.Descriptors.Select(d => d.Ref).Should().Equal(pack2.Descriptors.Select(d => d.Ref).ToArray());
        pack1.Relationships.Should().Equal(pack2.Relationships);
    }

    // ── F. Version-Aware Regression ──

    [Fact]
    public void Versioned_Descriptor_Uses_Correct_Instance_For_Hashes()
    {
        // Two versions of the same descriptor (same Namespace/Id, different Version)
        var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
        var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);

        var topology = CreateSnapshot(
            new[] { (v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                    (v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
            NoEdges);

        var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
        var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

        // Track which descriptor instance the hash builder receives
        IDescriptor? hashBuilderReceived = null;
        var hashBuilderMock = new Mock<IDescriptorStableHashBuilder>();
        hashBuilderMock.Setup(h => h.Build(It.IsAny<IDescriptor>()))
            .Callback<IDescriptor>(d => hashBuilderReceived = d)
            .Returns(new DescriptorStableHashes("contract", "definition"));

        var builder = new DefaultMetadataContextPackBuilder(hashBuilderMock.Object);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { v2Ref },  // Focus on v2
            IncludeStableHashes = true
        };

        var pack = builder.Build(request, topology, descriptors);

        // Verify the hash builder received the v2 descriptor, not v1
        hashBuilderReceived.Should().NotBeNull();
        hashBuilderReceived.Should().BeSameAs(v2Desc);
        (hashBuilderReceived as IVersionedDescriptor)?.Version.Should().Be(2);

        // Verify the entry has the correct ref
        pack.Descriptors.Should().ContainSingle();
        pack.Descriptors[0].Ref.Should().Be(v2Ref);
    }

    // ── G. DescriptorSource Resolution ──

    [Fact]
    public void Fully_Resolved_Ref_Returns_Both_TopologyNode_And_Descriptor()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap") },
            NoEdges);

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Should().ContainSingle();
        pack.Descriptors[0].Ref.Should().Be(cap);
        pack.Descriptors[0].Kind.Should().Be(DescriptorKind.Capability);
        pack.Descriptors[0].Name.Should().Be("SubmitCap");
        pack.Diagnostics.Should().NotContain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef ||
            d.Code == MetadataContextPackDiagnosticCodes.TopologyNodeMissingForDescriptor);
    }

    [Fact]
    public void Topology_Only_Ref_Emits_DescriptorMissing_Error()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");

        // Node in topology but no matching descriptor in inventory
        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap") },
            NoEdges);

        // Empty inventory — no descriptors at all
        var descriptors = new List<IDescriptor>();

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Descriptor entry should NOT be fabricated from topology node
        pack.Descriptors.Should().BeEmpty();
        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef &&
            d.Severity == MetadataContextPackDiagnosticSeverity.Error);
    }

    [Fact]
    public void Inventory_Only_Ref_Emits_TopologyNodeMissing_Warning()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");

        // Empty topology — no nodes
        var topology = CreateSnapshot(
            Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
            NoEdges);

        // Descriptor exists in inventory
        var descriptors = new List<IDescriptor>
        {
            new InventoryOnlyDescriptor(cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active)
        };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Inventory-only focus should still be included (no traversal possible)
        pack.Descriptors.Should().ContainSingle();
        pack.Descriptors[0].Ref.Should().Be(cap);
        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.TopologyNodeMissingForDescriptor &&
            d.Severity == MetadataContextPackDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Neither_Topology_Nor_Inventory_Ref_Treated_As_FocusNotFound()
    {
        var missing = new DescriptorRef("ns", "Missing");

        var topology = CreateSnapshot(
            Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
            NoEdges);

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { missing }
        };

        var pack = _builder.Build(request, topology, Array.Empty<IDescriptor>());

        pack.Descriptors.Should().BeEmpty();
        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.FocusNotFound);
    }

    // ── H. Multi-Version Coexistence ──

    [Fact]
    public void MultiVersion_Focus_On_V2_Resolves_To_V2_Only()
    {
        var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
        var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);

        var topology = CreateSnapshot(
            new[] { (v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
                    (v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
            NoEdges);

        var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
        var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { v2Ref }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Should().ContainSingle();
        pack.Descriptors[0].Ref.Should().Be(v2Ref);
    }

    [Fact]
    public void MultiVersion_ImpactRadius_Traverses_Each_Version_Separately()
    {
        var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
        var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);
        var cap = new DescriptorRef("capability", "SubmitCap");
        var capV1 = new DescriptorRef("capability", "SubmitCap", 1);

        var topology = CreateSnapshot(
            new[] { (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                    (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                    (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, cap, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                (1, cap, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
            });

        var v1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
        var v2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
        var capDesc = new VersionedTestDescriptor(cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var descriptors = new List<IDescriptor> { v1Desc, v2Desc, capDesc };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.ImpactRadius,
            FocusDescriptors = new[] { cap },
            MaxTraversalDepth = 1
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Both v1 and v2 should appear — versions are not collapsed
        // Unpinned cap is canonicalized to v1; schemaV1/V2 already versioned
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(
            new[] { capV1, schemaV1, schemaV2 });
    }

    [Fact]
    public void MultiVersion_Kind_Filter_Applies_Per_Version()
    {
        var schemaV1 = new DescriptorRef("schema", "InputSchema", 1);
        var schemaV2 = new DescriptorRef("schema", "InputSchema", 2);
        var cap = new DescriptorRef("capability", "SubmitCap");
        var capV1 = new DescriptorRef("capability", "SubmitCap", 1);

        var topology = CreateSnapshot(
            new[] { (schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                    (schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
                    (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, cap, schemaV1, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                (1, cap, schemaV2, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
            });

        var v1Desc = new VersionedTestDescriptor(schemaV1, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
        var v2Desc = new VersionedTestDescriptor(schemaV2, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
        var capDesc = new VersionedTestDescriptor(cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var descriptors = new List<IDescriptor> { v1Desc, v2Desc, capDesc };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap },
            ExcludeKinds = new[] { DescriptorKind.Schema }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Both v1 and v2 Schema excluded by kind filter
        // Unpinned cap canonicalized to v1
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { capV1 });
    }

    [Fact]
    public void Unpinned_Ref_Single_Version_Resolves_To_Canonical_Versioned_Ref()
    {
        // Versioned descriptor in inventory, unpinned ref in focus
        var versionedRef = new DescriptorRef("capability", "SubmitCap", 1);
        var unpinnedRef = new DescriptorRef("capability", "SubmitCap");  // Version = null

        var topology = CreateSnapshot(
            new[] { (unpinnedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
            NoEdges);

        var v1Desc = new VersionedTestDescriptor(versionedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var descriptors = new List<IDescriptor> { v1Desc };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { unpinnedRef }
        };

        var pack = _builder.Build(request, topology, descriptors);

        pack.Descriptors.Should().ContainSingle();
        // Entry.Ref should be canonical versioned ref, not the unpinned input ref
        pack.Descriptors[0].Ref.Should().Be(versionedRef);
        pack.Descriptors[0].Ref.Version.Should().Be(1);
        pack.Diagnostics.Should().NotContain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef);
    }

    [Fact]
    public void Unpinned_Ref_Multiple_Versions_Emits_Ambiguous_Diagnostic()
    {
        var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
        var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);
        var unpinnedRef = new DescriptorRef("capability", "SubmitCap");  // Version = null

        var topology = CreateSnapshot(
            new[] { (unpinnedRef, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active) },
            NoEdges);

        var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
        var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { unpinnedRef }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Unpinned ref with multiple versions should emit ambiguous diagnostic
        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef);
        // No descriptor entry should be produced for the ambiguous ref
        pack.Descriptors.Should().BeEmpty();
    }

    [Fact]
    public void Inventory_Only_Unpinned_Focus_With_Multiple_Versions_Emits_Ambiguous()
    {
        // Topology has no node for this ref, but inventory has v1 and v2
        var v1Ref = new DescriptorRef("capability", "SubmitCap", 1);
        var v2Ref = new DescriptorRef("capability", "SubmitCap", 2);
        var unpinnedRef = new DescriptorRef("capability", "SubmitCap");

        var topology = CreateSnapshot(
            Array.Empty<(DescriptorRef, DescriptorKind, string)>(),
            NoEdges);

        var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 2);
        var descriptors = new List<IDescriptor> { v1Desc, v2Desc };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = new[] { unpinnedRef }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Should emit AMBIGUOUS, not FOCUS_NOT_FOUND
        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef);
        pack.Diagnostics.Should().NotContain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.FocusNotFound);
        pack.Descriptors.Should().BeEmpty();
    }

    [Fact]
    public void Traversal_Target_Unpinned_With_Multiple_Versions_Emits_Ambiguous()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");
        var capV1 = new DescriptorRef("capability", "SubmitCap", 1);
        var v1Ref = new DescriptorRef("schema", "InputSchema", 1);
        var v2Ref = new DescriptorRef("schema", "InputSchema", 2);
        // Topology node is unpinned — traversal discovers this ref
        var unpinnedSchema = new DescriptorRef("schema", "InputSchema");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                    (unpinnedSchema, DescriptorKind.Schema, "InputSchema") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, cap, unpinnedSchema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
            });

        // Inventory has v1 and v2 of the same schema — unpinned ref is ambiguous
        var capDesc = new VersionedTestDescriptor(cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active, 1);
        var v1Desc = new VersionedTestDescriptor(v1Ref, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 1);
        var v2Desc = new VersionedTestDescriptor(v2Ref, DescriptorKind.Schema, "InputSchema", DescriptorState.Active, 2);
        var descriptors = new List<IDescriptor> { capDesc, v1Desc, v2Desc };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Focus cap is fully resolved — included with canonical versioned ref
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { capV1 });
        // Traversal-discovered unpinned schema with v1+v2 emits AMBIGUOUS, not DESCRIPTOR_MISSING
        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.AmbiguousDescriptorRef);
        pack.Diagnostics.Should().NotContain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef);
        // Relationship excluded by pack closure (schema not in descriptor entries)
        pack.Relationships.Should().BeEmpty();
    }

    // ── I. Direction-Aware Traversal ──

    [Fact]
    public void DirectDependencies_Follows_Only_Outgoing_Edges()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");
        var schema = new DescriptorRef("schema", "InputSchema");
        var workflow = new DescriptorRef("workflow", "ApprovalWf");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                    (schema, DescriptorKind.Schema, "InputSchema"),
                    (workflow, DescriptorKind.Workflow, "ApprovalWf") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                // Outgoing from cap: cap → schema (Uses)
                (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                // Incoming to cap: workflow → cap (Triggers) — should NOT be followed
                (1, workflow, cap, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
            });

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
            (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
            (workflow, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Should include cap + schema (outgoing), but NOT workflow (incoming)
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, schema });
        pack.Relationships.Select(r => r.Kind).Should().BeEquivalentTo(new[] { RelationshipKind.Uses });
    }

    [Fact]
    public void DirectDependents_Follows_Only_Incoming_Edges()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");
        var schema = new DescriptorRef("schema", "InputSchema");
        var workflow = new DescriptorRef("workflow", "ApprovalWf");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                    (schema, DescriptorKind.Schema, "InputSchema"),
                    (workflow, DescriptorKind.Workflow, "ApprovalWf") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                // Outgoing from cap: cap → schema (Uses) — should NOT be followed
                (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                // Incoming to cap: workflow → cap (Triggers) — should be followed
                (1, workflow, cap, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
            });

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
            (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
            (workflow, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependents,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Should include cap + workflow (incoming), but NOT schema (outgoing)
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, workflow });
        pack.Relationships.Select(r => r.Kind).Should().BeEquivalentTo(new[] { RelationshipKind.Triggers });
    }

    [Fact]
    public void RuntimeScenario_Both_Follows_Outgoing_Then_Incoming()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");
        var schema = new DescriptorRef("schema", "InputSchema");
        var workflow = new DescriptorRef("workflow", "ApprovalWf");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                    (schema, DescriptorKind.Schema, "InputSchema"),
                    (workflow, DescriptorKind.Workflow, "ApprovalWf") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                (1, workflow, cap, RelationshipKind.Triggers, null, RelationshipStrength.Strong, true)
            });

        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active),
            (schema, DescriptorKind.Schema, "InputSchema", DescriptorState.Active),
            (workflow, DescriptorKind.Workflow, "ApprovalWf", DescriptorState.Active));

        var recipe = new RuntimeScenarioRecipe
        {
            Name = "BothDirections",
            Steps = new[]
            {
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.Uses,
                    Direction = ScenarioTraversalDirection.Both,
                    MaxDepth = 1
                }
            }
        };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { cap },
            ScenarioRecipe = recipe
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Step follows Uses in Both direction:
        // Outgoing: cap → schema (Uses) → included
        // Incoming: workflow → cap (Triggers) → NOT included (Kind=Triggers ≠ FollowKind=Uses)
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap, schema });
    }

    [Fact]
    public void RuntimeScenario_NextStep_Uses_Only_PreviousStep_DiscoveredNodes()
    {
        // Workflow → HumanTask (Triggers), Workflow → Schema (Uses)
        // HumanTask → CapabilityA (Triggers, Outcome)
        // Workflow also → CapabilityA (Uses) — different FollowKind, so only reachable in step 2
        //
        // Step 1: FollowKind=Triggers from Workflow → discovers HumanTask
        //   (Workflow→Schema is Uses, not Triggers — skipped)
        //   (Workflow→CapabilityA is Uses, not Triggers — skipped)
        // Step 2: FollowKind=Uses from {HumanTask only} → discovers nothing
        //   If boundary were {Workflow + HumanTask}, Workflow→CapabilityA (Uses) would be included
        //
        var workflow = new DescriptorRef("workflow", "Wf");
        var humanTask = new DescriptorRef("humantask", "Ht");
        var schema = new DescriptorRef("schema", "Schema");
        var capFromWf = new DescriptorRef("capability", "CapFromWf");

        var topology = CreateSnapshot(
            new[] { (workflow, DescriptorKind.Workflow, "Wf"),
                    (humanTask, DescriptorKind.HumanTask, "Ht"),
                    (schema, DescriptorKind.Schema, "Schema"),
                    (capFromWf, DescriptorKind.Capability, "CapFromWf") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                // Step 1 matches: Workflow → HumanTask (Triggers)
                (0, workflow, humanTask, RelationshipKind.Triggers, "HumanTaskStep", RelationshipStrength.Strong, true),
                // Step 1 skips: Workflow → Schema (Uses, not Triggers)
                (1, workflow, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                // Step 2 would match if Workflow were in boundary: Workflow → CapFromWf (Uses)
                (2, workflow, capFromWf, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
            });

        var descriptors = CreateDescriptors(
            (workflow, DescriptorKind.Workflow, "Wf", DescriptorState.Active),
            (humanTask, DescriptorKind.HumanTask, "Ht", DescriptorState.Active),
            (schema, DescriptorKind.Schema, "Schema", DescriptorState.Active),
            (capFromWf, DescriptorKind.Capability, "CapFromWf", DescriptorState.Active));

        var recipe = new RuntimeScenarioRecipe
        {
            Name = "StrictStepBoundary",
            Steps = new[]
            {
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.Triggers,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    MaxDepth = 1
                },
                new ScenarioTraversalStep
                {
                    FollowKind = RelationshipKind.Uses,
                    Direction = ScenarioTraversalDirection.Dependencies,
                    MaxDepth = 1
                }
            }
        };

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = new[] { workflow },
            ScenarioRecipe = recipe
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Step 1 (Triggers): Workflow → HumanTask. Boundary for step 2 = {HumanTask}
        // Step 2 (Uses): HumanTask has no Uses edges → nothing discovered
        // If boundary were {Workflow + HumanTask}, Workflow→CapFromWf (Uses) would be included
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(
            new[] { workflow, humanTask });
        // Only 1 relationship: Workflow → HumanTask (Triggers)
        // NOT Workflow→Schema or Workflow→CapFromWf
        pack.Relationships.Should().ContainSingle();
        pack.Relationships[0].From.Should().Be(workflow);
        pack.Relationships[0].To.Should().Be(humanTask);
    }

    [Fact]
    public void ImpactRadius_Bidirectional_BFS_With_Self_Cycle_Terminates()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");

        // A self-references and A → B
        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Schema, "B") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, a, a, RelationshipKind.References, null, RelationshipStrength.Weak, false),
                (1, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
            });

        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Schema, "B", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.ImpactRadius,
            FocusDescriptors = new[] { a },
            MaxTraversalDepth = 5
        };

        var pack = _builder.Build(request, topology, descriptors);

        // Self-cycle should not cause infinite loop
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { a, b });
        // No truncation diagnostic — graph is fully explored
        pack.Diagnostics.Should().NotContain(d => d.Code == MetadataContextPackDiagnosticCodes.TruncatedByDepth);
    }

    // ── J. Pack Closure Invariant ──

    [Fact]
    public void Missing_Inventory_Descriptor_Excludes_Relationship()
    {
        var cap = new DescriptorRef("capability", "SubmitCap");
        var schema = new DescriptorRef("schema", "InputSchema");

        var topology = CreateSnapshot(
            new[] { (cap, DescriptorKind.Capability, "SubmitCap"),
                    (schema, DescriptorKind.Schema, "InputSchema") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, cap, schema, RelationshipKind.Uses, null, RelationshipStrength.Strong, false)
            });

        // Only cap descriptor in inventory — schema is missing
        var descriptors = CreateDescriptors(
            (cap, DescriptorKind.Capability, "SubmitCap", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { cap }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // cap entry exists, schema entry is excluded (no descriptor in inventory)
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { cap });
        // Relationship should be excluded by pack closure invariant (schema endpoint missing from descriptors)
        pack.Relationships.Should().BeEmpty();
        pack.Diagnostics.Should().Contain(d =>
            d.Code == MetadataContextPackDiagnosticCodes.DescriptorMissingForTopologyRef);
    }

    [Fact]
    public void Mixed_Resolved_Unresolved_Endpoints_Only_Fully_Contained_Relationships()
    {
        var a = new DescriptorRef("ns", "A");
        var b = new DescriptorRef("ns", "B");
        var c = new DescriptorRef("ns", "C");

        var topology = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"),
                    (b, DescriptorKind.Schema, "B"),
                    (c, DescriptorKind.Event, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength, bool)[] {
                (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong, false),
                (1, a, c, RelationshipKind.Produces, null, RelationshipStrength.Weak, false)
            });

        // Only A and B descriptors in inventory — C is missing
        var descriptors = CreateDescriptors(
            (a, DescriptorKind.Capability, "A", DescriptorState.Active),
            (b, DescriptorKind.Schema, "B", DescriptorState.Active));

        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.DirectDependencies,
            FocusDescriptors = new[] { a }
        };

        var pack = _builder.Build(request, topology, descriptors);

        // A and B entries exist, C excluded
        pack.Descriptors.Select(d => d.Ref).Should().BeEquivalentTo(new[] { a, b });
        // A→B relationship preserved (both endpoints in descriptor set)
        // A→C relationship excluded (C endpoint missing from descriptor set)
        pack.Relationships.Should().ContainSingle();
        pack.Relationships[0].From.Should().Be(a);
        pack.Relationships[0].To.Should().Be(b);
    }

    private sealed class InventoryOnlyDescriptor : IDescriptor
    {
        private readonly DescriptorRef _ref;
        public InventoryOnlyDescriptor(DescriptorRef ref_, DescriptorKind kind, string name, DescriptorState state)
        {
            _ref = ref_; Kind = kind; Name = name; State = state;
        }
        public string Namespace => _ref.Namespace;
        public string Id => _ref.Id;
        public string Name { get; }
        public string FullId => $"{Namespace}.{Id}";
        public DescriptorKind Kind { get; }
        public DescriptorState State { get; }
        public string ContractHash => "";
        public string DefinitionHash => "";
        public string? SupersededById => null;
    }

    private sealed class VersionedTestDescriptor : IVersionedDescriptor
    {
        private readonly DescriptorRef _ref;
        public VersionedTestDescriptor(DescriptorRef ref_, DescriptorKind kind, string name, DescriptorState state, int version)
        {
            _ref = ref_; Kind = kind; Name = name; State = state; Version = version;
        }
        public string Namespace => _ref.Namespace;
        public string Id => _ref.Id;
        public string Name { get; }
        public string FullId => $"{Namespace}.{Id}";
        public DescriptorKind Kind { get; }
        public DescriptorState State { get; }
        public int Version { get; }
        public string ContractHash => "";
        public string DefinitionHash => "";
        public string? SupersededById => null;
    }
}
