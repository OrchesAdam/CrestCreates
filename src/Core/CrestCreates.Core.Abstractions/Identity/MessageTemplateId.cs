namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct MessageTemplateId
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public MessageTemplateId(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Message template id cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Message template id is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(MessageTemplateId id) => id.Value ?? string.Empty;
}
