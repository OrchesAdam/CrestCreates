# Spec: Customer Management

## 概述

客户是工单的发起者。支持客户基本 CRUD、激活/停用管理，以及与组织结构的关联。

## 实体

```csharp
[Entity]
public class Customer : AuditedAggregateRoot<Guid>, IHasDomainEvents
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }         // 1-100字符
    public string Email { get; private set; }        // 邮箱，唯一（同租户内）
    public string Phone { get; private set; }        // 可选
    public string Company { get; private set; }      // 公司名，可选
    public Guid? OrganizationId { get; private set; } // 组织层级关联
    public bool IsActive { get; private set; }

    // 导航属性
    public virtual ICollection<Ticket> Tickets { get; private set; }

    // 领域方法
    public void UpdateProfile(string name, string phone, string company);
    public void ChangeEmail(string newEmail);
    public void Deactivate();    // → IsActive = false
    public void Reactivate();    // → IsActive = true

    // 统计
    public int OpenTicketCount { get; private set; }        // 非实时, 通过领域事件更新
    public int TotalTicketCount { get; private set; }
}
```

## API

| 方法 | 路径 | 权限 | 说明 |
|------|------|------|------|
| `GET` | `/api/customers` | Customers | 客户列表（分页+过滤） |
| `GET` | `/api/customers/{id}` | Customers | 客户详情（含工单统计） |
| `POST` | `/api/customers` | Customers.Create | 创建客户 |
| `PUT` | `/api/customers/{id}` | Customers.Update | 更新客户 |
| `DELETE` | `/api/customers/{id}` | Customers.Delete | 删除客户 |
| `PUT` | `/api/customers/{id}/deactivate` | Customers.Update | 停用客户 |
| `PUT` | `/api/customers/{id}/reactivate` | Customers.Update | 重新激活客户 |

### 查询参数

```
GET /api/customers?page=1&pageSize=20&sort=name+asc
    &filter=customer.name:contains:张
    &filter=customer.isActive:eq:true

可过滤字段: Name, Email, Company, IsActive, OrganizationId
可排序字段: Name, CreatedAt, OpenTicketCount
```

### DTO 示例

```csharp
// CreateCustomerDto
{
    "name": "张三",
    "email": "zhangsan@example.com",
    "phone": "+86 13800138000",
    "company": "科技有限公司",
    "organizationId": null
}

// CustomerDto (Response)
{
    "id": "guid",
    "name": "张三",
    "email": "zhangsan@example.com",
    "phone": "+86 13800138000",
    "company": "科技有限公司",
    "isActive": true,
    "openTicketCount": 2,
    "totalTicketCount": 15,
    "createdAt": "2026-01-15T08:30:00Z"
}
```

## 验证规则

| 字段 | 规则 |
|------|------|
| `Name` | 必填, 1-100 字符 |
| `Email` | 必填, 合法邮箱格式, 同租户内唯一 |
| `Phone` | 可选, 合法电话格式 |
| `Company` | 可选, 1-200 字符 |

## 领域事件

| 事件 | 触发 |
|------|------|
| `CustomerCreatedDomainEvent` | 客户创建 |
| `CustomerDeactivatedDomainEvent` | 客户停用 |

## 验证检查点

- [ ] 同租户下 Email 唯一
- [ ] 停用客户后 `IsActive = false`
- [ ] 停用客户不影响历史工单查询
- [ ] 客户详情包含工单统计
- [ ] 删除客户时级联不影响工单数据（工单保留，客户引用设 null）
