namespace CrestCreates.Agent.Tools;

public sealed class AgentToolConfigurationException : Exception
{
    public AgentToolConfigurationException(string code, string message)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A diagnostic code is required.", nameof(code));

        Code = code;
    }

    public AgentToolConfigurationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A diagnostic code is required.", nameof(code));

        Code = code;
    }

    public string Code { get; }
}
