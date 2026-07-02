namespace CrestCreates.Agent.Prompting.Abstractions;

public readonly record struct AgentPromptTemplateId
{
    public AgentPromptTemplateId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptVersion
{
    public AgentPromptVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptContractVersion
{
    public AgentPromptContractVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptModelProfileRef
{
    public AgentPromptModelProfileRef(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct AgentPromptProviderProfileRef
{
    public AgentPromptProviderProfileRef(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
