using CrestCreates.Domain.Shared.Entities.Auditing;

namespace SaaSHelpdesk.Application.Contracts.DTOs;

/// <summary>
/// The generated UpdateTicketDto carries the client's concurrency stamp. Opting into the typed input
/// contract lets the generic CrestAppServiceBase compare it with the stored entity stamp.
/// </summary>
public partial class UpdateTicketDto : IHasConcurrencyStampInput;
