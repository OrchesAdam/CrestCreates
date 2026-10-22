using SaaSHelpdesk.Application.Validators;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// The formal precompile discovers typed validators in the HTTP owner assembly or the body's
/// assembly. The real Ticket validators live in SaaSHelpdesk.Application (neither of those), so the
/// fixture exposes thin closed validators that inherit the real rules. No rule is re-implemented.
/// </summary>
public sealed class FixtureCreateTicketDtoValidator : CreateTicketDtoValidator;

public sealed class FixtureUpdateTicketDtoValidator : UpdateTicketDtoValidator;
