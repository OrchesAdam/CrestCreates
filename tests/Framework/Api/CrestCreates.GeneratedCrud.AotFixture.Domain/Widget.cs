using System;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Shared.Entities.Auditing;

namespace CrestCreates.GeneratedCrud.AotFixture.Domain;

/// <summary>
/// #128 S4 fixture aggregate root. It is declared against the real framework contracts only:
/// <see cref="IEntity{TId}"/> for identity and <see cref="IHasConcurrencyStamp"/> so the generated
/// CRUD surface carries the real concurrency contract. The public parameterless constructor and the
/// public setters keep the frozen mapper closure satisfiable, and <c>CreationTime</c> backs the typed
/// query adapter's default ordering.
/// </summary>
public class Widget : IEntity<Guid>, IHasConcurrencyStamp
{
    public Widget()
    {
    }

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string? Description { get; set; }

    public DateTime CreationTime { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;
}
