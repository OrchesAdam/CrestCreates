using CrestCreates.Application.Contracts.DTOs.Common;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Application.Contracts.DTOs;

public class LoanSearchRequest : PagedRequestDto
{
    public Guid? MemberId { get; set; }
    public Guid? BookId { get; set; }
    public bool? IsReturned { get; set; }
    public bool? IsOverdue { get; set; }
    public LoanStatus? Status { get; set; }
}
