# Spec: Agent Management

## 概述

Agent（客服人员）是系统的内部用户。Agent 基于框架 `IdentityUser` 实现，通过角色区分权限，通过 `ExtraProperties` 存储扩展属性。本 Spec 定义 Agent 的管理 API、DTO 和生命周期。

Agent 管理属于租户管理员的操作范畴，所有端点需要 `Agents.*` 权限。

## 实体映射

Agent 不创建独立实体表，而是使用框架的 `IdentityUser` + 角色 + ExtraProperties：

| 框架字段/扩展 | 用途 |
|--------------|------|
| `IdentityUser.Id` | Agent ID |
| `IdentityUser.UserName` | 登录用户名（邮箱） |
| `IdentityUser.Email` | 邮箱 |
| `IdentityUser.Name` | 显示名称 |
| `IdentityUser.IsActive` | 是否在职 |
| `ExtraProperties["SkillGroup"]` | 技能组（如 "技术支持"、"售后"） |
| `ExtraProperties["MaxConcurrentTickets"]` | 最大并发工单数（默认 10） |
| 角色 `Helpdesk.Agent` | 普通客服 |
| 角色 `Helpdesk.Supervisor` | 客服主管 |
| 角色 `Helpdesk.Admin` | 租户管理员 |

## Feature 限制

```
Feature: "Helpdesk.MaxAgents"
  默认值: 3
  验证: 创建 Agent 前检查当前活跃 Agent 数是否已达上限
```

## API

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `GET` | `/api/agents` | Agents | Agent 列表（分页+过滤） |
| `GET` | `/api/agents/{id}` | Agents | Agent 详情（含工单统计） |
| `POST` | `/api/agents` | Agents.Create | 创建 Agent（创建 IdentityUser + 分配角色 + 设置扩展属性） |
| `PUT` | `/api/agents/{id}` | Agents.Update | 更新 Agent（名称、角色、技能组、并发上限） |
| `PUT` | `/api/agents/{id}/deactivate` | Agents.Deactivate | 停用 Agent（自动重分配工单） |
| `PUT` | `/api/agents/{id}/reactivate` | Agents.Create | 重新激活 Agent |
| `PUT` | `/api/agents/{id}/change-role` | Agents.Update | 修改 Agent 角色（Agent ↔ Supervisor） |

### DTO

```csharp
// CreateAgentDto
{
    "userName": "agent.li@acme.com",        // 必填, 邮箱格式, 同租户内唯一
    "password": "Temp@123456",               // 必填, 最小8字符, 含大小写+数字
    "name": "客服小李",                       // 必填, 1-50字符
    "role": "Helpdesk.Agent",                // 必填, Agent/Supervisor
    "skillGroup": "技术支持",                  // 可选
    "maxConcurrentTickets": 10               // 可选, 1-50, 默认10
}

// AgentDto (Response)
{
    "id": "guid",
    "userName": "agent.li@acme.com",
    "name": "客服小李",
    "email": "agent.li@acme.com",
    "role": "Helpdesk.Agent",
    "isActive": true,
    "skillGroup": "技术支持",
    "maxConcurrentTickets": 10,
    "currentTicketCount": 5,                 // 当前分配的未关闭工单数
    "totalResolvedTickets": 128,             // 历史解决工单总数
    "averageResolutionHours": 4.2,
    "createdAt": "2026-01-15T08:00:00Z"
}

// UpdateAgentDto
{
    "name": "客服小李（高级）",
    "skillGroup": "VIP技术支持",
    "maxConcurrentTickets": 15
}
```

### 查询参数

```
GET /api/agents?page=1&pageSize=20&sort=name+asc
    &filter=agent.role.name:eq:Helpdesk.Agent
    &filter=agent.isActive:eq:true
    &search=李

可过滤字段: Role, IsActive, SkillGroup
可排序字段: Name, CreatedAt, CurrentTicketCount, TotalResolvedTickets
```

## 创建 Agent 流程

```csharp
[CrestService]
[Authorize(HelpdeskPermissions.Agents_Create)]
public class AgentAppService : ApplicationService, IAgentAppService
{
    private readonly IFeatureChecker _featureChecker;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public async Task<AgentDto> CreateAsync(CreateAgentDto input)
    {
        // 1. 检查 Feature 限制
        var maxAgents = await _featureChecker.GetAsync<int>("Helpdesk.MaxAgents");
        var currentCount = await CountActiveAgentsAsync();
        if (currentCount >= maxAgents)
        {
            throw new FeatureLimitExceededException(
                "Helpdesk.MaxAgents", maxAgents, currentCount);
        }

        // 2. 创建 IdentityUser
        var user = new IdentityUser
        {
            UserName = input.UserName,
            Email = input.UserName,
            Name = input.Name,
            IsActive = true,
        };
        user.SetProperty("SkillGroup", input.SkillGroup);
        user.SetProperty("MaxConcurrentTickets",
            input.MaxConcurrentTickets > 0 ? input.MaxConcurrentTickets : 10);

        var result = await _userManager.CreateAsync(user, input.Password);
        if (!result.Succeeded)
            throw new ValidationException(result.Errors);

        // 3. 分配角色
        await _userManager.AddToRoleAsync(user, input.Role);

        // 4. 审计
        await _auditLogService.LogAsync("Agent.Created", user.Id);

        return MapToDto(user);
    }
}
```

## 停用 Agent — 工单重分配

```
AgentDeactivate(id)
    │
    ├── 1. 检查是否为最后一个 Admin（至少保留1个Admin）
    ├── 2. 查询该 Agent 所有未关闭工单
    ├── 3. 按技能组匹配 → 分配给同技能组的活跃 Agent（轮询分配）
    │       └── 若无同技能组 → 分配给任意 Supervisor
    ├── 4. 对每个工单:
    │       ├── ticket.Assign(newAgentId)
    │       └── ticket.AddSystemMessage($"原客服 {agent.Name} 已离职，工单重新分配")
    ├── 5. user.IsActive = false
    └── 6. 保存 + 审计
```

## 修改角色

```
ChangeRole(agentId, newRole)
    │
    ├── 1. 验证 newRole 合法（Agent/Supervisor/Admin）
    ├── 2. RemoveFromRole(oldRole) + AddToRole(newRole)
    ├── 3. 权限变更在下一次登录时生效（Token 中的角色 claims 在下次刷新时更新）
    └── 4. 审计
```

## 验证规则

| 字段 | 规则 |
|------|------|
| `UserName` | 必填, 合法邮箱, 同租户内唯一 |
| `Password` | 必填, 最小8字符, 必须含大小写字母+数字 |
| `Name` | 必填, 1-50 字符 |
| `Role` | 必填, 合法角色名（Agent/Supervisor） |
| `MaxConcurrentTickets` | 可选, 1-50, 默认 10 |
| `SkillGroup` | 可选, 1-30 字符 |

## 验证检查点

- [ ] Feature `MaxAgents = 3` 时创建第4个 Agent 返回错误
- [ ] 创建 Agent 后 `IdentityUser` 和角色关联正确
- [ ] Agent 的 `ExtraProperties["SkillGroup"]` 正确保存和读取
- [ ] 停用 Agent 后所有工单自动重分配
- [ ] 停用最后一个 Admin 返回错误
- [ ] 停用 Agent 后 `IsActive = false`，登录被拒绝
- [ ] 修改角色后 Token 中的权限在下次刷新时更新
- [ ] 同租户下 `UserName` 唯一
- [ ] Agent 列表可按角色过滤
- [ ] 审计日志记录 Agent 创建/停用/角色变更
