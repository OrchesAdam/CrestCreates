using CrestCreates.Domain.Shared.Attributes;

namespace LibraryManagement.Domain.Shared.Enums;

public enum BookStatus
{
    [EnumDisplay(Name = "可借")]
    Available = 0,

    [EnumDisplay(Name = "已借出")]
    Borrowed = 1,

    [EnumDisplay(Name = "已预约")]
    Reserved = 2,

    [EnumDisplay(Name = "维护中")]
    Maintenance = 3,

    [EnumDisplay(Name = "已遗失")]
    Lost = 4
}
