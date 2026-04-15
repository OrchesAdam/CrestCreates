using System;

namespace CrestCreates.Scheduling.Jobs;

public readonly record struct JobId
{
    public string? Name { get; init; }
    public string? Group { get; init; }
    public Guid Uuid { get; init; }

    public JobId(Guid uuid)
    {
        Uuid = uuid;
        Name = null;
        Group = null;
    }

    public JobId(string name, string group, Guid uuid)
    {
        Name = name;
        Group = group;
        Uuid = uuid;
    }

    public static JobId New() => new(Guid.NewGuid());
    public static JobId Create(string name, string group = "Default") => new(name, group, Guid.Empty);

    public override string ToString()
        => Uuid != Guid.Empty ? Uuid.ToString() : $"{Group}/{Name}";
}
