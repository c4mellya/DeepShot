# Zeep Cut — 当前状态

> 每轮工作结束时更新本文件。下一位接手者先读这里。

## 本轮快照

- 日期：2026-10-18
- 当前阶段：阶段一（截图闭环）
- 下一步任务：**T2 Core 选区状态机**
- 构建状态：✅ 编译通过，0 warning 0 error
- 测试状态：✅ 74 个测试通过（T1 新增 73 个 + 1 个 smoke）

## 已完成

- 2026-10-18 环境探测：Windows .NET SDK 10.0.100；WSL 经 UNC 调用 Windows dotnet 构建成功；桌面 GUI 可启动；双屏（主屏 2048×1152 @ 0,0；副屏 1080×1920 @ -1080,0）；均为 100% 缩放。
- 2026-10-18 工程骨架：`ZeepCut.slnx`、Core/App/Tests 三个项目、app.manifest（PerMonitorV2）、图标、构建与验证脚本（build/run/screenshot/keys）。
- 2026-10-18 文档：AGENTS.md、ARCHITECTURE.md、ROADMAP.md、PLAN.md、IMPLEMENTATION_HANDOFF.md。
- 2026-10-18 **T1 Core 几何与屏幕模型**：新增 `src/ZeepCut.Core/Geometry/`（纯逻辑，无 WPF/Win32 依赖）——`PixelRect`（Right/Bottom、IsEmpty、Intersect、Contains、Clamp、FromTwoPoints、Offset）、`MonitorInfo`（物理像素 Bounds/WorkArea、Dpi、IsPrimary）、`DisplaySpace`（点→屏幕查找，含负坐标与最近屏规则）、`DpiTransform`（物理像素↔DIP，scale=dpi/96）及 `DipRect`。xUnit 覆盖负坐标屏幕查找、x=0 边界归属主屏、选区夹取、任意方向拖拽、125%/150% 往返。

## 验证证据

- 2026-10-18 T1：`powershell.exe ... -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 74, Skipped: 0, Total: 74`（161 ms）；脚本末尾输出 `BUILD+TEST OK`。
- 2026-10-18 T1 单测覆盖：负坐标屏（-1080..0）点查找、双屏 x=0 归属主屏、选区越界/完全在外/部分重叠夹取、任意方向拖拽正矩形、125%/150% 像素↔DIP 换算与往返。
- 2026-10-18 骨架：`scripts/run.ps1` → 应用可启动（当时为空壳，启动即退出，属预期）。
- 2026-10-18 环境：`scripts/screenshot.ps1` → 成功抓取 3128×1920 双屏拼接图，原点 (-1080,0)，证明负坐标桌面可实现。

## 已知问题 / 风险

- 本机两块屏幕均为 100% 缩放，**混合 DPI 无法真实验证**，只能单元测试换算逻辑；最终验收须如实标注。
- Inno Setup 未安装，阶段四需尝试 winget 安装；失败则只交付便携版并说明。
- 尚无真实更新渠道地址（`AppInfo.UpdateManifestUrl` 为空），实现时必须显示"尚未配置更新渠道"。
- 无代码签名证书，安装包将为未签名。

## 决策记录

- 不使用 WinForms（`Application` 类型冲突），托盘用 Win32 `Shell_NotifyIcon`。
- Core 保持无 WPF 依赖，所有可测逻辑下沉到 Core。
- T1 几何类型统一放在 `ZeepCut.Core.Geometry` 命名空间，坐标为物理像素；DIP 只通过 `DpiTransform` 显式换算。
- `PixelRect` 边界为半开区间（Right/Bottom 不包含），`Clamp` 语义是"平移回屏幕内、保持尺寸"，只保留重叠部分请用 `Intersect`。
- Debug 构建不带 RID，便于快速编译；Release 分发时再 `-r win-x64 --self-contained`。
