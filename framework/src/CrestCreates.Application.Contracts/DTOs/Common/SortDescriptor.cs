namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// 排序描述符
/// </summary>
public class SortDescriptor
{
    /// <summary>
    /// 排序字段名称
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 排序方向
    /// </summary>
    public SortDirection Direction { get; set; } = SortDirection.Ascending;

    /// <summary>
    /// 创建排序描述符实例
    /// </summary>
    public SortDescriptor()
    {
    }

    /// <summary>
    /// 创建排序描述符实例
    /// </summary>
    /// <param name="field">排序字段名称</param>
    /// <param name="direction">排序方向</param>
    public SortDescriptor(string field, SortDirection direction = SortDirection.Ascending)
    {
        Field = field;
        Direction = direction;
    }
}
