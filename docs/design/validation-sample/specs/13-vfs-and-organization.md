# Spec: Virtual File System & Organization Hierarchy

## 1. Virtual File System（虚拟文件系统）

### 用途

嵌入邮件通知模板和系统默认配置，支持运行时读取，不依赖物理文件路径。

### 嵌入文件

```
SaaSHelpdesk.Web/
└── EmbeddedResources/
    └── EmailTemplates/
        ├── ticket_created.html
        ├── ticket_assigned.html
        ├── ticket_resolved.html
        ├── sla_warning.html
        ├── weekly_report.html
        └── customer_welcome.html
```

### 模板内容示例

```html
<!-- ticket_assigned.html -->
<html>
<body>
    <h2>工单分配通知</h2>
    <p>工单 <strong>#{{TicketNumber}}</strong> 已分配给您</p>
    <p><strong>标题:</strong> {{TicketTitle}}</p>
    <p><strong>优先级:</strong> {{Priority}}</p>
    <p><strong>来自:</strong> {{CustomerName}}</p>
    <p><a href="{{TicketUrl}}">查看工单</a></p>
</body>
</html>
```

### 配置

```csharp
// 注册 VFS + 扫描嵌入资源
services.AddVirtualFileSystem(options =>
{
    options.AddEmbedded<HelpdeskWebModule>(
        baseNamespace: "SaaSHelpdesk.Web.EmbeddedResources",
        baseFolder: "/EmbeddedResources"
    );
});
```

### 读取模板

```csharp
public class EmailTemplateService
{
    private readonly IVirtualFileSystem _vfs;

    public async Task<string> GetTemplateAsync(string templateName)
    {
        var file = _vfs.GetFile($"/EmbeddedResources/EmailTemplates/{templateName}.html");
        return await file.ReadAsStringAsync();
    }

    // V1: 占位符替换，不集成真实邮件发送
    public string Render(string template, Dictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
        {
            template = template.Replace($"{{{{{{{key}}}}}}}", value);
        }
        return template;
    }
}
```

### 验证检查点

- [ ] VFS 可读取 `ticket_assigned.html`
- [ ] 模板变量替换正确
- [ ] 不存在的模板返回错误
- [ ] VFS 读取的文件内容与源文件一致

---

## 2. Organization Hierarchy（组织层级）

### 用途

支持客户公司组织层级和客服团队层级，上层可见下层数据。

### 客户公司组织

客户通过 `OrganizationId` 关联组织节点：

```
顶级公司 (Acme Corp)
├── 北京分部
│   ├── 技术部 → 客户A, 客户B
│   └── 市场部 → 客户C
└── 上海分部
    └── 运营部 → 客户D
```

### 客服团队层级

```
客服主管 (Supervisor)
├── 客服组长A
│   ├── 客服小李
│   └── 客服小王
└── 客服组长B
    ├── 客服小张
    └── 客服小赵
```

### 配置

```csharp
// 注册组织层级服务
services.AddOrganizationHierarchy(options =>
{
    options.EntityTypes = new[]
    {
        typeof(Customer),    // 客户公司层级
        typeof(IdentityUser) // 客服团队层级 (使用框架 IdentityUser)
    };
});
```

### 使用方式

```csharp
public class TicketAppService : ...
{
    private readonly IOrganizationHierarchyService _orgService;

    public async Task<List<TicketDto>> GetTeamTicketsAsync()
    {
        var currentUserId = CurrentUser.Id.Value;

        // 获取下级客服的所有工单
        var subordinateIds = await _orgService
            .GetDescendantIdsAsync(typeof(IdentityUser), currentUserId);

        // 包含自己的工单
        subordinateIds.Add(currentUserId);

        return await _ticketRepo.AsQueryable()
            .Where(t => t.AssigneeId != null && subordinateIds.Contains(t.AssigneeId.Value))
            .ToListAsync();
    }
}
```

### 验证检查点

- [ ] 主管可查看下属客服的所有工单
- [ ] 客服组长可查看组员的工单
- [ ] 普通客服不能查看同级客服的工单
- [ ] 客户按组织层级分组查询
- [ ] 组织节点不能将自己设为父节点（循环引用检测）
