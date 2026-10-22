using CrestCreates.Authorization.Abstractions;

namespace SaaSHelpdesk.Application.Authorization;

/// <summary>
/// Issue #129 S3: the five formal Ticket permissions frozen by the design. The capability endpoint
/// metadata and the inherited CrestAppServiceBase checks use exactly these names.
/// </summary>
public sealed class TicketPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public const string Create = "Ticket.Create";
    public const string Get = "Ticket.Get";
    public const string Search = "Ticket.Search";
    public const string Update = "Ticket.Update";
    public const string Delete = "Ticket.Delete";

    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull("Ticket") ?? context.AddGroup("Ticket", "Ticket");
        group.AddPermission(Create, "Create tickets");
        group.AddPermission(Get, "Read a ticket");
        group.AddPermission(Search, "List and search tickets");
        group.AddPermission(Update, "Update tickets");
        group.AddPermission(Delete, "Delete tickets");
    }
}
