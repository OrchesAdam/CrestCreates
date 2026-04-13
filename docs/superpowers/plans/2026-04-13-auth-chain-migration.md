# 认证主链迁移实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将认证主链从双入口（AuthController + OpenIddict）统一到 OpenIddict 单主链

**Architecture:**
- OpenIddict 作为唯一认证协议入口（登录、签发 Token、刷新 Token）
- OpenIddict Validation 作为唯一 Token 校验入口（替代 JwtBearer）
- 权限运行时动态查询，Token 不内嵌 permission
- 租户边界通过 TenantBoundary 中间件校验

**Tech Stack:** OpenIddict, ASP.NET Core Authentication, MultiTenancy Middleware

---

## Task 1: 扩展 OpenIddict Password Grant 登录能力

**Files:**
- Create: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Services/IdentitySecurityLogService.cs`
- Create: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/PasswordGrantHandler.cs`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Controllers/OpenIddictController.cs`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/OpenIddictServiceCollectionExtensions.cs`

实现密码登录、租户边界校验、用户状态检查、安全日志记录。

- [ ] 创建 IdentitySecurityLogService（内部服务）
- [ ] 创建 PasswordGrantHandler
- [ ] 更新 OpenIddictController 使用 PasswordGrantHandler
- [ ] 更新 OpenIddictServiceCollectionExtensions 注册新服务
- [ ] 提交

---

## Task 2: 扩展 OpenIddict Refresh Token 能力

**Files:**
- Create: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Handlers/RefreshTokenGrantHandler.cs`
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Controllers/OpenIddictController.cs`

实现刷新令牌校验、用户状态验证。

- [ ] 创建 RefreshTokenGrantHandler
- [ ] 更新 OpenIddictController
- [ ] 提交

---

## Task 3: 完善 OpenIddict UserInfo 端点

**Files:**
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/Controllers/OpenIddictController.cs`

扩展 UserInfo 端点返回完整用户信息（包含 is_super_admin 和 roles）。

- [ ] 扩展 UserInfo 端点
- [ ] 提交

---

## Task 4: 配置 OpenIddict Validation 作为 Token 校验主链

**Files:**
- Modify: `framework/src/CrestCreates.AspNetCore.Authentication.OpenIddict/OpenIddictServiceCollectionExtensions.cs`
- Modify: `framework/src/CrestCreates.Web/Startup.cs`

配置 OpenIddict Validation 替代 JwtBearer 作为默认认证方案。

- [ ] 更新 OpenIddictServiceCollectionExtensions
- [ ] 更新 Startup.cs 移除 JwtBearer
- [ ] 提交

---

## Task 5: 移除 AuthController

**Files:**
- Delete: `framework/src/CrestCreates.AspNetCore/Controllers/AuthController.cs`

- [ ] 删除 AuthController
- [ ] 验证编译通过
- [ ] 提交

---

## Task 6: 移除 JwtBearer 模块

**Files:**
- Delete: `framework/src/CrestCreates.AspNetCore.Authentication.JwtBearer/` 目录及所有文件
- Modify: `framework/src/CrestCreates.Web/Startup.cs`

- [ ] 删除 JwtBearer 目录
- [ ] 验证编译通过
- [ ] 提交

---

## Task 7: 移除 IAuthService 和 AuthService

**Files:**
- Delete: `framework/src/CrestCreates.Application.Contracts/Interfaces/IAuthService.cs`
- Delete: `framework/src/CrestCreates.Infrastructure/Authorization/AuthService.cs`

- [ ] 审计 IAuthService 使用者
- [ ] 移除所有引用
- [ ] 删除文件
- [ ] 验证编译通过
- [ ] 提交

---

## Task 8: 更新示例项目

**Files:**
- Modify: `samples/LibraryManagement/LibraryManagement.Web/Program.cs`

- [ ] 更新示例项目使用 OpenIddict 认证
- [ ] 验证编译和运行
- [ ] 提交

---

## Task 9: 更新文档

**Files:**
- Create: `docs/components/Authentication-README.md`
- Modify: 相关文档

- [ ] 创建认证主链文档
- [ ] 提交

---

## 验收清单

| 验收点 | 状态 |
|--------|------|
| 登录入口唯一：OpenIddict `/connect/token` | ⬜ |
| Token 签发唯一：OpenIddict Token Endpoint | ⬜ |
| Token 校验唯一：OpenIddict Validation | ⬜ |
| 权限来源唯一：IPermissionChecker 运行时查询 | ⬜ |
| 租户边界校验：TenantBoundaryMiddleware | ⬜ |
| AuthController 已删除 | ⬜ |
| JwtBearer 模块已删除 | ⬜ |
| IAuthService/AuthService 已删除 | ⬜ |
| 示例项目已更新 | ⬜ |
| 文档已更新 | ⬜ |
| 所有测试通过 | ⬜ |
