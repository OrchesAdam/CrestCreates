# #129 S5 Ticket NativeAOT 与最终交接

审查入口：PR #152（本报告随最终 head 提交）。S4 已放行；本切片新增 S5 Ticket 原生 fixture，不改既有生产主链。

## 交付

- `tests/Framework/Api/CrestCreates.TicketHttpProjection.AotFixture`：local anchor + 真实 Ticket service/DTO/validator + 正式生成 bundle；内存 Ticket/SLA 持久化叶子与 UoW；fixture identity/permission/failure presenter；有界静态 Ticket/SLA queryable。
- `tests/Framework/Api/CrestCreates.TicketHttpProjection.AotFixture.Tests`：JIT HTTP 1/1；NativeAOT 门禁 3/3（绿色 + 两变异）。
- 主/分层解决方案加入两个 fixture 项目；`CapabilityCompatibilityProjectionAttribute` XML 注释同步 P3。

## 原生场景

create / get-by-id / unknown-404 / list-all / by-customer / update-with-stamp / assign-with-active-SLA / by-assignee / resolve / close / business-failure-serializable / delete-If-Match / invalid-body-zero-business / permission-403 / metadata-rejection。每场景 `<name>:PASS`，全部通过后打印 `CRESTCREATES_TICKET_NATIVE_OK`。

零业务判别：fixture 业务入口/UoW 启动/仓储写计数器独立断言；create 正向各 +1，非法输入三项均不增加。内置 `late-validation` 注入（跳过 pre-service validation、业务/UoW 已进入后仓储拒绝）使 invalid-body-zero-business:FAIL。

metadata 判别：破坏 JSON 前锁定 Ticket owner、11 个 endpoint/capability/identity 与 CreateTicketDto body / Ticket result-envelope root；失败异常须含该 owner/action/type 且零新增路由。内置 `decoy-metadata`（无 Ticket 端点的 decoy 贡献）使 metadata-rejection:FAIL。

五控制均 exit 0/1 符合预期，失败控制无总 sentinel。

## 证据

`docs/review/2026-10-21-issue-129-s5-ticket-native-evidence/`（由本地 `tests/artifacts/aot-native-evidence/ticket-native-http*.json` 原样入库）：五个控制（green + 两期望变异 + late-validation + decoy-metadata）的 publish 命令/退出/日志、SHA（`5e3878b2`）、RID、gate 输出、result/failure。publish/link/run 为 linux-x64；fixture 自身无 IL2026/IL3050。

## 范围限制

- 内存持久化叶子；不证明 EF/PostgreSQL、真实认证/租户/多租户、整个 SaaSHelpdesk。
- LibraryManagement 完整 HTTP 仍未验证（既有 `OpenIddictScopes` 测试环境缺失）；S4 旧链退出结论保持。
- 其他 RID、Windows/macOS 未验证；CRUD/CapabilityEndpoint 原生结果不推导 Ticket/EF/认证/租户全链。
- 未合并、CI 暂停；不自动恢复。
