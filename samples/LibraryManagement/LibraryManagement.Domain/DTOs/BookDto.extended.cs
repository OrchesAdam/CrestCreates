namespace LibraryManagement.Application.Contracts.DTOs;

public partial class BookDto
{
    /// <summary>
    /// 分类名称（从 Category.Navigation 属性映射）
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// 状态的可读文本（从 BookStatus 枚举转换）
    /// </summary>
    public string StatusDisplay { get; set; } = string.Empty;

    /// <summary>
    /// 展示用标题（Title + Author 组合）
    /// </summary>
    public string DisplayTitle { get; set; } = string.Empty;
}
