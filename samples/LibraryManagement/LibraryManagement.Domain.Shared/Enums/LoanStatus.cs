using CrestCreates.Domain.Shared.Attributes;

namespace LibraryManagement.Domain.Shared.Enums;

public enum LoanStatus
{
    [EnumDisplay(Name = "借阅中")]
    Active = 0,

    [EnumDisplay(Name = "已归还")]
    Returned = 1,

    [EnumDisplay(Name = "已逾期")]
    Overdue = 2,

    [EnumDisplay(Name = "已遗失")]
    Lost = 3
}
