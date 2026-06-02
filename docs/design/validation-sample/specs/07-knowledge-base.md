# Spec: Knowledge Base

## 概述

知识库是客服人员查阅和共享解决方案的文章系统。核心验证点是缓存（[CacheMo]）、浏览量统计、以及 Feature 开关控制。

## 实体

```csharp
[Entity]
public class KnowledgeBaseArticle : AuditedAggregateRoot<Guid>
{
    public Guid TenantId { get; private set; }
    public string Title { get; private set; }       // 5-200字符
    public string Content { get; private set; }     // Markdown 格式
    public Guid? CategoryId { get; private set; }
    public bool IsPublished { get; private set; }
    public int ViewCount { get; private set; }
    public string Tags { get; private set; }        // 逗号分隔标签 "login,password,reset"

    // 领域方法
    public void UpdateContent(string title, string content, string tags);
    public void Publish();                          // IsPublished = true
    public void Unpublish();                        // IsPublished = false
    public void IncrementViewCount();
}
```

## Feature 开关

知识库是否启用由 Feature Management 控制：

```
Feature: "Helpdesk.KnowledgeBase.Enabled"
  默认值: true
  作用域: Tenant
  关闭时: 知识库相关 API 返回 403 / 菜单隐藏
```

## API

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `GET` | `/api/knowledge-base` | KnowledgeBase.Read | 文章列表（分页+搜索） |
| `GET` | `/api/knowledge-base/{id}` | KnowledgeBase.Read | 文章详情（阅读时 +1 ViewCount） |
| `POST` | `/api/knowledge-base` | KnowledgeBase.Manage | 创建文章 |
| `PUT` | `/api/knowledge-base/{id}` | KnowledgeBase.Manage | 更新文章 |
| `DELETE` | `/api/knowledge-base/{id}` | KnowledgeBase.Manage | 删除文章 |
| `PUT` | `/api/knowledge-base/{id}/publish` | KnowledgeBase.Manage | 发布文章 |
| `PUT` | `/api/knowledge-base/{id}/unpublish` | KnowledgeBase.Manage | 取消发布 |
| `GET` | `/api/knowledge-base/popular` | KnowledgeBase.Read | 热门文章 TOP 10 |

### DTO 示例

```csharp
// CreateKnowledgeBaseDto
{
    "title": "如何重置密码",
    "content": "## 步骤一\n登录后点击...",
    "tags": "password,reset,account",
    "categoryId": "guid"
}

// KnowledgeBaseDto (列表)
{
    "id": "guid",
    "title": "如何重置密码",
    "isPublished": true,
    "viewCount": 128,
    "tags": "password,reset,account",
    "createdAt": "2026-03-01T09:00:00Z"
}

// KnowledgeBaseDetailDto (详情)
{
    ...KnowledgeBaseDto,
    "content": "## 步骤一\n...",
    "category": { "id": "guid", "name": "账号问题" }
}
```

### 查询参数

```
GET /api/knowledge-base?page=1&pageSize=20&sort=viewCount+desc
    &search=密码重置      (全文搜索 Title + Tags)
    &filter=article.isPublished:eq:true
    &filter=article.categoryId:eq:guid

可过滤字段: IsPublished, CategoryId
搜索: 对 Title 和 Tags 做 contains 匹配
可排序字段: Title, ViewCount, CreatedAt, UpdatedAt
```

## 缓存策略

```csharp
// 热门文章缓存 (Dashboard展示用)
[CacheMo(CacheName = "PopularArticles", ExpirationMinutes = 10)]
public virtual async Task<List<KnowledgeBaseDto>> GetPopularAsync()
{
    return await repository.AsQueryable()
        .Where(a => a.IsPublished)
        .OrderByDescending(a => a.ViewCount)
        .Take(10)
        .ToListAsync();
}

// 文章搜索 (不缓存，结果动态变化)
public async Task<PagedResultDto<KnowledgeBaseDto>> SearchAsync(string keyword, ...) { ... }

// PageView 缓存 (防止同一用户短时间内重复计数)
// 使用 ICrestCacheService 存储 "KB_View_{articleId}_{userId}" 1分钟TTL
public async Task<KnowledgeBaseDetailDto> GetAsync(Guid id)
{
    var article = await repository.GetAsync(id);

    var cacheKey = $"KB_View_{id}_{CurrentUser.Id}";
    if (!await cacheService.ExistsAsync(cacheKey))
    {
        article.IncrementViewCount();
        await cacheService.SetAsync(cacheKey, true, TimeSpan.FromMinutes(1));
    }

    await unitOfWork.SaveChangesAsync();
    return article.ToDetailDto();
}
```

## Feature Checker 集成

```csharp
public class KnowledgeBaseAppService : ..., IKnowledgeBaseAppService
{
    private readonly IFeatureChecker _featureChecker;

    public async Task<PagedResultDto<KnowledgeBaseDto>> GetListAsync(...)
    {
        // 检查功能是否启用
        await _featureChecker.CheckEnabledAsync(HelpdeskFeatures.KnowledgeBase_Enabled);

        return await base.GetPagedListAsync(...);
    }
}
```

## 验证规则

| 字段 | 规则 |
|------|------|
| `Title` | 必填, 5-200 字符 |
| `Content` | 必填, 最少 20 字符 |
| `Tags` | 可选, 逗号分隔 |

## 验证检查点

- [ ] Feature `KnowledgeBase.Enabled = false` 时 API 返回 403
- [ ] 热门文章缓存命中后不查 DB
- [ ] 文章发布/更新后热门缓存不自动失效（TTL到期后自然刷新）
- [ ] 同一用户1分钟内重复读同一文章不重复计数
- [ ] 未发布文章不出现在搜索和列表中
- [ ] 浏览量 Top 10 排序正确
- [ ] 搜索 "密码" 匹配 Title 和 Tags 中包含 "密码" 的文章
