namespace LibraryManagement.Application.Contracts.DTOs;

public partial class LoanDto
{
    public string? BookTitle { get; set; }
    public string? BookISBN { get; set; }
    public string? MemberName { get; set; }
    public string? MemberEmail { get; set; }
    public int OverdueDays { get; set; }
}
