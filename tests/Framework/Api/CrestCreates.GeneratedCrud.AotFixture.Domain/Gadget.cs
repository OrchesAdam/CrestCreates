using System;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.GeneratedCrud.AotFixture.Domain;

/// <summary>
/// #128 R1 dual-feature fixture aggregate: the second entity of the second generated Feature, used to
/// prove two independently generated assemblies activate together (shared void/error JSON roots) in
/// both activation orders, with per-feature requests and responses. Intentionally minimal — identity,
/// name, price and the CreationTime backing the adapter's default ordering.
/// </summary>
public class Gadget : IEntity<Guid>
{
    public Gadget()
    {
    }

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public DateTime CreationTime { get; set; }
}
