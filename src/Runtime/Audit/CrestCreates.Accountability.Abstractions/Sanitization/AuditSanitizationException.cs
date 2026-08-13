namespace CrestCreates.Accountability.Abstractions.Sanitization;

public sealed class AuditSanitizationException : Exception
{
    public AuditSanitizationException(string code, string path)
        : base(code)
    {
        Code = code;
        Path = path;
    }

    public AuditSanitizationException(string code, string path, Exception innerException)
        : base(code, innerException)
    {
        Code = code;
        Path = path;
    }

    public string Code { get; }

    public string Path { get; }
}
