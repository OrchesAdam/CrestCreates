# 对象映射系统使用指南

CrestCreates 对象映射系统提供编译时代码生成能力，替代传统的运行时 AutoMapper 映射。支持特性驱动的属性匹配、导航路径映射、自定义值转换和 partial hook 扩展点。

## 目录

- [快速开始](#快速开始)
- [映射特性](#映射特性)
- [Partial Hook 扩展](#partial-hook-扩展)
- [自定义转换器](#自定义转换器)
- [枚举显示名称](#枚举显示名称)
- [导航路径映射](#导航路径映射)
- [ObjectMappingSourceGenerator 路径](#objectmappingsourcegenerator-路径)
- [限制与注意事项](#限制与注意事项)
- [LibraryManagement 完整示例](#librarymanagement-完整示例)

## 快速开始

对象映射系统有两条生成路径，根据场景自动选择：

**路径 1：EntitySourceGenerator（实体 → DTO 自动映射）**

当实体类标注 `[Entity]` 时，源生成器自动生成 `{EntityName}MappingExtensions` 静态扩展类，提供：

- `entity.ToDto()` — 实体 → DTO 映射（创建新实例）
- `createDto.ApplyTo(entity)` — Create DTO → 实体（属性赋值）
- `updateDto.ApplyTo(entity)` — Update DTO → 实体（属性赋值）
- `ToDtoExpression` — LINQ 投影表达式

**路径 2：ObjectMappingSourceGenerator（任意两个类型间映射）**

在 `partial static class` 上标注 `[GenerateObjectMapping]`，源生成器生成对应映射方法。

## 映射特性

所有特性位于 `CrestCreates.Domain.Shared.ObjectMapping` 命名空间。

### `[MapIgnore]`

排除属性，不参与自动映射。

**标注位置：**

| 场景 | 标注位置 | 效果 |
|------|---------|------|
| EntitySourceGenerator（实体 → DTO） | 实体属性 | 该属性不出现在生成的 DTO 映射中 |
| ObjectMappingSourceGenerator（两类型映射） | 目标类型属性 | 该属性不参与映射 |

**示例（实体侧）：**

```csharp
using CrestCreates.Domain.Shared.ObjectMapping;

[Entity]
public class Book : AuditedEntity<Guid>
{
    public string Title { get; set; }
    public string Author { get; set; }

    [MapIgnore]
    public string? Location { get; set; }  // 不会映射到 DTO
}
```

生成的 `ToDto()` 中将不包含 `Location = source.Location`。

**示例（目标侧）：**

```csharp
public class BookDetailDto
{
    public string Title { get; set; }

    [MapIgnore]
    public string InternalNotes { get; set; }  // 不从源类型映射
}
```

### `[MapName]`

当源属性名和目标属性名不同时，显式指定源属性名。

**标注位置：** 目标类型属性（ObjectMappingSourceGenerator 路径）或源类型属性（EntitySourceGenerator 路径）。

**示例（目标侧）：**

```csharp
public class BookDto
{
    [MapName("ISBN")]
    public string IsbnCode { get; set; }  // 从源属性 "ISBN" 映射
}
```

生成代码：`IsbnCode = source.ISBN`

**示例（源侧，EntitySourceGenerator 路径）：**

```csharp
[Entity]
public class Book : AuditedEntity<Guid>
{
    [MapName("BookTitle")]
    public string Title { get; set; }  // 映射到目标属性 "BookTitle"
}
```

生成的 DTO 属性名将使用 `BookTitle` 而非 `Title`。

### `[MapFrom]`

显式指定源属性名。与 `[MapName]` 功能类似，但支持导航路径（见下文）。

**示例：**

```csharp
public class BookDetailDto
{
    [MapFrom("Author")]
    public string Writer { get; set; }  // 从源属性 "Author" 映射
}
```

### `[MapConvert]`

指定自定义值转换器。转换器必须是静态类，包含 `static TResult Convert(TSource value)` 方法。

> 对于枚举 → 字符串场景，推荐使用 [`[EnumDisplay]`](#枚举显示名称) 替代手写转换器，零反射且维护性更好。

**示例：**

```csharp
public class BookDto
{
    [MapConvert(typeof(DateTimeToStringConverter))]
    public string PublishDateDisplay { get; set; }
}

public static class DateTimeToStringConverter
{
    public static string Convert(DateTime value) => value.ToString("yyyy-MM-dd");
}
```

生成代码：`PublishDateDisplay = DateTimeToStringConverter.Convert(source.PublishDate)`

**标注位置：** 目标类型属性（ObjectMappingSourceGenerator 路径）或源类型属性（EntitySourceGenerator 路径）。

## Partial Hook 扩展

EntitySourceGenerator 生成的 `{Entity}MappingExtensions` 类声明了 5 个 partial 方法，用户在单独文件中实现以注入自定义逻辑。

### 可用 Hook

| Hook | 签名 | 调用时机 |
|------|------|---------|
| `AfterToDto` | `(Entity source, EntityDto destination)` | ToDto() 属性赋值完成后，return 前 |
| `BeforeApplyTo(Create)` | `(CreateEntityDto source, Entity destination)` | CreateDto.ApplyTo() 属性赋值前 |
| `AfterApplyTo(Create)` | `(CreateEntityDto source, Entity destination)` | CreateDto.ApplyTo() 属性赋值后 |
| `BeforeApplyTo(Update)` | `(UpdateEntityDto source, Entity destination)` | UpdateDto.ApplyTo() 属性赋值前 |
| `AfterApplyTo(Update)` | `(UpdateEntityDto source, Entity destination)` | UpdateDto.ApplyTo() 属性赋值后 |

### 实现方式

在同一个 namespace 下创建 partial 类的实现文件：

```csharp
// BookMappingExtensions.custom.cs
namespace LibraryManagement.Domain.Entities.Extensions;

public static partial class BookMappingExtensions
{
    static partial void AfterToDto(Book source, BookDto destination)
    {
        // 导航属性映射
        destination.CategoryName = source.Category?.Name;

        // 枚举显示名称（源生成器生成，零反射）
        destination.StatusDisplay = source.Status.GetDisplayName();

        // 计算属性
        destination.DisplayTitle = $"{source.Title} ({source.Author})";
    }
}
```

### 典型用途

- **导航属性映射：** 将 `source.Category?.Name` 赋值到 DTO 的 `CategoryName`
- **计算属性：** 组合多个字段生成展示文本
- **枚举 → 字符串：** 通过 `GetDisplayName()` 一行搞定
- **关联数据填充：** 需要外部数据源（如查库）才能填充的属性
- **BeforeApplyTo：** 在 Apply 前做校验或修改 source 值

## 自定义转换器

`[MapConvert]` 用于非枚举的自定义类型转换场景（如 `DateTime → string` 格式化、数值单位换算等）。转换为**静态类**，提供 `static TResult Convert(TSource value)` 静态方法。

### 使用方式

**方式 1：特性标注（自动生成）**

```csharp
[MapConvert(typeof(DateTimeToStringConverter))]
public string PublishDateDisplay { get; set; }

// 生成: PublishDateDisplay = DateTimeToStringConverter.Convert(source.PublishDate)
```

**方式 2：在 partial hook 中手动调用**

```csharp
static partial void AfterToDto(Book source, BookDto destination)
{
    destination.PublishDateDisplay = DateTimeToStringConverter.Convert(source.PublishDate);
}
```

### 选择建议

| 场景 | 推荐方式 |
|------|---------|
| 简单的 1:1 类型转换 | `[MapConvert]` 特性 |
| 枚举 → 字符串 | 使用 [`[EnumDisplay]`](#枚举显示名称)（零反射，AOT 安全） |
| 需要组合多个源属性 | `AfterToDto` hook |
| 需要访问外部服务/数据库 | `AfterToDto` hook |

### 约定

转换器必须是**静态类**，包含 `static TResult Convert(TSource value)` 方法。

```csharp
public static class DateTimeToStringConverter
{
    public static string Convert(DateTime value) => value.ToString("yyyy-MM-dd");
}
```

## 枚举显示名称

枚举 → 字符串转换是最常见的映射场景。CrestCreates 提供 `[EnumDisplay]` + 源生成器方案，**零反射，完全 AOT 兼容**。

### 与反射方案、转换器方案的对比

| 方案 | AOT | 维护性 | 示例 |
|------|-----|--------|------|
| `DescriptionAttribute` + 反射 | 不支持 | 字符串紧贴枚举值 | `status.GetDescription()` |
| `[MapConvert]` + 手写转换器 | 支持 | 枚举值与显示字符串分两处 | `XxxConverter.Convert(status)` |
| **`[EnumDisplay]` + 源生成器** | **支持** | **字符串紧贴枚举值** | `status.GetDisplayName()` |

### 用法

**Step 1：在枚举值上标注**

```csharp
using CrestCreates.Domain.Shared.Attributes;

public enum BookStatus
{
    [EnumDisplay(Name = "可借")]
    Available = 0,

    [EnumDisplay(Name = "已借出")]
    Borrowed = 1,

    [EnumDisplay(Name = "已预约")]
    Reserved = 2
}
```

> 也支持 `[EnumDisplay("可借")]` 位置参数语法，效果相同。

**Step 2：编译时自动生成**

EnumDisplaySourceGenerator 在编译时扫描所有带 `[EnumDisplay]` 的枚举，生成 `GetDisplayName()` 扩展方法：

```csharp
// 自动生成：BookStatusDisplayExtensions.g.cs
public static partial class BookStatusDisplayExtensions
{
    public static string GetDisplayName(this BookStatus value) => value switch
    {
        BookStatus.Available   => "可借",
        BookStatus.Borrowed    => "已借出",
        BookStatus.Reserved    => "已预约",
        _                      => value.ToString()
    };
}
```

**Step 3：在映射中使用**

```csharp
// AfterToDto hook 中调用
static partial void AfterToDto(Book source, BookDto destination)
{
    destination.StatusDisplay = source.Status.GetDisplayName();
}
```

### 原理

`EnumDisplaySourceGenerator`（`IIncrementalGenerator`）在编译时遍历当前项目的所有枚举类型，对带有 `[EnumDisplay]` 的成员生成 switch 表达式。无反射、无运行时遍历、无装箱——就是简单的静态方法调用。

## 导航路径映射

当源类型通过导航属性关联到另一类型的属性时，使用 `[MapFrom]` 的点号语法。

### 语法

```csharp
// DTO 定义
public class BookDto
{
    [MapFrom("Category.Name")]
    public string? CategoryName { get; set; }
}
```

生成代码：

```csharp
result.CategoryName = source.Category?.Name;
```

### 规则

- 路径以 `.` 分隔，第一段必须是源类型上的属性名
- 中间段的引用类型自动使用 `?.` 空传播
- 值类型段使用 `.` 直接访问
- 路径段在编译时验证：无效段会触发 OM010 诊断错误

### 多级导航

```csharp
[MapFrom("Category.Parent.Name")]
public string? ParentCategoryName { get; set; }

// 生成: source.Category?.Parent?.Name
```

### 限制

- 导航路径映射**不支持** `ToDtoExpression`（Expression 树中无法使用 `?.`）。带有导航路径的属性不会出现在 `ToDtoExpression` 生成的属性列表中。
- 在 EntitySourceGenerator 路径中，导航路径标注应在目标（DTO）侧。若在源（实体）侧使用 `[MapFrom("Category.Name")]`，导航路径语义与目标侧相同——Generator 仍会正确解析段并生成 `?.` 链，只是第一段属性取自源类型直接成员。

## ObjectMappingSourceGenerator 路径

适用于任意两个已有类型之间的映射（非实体 → DTO 场景）。

### 声明映射

```csharp
using CrestCreates.Domain.Shared.ObjectMapping;

[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
internal static partial class BookToBookDtoMapper { }
```

属性在**目标类型**（`BookDto`）的属性上标注。

### 生成的方法

| `MapDirection` | 生成的方法 |
|----------------|-----------|
| `Create`（默认） | `ToTarget(TSource)` + `ToTargetExpression` |
| `Apply` | `Apply(TSource, TDestination)` |
| `Both` | 以上全部 + `AfterToTarget` / `BeforeApply` / `AfterApply` partial hooks |

### 与 EntitySourceGenerator 的关系

`EntitySourceGenerator` 生成的 `*ObjectMappings.g.cs` 中已自动包含 `[GenerateObjectMapping]` 声明类。因此以下声明**无需手写**：

```csharp
// 以下由 EntitySourceGenerator 自动生成，无需手写
[GenerateObjectMapping(typeof(Book), typeof(BookDto))]
public static partial class BookToBookDtoMapper { }

[GenerateObjectMapping(typeof(UpdateBookDto), typeof(Book), Direction = MapDirection.Apply)]
public static partial class UpdateBookDtoToBookMapper { }
```

仅在需要映射两个**非实体类型**时，才需要手动声明 `[GenerateObjectMapping]`。

## 限制与注意事项

### Expression 树限制

以下特性**不支持** `ToDtoExpression`（LINQ 投影）：

- 导航路径映射（`[MapFrom("Category.Name")]`）
- 自定义转换器（`[MapConvert]`）

这些属性在 `ToDtoExpression` 中会被**跳过**，不会出现在生成的属性列表中。若查询中需要这些字段，请在 `Select()` 中手动追加赋值，或使用 `ToDto()` + 内存后续操作。

### EntitySourceGenerator 路径的属性标注方向

| 特性 | 标注位置 | 支持的路径 |
|------|---------|-----------|
| `[MapIgnore]` | 实体属性 | EntitySourceGenerator |
| `[MapName]` | 实体属性 | EntitySourceGenerator |
| `[MapConvert]` | 实体属性 | EntitySourceGenerator |
| `[MapFrom]` | 实体或 DTO 属性 | 两个路径均可 |
| `[MapFrom("Nav.Path")]` | DTO 属性 | ObjectMappingSourceGenerator |

> 原因：EntitySourceGenerator 在同一编译中生成 DTO，DTO 无 Roslyn 符号，无法读取 DTO 属性上的特性。因此 `[MapIgnore]`、`[MapName]`、`[MapConvert]` 必须标注在**实体属性**上。

### 转换器验证

`[MapConvert]` 的类型验证仅对**已编译的程序集中的类型**生效。若转换器类位于当前正在编译的项目中，编译时无法完全验证其是否符合约定（静态类 + Convert 方法）。建议将转换器放在编译顺序靠前的项目中。

## LibraryManagement 完整示例

该示例展示了一个实体映射的完整生命周期：特性标注 → 自动生成 → partial hook 补充。

### 文件清单

```
LibraryManagement.Domain/
├── Entities/
│   └── Book.cs                          # 实体类，标注 [MapIgnore]
├── DTOs/
│   └── BookDto.extended.cs              # DTO 扩展属性（partial class）
└── Extensions/
    └── BookMappingExtensions.custom.cs  # partial hook 实现

LibraryManagement.Domain.Shared/
└── Enums/
    └── BookStatus.cs                    # 枚举，标注 [EnumDisplay]
```

### Step 1：实体定义

```csharp
// Book.cs
using CrestCreates.Domain.Shared.ObjectMapping;

[Entity]
public class Book : AuditedEntity<Guid>
{
    public string Title { get; set; }
    public string Author { get; set; }
    public string ISBN { get; set; }
    public BookStatus Status { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; }

    [MapIgnore]  // 不映射到 DTO
    public string? Location { get; set; }
}
```

### Step 2：DTO 扩展

```csharp
// BookDto.extended.cs
namespace LibraryManagement.Application.Contracts.DTOs;

public partial class BookDto
{
    public string? CategoryName { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
}
```

### Step 3：枚举显示名称

```csharp
// BookStatus.cs（位于 Domain.Shared 项目）
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
```

编译时 `EnumDisplaySourceGenerator` 自动生成 `GetDisplayName()` 扩展方法——零反射，AOT 安全。

### Step 4：Partial Hook

```csharp
// BookMappingExtensions.custom.cs
namespace LibraryManagement.Domain.Entities.Extensions;

public static partial class BookMappingExtensions
{
    static partial void AfterToDto(Book source, BookDto destination)
    {
        destination.CategoryName = source.Category?.Name;
        destination.StatusDisplay = source.Status.GetDisplayName();
        destination.DisplayTitle = $"{source.Title} ({source.Author})";
    }
}
```

### Step 5：生成结果

编译后产生两份生成文件：

**BookMappingExtensions.g.cs**（EntitySourceGenerator）：

```csharp
public static partial class BookMappingExtensions
{
    public static BookDto ToDto(this Book source)
    {
        // ... null check ...
        var result = new BookDto
        {
            Id = source.Id,
            Title = source.Title,
            Author = source.Author,
            // ... 其他实体属性（Location 已被 MapIgnore 排除）
        };

        AfterToDto(source, result);  // ← 调用 partial hook
        return result;
    }

    static partial void AfterToDto(Book source, BookDto destination);
    // ... BeforeApplyTo / AfterApplyTo × Create + Update
}
```

**BookStatusDisplayExtensions.g.cs**（EnumDisplaySourceGenerator）：

```csharp
public static partial class BookStatusDisplayExtensions
{
    public static string GetDisplayName(this BookStatus value) => value switch
    {
        BookStatus.Available   => "可借",
        BookStatus.Borrowed    => "已借出",
        BookStatus.Reserved    => "已预约",
        BookStatus.Maintenance => "维护中",
        BookStatus.Lost        => "已遗失",
        _                      => value.ToString()
    };
}
```

### Step 6：业务代码使用

```csharp
// BookAppService.cs
protected override BookDto MapToDto(Book entity)
    => entity.ToDto();  // 自动映射 + AfterToDto hook
```

业务代码无需感知 hook 的存在——`entity.ToDto()` 调用自动携带 `AfterToDto` 的全部逻辑。
