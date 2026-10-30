# #132 当前清理缺口复现

从仓库根运行：

```bash
python3 docs/review/probes/issue132/run.py \
  --output docs/review/2026-10-27-issue-132-design-gap-evidence.json
```

默认固定 master `5f4cfc8f37d6ef0e996598eb08ae0922b8b60a34`。`--source-ref` 可以验证后续版本；如果行为已修复，探针应失败并要求重新审查 characterization。脚本在 `/tmp` 保留完整 Git archive、构建日志与执行输出，不改当前 checkout，也不清除已有文件。

使用真实 `AuditLogCleanupAppService`、`EfCoreAuditLogRepository`、EF adapter/base 的 ProjectReference 构建和 SQLite 数据库执行。Fixture 只提供 DbContext 模型、可变 ICurrentTenant 和固定 ISettingProvider；没有复制删除算法，没有替换仓储。无需 PostgreSQL 或容器。先单节点 restore/build，再运行 DLL，适用于本机多节点 MSBuild 无诊断退出的环境。

10 个 case 中，C01/C02/C09 是现有行为控制，其余 7 个是已知缺口的 characterization；绿色表示“当前缺口确实重现”，不是安全清理验收通过。C04/C08 的提示真实持久化在 ExtraProperties，但只是 fixture marker，现有生产代码没有原生 hold 或 cleanup receipt 契约，不能把 marker 当作已配置的受保护记录。

C10 用真实 EF command interceptor 在 Count 和目标 DELETE 之间执行一个相同连接上的独立 DELETE，确定性复现语句间交错：竞争语句删 1 行，本次目标 DELETE 实际删 0 行，服务仍返回 1。不声称这是多进程或 PostgreSQL 并发实验。

该项目为 docs 下的独立复现工具，不加入正式方案或默认测试，不新增生产扩展点，不声明正式 AuditEnvelope retention/认证/HTTP/NativeAOT 支持。生产实现时将正式 P01–P09 场景放入现有测试项目，将被替代的旧链探针归档。
