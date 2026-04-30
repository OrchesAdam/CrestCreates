using CrestCreates.Domain.Shared.Attributes;

namespace LibraryManagement.Domain.Shared.Enums;

public enum MemberType
{
    [EnumDisplay(Name = "普通会员")]
    Regular = 0,

    [EnumDisplay(Name = "学生")]
    Student = 1,

    [EnumDisplay(Name = "教师")]
    Teacher = 2,

    [EnumDisplay(Name = "工作人员")]
    Staff = 3,

    [EnumDisplay(Name = "VIP")]
    VIP = 4
}
