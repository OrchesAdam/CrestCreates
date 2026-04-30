using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Domain.Converters;

public static class BookStatusToStringConverter
{
    public static string Convert(BookStatus status) => status switch
    {
        BookStatus.Available => "可借",
        BookStatus.Borrowed => "已借出",
        BookStatus.Reserved => "已预约",
        BookStatus.Maintenance => "维护中",
        BookStatus.Lost => "已遗失",
        _ => "未知"
    };
}
