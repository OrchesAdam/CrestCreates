# 对象映射系统扩展：支持 AutoMapper 级别的配置映射（修订版）

## 修订说明

基于原始计划的分析，修正了两个架构问题：

1. **EntitySourceGenerator 与 RuleResolver 集成** —— 不再强行统一源侧/目标侧属性解析，改为新增独立的源侧解析方法
2. **导航路径类型兼容性检查** —— 改为根据导航链最终段属性类型做兼容性检查

---

## 1. 新增 [MapConvert] 特性（已存在，确认即可）

**文件**: `framework/src/CrestCreates.Domain.Shared/ObjectMapping/MapConvertAttribute.cs`

文件已存在于 untracked files 中。约定：`ConverterType` 必须是一个静态类，包含 `static TResult Convert(TSource value)` 方法。

## 2. 扩展 PropertyMapping 模型

**文件**: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingModel.cs`

将 `PropertyMapping` 从 `sealed class` 改为 `record`（或保持 class，按现有风格），增加字段：

```csharp
internal sealed class PropertyMapping
{
    public IPropertySymbol SourceProperty { get; set; } = null!;
    public IPropertySymbol? TargetProperty { get; set; }           // null for source-side resolution (no target symbol)
    public string TargetPropertyName { get; set; } = string.Empty; // NEW: independent from TargetProperty
    public bool IsIgnored { get; set; }
    public bool IsReadOnly { get; set; }
    public string? CustomSourceName { get; set; }
    public bool NeedsNullCheck { get; set; }
    public bool NeedsCollectionConversion { get; set; }
    public string? CollectionConversionMethod { get; set; }
    public string? SourceNavigationPath { get; set; }              // NEW: "Category.Name"
    public List<string>? NavigationSegments { get; set; }          // NEW: ["Category", "Name"]
    public string? ConverterTypeFullName { get; set; }             // NEW: "MyNamespace.EnumToStringConverter"
}
```

`TargetPropertyName` 与 `TargetProperty.Name` 的区别：当 target 属性有 Roslyn 符号时两者一致；当没有 target 符号（EntitySourceGenerator 路径）时，`TargetProperty` 为 null，`TargetPropertyName` 承载属性名字符串。

## 3. 扩展 ObjectMappingRuleResolver

**文件**: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingRuleResolver.cs`

### 3a. 修改 FindAllMatchingSourceProperties — 返回 PropertyMapping 列表

将返回值从 `List<IPropertySymbol>` 改为 `List<PropertyMapping>`，在方法内部直接构建 `PropertyMapping`（携带导航段、转换器等元数据）。

当 `[MapFrom]` 的值包含 `.` 时（如 `"Category.Name"`）：

```csharp
if (mapFromValue.Contains('.'))
{
    var segments = mapFromValue.Split('.').ToList();
    var firstProp = sourceProperties.FirstOrDefault(p => p.Name == segments[0]);
    if (firstProp == null)
    {
        // 返回空列表，由调用方报诊断
        return new List<PropertyMapping>();
    }

    // 逐段验证导航链
    var valid = true;
    INamedTypeSymbol? currentType = firstProp.Type as INamedTypeSymbol;
    for (int i = 1; i < segments.Count && valid; i++)
    {
        if (currentType == null) { valid = false; break; }
        var segmentProp = currentType.GetMembers(segments[i])
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic);
        if (segmentProp == null) { valid = false; break; }
        currentType = segmentProp.Type as INamedTypeSymbol;
    }

    if (!valid)
    {
        // 报诊断：导航路径中间段不存在
        return new List<PropertyMapping>();
    }

    var mapping = new PropertyMapping
    {
        SourceProperty = firstProp,
        TargetProperty = targetProp,
        TargetPropertyName = targetProp.Name,
        SourceNavigationPath = mapFromValue,
        NavigationSegments = segments,
    };
    return new List<PropertyMapping> { mapping };
}
```

### 3b. 新增 FindConverterForProperty 方法

```csharp
private string? FindConverterForProperty(IPropertySymbol property)
{
    var attr = property.GetAttributes().FirstOrDefault(a =>
        a.AttributeClass != null && (
            a.AttributeClass.Name == "MapConvertAttribute" ||
            a.AttributeClass.Name == "MapConvert" ||
            a.AttributeClass.ToDisplayString().EndsWith(".MapConvertAttribute") ||
            a.AttributeClass.ToDisplayString().EndsWith(".MapConvert")));

    if (attr?.ConstructorArguments.Length > 0)
    {
        return (attr.ConstructorArguments[0].Value as INamedTypeSymbol)?.ToDisplayString();
    }
    return null;
}
```

在 `FindAllMatchingSourceProperties` 中对每个匹配调用此方法，设置 `ConverterTypeFullName`。

### 3c. 修改 CreateValidMapping — 导航路径类型检查

当 `mapping.NavigationSegments != null && mapping.NavigationSegments.Count > 1` 时，用导航链**最终段**属性类型做类型兼容性检查，而非 `sourceProp.Type`：

```csharp
ITypeSymbol effectiveSourceType = sourceProp.Type;

if (mapping.NavigationSegments != null && mapping.NavigationSegments.Count > 1)
{
    var finalType = ResolveFinalSegmentType(sourceProp, mapping.NavigationSegments);
    if (finalType != null)
        effectiveSourceType = finalType;
}

if (!IsTypeCompatible(effectiveSourceType, targetProp.Type, out var needsNullCheck,
        out var collectionConversion, out var incompatibleElementTypes))
{
    // ... existing error handling
}
```

新增辅助方法：

```csharp
private static ITypeSymbol? ResolveFinalSegmentType(IPropertySymbol firstProp, List<string> segments)
{
    INamedTypeSymbol? currentType = firstProp.Type as INamedTypeSymbol;
    for (int i = 1; i < segments.Count; i++)
    {
        if (currentType == null) return null;
        var prop = currentType.GetMembers(segments[i])
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic);
        if (prop == null) return null;
        if (i == segments.Count - 1) return prop.Type;
        currentType = prop.Type as INamedTypeSymbol;
    }
    return null;
}
```

### 3d. 新增 ResolvePropertyMappingsFromSource — 源侧属性解析

这是为 EntitySourceGenerator 路径提供的新入口方法。从**源类型**属性上读取 `[MapIgnore]` / `[MapName]` / `[MapConvert]` / `[MapFrom]`：

```csharp
/// <summary>
/// 从源类型属性解析映射关系。用于目标类型无 Roslyn 符号的场景（如同一次编译中生成的 DTO）。
/// 属性标注在源侧：[MapIgnore]、[MapName]、[MapConvert]、[MapFrom]
/// </summary>
public List<PropertyMapping> ResolvePropertyMappingsFromSource(
    INamedTypeSymbol sourceType,
    IEnumerable<string> targetPropertyNames,
    SourceProductionContext? context = null)
{
    var sourceProperties = sourceType.GetMembers()
        .OfType<IPropertySymbol>()
        .Where(p => !p.IsStatic && p.CanBeReferencedByName)
        .ToList();

    var mappings = new List<PropertyMapping>();
    var targetNameSet = new HashSet<string>(targetPropertyNames);

    foreach (var sourceProp in sourceProperties)
    {
        // 检查源侧 [MapIgnore]
        if (HasMapIgnoreAttribute(sourceProp))
            continue;

        // 确定目标属性名：[MapName] > 同名
        var targetName = GetMapNameAttribute(sourceProp) ?? sourceProp.Name;

        // 检查源侧 [MapFrom]（在源侧语义为"映射到目标属性X"）
        var mapFrom = GetMapFromAttributeName(sourceProp);
        if (mapFrom != null)
            targetName = mapFrom;

        if (!targetNameSet.Contains(targetName))
        {
            // 目标属性名不在 DTO 属性列表中，跳过（可能是 DTO 生成阶段已排除的审计属性等）
            // 也可以报 warning
            continue;
        }

        var mapping = new PropertyMapping
        {
            SourceProperty = sourceProp,
            TargetProperty = null, // 无目标符号
            TargetPropertyName = targetName,
            ConverterTypeFullName = FindConverterForProperty(sourceProp),
        };

        // 处理导航路径（源侧 [MapFrom("Category.Name")]）
        if (mapFrom != null && mapFrom.Contains('.'))
        {
            var segments = mapFrom.Split('.').ToList();
            // 第一个段必须是源属性的某个导航属性... 
            // 注：在源侧语境下，MapFrom 的值如果是导航路径，语义为"用导航路径的值映射到目标属性"
            // 但第一段在源侧语境下 = 当前正在处理的 sourceProp 本身
            // 这种场景更适合用在 ObjectMappingSourceGenerator 路径（目标侧标注）
            // 源侧标注导航路径的用例较少，但保留可能性
            mapping.SourceNavigationPath = mapFrom;
            mapping.NavigationSegments = segments;
        }

        mappings.Add(mapping);
    }

    return mappings;
}
```

**设计决策**：源侧解析和目标侧解析是**两个独立方法**，各自处理各自的属性标注方向。共享底层的类型兼容性检查、诊断基础设施，但属性扫描逻辑分开。这样两条路径互不拖累。

## 4. 扩展 ObjectMappingCodeWriter

**文件**: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingCodeWriter.cs`

### 4a. 修改 GetPropertyAssignmentExpression

```csharp
private string GetPropertyAssignmentExpression(PropertyMapping mapping)
{
    string sourceExpression;

    if (mapping.NavigationSegments != null && mapping.NavigationSegments.Count > 1)
    {
        sourceExpression = BuildNavigationExpression(mapping);
    }
    else
    {
        sourceExpression = $"source.{mapping.SourceProperty.Name}";
    }

    // 自定义转换
    if (mapping.ConverterTypeFullName != null)
    {
        sourceExpression = $"{mapping.ConverterTypeFullName}.Convert({sourceExpression})";
    }

    // 集合转换
    if (mapping.NeedsCollectionConversion && mapping.CollectionConversionMethod != null)
    {
        sourceExpression = $"{sourceExpression}.{mapping.CollectionConversionMethod}";
    }

    // Null 检查
    if (mapping.NeedsNullCheck)
    {
        var targetTypeName = mapping.TargetProperty?.Type.ToDisplayString()
            ?? mapping.TargetPropertyName;
        var defaultValue = GetDefaultValue(targetTypeName);
        return $"{sourceExpression} ?? {defaultValue}";
    }

    return sourceExpression;
}
```

### 4b. 新增 BuildNavigationExpression

```csharp
private string BuildNavigationExpression(PropertyMapping mapping)
{
    var segments = mapping.NavigationSegments!;
    var parts = new List<string>();

    // 第一段：source.PropName（引用类型也加 ?. 避免 NRE）
    var firstProp = mapping.SourceProperty;
    bool firstIsNullable = firstProp.Type.IsReferenceType
        || (firstProp.Type is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
    parts.Add($"source.{segments[0]}");

    INamedTypeSymbol? currentType = firstProp.Type as INamedTypeSymbol;
    for (int i = 1; i < segments.Count; i++)
    {
        if (currentType == null) break;
        bool isRefType = currentType.IsReferenceType;
        string op = isRefType ? "?." : ".";
        parts.Add($"{op}{segments[i]}");

        var segmentProp = currentType.GetMembers(segments[i])
            .OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic);
        currentType = segmentProp?.Type as INamedTypeSymbol;
    }

    return string.Concat(parts);
}
```

### 4c. Expression 树中跳过不支持的特性

`ToTargetExpression` 中，对 `NavigationSegments != null` 或 `ConverterTypeFullName != null` 的映射，跳过该属性赋值。这避免了 `?.` 和静态方法调用无法在 Expression 树中表达的问题。

## 5. 重构 EntitySourceGenerator.GenerateMappingExtensions

**文件**: `framework/tools/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`

### 5a. 核心策略

DTO 类型在同一次编译中生成，无 Roslyn 符号可用。使用**源侧属性解析**：

1. 用 `GetAllEntityProperties(entityClass)` 获取实体属性列表
2. 派生 DTO 属性名列表（与 DTO 生成逻辑一致：排除审计属性）
3. 调用 `ResolvePropertyMappingsFromSource` 获取带特性的映射列表
4. 用映射列表生成 ToDto()、ApplyTo() 代码

### 5b. 重写后的 GenerateMappingExtensions

```csharp
private void GenerateMappingExtensions(SourceProductionContext context, INamedTypeSymbol entityClass)
{
    var entityName = entityClass.Name;
    var namespaceName = entityClass.ContainingNamespace.ToDisplayString();
    var dtosNamespace = GetTargetNamespace(namespaceName, GeneratedCodeType.Dto);
    var idType = GetEntityIdType(entityClass);

    // 1. 获取实体属性
    var properties = GetAllEntityProperties(entityClass);

    // 2. 派生 DTO 属性名列表（与 DTO 生成逻辑一致）
    var dtoPropertyNames = properties
        .Where(p => p.Name != "CreationTime" && p.Name != "LastModificationTime"
            && p.Name != "CreatorId" && p.Name != "LastModifierId")
        .Select(p => p.Name)
        .ToHashSet();

    // 3. 源侧属性解析（从实体属性上读取 [MapIgnore]/[MapName]/[MapConvert]）
    var resolver = new ObjectMappingRuleResolver();
    var mappings = resolver.ResolvePropertyMappingsFromSource(entityClass, dtoPropertyNames, context);

    // 4. 过滤掉 IsIgnored 的映射，按 Id 优先排序
    var activeMappings = mappings
        .Where(m => !m.IsIgnored)
        .OrderBy(m => m.TargetPropertyName == "Id" ? 0 : 1)
        .ThenBy(m => m.TargetPropertyName)
        .ToList();

    // 5. 判断 Id 是否可写
    var hasWritableId = entityClass.GetMembers().OfType<IPropertySymbol>()
        .Any(p => p.Name == "Id" && p.SetMethod != null
            && p.SetMethod.DeclaredAccessibility == Accessibility.Public);

    // 6. 生成代码...
    var builder = new StringBuilder();
    builder.AppendLine("#nullable enable");
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("using System;");
    builder.AppendLine("using System.Linq.Expressions;");
    builder.AppendLine($"using {namespaceName};");
    builder.AppendLine($"using {dtosNamespace};");
    builder.AppendLine();
    builder.AppendLine($"namespace {namespaceName}.Extensions");
    builder.AppendLine("{");
    builder.AppendLine($"    public static partial class {entityName}MappingExtensions");
    builder.AppendLine("    {");

    // --- ToDto() ---
    WriteToDtoMethod(builder, entityName, activeMappings);

    // --- CreateXxxDto.ApplyTo() ---
    WriteApplyToMethod(builder, entityName, "Create", activeMappings, writableId: false);

    // --- UpdateXxxDto.ApplyTo() ---
    WriteApplyToMethod(builder, entityName, "Update", activeMappings, hasWritableId);

    // --- ToDtoExpression ---
    WriteToDtoExpressionMethod(builder, entityName, activeMappings);

    // --- Partial hook declarations ---
    WritePartialHookDeclarations(builder, entityName);

    builder.AppendLine("    }");
    builder.AppendLine("}");

    context.AddSource($"{entityName}MappingExtensions.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
}
```

### 5c. 各生成方法的实现

```csharp
private void WriteToDtoMethod(StringBuilder builder, string entityName, List<PropertyMapping> mappings)
{
    builder.AppendLine($"        public static {entityName}Dto ToDto(this {entityName} source)");
    builder.AppendLine("        {");
    builder.AppendLine("            if (source is null)");
    builder.AppendLine("                throw new ArgumentNullException(nameof(source));");
    builder.AppendLine();
    builder.AppendLine($"            var result = new {entityName}Dto");
    builder.AppendLine("            {");

    for (int i = 0; i < mappings.Count; i++)
    {
        var mapping = mappings[i];
        var comma = i < mappings.Count - 1 ? "," : "";
        var expr = GetPropertyAssignmentExpression(mapping);
        builder.AppendLine($"                {mapping.TargetPropertyName} = {expr}{comma}");
    }

    builder.AppendLine("            };");
    builder.AppendLine();
    builder.AppendLine("            AfterToDto(source, result);");
    builder.AppendLine("            return result;");
    builder.AppendLine("        }");
    builder.AppendLine();
}

private void WriteApplyToMethod(StringBuilder builder, string entityName,
    string dtoPrefix, List<PropertyMapping> mappings, bool writableId)
{
    var dtoName = $"{dtoPrefix}{entityName}Dto";

    builder.AppendLine($"        public static void ApplyTo(this {dtoName} source, {entityName} destination)");
    builder.AppendLine("        {");
    builder.AppendLine("            if (source is null)");
    builder.AppendLine("                throw new ArgumentNullException(nameof(source));");
    builder.AppendLine("            if (destination is null)");
    builder.AppendLine("                throw new ArgumentNullException(nameof(destination));");
    builder.AppendLine();
    builder.AppendLine("            BeforeApplyTo(source, destination);");
    builder.AppendLine();

    // Id（仅 Update 且 Id 可写时）
    if (writableId && dtoPrefix == "Update")
    {
        builder.AppendLine("            destination.Id = source.Id;");
    }

    // 仅映射有 public setter 的属性（排除审计属性和 Id）
    var excluded = new HashSet<string> { "Id", "CreationTime", "LastModificationTime", "CreatorId", "LastModifierId" };
    foreach (var mapping in mappings.Where(m => !excluded.Contains(m.TargetPropertyName)))
    {
        var expr = GetPropertyAssignmentExpression(mapping);
        // ApplyTo 中 source 是 DTO
        builder.AppendLine($"            destination.{mapping.TargetPropertyName} = source.{mapping.TargetPropertyName};");
    }

    builder.AppendLine();
    builder.AppendLine("            AfterApplyTo(source, destination);");
    builder.AppendLine("        }");
    builder.AppendLine();
}

private void WriteToDtoExpressionMethod(StringBuilder builder, string entityName, List<PropertyMapping> mappings)
{
    // 跳过不支持 Expression 的特性
    var exprSafeMappings = mappings
        .Where(m => m.NavigationSegments == null && m.ConverterTypeFullName == null)
        .ToList();

    builder.AppendLine($"        public static Expression<Func<{entityName}, {entityName}Dto>> ToDtoExpression =>");
    builder.AppendLine($"            source => new {entityName}Dto");
    builder.AppendLine("            {");

    for (int i = 0; i < exprSafeMappings.Count; i++)
    {
        var mapping = exprSafeMappings[i];
        var comma = i < exprSafeMappings.Count - 1 ? "," : "";
        builder.AppendLine($"                {mapping.TargetPropertyName} = source.{mapping.SourceProperty.Name}{comma}");
    }

    builder.AppendLine("            };");
    builder.AppendLine();
}

private void WritePartialHookDeclarations(StringBuilder builder, string entityName)
{
    builder.AppendLine($"        static partial void AfterToDto({entityName} source, {entityName}Dto destination);");
    builder.AppendLine($"        static partial void BeforeApplyTo(Create{entityName}Dto source, {entityName} destination);");
    builder.AppendLine($"        static partial void AfterApplyTo(Create{entityName}Dto source, {entityName} destination);");
    builder.AppendLine($"        static partial void BeforeApplyTo(Update{entityName}Dto source, {entityName} destination);");
    builder.AppendLine($"        static partial void AfterApplyTo(Update{entityName}Dto source, {entityName} destination);");
}
```

**注意**：ApplyTo 中不直接使用 `GetPropertyAssignmentExpression`（因为 source 是 DTO 而非 entity），但映射列表中的 `TargetPropertyName` 就是 DTO/Entity 的属性名，所以直接用 `source.{TargetPropertyName}` 即可。

## 6. 诊断信息扩展

**文件**: `framework/tools/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingDiagnostics.cs`

新增诊断：

| ID | Title | Severity |
|----|-------|----------|
| OM010 | Navigation segment not found | Error |
| OM011 | Navigation segment is not a property | Error |
| OM012 | Invalid converter type | Error |

## 7. CrudServiceSourceGenerator 无需修改

`entity.ToDto()` 和 `input.ApplyTo(entity)` 的调用签名不变，自动获得新功能。

---

## 8. 验证清单

1. `dotnet build framework/tools/CrestCreates.CodeGenerator/` — CodeGenerator 编译通过
2. `dotnet build samples/LibraryManagement/LibraryManagement.Domain/` — 生成文件正确
3. 检查生成的 `*MappingExtensions.g.cs` 包含：
   - 类声明为 `public static partial class`
   - `AfterToDto` / `BeforeApplyTo` / `AfterApplyTo` partial 方法声明
   - 调用 partial hook 的代码
4. 在 Book 实体上测试 `[MapIgnore]` / `[MapName]` / `[MapConvert]` 特性
5. 测试 partial hook 自定义实现
6. `dotnet build CrestCreates.slnx` — 全量编译 0 错误
7. 现有测试全部通过

---

## 9. 样本代码（LibraryManagement 项目）

以下样本代码展示四个新特性的实际用法。

### 9a. 实体层：Book.cs 增加映射特性

**文件**: `samples/LibraryManagement/LibraryManagement.Domain/Entities/Book.cs`

在现有属性上增加特性标注（示意）：

```csharp
using CrestCreates.Domain.Shared.ObjectMapping;

[Entity]
public class Book : AuditedEntity<Guid>
{
    // ... 现有属性 ...

    public BookStatus Status { get; set; }    // 无变化，通过 hook 手动映射文本

    [MapIgnore]                               // ← 新增：排除 Location 不生成到 DTO
    public string? Location { get; set; }

    // ... 其余属性不变 ...
}
```

**设计说明**：在 Book 实体上只用 `[MapIgnore]` 演示源侧标注。`Status → StatusDisplay` 的转换用 partial hook 演示（比自动转换更灵活，展示 hook 的价值）。

### 9b. DTO 扩展：BookDto.extended.cs（新建）

**文件**: `samples/LibraryManagement/LibraryManagement.Domain/DTOs/BookDto.extended.cs`

```csharp
namespace LibraryManagement.Application.Contracts.DTOs;

public partial class BookDto
{
    /// <summary>
    /// 分类名称（从 Category.Name 导航路径获取）
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
```

### 9c. 转换器：BookStatusToStringConverter.cs（新建）

**文件**: `samples/LibraryManagement/LibraryManagement.Domain/Converters/BookStatusToStringConverter.cs`

```csharp
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
```

### 9d. 映射 Hook：BookMappingExtensions.custom.cs（新建）

**文件**: `samples/LibraryManagement/LibraryManagement.Domain/Extensions/BookMappingExtensions.custom.cs`

```csharp
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Domain.Converters;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Domain.Entities.Extensions;

public static partial class BookMappingExtensions
{
    static partial void AfterToDto(Book source, BookDto destination)
    {
        // 导航属性映射：从 Category.Name 获取分类名称
        destination.CategoryName = source.Category?.Name;

        // 自定义转换：BookStatus → 中文显示
        destination.StatusDisplay = BookStatusToStringConverter.Convert(source.Status);

        // 计算属性：组合展示标题
        destination.DisplayTitle = $"{source.Title} ({source.Author})";
    }
}
```

### 9e. 生成结果验证

运行 `dotnet build samples/LibraryManagement/LibraryManagement.Domain/` 后，生成的 `BookMappingExtensions.g.cs` 应呈现为：

```csharp
namespace LibraryManagement.Domain.Entities.Extensions
{
    public static partial class BookMappingExtensions
    {
        public static BookDto ToDto(this Book source)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var result = new BookDto
            {
                Id = source.Id,
                Title = source.Title,
                Author = source.Author,
                ISBN = source.ISBN,
                Description = source.Description,
                PublishDate = source.PublishDate,
                Publisher = source.Publisher,
                Status = source.Status,
                CategoryId = source.CategoryId,
                Category = source.Category,
                TotalCopies = source.TotalCopies,
                AvailableCopies = source.AvailableCopies
                // Location 被排除（[MapIgnore]）
            };

            AfterToDto(source, result);  // ← partial hook 调用
            return result;
        }

        // ... ApplyTo 方法同理，包含 BeforeApplyTo/AfterApplyTo ...

        static partial void AfterToDto(Book source, BookDto destination);
        static partial void BeforeApplyTo(CreateBookDto source, Book destination);
        static partial void AfterApplyTo(CreateBookDto source, Book destination);
        static partial void BeforeApplyTo(UpdateBookDto source, Book destination);
        static partial void AfterApplyTo(UpdateBookDto source, Book destination);
    }
}
```

### 9f. ObjectMappingSourceGenerator 路径的导航路径演示（可选，独立映射场景）

如果在某个手写的 `partial static class` 上使用 `[GenerateObjectMapping]`，且 DTO 属性标注了 `[MapFrom("Category.Name")]`：

```csharp
// 假设有一个独立的手写映射类
[GenerateObjectMapping(typeof(Book), typeof(BookDetailDto))]
internal static partial class BookToBookDetailMapper { }

// BookDetailDto 中：
public class BookDetailDto
{
    [MapFrom("Category.Name")]
    public string? CategoryName { get; set; }
}
```

则 `ObjectMappingSourceGenerator` 生成的代码会包含：

```csharp
result.CategoryName = source.Category?.Name,
```

这是 ObjectMappingCodeWriter 的 `BuildNavigationExpression` 自动生成的（对应计划的 4b 部分）。

### 9g. 文件清单

| 操作 | 文件 |
|------|------|
| 新建 | `samples/LibraryManagement/LibraryManagement.Domain/DTOs/BookDto.extended.cs` |
| 新建 | `samples/LibraryManagement/LibraryManagement.Domain/Converters/BookStatusToStringConverter.cs` |
| 新建 | `samples/LibraryManagement/LibraryManagement.Domain/Extensions/BookMappingExtensions.custom.cs` |
| 修改 | `samples/LibraryManagement/LibraryManagement.Domain/Entities/Book.cs` |
```

