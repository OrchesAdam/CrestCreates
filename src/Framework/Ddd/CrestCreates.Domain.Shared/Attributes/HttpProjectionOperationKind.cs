namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// Query or command semantics of one projected action. Declared explicitly because a GET route can
/// still be a command (the frozen Ticket compatibility routes), so the verb alone never decides.
/// </summary>
public enum HttpProjectionOperationKind
{
    Unspecified = 0,
    Query = 1,
    Command = 2,
}
