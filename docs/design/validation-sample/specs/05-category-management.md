# Spec: Category Management

## 概述

工单分类采用自引用树形结构，支持多级分类。分类树将被高频读取（工单创建选择分类、工单列表过滤），需要缓存优化。

## 实体

```csharp
[Entity]
public class Category : AuditedEntity<Guid>, ISoftDelete
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }        // 1-50字符
    public string Description { get; private set; }
    public Guid? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }     // Soft Delete

    // 导航属性
    public virtual Category Parent { get; private set; }
    public virtual ICollection<Category> Children { get; private set; }

    // 领域方法
    public void SetParent(Guid? parentId);          // 防止循环引用
    public void Activate();
    public void Deactivate();
    public void Reorder(int sortOrder);
}
```

## 约束

- 循环引用检测：`SetParent` 时检查新父节点是否是自己的子孙
- Soft Delete：删除时 `IsDeleted = true`，不影响历史工单分类引用
- 停用：`IsActive = false`，新建工单不可选，但不影响历史工单

## API

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `GET` | `/api/categories` | Tickets | 分类列表（平铺，含ParentId用于前端构建树） |
| `GET` | `/api/categories/tree` | Tickets | 分类树（嵌套结构，从缓存读取） |
| `GET` | `/api/categories/{id}` | Tickets | 分类详情 |
| `POST` | `/api/categories` | Categories.Manage | 创建分类 |
| `PUT` | `/api/categories/{id}` | Categories.Manage | 更新分类 |
| `DELETE` | `/api/categories/{id}` | Categories.Manage | 删除分类 (Soft Delete) |
| `PUT` | `/api/categories/{id}/move/{newParentId}` | Categories.Manage | 移动分类到新父节点 |

### DTO 示例

```csharp
// CreateCategoryDto
{
    "name": "账号问题",
    "description": "登录、注册、密码重置相关问题",
    "parentId": null,
    "sortOrder": 1
}

// CategoryTreeDto (树形)
{
    "id": "guid-root",
    "name": "全部",
    "sortOrder": 0,
    "children": [
        {
            "id": "guid-1",
            "name": "账号问题",
            "sortOrder": 1,
            "children": [
                { "id": "guid-1-1", "name": "登录", "sortOrder": 1, "children": [] },
                { "id": "guid-1-2", "name": "密码重置", "sortOrder": 2, "children": [] }
            ]
        },
        {
            "id": "guid-2",
            "name": "功能问题",
            "sortOrder": 2,
            "children": []
        }
    ]
}
```

## 缓存策略

```csharp
// 分类树缓存
[CacheMo(CacheName = "CategoryTree", ExpirationMinutes = 30)]
public virtual async Task<List<CategoryTreeDto>> GetTreeAsync()
{
    // 从DB加载全部分类并构建树
}

// 主动失效：修改分类时
public async Task<CategoryDto> UpdateAsync(...)
{
    await ICrestCacheService.RemoveAsync("CategoryTree");
    // ... 更新操作
}
```

## 验证检查点

- [ ] 分类树 API 首次请求查DB，后续命中缓存
- [ ] 更新/删除/新增分类后缓存失效
- [ ] 移动分类到自己的子节点时抛出异常（循环引用检测）
- [ ] 删除分类后 `IsDeleted = true`，查询不返回
- [ ] 历史工单的分类引用不受 Soft Delete 影响
- [ ] 停用分类后新建工单不可选
- [ ] `SortOrder` 正确排序

## FK 策略说明

`Ticket.CategoryId` 的 FK 配置为 `OnDelete(DeleteBehavior.SetNull)`。由于 Category 使用 Soft Delete（仅标记 `IsDeleted = true`，不物理删除行），**此 FK 行为仅在物理 DELETE 时触发，正常运营中不会执行**。因此：

- 软删除分类后，历史工单的 `CategoryId` **保持不变**，工单详情仍可显示原分类名
- 如果未来支持物理删除分类，`SetNull` 确保工单不会因 FK 约束而删除失败
- 物理删除分类前应确保无活跃工单引用，否则工单的分类信息将丢失
