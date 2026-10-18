# Zeep Cut — 当前状态

> 每轮工作结束时更新本文件。下一位接手者先读这里。

## 本轮快照

- 日期：2026-10-18
- 当前阶段：阶段一（截图闭环）
- 下一步任务：**T5 全局热键**
- 构建状态：✅ 编译通过，0 warning 0 error
- 测试状态：✅ 141 个测试通过（本轮未改动 Core，未新增测试）

## 已完成

- 2026-10-18 环境探测：Windows .NET SDK 10.0.100；WSL 经 UNC 调用 Windows dotnet 构建成功；桌面 GUI 可启动；双屏（主屏 2048×1152 @ 0,0；副屏 1080×1920 @ -1080,0）；均为 100% 缩放。
- 2026-10-18 工程骨架：`ZeepCut.slnx`、Core/App/Tests 三个项目、app.manifest（PerMonitorV2）、图标、构建与验证脚本（build/run/screenshot/keys）。
- 2026-10-18 文档：AGENTS.md、ARCHITECTURE.md、ROADMAP.md、PLAN.md、IMPLEMENTATION_HANDOFF.md。
- 2026-10-18 **T1 Core 几何与屏幕模型**：新增 `src/ZeepCut.Core/Geometry/`（纯逻辑，无 WPF/Win32 依赖）——`PixelRect`（Right/Bottom、IsEmpty、Intersect、Contains、Clamp、FromTwoPoints、Offset）、`MonitorInfo`（物理像素 Bounds/WorkArea、Dpi、IsPrimary）、`DisplaySpace`（点→屏幕查找，含负坐标与最近屏规则）、`DpiTransform`（物理像素↔DIP，scale=dpi/96）及 `DipRect`。xUnit 覆盖负坐标屏幕查找、x=0 边界归属主屏、选区夹取、任意方向拖拽、125%/150% 往返。
- 2026-10-18 **T2 Core 选区状态机**：新增 `src/ZeepCut.Core/Selection/`（纯逻辑，无 WPF/Win32 依赖）——`SelectionState`（None/HoverWindow/Dragging/Selected/Adjusting）、`SelectionHandle`（Move + 八向）、`SelectionController`。支持窗口候选悬停（按 Z 序取首个命中、裁剪到屏幕）、按下拖拽生成任意方向正矩形、抬起定稿/小于最小尺寸回退、单击候选窗口采用候选、选区内整体移动、八向调整（不翻转，拖过对面边缘钳制在最小尺寸）、所有几何夹取到目标屏幕、`IsCaptureReady`/`MeetsMinimumSize`（默认最小 4×4，可配置）、非法状态转移静默忽略。xUnit 覆盖完整状态序列、八手柄、翻转钳制、越界夹取、最小尺寸、负坐标屏幕与非法转移。
- 2026-10-18 **T3 应用外壳**：
  - 单实例：命名 Mutex `Local\ZeepCut.SingleInstance`；已有实例时弹标题为 “Zeep Cut” 的提示框后退出（首版不实现 IPC/激活已有窗口）。
  - 无主窗口，`App.xaml` 使用 `ShutdownMode=OnExplicitShutdown`；托盘常驻，只有托盘菜单“退出”才 `Shutdown()`（exit code 0）。
  - 全局异常：`DispatcherUnhandledException`（写日志 + 简短 MessageBox，`e.Handled=true`，不静默崩溃）、`AppDomain.UnhandledException`（写日志）、`TaskScheduler.UnobservedTaskException`（写日志 + `SetObserved()`）。
  - 日志：`%LOCALAPPDATA%\ZeepCut\logs\app-YYYYMMDD.log`，同一天追加；每行含本地毫秒时间戳、级别、来源、线程 ID，异常附带完整 `ToString()` 堆栈；写日志自身异常被吞掉，不影响应用。
  - `src/ZeepCut/Platform/AppPaths.cs`：数据根目录 `LocalAppData\ZeepCut`，子目录 `logs/`、`images/`、`thumbs/`，启动时创建；`GetLogFilePath` 按本地日期生成 `app-YYYYMMDD.log`。
- 2026-10-18 **T4 托盘**：
  - 纯 Win32，无 WinForms：`CreateWindowEx` 隐藏顶层窗口（class `ZeepCut.TrayMessageWindow`）接收 `WM_TRAYICON`；`Shell_NotifyIcon` 添加/删除；`ExtractIconEx` 从 exe 取 `Assets/app.ico`（`ApplicationIcon` 已嵌入），失败回退 `IDI_APPLICATION`；处理 `TaskbarCreated` 自动重加。
  - 菜单：`CreatePopupMenu` + `TrackPopupMenuEx`（`TPM_RETURNCMD | TPM_NONOTIFY`）；左键单击（延迟一个双击时间）与右键都弹菜单；`SetForegroundWindow` + 菜单关闭后 `PostMessage(WM_NULL)` 修正，避免菜单不消失。
  - 双击：写日志、不执行动作、不误弹菜单（吞掉双击后的 mouse-up）；进程保持运行。
  - 菜单项：截图、从剪贴板贴图、显示/隐藏全部贴图、历史、设置、检查更新、退出。截图/从剪贴板贴图为**空占位方法**（仅写日志）；显示/隐藏全部贴图、历史、设置、检查更新弹“尚未实现”提示；退出移除托盘图标后结束进程。
  - 新增验证脚本：`scripts/tray.ps1`（读取托盘图标 rect / 点击图标）、`scripts/mouse.ps1`（按屏幕坐标移动/单击/右键/双击）。
  - `scripts/run.ps1` 改用 `Invoke-CimMethod Win32_Process Create` 启动，规避本机 `Start-Process` 环境变量重复键异常与 WSL 管道继承（详见已知问题）。

## 验证证据

- 2026-10-18 T3/T4 构建+测试：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 141, Skipped: 0, Total: 141`；脚本末尾输出 `BUILD+TEST OK`。
- 2026-10-18 启动（真实桌面）：`scripts/run.ps1` → `STARTED pid 56932`、`RUNNING pid 56932 title ''`；`tasklist` 显示 Session 1 Console，无主窗口但进程持续运行。
- 2026-10-18 托盘图标（真实桌面 + UI Automation）：`Show Hidden Icons` 按钮 rect (1852,10,40,40)；展开 “System tray overflow window.” 后找到名称 `Zeep Cut` 的 `SystemTray.NormalButton`，rect (1747,81,50,50)，`IsOffscreen=False`。截图：`artifacts/t3t4/final-01-tray-flyout.png`（裁切 `final-01-tray-flyout-crop.png`，可见蓝底白色裁切角标的 Zeep Cut 图标）。
- 2026-10-18 托盘菜单（真实桌面）：单击托盘图标后弹出菜单，截图 `artifacts/t3t4/final-02-tray-menu.png`（裁切 `final-02-tray-menu-crop.png`），含“截图 / 从剪贴板贴图 / 显示/隐藏全部贴图 / 历史 / 设置 / 检查更新 / 退出”及两条分隔线。
- 2026-10-18 退出（真实桌面）：点击菜单“退出”后进程结束（`tasklist /fi "imagename eq ZeepCut.exe"` → `INFO: No tasks are running which match the specified criteria.`）。同一次运行的日志（04:26:07–04:26:08）依次为：
  `显示托盘菜单（cursor=1772,106）` → `菜单命令: Exit` → `退出：用户选择托盘菜单“退出”` → `托盘图标已从通知区域移除` → `退出完成 (exit code=0)`。
- 2026-10-18 单实例（真实桌面）：先启动 pid 59376，再启动第二实例 pid 59676；第二个进程出现标题 “Zeep Cut” 的提示框，内容为“Zeep Cut 已在运行。请使用任务栏通知区域（如未显示，请点击“显示隐藏的图标”）中的托盘图标。”截图：`artifacts/t3t4/desktop-13-single-instance.png`。关闭提示后第二个进程退出、第一个继续运行；日志有 `检测到已有实例，提示后退出` 与 `退出完成 (exit code=0)`。
- 2026-10-18 托盘移除：退出后再次打开溢出面板，截图中已无 Zeep Cut 图标（`artifacts/t3t4/desktop-12-after-exit.png`，裁切 `crop-12-after-exit-flyout.png`）。
- 2026-10-18 双击托盘：双击后进程保持运行；日志只有 `双击托盘图标（本轮未绑定动作）`，没有紧随其后的 `显示托盘菜单`，说明不会误弹菜单。
- 2026-10-18 数据目录：`C:\Users\19427\AppData\Local\ZeepCut\` 下自动创建 `images/`、`logs/`、`thumbs/`；`logs\app-20261018.log` 按天追加，包含多次启动/退出、菜单命令与双击记录。
- 2026-10-18 **未验证**：三个全局异常处理器只做了代码接线与正常路径日志验证，**未做真实崩溃注入**（Dispatcher/AppDomain/TaskScheduler 分支未实际触发）；混合 DPI 仍只能靠 Core 单测。
- 2026-10-18 T1/T2 旧证据保留：T1 74 个测试、T2 141 个测试；T2 未验证 WPF 遮罩真实鼠标/DPI 交互（接 UI 在 T9）。

## 已知问题 / 风险

- 托盘图标默认进入 Windows 11 的“显示隐藏的图标”溢出面板，不在任务栏直接可见；本轮验证即展开溢出面板后确认（`Show Hidden Icons` → `Zeep Cut`）。用户可手动固定到任务栏。
- 菜单占位：截图、从剪贴板贴图是空方法（仅写日志，不弹提示）；显示/隐藏全部贴图、历史、设置、检查更新弹“尚未实现”。后续任务接入后替换。
- 本轮未新增 Core 单元测试：T3/T4 都是 Win32/WPF 外壳代码，按 ARCHITECTURE 的测试策略用真实桌面验证；Core 未改动，141 个旧测试全绿。
- 本机 Windows PowerShell 5.1 的 `Start-Process` 会因环境变量 `NO_PROXY`/`no_proxy` 大小写重复键抛 `Item has already been added`；且 GUI 进程继承 WSL 管道会让调用一直等待。`scripts/run.ps1` 已改用 `Invoke-CimMethod Win32_Process Create`（立即返回、不继承管道）。GUI 自动化时 UIA 坐标与 `scripts/mouse.ps1` 坐标一致（本机托盘按钮 1747,81,50,50）。
- 本机两块屏幕均为 100% 缩放，**混合 DPI 无法真实验证**，只能单元测试换算逻辑；最终验收须如实标注。
- Inno Setup 未安装，阶段四需尝试 winget 安装；失败则只交付便携版并说明。
- 尚无真实更新渠道地址（`AppInfo.UpdateManifestUrl` 为空），实现时必须显示“尚未配置更新渠道”。
- 无代码签名证书，安装包将为未签名。

## 决策记录

- 不使用 WinForms（`Application` 类型冲突），托盘用 Win32 `Shell_NotifyIcon`。
- Core 保持无 WPF 依赖，所有可测逻辑下沉到 Core。
- T1 几何类型统一放在 `ZeepCut.Core.Geometry` 命名空间，坐标为物理像素；DIP 只通过 `DpiTransform` 显式换算。
- `PixelRect` 边界为半开区间（Right/Bottom 不包含），`Clamp` 语义是“平移回屏幕内、保持尺寸”，只保留重叠部分请用 `Intersect`。
- Debug 构建不带 RID，便于快速编译；Release 分发时再 `-r win-x64 --self-contained`。
- T2 状态机放在 `ZeepCut.Core.Selection` 命名空间：输入只有整数像素点和窗口候选矩形，输出 `PixelRect`，不接触 WPF/鼠标消息。
- T2 调整规则：**不翻转**。拖过对面边缘时停在 `MinimumSize`，拖出屏幕时停在屏幕边缘；整体移动保持尺寸由 `PixelRect.Clamp` 夹回。
- T2 最小尺寸默认 4×4（`SelectionController.MinimumSize` 可配置），`IsCaptureReady` 只在 `Selected` 且宽高均达标时为真。
- T2 窗口候选按传入顺序（Z 序从上到下）命中首个包含指针的矩形，并先与目标屏幕求交集；完全在屏幕外的候选丢弃。
- T3 单实例使用每会话命名 Mutex；已运行实例只提示后退出，不做 IPC 激活（首版允许，后续需要时再加 `WM_COPYDATA`/命名事件）。
- T3 日志用静态 `AppLog` 按天追加；日志失败不能影响主流程。UI 未处理异常记录后 `Handled=true` 并显示简短提示。
- T4 托盘用**隐藏顶层窗口**（非 message-only）承载 `WM_TRAYICON`，保证 `SetForegroundWindow` 与菜单消失修正有效；菜单命令经 `TrackPopupMenuEx` 同步取回后交回 Dispatcher 执行，避免在窗口过程里弹模态框或直接退出。
- T4 单击延迟取系统双击时间（最少 200ms）；双击后紧邻的 mouse-up 被吞掉，双击不触发菜单。
- T4 托盘图标从 exe 资源 `ExtractIconEx` 获取（`ApplicationIcon` 已嵌入 app.ico），不依赖 exe 旁的 ico 文件；提取失败回退系统默认图标。
- `scripts/run.ps1` 用 WMI 创建进程，规避 `Start-Process` 环境字典 bug 与 WSL 管道继承；`scripts/tray.ps1`、`scripts/mouse.ps1` 作为可复用的真实桌面验证工具。
