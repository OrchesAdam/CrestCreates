using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.DescriptorImpact;

public sealed class DescriptorChangeSetBuilder : IDescriptorChangeSetBuilder
{
    private readonly IDescriptorStableHashBuilder _hashBuilder;

    public DescriptorChangeSetBuilder(IDescriptorStableHashBuilder hashBuilder)
    {
        _hashBuilder = hashBuilder;
    }

    public DescriptorChangeSet Build(
        IReadOnlyList<IDescriptor> before,
        IReadOnlyList<IDescriptor> after)
    {
        var beforeByRef = before.ToDictionary(
            d => new DescriptorRef(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version));

        var changes = new List<DescriptorChange>();

        foreach (var d in after)
        {
            var refKey = new DescriptorRef(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version);
            var afterHashes = _hashBuilder.Build(d);

            if (!beforeByRef.TryGetValue(refKey, out var beforeDesc))
            {
                changes.Add(new DescriptorChange
                {
                    Ref = refKey,
                    Kind = DescriptorChangeKind.Added,
                    AfterState = d.State,
                    AfterContractHash = afterHashes.ContractHash.Value,
                    AfterDefinitionHash = afterHashes.DefinitionHash.Value
                });
                continue;
            }

            var beforeHashes = _hashBuilder.Build(beforeDesc);

            var beforeState = beforeDesc.State;
            var afterState = d.State;

            DescriptorChangeKind kind;
            if (afterState == DescriptorState.Removed && beforeState != DescriptorState.Removed)
                kind = DescriptorChangeKind.Removed;
            else if (afterState == DescriptorState.Deprecated && beforeState != DescriptorState.Deprecated)
                kind = DescriptorChangeKind.Deprecated;
            else if (afterState == DescriptorState.Active && beforeState == DescriptorState.Draft)
                kind = DescriptorChangeKind.Activated;
            else if (beforeState != afterState)
                kind = DescriptorChangeKind.StateChanged;
            else if (afterHashes.ContractHash != beforeHashes.ContractHash)
                kind = DescriptorChangeKind.ContractHashChanged;
            else if (afterHashes.DefinitionHash != beforeHashes.DefinitionHash)
                kind = DescriptorChangeKind.DefinitionHashChanged;
            else if (d.Name != beforeDesc.Name)
                kind = DescriptorChangeKind.Updated;
            else
                continue;

            changes.Add(new DescriptorChange
            {
                Ref = refKey,
                Kind = kind,
                BeforeState = beforeState,
                AfterState = afterState,
                BeforeContractHash = beforeHashes.ContractHash.Value,
                AfterContractHash = afterHashes.ContractHash.Value,
                BeforeDefinitionHash = beforeHashes.DefinitionHash.Value,
                AfterDefinitionHash = afterHashes.DefinitionHash.Value
            });
        }

        // Removed: in before but not in after
        var afterRefs = after.Select(d =>
            new DescriptorRef(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version))
            .ToHashSet();

        foreach (var kv in beforeByRef)
        {
            if (!afterRefs.Contains(kv.Key))
            {
                var beforeHashes = _hashBuilder.Build(kv.Value);
                changes.Add(new DescriptorChange
                {
                    Ref = kv.Key,
                    Kind = DescriptorChangeKind.Removed,
                    BeforeState = kv.Value.State,
                    BeforeContractHash = beforeHashes.ContractHash.Value,
                    BeforeDefinitionHash = beforeHashes.DefinitionHash.Value
                });
            }
        }

        var deduped = DeduplicateByPriority(changes);
        return new DescriptorChangeSet { Changes = deduped };
    }

    private static IReadOnlyList<DescriptorChange> DeduplicateByPriority(List<DescriptorChange> changes)
    {
        var result = new Dictionary<DescriptorRef, DescriptorChange>();
        foreach (var c in changes)
        {
            if (!result.TryGetValue(c.Ref, out var existing) || Priority(c.Kind) < Priority(existing.Kind))
                result[c.Ref] = c;
        }
        return result.Values.ToList().AsReadOnly();
    }

    private static int Priority(DescriptorChangeKind kind) => kind switch
    {
        DescriptorChangeKind.Removed => 1,
        DescriptorChangeKind.Deprecated => 2,
        DescriptorChangeKind.StateChanged => 3,
        DescriptorChangeKind.ContractHashChanged => 4,
        DescriptorChangeKind.DefinitionHashChanged => 5,
        DescriptorChangeKind.Updated => 6,
        DescriptorChangeKind.Added => 7,
        DescriptorChangeKind.Activated => 8,
        _ => int.MaxValue
    };
}
