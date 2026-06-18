using System;

namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// 为枚举值指定显示名称。编译时源生成器将其处理为 switch 表达式，零反射，AOT 安全。
/// </summary>
/// <example>
/// public enum BookStatus
/// {
///     [EnumDisplay(Name = "可借")]
///     Available = 0,
///
///     [EnumDisplay(Name = "已借出")]
///     Borrowed = 1
/// }
///
/// // 生成: status.GetDisplayName() => "可借" / "已借出"
/// </example>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EnumDisplayAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;

    public EnumDisplayAttribute() { }

    public EnumDisplayAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}
