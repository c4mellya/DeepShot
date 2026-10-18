# Zeep Cut — 当前状态

> 每轮工作结束时更新本文件。下一位接手者先读这里。

## 本轮快照

- 日期：2026-10-18
- 当前阶段：阶段一（截图闭环）
- 下一步任务：**T3 应用外壳**
- 构建状态：✅ 编译通过，0 warning 0 error
- 测试状态：✅ 141 个测试通过（T2 新增 67 个用例 + T1/smoke 74 个）

## 已完成

- 2026-10-18 环境探测：Windows .NET SDK 10.0.100；WSL 经 UNC 调用 Windows dotnet 构建成功；桌面 GUI 可启动；双屏（主屏 2048×1152 @ 0,0；副屏 1080×1920 @ -1080,0）；均为 100% 缩放。
- 2026-10-18 工程骨架：`ZeepCut.slnx`、Core/App/Tests 三个项目、app.manifest（PerMonitorV2）、图标、构建与验证脚本（build/run/screenshot/keys）。
- 2026-10-18 文档：AGENTS.md、ARCHITECTURE.md、ROADMAP.md、PLAN.md、IMPLEMENTATION_HANDOFF.md。
- 2026-10-18 **T1 Core 几何与屏幕模型**：新增 `src/ZeepCut.Core/Geometry/`（纯逻辑，无 WPF/Win32 依赖）——`PixelRect`（Right/Bottom、IsEmpty、Intersect、Contains、Clamp、FromTwoPoints、Offset）、`MonitorInfo`（物理像素 Bounds/WorkArea、Dpi、IsPrimary）、`DisplaySpace`（点→屏幕查找，含负坐标与最近屏规则）、`DpiTransform`（物理像素↔DIP，scale=dpi/96）及 `DipRect`。xUnit 覆盖负坐标屏幕查找、x=0 边界归属主屏、选区夹取、任意方向拖拽、125%/150% 往返。
- 2026-10-18 **T2 Core 选区状态机**：新增 `src/ZeepCut.Core/Selection/`（纯逻辑，无 WPF/Win32 依赖）——`SelectionState`（None/HoverWindow/Dragging/Selected/Adjusting）、`SelectionHandle`（Move + 八向）、`SelectionController`。支持窗口候选悬停（按 Z 序取首个命中、裁剪到屏幕）、按下拖拽生成任意方向正矩形、抬起定稿/小于最小尺寸回退、单击候选窗口采用候选、选区内整体移动、八向调整（不翻转，拖过对面边缘钳制在最小尺寸）、所有几何夹取到目标屏幕、`IsCaptureReady`/`MeetsMinimumSize`（默认最小 4×4，可配置）、非法状态转移静默忽略。xUnit 覆盖完整状态序列、八手柄、翻转钳制、越界夹取、最小尺寸、负坐标屏幕与非法转移。

## 验证证据

- 2026-10-18 T1：`powershell.exe ... -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 74, Skipped: 0, Total: 74`（161 ms）；脚本末尾输出 `BUILD+TEST OK`。
- 2026-10-18 T1 单测覆盖：负坐标屏（-1080..0）点查找、双屏 x=0 归属主屏、选区越界/完全在外/部分重叠夹取、任意方向拖拽正矩形、125%/150% 像素↔DIP 换算与往返。
- 2026-10-18 T2：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 141, Skipped: 0, Total: 141`（127 ms）；脚本末尾输出 `BUILD+TEST OK`。
- 2026-10-18 T2 单测覆盖：悬停→按下→拖动→抬起的逐阶段状态断言；四方向拖拽均得正矩形；拖出屏幕夹取；最小尺寸 4×4 边界（3×4/4×3 无效、4×4 有效，可配置最小尺寸）；单击/微拖窗口候选采用候选；八手柄调整各自结果；拖过对面边缘钳制在最小尺寸而不翻转；八手柄与整体移动越界夹回；HitTest 角/边/中心/开边界；非法转移不抛异常且状态正确；负坐标屏幕（-1080..0）拖拽与夹取。
- 2026-10-18 T2 未验证项：WPF 遮罩窗口中的真实鼠标/DPI 交互未验证（本轮只做 Core 纯逻辑，接 UI 在 T9）。
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
- T2 状态机放在 `ZeepCut.Core.Selection` 命名空间：输入只有整数像素点和窗口候选矩形，输出 `PixelRect`，不接触 WPF/鼠标消息。
- T2 调整规则：**不翻转**。拖过对面边缘时停在 `MinimumSize`，拖出屏幕时停在屏幕边缘；整体移动保持尺寸由 `PixelRect.Clamp` 夹回。
- T2 最小尺寸默认 4×4（`SelectionController.MinimumSize` 可配置），`IsCaptureReady` 只在 `Selected` 且宽高均达标时为真。
- T2 窗口候选按传入顺序（Z 序从上到下）命中首个包含指针的矩形，并先与目标屏幕求交集；完全在屏幕外的候选丢弃。
