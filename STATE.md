# Zeep Cut — 当前状态

> 每轮工作结束时更新本文件。下一位接手者先读这里。

## 本轮快照

- 日期：2026-10-18
- 当前阶段：阶段一（截图闭环）
- 本轮任务：**T7 冻结遮罩窗口 + T8 窗口识别**
- 下一步任务：**T9 框选交互**（拖动框选、移动选区、八向调整、尺寸提示、限制在屏幕内）
- 构建状态：✅ 编译通过，0 warning 0 error
- 测试状态：✅ 274 个测试通过（本轮新增 20 个 WindowCandidateSelector 测试；T1/T2/T5 旧测试全绿）

## 已完成

- 2026-10-18 环境探测：Windows .NET SDK 10.0.100；WSL 经 UNC 调用 Windows dotnet 构建成功；桌面 GUI 可启动。
  **2026-10-18 T5/T6 复测修正**：主屏 `\\.\DISPLAY1` 物理 2560×1440 @ (0,0)、DPI 120（125%，逻辑 2048×1152）；副屏 `\\.\DISPLAY2` 物理 1080×1920 @ (-1080,0)、DPI 96（100%）。即本机当前是 **125% + 100% 混合 DPI**，此前“两块都是 100%、主屏物理 2048×1152”的记录基于逻辑分辨率，已在本轮更正。
- 2026-10-18 工程骨架：`ZeepCut.slnx`、Core/App/Tests 三个项目、app.manifest（PerMonitorV2）、图标、构建与验证脚本（build/run/screenshot/keys）。
- 2026-10-18 文档：AGENTS.md、ARCHITECTURE.md、ROADMAP.md、PLAN.md、IMPLEMENTATION_HANDOFF.md。
- 2026-10-18 **T1 Core 几何与屏幕模型**：`src/ZeepCut.Core/Geometry/`（纯逻辑，无 WPF/Win32 依赖）——`PixelRect`（Right/Bottom、IsEmpty、Intersect、Contains、Clamp、FromTwoPoints、Offset）、`MonitorInfo`（物理像素 Bounds/WorkArea、Dpi、IsPrimary）、`DisplaySpace`（点→屏幕查找，含负坐标与最近屏规则）、`DpiTransform`（物理像素↔DIP，scale=dpi/96）及 `DipRect`。xUnit 覆盖负坐标屏幕查找、x=0 边界归属主屏、选区夹取、任意方向拖拽、125%/150% 往返。
- 2026-10-18 **T2 Core 选区状态机**：`src/ZeepCut.Core/Selection/`（纯逻辑，无 WPF/Win32 依赖）——`SelectionState`（None/HoverWindow/Dragging/Selected/Adjusting）、`SelectionHandle`（Move + 八向）、`SelectionController`。支持窗口候选悬停（按 Z 序取首个命中、裁剪到屏幕）、按下拖拽生成任意方向正矩形、抬起定稿/小于最小尺寸回退、单击候选窗口采用候选、选区内整体移动、八向调整（不翻转，拖过对面边缘钳制在最小尺寸）、所有几何夹取到目标屏幕、`IsCaptureReady`/`MeetsMinimumSize`（默认最小 4×4，可配置）、非法状态转移静默忽略。xUnit 覆盖完整状态序列、八手柄、翻转钳制、越界夹取、最小尺寸、负坐标屏幕与非法转移。
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
- 2026-10-18 **T5 全局热键**：
  - Core 新增 `src/ZeepCut.Core/Shortcuts/ShortcutSpec.cs`：`ShortcutSpec` + `[Flags] ShortcutModifiers`（数值与 Win32 `MOD_*` 一致）。支持解析/格式化 `Alt+A` 这类文本；修饰键 Ctrl/Control、Alt、Shift、Win/Windows，大小写与顺序任意、不可重复；主键 A–Z、0–9、F1–F12；至少一个修饰键；非法输入返回明确中文原因或抛 `FormatException`；格式固定为 `Ctrl+Alt+Shift+Win+主键`；`record` 值相等按修饰键+虚拟键，`ConflictsWith` 用于冲突检测。
  - `tests/ZeepCut.Tests/Shortcuts/ShortcutSpecTests.cs`：解析/别名/大小写/顺序、全部主键往返、格式化顺序、22 种非法输入、TryParse、Equals/冲突、HashSet 去重、虚拟键边界。
  - WPF `src/ZeepCut/Platform/GlobalHotkey.cs`：专用 message-only `HwndSource`（`ParentWindow=HWND_MESSAGE`）接收 `WM_HOTKEY`；`RegisterHotKey` 带 `MOD_NOREPEAT`；一个实例可注册多个组合并按 id 映射；失败返回 Win32 错误码（1409=被占用）；`UnregisterAll`/`Dispose` 注销全部。
  - App 接线：注册 `Alt+A`→截图、`Alt+S`→剪贴板贴图（本轮仅写日志占位）；每次启动分别注册，单项失败不影响另一项。
  - 失败提示：`TrayIcon.ShowNotification` 用 `Shell_NotifyIcon(NIM_MODIFY + NIF_INFO)` 弹通知，并写 WARN 日志；由于本机通知横幅未被桌面截图捕捉到，额外在两项都注册完成后弹一次汇总 MessageBox，确保“快捷键 X 被占用，请修改”不会静默。
  - 退出：托盘“退出”与 `OnExit` 都调用 `DisposeGlobalHotkey`，日志逐项记录注销；托盘菜单“截图”改为触发真实捕获（不再是占位）。
- 2026-10-18 **T6 屏幕捕获**：
  - `src/ZeepCut/Capture/ScreenCapture.cs`：`GetCursorPos` → `MonitorFromPoint(MONITOR_DEFAULTTONEAREST)` → `GetMonitorInfo`（`MONITORINFOEXW`，物理像素 `rcMonitor`）定位目标屏幕；`GetDC(NULL)` + `CreateCompatibleDC/Bitmap` + `BitBlt(SRCCOPY|CAPTUREBLT)` 复制该矩形；负坐标屏幕直接把 `Left=-1080` 作为 BitBlt 源坐标。
  - 输出 `ScreenCaptureResult`：冻结的 `BitmapSource`（`BGR32` → `FormatConvertedBitmap(Pbgra32)` → 复制到 `WriteableBitmap` 后释放 GDI 位图，确保像素独立）、`Bounds`（物理像素，可负）、`DeviceName`、`IsPrimary`、`PixelWidth/PixelHeight`。
  - 调试落盘：`AppPaths.CaptureDebugDirectory` = `%LOCALAPPDATA%\ZeepCut\logs\capture-debug\`；文件名含时间、设备名、屏幕原点、像素尺寸；每次捕获写 INFO 日志（鼠标坐标、屏幕名、物理矩形、图像尺寸、文件路径）。
  - 触发入口：全局热键 `Alt+A` 与托盘菜单“截图”共用同一捕获方法；本轮捕获后只存 PNG，尚未显示冻结遮罩（T7）。
- 2026-10-18 **T7 冻结遮罩窗口**：
  - `Capture/ScreenCapture.cs` 的 `ScreenCaptureResult` 新增 `Dpi`：`GetDpiForMonitor(MDT_EFFECTIVE_DPI)` 取目标屏幕 DPI，失败回退 96 并 WARN。
  - 新增 `Capture/CaptureOverlayWindow.cs`：无边框、`Topmost`、`ShowInTaskbar=false`、`WS_EX_TOOLWINDOW`；用 `DpiTransform.ToDip(屏幕物理矩形)` 设置 WPF 的 DIP `Left/Top/Width/Height`；冻结图以 `Image(Stretch=Fill, NearestNeighbor)` 1:1 铺满；`Loaded` 时 `GetWindowRect` 复核物理矩形，不一致时 `SetWindowPos` 校正；Esc 取消；鼠标 DIP→物理换算后按 Z 序命中窗口，并绘制 3 DIP 蓝色边框。
  - `App.StartCapture`：**先捕获、再 `WindowEnumerator.Enumerate`、最后 `Show` 遮罩**；遮罩期间重复触发被忽略；托盘“截图”共用入口；退出时关闭遮罩。Alt+A 不再自动保存 debug PNG（T6 临时行为结束，`SaveDebugPng` 保留为诊断工具）。
- 2026-10-18 **T8 窗口识别**：
  - Core 新增 `ZeepCut.Core.Capture.WindowCandidate` + `WindowCandidateSelector`：`ClipToScreen` 保留 Z 序并丢弃空交集；`HitTest` 取首个命中；`HitTestDip` 按目标屏幕 DPI 把 DIP 换算为物理像素再加屏幕原点（负坐标）；`ToDipBounds` 供高亮绘制。
  - WPF 新增 `Capture/WindowEnumerator.cs`：`EnumWindows` 顶层 Z 序；过滤不可见、最小化、`WS_EX_TOOLWINDOW`、自身进程、DWM cloaked、`GetWindowRect` 失败、宽或高为 0；`DWMWA_EXTENDED_FRAME_BOUNDS` 优先、失败回退 `GetWindowRect`；最后用 Core 裁剪到目标屏幕，并按 Z# 记录候选（含标题）。
  - 新增 `tests/ZeepCut.Tests/Capture/WindowCandidateSelectorTests.cs`（20 个）：Z 序取首个命中、跨屏只保留交集、空交集丢弃、负坐标屏幕、125%/100% DIP 映射、半开边界、非法输入。
  - 新增验证脚本：`scripts/screenshot-physical.ps1`（PerMonitorV2 物理像素截图，支持按屏/整屏）、`scripts/window.ps1`（把窗口摆到物理坐标，用于副屏/跨屏验证）；`scripts/mouse.ps1` 改为 DPI-aware 并回读 `GetCursorPos`。

## 验证证据

- 2026-10-18 T5/T6 构建+测试：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 254, Skipped: 0, Total: 254`；脚本末尾 `BUILD+TEST OK`。
- 2026-10-18 真实显示器与 DPI（PerMonitorV2 探针 `SetProcessDpiAwarenessContext(-4)`）：
  `monitor \\.\DISPLAY1 rc=0,0 2560x1440 work=0,60 2560x1380 primary=True dpi=120x120`；
  `monitor \\.\DISPLAY2 rc=-1080,0 1080x1920 work=-1080,48 1080x1872 primary=False dpi=96x96`；
  `SM_XVIRTUALSCREEN=-1080 SM_CXVIRTUALSCREEN=3640 SM_CYVIRTUALSCREEN=1920`。
  即主屏 125%、副屏 100%，主屏逻辑尺寸正好 2048×1152（任务描述中的 2048×1152 是逻辑/DIP 尺寸，不是物理像素）。
- 2026-10-18 启动注册（真实桌面，`scripts/run.ps1`，pid 57848）：日志
  `已注册全局热键 Alt+A（id=1, modifiers=0x4001, vk=0x41）`、
  `已注册全局热键 Alt+S（id=2, modifiers=0x4001, vk=0x53）`。
- 2026-10-18 **主屏捕获（真实桌面）**：鼠标移到物理 (1800,900)，发 `Alt+A`（`scripts/keys.ps1 -Keys '%a'`）。日志：
  `热键触发：Alt+A（id=1）` → `鼠标 (1800,900) → 屏幕 \\.\DISPLAY1（主屏=True），物理矩形=(0,0) 2560x1440` →
  `图像=2560x1440`。PNG：`C:\Users\19427\AppData\Local\ZeepCut\logs\capture-debug\capture-20261018-043518-809-DISPLAY1-0x0-2560x1440.png`
  （副本 `artifacts/t5t6/primary-DISPLAY1-2560x1440.png`）。用 read_image 查看：2560×1440，内容为当时主屏桌面（浏览器/DSH 窗口，任务栏时钟 4:35:18 PM），非黑屏、无错位。
- 2026-10-18 **副屏捕获（真实桌面）**：鼠标移到物理 (-540,900)，发 `Alt+A`。日志：
  `鼠标 (-540,900) → 屏幕 \\.\DISPLAY2（主屏=False），物理矩形=(-1080,0) 1080x1920` → `图像=1080x1920`。
  PNG：`C:\Users\19427\AppData\Local\ZeepCut\logs\capture-debug\capture-20261018-043504-585-DISPLAY2--1080x0-1080x1920.png`
  （副本 `artifacts/t5t6/secondary-DISPLAY2-1080x1920.png`）。用 read_image 查看：1080×1920 竖屏，内容为副屏桌面（顶部任务栏时钟 4:35:04 PM、Windhawk 窗口与壁纸），负坐标原点正确、非黑屏、无错位。
- 2026-10-18 副屏捕获路径中的 `--1080x0` 是“屏幕原点 -1080,0”拼出的文件名，非笔误。
- 2026-10-18 **Alt+S 占位**：发 `Alt+S` 后日志 `热键触发：Alt+S（id=2）` → `从剪贴板贴图触发（全局热键 Alt+S）：占位入口，T21/T22 实现`；未创建贴图窗口（符合本轮范围）。
- 2026-10-18 **热键冲突（真实桌面）**：用独立 PowerShell 进程先 `RegisterHotKey(Alt+A)` 占用，再启动应用。日志：
  `注册 Alt+A 失败（id=1, Win32Error=1409）: 快捷键 Alt+A 被占用，请修改。` →
  `已显示通知：全局热键注册失败 - 快捷键 Alt+A 被占用，请修改。` →
  `已注册全局热键 Alt+S（id=2, ...）` →
  `启动时共有 1 个热键注册失败，已提示用户`。
  提示对话框截图：`artifacts/t5t6/hotkey-conflict-dialog.png`，内容为“快捷键 Alt+A 被占用，请修改。”。
  关闭对话框后发 `Alt+S` 仍触发（日志 04:38:29 `热键触发：Alt+S`），证明一项失败不影响另一项。
  **注**：`Shell_NotifyIcon` 返回成功（日志“已显示通知”），但本次自动截图未捕捉到通知横幅（可能被系统通知/专注设置抑制）；因此增加了汇总 MessageBox 兜底并截图证明提示确实可见。
- 2026-10-18 **退出注销（真实桌面）**：正常注册 Alt+A/Alt+S 后通过托盘菜单“退出”（`PostMessage(WM_TRAYICON)` 打开菜单，点击“退出”）。日志：
  `已注销全局热键 Alt+A（id=1）` → `已注销全局热键 Alt+S（id=2）` → `托盘图标已从通知区域移除` → `退出完成 (exit code=0)`；
  之后 `Get-Process ZeepCut` 无输出（进程已结束）。
- 2026-10-18 **T7/T8 构建+测试（最终代码）**：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 274, Skipped: 0, Total: 274`（本轮新增 20）；末尾 `BUILD+TEST OK`。
- 2026-10-18 **主屏遮罩（真实桌面，pid 33848）**：鼠标物理 (1800,900) → `Alt+A`。日志：
  `鼠标 (1800,900) → 屏幕 \\.\DISPLAY1（主屏=True），物理矩形=(0,0) 2560x1440 DPI=120` →
  `冻结遮罩：屏幕=\\.\DISPLAY1 主屏=True 物理=(0,0) 2560x1440 图像=2560x1440 DPI=120 → DIP=(0,0) 2048x1152，窗口候选=8` →
  `Loaded：遮罩物理矩形与目标屏幕一致 (0,0) 2560x1440` →
  `遮罩窗口：... DPI=120（WPF PixelsPerDip=1.25）；期望 DIP=(0,0) 2048x1152，实际 DIP=2048x1152`。
  截图 `artifacts/t7t8/primary-overlay.png`（2560×1440，read_image 查看：整屏冻结画面，DSH 窗口周围有蓝色高亮边框）。
  候选 `Z#0 "Windows截图工具规划与实现 — DeepSeek Harness" (534,245) 1603x1033`；鼠标处命中该窗口，日志 `边框 DIP=(427.2,196) 1282.4x826.4`（= 物理/1.25）。
- 2026-10-18 **冻结证明（真实桌面）**：遮罩显示期间用 WMI 启动 notepad（PID 58816），再次截主屏 →
  `artifacts/t7t8/primary-overlay-notepad-frozen.png` 与 `primary-overlay.png` **SHA-256 完全相同**
  （489,862 bytes，`8e2bb1d4186c9feeae724c9c9361c80d0ee19bc7d52e3559e0828d642bf1bb64`），任务栏时钟停在 4:49:10；
  按 Esc 后 `artifacts/t7t8/primary-after-esc.png` 显示 Notepad 已出现、时钟 4:49:30。证明遮罩期间画面确实冻结、Notepad 没有被画进冻结图。
- 2026-10-18 **Esc 取消（真实桌面）**：日志 `取消本次截图（Esc）：屏幕=\\.\DISPLAY1 ... 不写入历史` →
  `遮罩窗口已关闭 ... 已取消=True` → `截图结束 ... 已取消=True`；`Get-Process ZeepCut` 仍返回 pid 33848；
  `%LOCALAPPDATA%\ZeepCut` 下只有日志（无 `history.db`、`images/` 文件），本次未产生任何记录（历史功能尚未实现）。
- 2026-10-18 **副屏（负坐标）遮罩（真实桌面）**：鼠标物理 (-540,900) → `Alt+A`。日志
  `屏幕 \\.\DISPLAY2（主屏=False），物理矩形=(-1080,0) 1080x1920 DPI=96` →
  `冻结遮罩：... 图像=1080x1920 DPI=96 → DIP=(-1080,0) 1080x1920`。
  **WPF 首次摆放副屏时确实按主屏 125% 换算错了**：
  `Loaded：遮罩物理矩形 (-1350,0) 1350x1942 与目标屏幕 (-1080,0) 1080x1920 不一致，用 SetWindowPos 校正` →
  `Loaded：校正后遮罩物理矩形 (-1080,0) 1080x1920` → `期望 DIP=(-1080,0) 1080x1920，实际 DIP=1080x1920`。
  截图 `artifacts/t7t8/secondary-overlay.png`（1080×1920，壁纸铺满，无黑边/错位）。
- 2026-10-18 **副屏窗口高亮（真实桌面）**：用 `scripts/window.ps1` 把 Notepad 摆到副屏物理 (-950,300)，触发后候选
  `Z#0 0x3B80F78 (-943,300) 386x313 "Untitled - Notepad"`；鼠标物理 (-700,450) 时日志
  `高亮窗口候选 ... 边框 DIP=(137,300) 386x313`（137 = -943 - (-1080)）。
  截图 `artifacts/t7t8/secondary-overlay-highlight.png`：Notepad 周围蓝色边框，位置正确。
- 2026-10-18 **跨屏窗口裁剪（真实桌面）**：Notepad 摆到物理 (-500,300,640x320)（跨越 x=0），在副屏触发后主屏枚举到
  `Z#1 0x54079C (0,300) 133x313 "Untitled - Notepad"`（只保留主屏交集）；主屏悬停物理 (100,400) 命中该候选，
  截图 `artifacts/t7t8/cross-screen-clipped-highlight.png`：只在主屏左边缘显示 133px 宽的截断高亮。
- 2026-10-18 **重复触发保护（真实桌面）**：遮罩显示期间再按 `Alt+A`，日志
  `已有截图遮罩显示中，忽略本次触发：全局热键 Alt+A`，没有出现第二张遮罩。
- 2026-10-18 **重编译后冒烟（最终代码）**：重新构建（274 测试通过）后启动（pid 52688），主屏 `Alt+A` → 遮罩物理 (0,0) 2560x1440、
  DIP 2048x1152、命中 DSH 窗口；Esc 关闭，日志与前述一致；截图 `artifacts/t7t8/final-smoke-primary.png`。
- 2026-10-18 **未验证**：
  - 非 1409 的热键注册失败分支（如权限/参数异常）未注入。
  - 通知气球/Toast 的视觉呈现未能在截图中确认（见上）；`MessageBox` 兜底已真实验证。
  - 150% 缩放、三屏以上、竖屏以外的特殊排列未测试（本机只有 125%+100% 双屏）。
  - 窗口过滤中的“最小化 / `WS_EX_TOOLWINDOW` / cloaked”没有单独注入验证；只验证了真实桌面上枚举结果正确（主屏 8–9 个候选、副屏 0/1 个候选）且本进程遮罩未进入候选。
  - 遮罩聚焦后被新启动的窗口（如测试用 Notepad）抢焦点时，Esc 不会送到遮罩；验证时先点击遮罩再按 Esc。正常截图过程中不会新建窗口，但这是已知的焦点限制。
  - 仅验证了 Esc 关闭；Alt+F4 / 托盘退出关闭遮罩路径只从代码上处理，未逐一真实验证（退出时 `DisposeCaptureOverlay` 会视作取消）。
  - 受保护内容、HDR、安全桌面等特殊捕获场景按 PLAN 不承诺，未测试。

## 已知问题 / 风险

- 托盘图标默认进入 Windows 11 的“显示隐藏的图标”溢出面板，不在任务栏直接可见；用户可手动固定到任务栏。
- 菜单占位：从剪贴板贴图仍是空方法（仅写日志）；显示/隐藏全部贴图、历史、设置、检查更新弹“尚未实现”。截图菜单已接真实捕获与冻结遮罩。
- `capture-debug` 是 T6 临时调试产物；T7 起 Alt+A 不再自动落盘，`ScreenCapture.SaveDebugPng` 仅保留为诊断方法，目录仍由 `AppPaths.EnsureCreated` 创建。
- 本机是**混合 DPI**（主屏 125%、副屏 100%）。T7 实测：主屏 WPF 按目标 DIP 摆放一次成功（物理 (0,0) 2560×1440、DIP 2048×1152）；副屏 WPF **首次按主屏 125% 摆放成 (-1350,0) 1350×1942**，由 `Loaded` 里的 `GetWindowRect` 复核 + `SetWindowPos` 校正为 (-1080,0) 1080×1920。当前做法已真实验证正确，但依赖“显示后校正”这一步；若以后 WPF 行为变化或出现可见闪烁，优先检查这里。
- 遮罩窗口显示期间点击左键目前没有动作（T9 才实现框选）；Esc 需要遮罩处于键盘焦点。若截图期间有新窗口抢焦点，Esc 可能收不到，需要先点一下遮罩。
- 主屏物理 2560×1440、逻辑 2048×1152。若按旧文档期待“2048×1152 PNG”会误判；产品按 PLAN/ARCHITECTURE 使用物理像素，捕获结果 2560×1440 是正确行为。
- Windows 通知横幅在本机自动截图中不可见；热键冲突已用启动 MessageBox 兜底，不影响另外的热键注册。
- 本机 Windows PowerShell 5.1 的 `Start-Process` 会因环境变量 `NO_PROXY`/`no_proxy` 大小写重复键抛 `Item has already been added`；且 GUI 进程继承 WSL 管道会让调用一直等待。`scripts/run.ps1` 已改用 `Invoke-CimMethod Win32_Process Create`（立即返回、不继承管道）。GUI 自动化时 UIA 坐标与 `scripts/mouse.ps1` 坐标一致（本机托盘按钮 1747,81,50,50）。
- `scripts/screenshot.ps1` 是 DPI-unaware 的 WinForms 脚本：在本机主屏上截到的是逻辑 2048×1152，而产品捕获为物理 2560×1440。需要物理像素基准时用新增的 `scripts/screenshot-physical.ps1`。
- T7 起 `scripts/mouse.ps1` 自己声明 PerMonitorV2 并回读 `GetCursorPos`；`scripts/window.ps1` 用物理坐标摆放测试窗口，`scripts/screenshot-physical.ps1` 截物理像素。
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
- T5 快捷键模型放 `ZeepCut.Core.Shortcuts`：**必须至少一个修饰键**（无修饰键的全局热键会吞普通输入）；规范化顺序 `Ctrl+Alt+Shift+Win+主键`；值相等只看修饰键+虚拟键，供冲突检测与字典键。
- T5 `GlobalHotkey` 用 message-only `HwndSource`（`HWND_MESSAGE`）承载 `WM_HOTKEY`，不依赖主窗口；`MOD_NOREPEAT` 防长按连发；热键动作经 Dispatcher 执行，不在窗口过程里做捕获/弹窗。
- T5 失败处理分级：`Shell_NotifyIcon` 通知 + WARN 日志（主）；两项注册完成后汇总 MessageBox（兜底，保证不被系统通知抑制时也可见）；另一项继续注册。
- T5 退出路径统一在 `App.OnExit` 注销，托盘“退出”提前注销一次（幂等），保证进程正常结束前释放全局热键。
- T6 捕获坐标以 `MONITORINFOEXW.rcMonitor` 的物理像素为准（进程 PerMonitorV2）；负坐标屏幕不做平移，直接作为 BitBlt 源坐标，避免“负坐标转正”的错位。
- T6 输出先 `FormatConvertedBitmap` 转 `Pbgra32`，再复制到 `WriteableBitmap` 并 `Freeze`，保证 GDI 位图释放后像素仍有效，也能跨线程交给 T7 遮罩。
- T6 调试 PNG 放 `logs/capture-debug/`，文件名带设备/原点/尺寸，便于按屏幕核对；T7 接入后不再需要每张都落盘。
- T7 遮罩用 WPF DIP 属性按“目标屏幕 DPI”摆放，但**不盲信首次摆放**：`Loaded` 用 `GetWindowRect` 比对物理矩形，不一致时 `SetWindowPos` 校正。实测副屏首次会被 WPF 按主屏 125% 算错，这一步是混合 DPI 下正确性的保证。
- T7 遮罩不透明、不调暗、不加工具栏（T9/T10 再做），冻结图 1:1 `Fill` 铺满；遮罩设 `WS_EX_TOOLWINDOW` 并排除在任务栏/Alt+Tab 之外。
- T7 捕获、窗口枚举都在 `Show()` 之前完成；`App` 用 `_captureOverlay` 单实例守卫，重复 Alt+A 只记日志；退出时把遮罩按取消关闭。
- T8 窗口候选模型（`WindowCandidate`/`WindowCandidateSelector`）放 Core：裁剪、Z 序命中、DIP→物理映射都可单测；WPF `WindowEnumerator` 只负责 Win32 枚举与 DWM 边框。
- T8 过滤链：可见、非最小化、非 `WS_EX_TOOLWINDOW`、非自身进程、非 DWM cloaked、`GetWindowRect` 成功、宽高 > 0；DWM 扩展边框失败回退 `GetWindowRect`，最终交集在 Core 完成。
- T8 高亮用 3 DIP 蓝色边框（内缩半个线宽）；鼠标位置用 `HitTestDip` 按目标屏幕 DPI 换算，避免把 WPF DIP 当物理像素。
- T8 新增 `screenshot-physical.ps1` / `window.ps1` 并把 `mouse.ps1` 改成 DPI-aware，混合 DPI 下的桌面验证不再依赖临时脚本。
