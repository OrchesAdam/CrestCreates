# Spec: Setting & Feature Management

## 概述

SaaS Helpdesk 使用框架的 Setting Management 管理租户级和用户级配置，使用 Feature Management 管理套餐分级功能。本 spec 定义所有 Setting 和 Feature 的完整清单。

## Setting Definitions

### SettingProvider

```csharp
public class HelpdeskSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // === 工单设置 ===
        context.Add(new SettingDefinition(
            "Helpdesk.Ticket.AutoCloseDays",
            "14",
            displayName: "工单自动关闭天数",
            description: "已解决工单N天后自动关闭",
            isVisibleToClients: true
        ));

        // === SLA 默认值 ===
        context.Add(new SettingDefinition(
            "Helpdesk.SLA.DefaultFirstResponseMinutes",
            "60",
            displayName: "默认首次响应时限(分钟)",
            isVisibleToClients: true
        ));

        context.Add(new SettingDefinition(
            "Helpdesk.SLA.DefaultResolutionMinutes",
            "480",
            displayName: "默认解决时限(分钟)",
            isVisibleToClients: true
        ));

        // === 通知设置 ===
        context.Add(new SettingDefinition(
            "Helpdesk.Notification.Enabled",
            "true",
            displayName: "启用通知",
            isVisibleToClients: true
        ));

        context.Add(new SettingDefinition(
            "Helpdesk.Notification.EmailTemplate.Customer",
            "default_customer_notification",
            displayName: "客户通知邮件模板"
        ));

        context.Add(new SettingDefinition(
            "Helpdesk.Notification.EmailTemplate.Agent",
            "default_agent_notification",
            displayName: "客服通知邮件模板"
        ));

        // === 附件设置 ===
        context.Add(new SettingDefinition(
            "Helpdesk.Attachment.MaxFileSizeMB",
            "10",
            displayName: "附件最大大小(MB)",
            isVisibleToClients: true
        ));

        context.Add(new SettingDefinition(
            "Helpdesk.Attachment.AllowedTypes",
            "jpg,jpeg,png,gif,pdf,doc,docx,xls,xlsx,txt,csv,zip",
            displayName: "允许的附件类型",
            isVisibleToClients: true
        ));

        // === 邮件服务器 (V1仅存储配置，不实际发送) ===
        context.Add(new SettingDefinition(
            "Helpdesk.Email.SmtpHost",
            "",
            displayName: "SMTP服务器地址",
            isVisibleToClients: true
        ));

        context.Add(new SettingDefinition(
            "Helpdesk.Email.SmtpPort",
            "587",
            displayName: "SMTP端口",
            isVisibleToClients: true
        ));

        context.Add(new SettingDefinition(
            "Helpdesk.Email.SmtpUsername",
            "",
            displayName: "SMTP用户名",
            isVisibleToClients: true
        ));

        context.Add(new SettingDefinition(
            "Helpdesk.Email.SmtpPassword",
            "",
            displayName: "SMTP密码",
            isVisibleToClients: false,   // 不应在客户端 UI 显示
            isEncrypted: true            // 加密存储 → 验证加密 Setting 链路
        ));

        // === 通用设置 ===
        context.Add(new SettingDefinition(
            "Helpdesk.General.Timezone",
            "Asia/Shanghai",             // IANA 时区标识符格式
            displayName: "租户时区",
            isVisibleToClients: true
        ));

        // User 作用域设置
        context.Add(new SettingDefinition(
            "Helpdesk.General.Language",
            "zh-CN",
            displayName: "用户语言偏好",
            isVisibleToClients: true
        ));
    }
}
```

### Setting 作用域

| Setting | Global | Tenant | User | 加密 |
|---------|:------:|:------:|:----:|:----:|
| `Ticket.AutoCloseDays` | ✗ | ✓ | ✗ | ✗ |
| `SLA.DefaultFirstResponseMinutes` | ✗ | ✓ | ✗ | ✗ |
| `SLA.DefaultResolutionMinutes` | ✗ | ✓ | ✗ | ✗ |
| `Notification.Enabled` | ✗ | ✓ | ✗ | ✗ |
| `Notification.EmailTemplate.Customer` | ✗ | ✓ | ✗ | ✗ |
| `Notification.EmailTemplate.Agent` | ✗ | ✓ | ✗ | ✗ |
| `Email.SmtpHost` | ✗ | ✓ | ✗ | ✗ |
| `Email.SmtpPort` | ✗ | ✓ | ✗ | ✗ |
| `Email.SmtpUsername` | ✗ | ✓ | ✗ | ✗ |
| `Email.SmtpPassword` | ✗ | ✓ | ✗ | ✓ |
| `Attachment.MaxFileSizeMB` | ✗ | ✓ | ✗ | ✗ |
| `Attachment.AllowedTypes` | ✗ | ✓ | ✗ | ✗ |
| `General.Timezone` | ✗ | ✓ | ✗ | ✗ |
| `General.Language` | ✗ | ✗ | ✓ | ✗ |

> **Setting 值校验**: `AutoCloseDays` 和 SLA 时限必须 ≥ 0（0 表示禁用自动关闭/SLA）。`MaxFileSizeMB` 必须 ≥ 1。`SmtpPort` 必须为 1-65535。

## Feature Definitions

### FeatureProvider

```csharp
public class HelpdeskFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        // === 客服管理 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.MaxAgents",
            "3",
            displayName: "最大客服数",
            description: "允许创建的客服人员数量上限",
            valueType: FeatureValueTypes.Numeric
        ));

        // === 工单处理 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.MaxTicketsPerMonth",
            "100",
            displayName: "月工单上限",
            valueType: FeatureValueTypes.Numeric
        ));

        // === 存储 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.StorageLimitMB",
            "500",
            displayName: "存储空间上限(MB)",
            valueType: FeatureValueTypes.Numeric
        ));

        // === 知识库 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.KnowledgeBase.Enabled",
            "true",
            displayName: "启用知识库",
            valueType: FeatureValueTypes.Boolean
        ));

        // === SLA自定义 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.SLACustomization",
            "false",
            displayName: "允许自定义SLA策略",
            valueType: FeatureValueTypes.Boolean
        ));

        // === 自定义域名 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.CustomDomain",
            "false",
            displayName: "支持自定义域名",
            valueType: FeatureValueTypes.Boolean
        ));

        // === 报表 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.Reports.Enabled",
            "true",
            displayName: "启用报表功能",
            valueType: FeatureValueTypes.Boolean
        ));

        // === API访问 ===
        context.Add(new FeatureDefinition(
            "Helpdesk.API.Access",
            "false",
            displayName: "启用API访问",
            valueType: FeatureValueTypes.Boolean
        ));
    }
}
```

### 套餐模板

| Feature | 免费版 | 专业版 | 企业版 |
|---------|:------:|:------:|:------:|
| `MaxAgents` | 3 | 20 | ∞ |
| `MaxTicketsPerMonth` | 100 | 1000 | ∞ |
| `StorageLimitMB` | 500 | 5000 | 50000 |
| `KnowledgeBase.Enabled` | ✓ | ✓ | ✓ |
| `SLACustomization` | ✗ | ✓ | ✓ |
| `CustomDomain` | ✗ | ✗ | ✓ |
| `Reports.Enabled` | ✓ | ✓ | ✓ |
| `API.Access` | ✗ | ✓ | ✓ |

> 注: V1 不实现套餐切换的 UI/API，但 Feature 值通过种子数据设定，验证框架的 Checker/Store 链路。

## Feature Checker 使用方式

```csharp
// 方式1: AppService 中显式检查
public async Task<TicketDto> CreateAsync(CreateTicketDto input)
{
    // 检查本月工单是否已达上限
    var maxTickets = await _featureChecker.GetAsync<int>("Helpdesk.MaxTicketsPerMonth");
    var currentMonthCount = await GetCurrentMonthTicketCountAsync();
    if (currentMonthCount >= maxTickets)
    {
        throw new FeatureLimitExceededException(
            "Helpdesk.MaxTicketsPerMonth", maxTickets, currentMonthCount);
    }
    // ...
}

// 方式2: 全局拦截（使用 Feature 属性或中间件）
[RequiresFeature("Helpdesk.KnowledgeBase.Enabled")]
public class KnowledgeBaseAppService : ... { ... }

// 方式3: 依赖注入检查
public class AgentAppService
{
    private readonly IFeatureChecker _featureChecker;

    public async Task<IdentityUserDto> CreateAgentAsync(CreateAgentDto input)
    {
        await _featureChecker.CheckEnabledAsync("Helpdesk.MaxAgents", async () =>
        {
            var current = await CountAgentsAsync();
            var max = await _featureChecker.GetAsync<int>("Helpdesk.MaxAgents");
            return current < max;
        });
        // ...
    }
}
```

## Setting / Feature 变更的缓存失效

```csharp
// TenantCacheInvalidator 在 Setting/Feature 变更时自动清除租户缓存
// 框架自动处理，无需额外代码

// 验证方式:
// 1. 修改某租户的 "Ticket.AutoCloseDays" 为 7
// 2. 下一分钟自动关闭 Job 对应该租户使用新值 7
```

## 管理 API (框架内置, 非自定义)

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/api/setting-definitions` | 获取 Setting 定义 |
| `GET` | `/api/setting-values` | 获取 Setting 值 |
| `PUT` | `/api/setting-values` | 更新 Setting 值 |
| `GET` | `/api/feature-definitions` | 获取 Feature 定义 |
| `GET` | `/api/feature-values` | 获取 Feature 值 |
| `PUT` | `/api/feature-values` | 更新 Feature 值 |

## 验证检查点

- [ ] Setting 定义在 `/api/setting-definitions` 可见
- [ ] 租户A修改 `AutoCloseDays = 7` 不影响租户B（值仍为14）
- [ ] Setting 变更后缓存失效，`ISettingProvider` 读到新值
- [ ] Feature Checker 拦截超限操作（如添加第4个客服时返回错误）
- [ ] `KnowledgeBase.Enabled = false` 时知识库API返回403
- [ ] `TenantFeatureDefaultsSeeder` 种子化正确初始值
- [ ] `TenantSettingDefaultsSeeder` 种子化正确初始值
- [ ] User 作用域 `General.Language` 对每个用户独立
- [ ] Setting 值类型正确（字符串/数值/布尔）
