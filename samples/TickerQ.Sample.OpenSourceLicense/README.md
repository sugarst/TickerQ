# TickerQ 开源快照本地证书验证

开源基线为 `b2aefd0932c97d13358c4def46cb5eba92c9b399`（源码版本 `10.4.2-beta`），对比目标为当前仓库本地 `main` 的 `2ac18b3b38150e52c1bdb5581a43b98aa569b73d`。下载的快照 `LICENSE` 文件 SHA-256 为 `4d825c68531c694f10e0aa45ef307aa94b3e22d7a9f0a4dec1086083b50596db`，与本仓库记录一致。[开源源码归档](https://codeload.github.com/Arcenox-co/TickerQ/tar.gz/b2aefd0932c97d13358c4def46cb5eba92c9b399) · [原许可证记录](../../LICENSING.md)

## 功能差距

| 范围 | 开源快照已有 | `main` 的差距及影响 |
| --- | --- | --- |
| .NET API | `AddTickerQ`、`MapTicker`、定时及 Cron 管理器、`UseDefinedCronApplicationNamespace`、`UseReconciliationEpoch` | 命名空间配置改为 `UseApplicationNamespace`，增加 Cron/TimeTicker/策略种子构建器、日期提供者和执行日志配置。业务若已调用这些 API，需要逐个对照。`main` 新增的 `TickerQ.Integrations` 也要单独评估。 |
| 调度 | 应用分区和激活 epoch、任务租约、节点收尾 outbox、TimeTicker 任务链和基础 Cron | `main` 增加周期任务链、调度策略、批量获取和队列容量、工作流及分步并发上限。高负载、公平性、抢占和恢复行为需要用真实任务负载验证。 |
| Dashboard | Overview、Time/Cron、Executions、Chain Builder 和 Assistant 页面及基础管理 API | `main` 另有 Nodes、Diagnostics、Schedule Policies、Integrations 和 License 页面，新增定义管理、日志尾部、结果、测试运行和按任务请求取消等接口，常用路由也有 AOT 注册和序列化变化。 |
| EF/SQLite | 应用样例有独立的请求契约、任务链代际、专业存储和应用分区等迁移 | `main` 的 WebApi 样例改用 `20261024223431_UpgradeTickerQStore`，模型新增或扩展 `SchedulePolicies`、`ExecutionLogs`、`TickerQDefinitions`、`TickerQNodes`、`TickerQControlCommands` 和 `IntegrationSettings` 等表。迁移 ID、主外键和数据意义均需逐项核对。 |
| 其他 Provider | 快照已有 EF Core、Redis 和 MongoDB 工程 | `main` 在多个 Provider 上实现了周期任务链和调度策略相关持久化。SQLite 验证无法覆盖这些 Provider 的拓扑、索引和迁移行为。 |

这是源码级对比。面向现有部署的验收还需使用实际数据库的副本、相同 Provider 和相同多实例拓扑，核对任务、历史、分区归属、迁移和回滚。托管 Hub 的功能另需服务端认证。[5.x 升级说明](https://tickerq.net/docs/5.x/getting-started/migration) · [存储升级说明](https://tickerq.net/docs/5.x/guides/storage-upgrade)

## 复现本地部署流程

1. 下载并解压上述开源快照。用仓库中的 AtomUI 桌面端选择北京时间（UTC+8）的到期日期和时间，点击 **生成并导出测试证书**。证书中的到期时间以 UTC 写入。点击 **打开文件夹** 查看导出的 `tickerq.tqlicense` 和 `fixture-authority.json`。
2. 将 `fixture-authority.json` 作为可信公钥通过独立路径配置给样例。只给下载的证书路径不会自动导入公钥。先做验签和有效期校验，再初始化数据库和启动服务。

在仓库根目录使用 PowerShell 和 .NET 10 SDK：

```powershell
$env:TickerQOpenSourceRoot = 'C:\work\TickerQ-b2aefd0932c97d13358c4def46cb5eba92c9b399'
$tickerqCronPatch = (Resolve-Path 'samples/TickerQ.Sample.OpenSourceLicense/patches/ef-cron-projections.patch').Path
git -C $env:TickerQOpenSourceRoot apply -- $tickerqCronPatch
if ($LASTEXITCODE -ne 0) { throw 'Could not apply the open-source snapshot Cron fix.' }
$tickerqExport = Join-Path $env:LOCALAPPDATA 'TickerQ\TestCertificates\tickerq-test-...'
$tickerqDb = Join-Path $env:TEMP "tickerq-standalone-$([guid]::NewGuid().ToString('N')).sqlite"
$tickerqDashboard = Join-Path $env:TickerQOpenSourceRoot 'src/TickerQ.Dashboard/wwwroot'
npm --prefix $tickerqDashboard ci
npm --prefix $tickerqDashboard run build
dotnet run --project samples/TickerQ.Sample.OpenSourceLicense -p:GeneratePackageOnBuild=false -- `
  --certificate "$tickerqExport/tickerq.tqlicense" `
  --authority "$tickerqExport/fixture-authority.json" `
  --database $tickerqDb --smoke true
```

省略 `--smoke true` 时，服务持续监听 `http://127.0.0.1:5219/tickerq/dashboard/`；Dashboard 的匿名访问仅绑定本机。示例默认从快照模型初始化专用 SQLite 文件。若要复现原 WebApi EF 迁移链，使用另一个全新 SQLite 文件并加上 `--migrate true`。原迁移链在 EF Core SQLite 10.0.15 上执行 `ApplicationRuntimePartitioning` 时出现 `CronTickerOccurrences` 外键不匹配，现有库升级仍待验证。请使用示例专用数据库文件。

`ef-cron-projections.patch` 对解压后的开源快照应用一次。如果快照解压在本仓库内部，先把 `GIT_CEILING_DIRECTORIES` 设为快照的父目录，以免 Git 按外层仓库解析补丁。

原先 EF Core 的 Cron 映射漏读 `DefinitionRevision`。SQLite 已保存版本 `1`，调度扫描却带着默认值 `0`，版本检查会跳过每个计划时点。补丁让 EF Core 复用快照内已有的映射，读取 Cron 定义及待执行 occurrence 的版本字段。已有定义保留原版本，应用这项修复无需变更数据库结构。

实测使用桌面端导出的证书，独立宿主验签后启动 Kestrel。冒烟检查读取 Dashboard 页面，通过其 API 新建一次性任务和 `*/3 * * * * *` Cron，并验证 SQLite 中完成的一次性任务以及 Dashboard 返回的两个连续 Cron 时点。检查过程通过调度器自然触发 Cron。

用 SQLite 的备份接口复制原先复现问题时的数据库，再用修复后的独立宿主启动。原有 Cron 定义直接恢复执行，Chromium 在 Dashboard 的 Cron 详情表中渲染了两条 `Done` 记录，并显示计划时间和执行时间。

## 独立维护评估

独立宿主已将证书校验放在业务服务启动之前，可单独构建和维护。桌面测试工具每次导出都生成新密钥，因此目前每次续发都需要重新登记公钥。长期维护需要独立管理持久签发密钥、轮换公钥，并确定运行中的到期处理。示例在启动时拒绝已过期的证书，运行中持续到期检查留待独立产品实现。对标 `main` 的核心难点是补齐调度和存储行为，并维护兼容性测试、数据迁移和安全更新。
