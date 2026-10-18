# Zeep Cut — 当前状态

> 每轮工作结束时更新本文件。下一位接手者先读这里。

## 本轮快照

- 日期：2026-10-18
- 当前阶段：阶段二（标注与贴图）
- 本轮任务：**T16 标注编辑 + T17 导出渲染**（接进真实截图遮罩，全部真实桌面验证）
- 下一步任务：**T18 贴图基础**（无边框置顶贴图窗口、拖动、滚轮缩放、关闭、多张并存）。注意 T11 的 `PinWindow` 仍是最小占位，T17 已把贴图输入换成合成图（`AnnotationExportRenderer`），T18 只需在此基础上补交互与集合管理。
- 构建状态：✅ 编译通过，0 warning 0 error
- 测试状态：✅ 625 个测试通过（T16 新增 `AnnotationEditControllerTests` 32 个、`MosaicPixelatorTests` 16 个；T1–T15 旧测试全绿）
- 桌面验证：✅ 主屏 125% 七种标注导出（含马赛克真实像素化）+ 副屏 100% 导出尺寸 + 选中/移动/缩放/删除/撤销 + 文字双击重编辑 + 复制/另存为/贴图三条输出路径

## 已完成

- 2026-10-18 **T16 标注编辑 + T17 导出渲染**（消费 T12/T13/T14/T15，未改动已通过的 Core 类型；只新增类型 + 在既有类型上加成员）：
  - Core 新增 `src/ZeepCut.Core/Annotations/AnnotationHandle.cs`：`AnnotationHandle`（`None`/`Move`/八向/`StartPoint`/`EndPoint`）与 `AnnotationEditMode`（`None`/`Move`/`Resize`/`StartPoint`/`EndPoint`）。与选区的 `SelectionHandle` 是两套语义，不要混用。
  - Core 新增 `src/ZeepCut.Core/Annotations/AnnotationEditController.cs`（**T16 状态机**，纯逻辑可单测）：
    - 选中：`Select`/`ClearSelection`/`HasSelection`/`Selected`/`SelectionBounds`；`HitTestHandle`（手柄半径默认 10px）与 `GetHandleRects`（区域型 8 个、线段型 2 个端点、画笔/文字无手柄）。
    - 按下：`BeginDrag(point, handleRadius, hitTolerance)` 先试选中标注的手柄，再走 `Document.HitTest`（命中即选中 + 整体平移）；`DragTo` 实时改几何（**不写历史**，用于实时预览）；`EndDrag` 比较初始/最终几何，**一次拖拽 = 一条历史**（先回滚到初始状态再用命令应用最终状态，保证 Execute/Undo 对称）；`CancelDrag` 恢复初始几何且不写历史。
    - 支持范围（**本轮决策**）：矩形/椭圆/马赛克支持八向缩放；箭头/直线支持整体平移 + 拖两端点；画笔/文字只支持整体平移（不提供缩放，避免点串/字形被拉伸）。所有几何夹取在图像 `[0,W]×[0,H]`，缩放到 `MinimumSize = 4px` 停住（不翻转）。
    - `DeleteSelected()`（`RemoveAnnotationCommand`）、`SetSelectedText()`（`AnnotationEdits.SetText`）、`ValidateSelection()`（撤销后选中对象已离开文档时取消选中）。
  - Core 新增 `src/ZeepCut.Core/Annotations/MosaicPixelator.cs`（**T17 马赛克真实像素化**，纯逻辑可单测）：BGRA 字节数组 + 宽高 + 区域 + 块大小（钳制 4–64）+ 强度（钳制 0–1，NaN 回退默认，0 = 不处理）；块网格以**区域左上角**为原点，边缘不完整块只对落在图像内/区域内的像素求平均；逐通道 `original×(1-strength) + average×strength` 四舍五入，alpha 不变；`Apply`（原地，Span）与 `ApplyCopy`；区域越界/为空/在图像外都是安全 no-op；缓冲区不足抛 `ArgumentException`。
  - Core `src/ZeepCut.Core/Editing/AnnotationCommands.cs` 新增两条命令（**不改动已有命令**）：`ResizeAnnotationCommand`（记录调整前后包围盒）、`SetSegmentEndpointsCommand`（记录前后两端点）；`AnnotationEdits` 增加 `Resize`/`SetEndpoints` 工厂。
  - Core `AnnotationSession` 增补（T16 接线，**既有行为不变**）：新增构造 `AnnotationSession(PixelRect imageBounds, int historyCapacity = 100)` 创建 `Editing`（`AnnotationEditController`）、`HasSelection`、`DeleteSelected()`、`ClearSelection()`；`Undo`/`Redo` 在拖拽中也返回 false，并在撤销/重做后 `ValidateSelection`；`SetActiveTool(非 null)` 会取消选中（选了绘制工具就不再编辑已有标注）。
  - Core `AnnotationDefaults.DefaultMosaicStrength` 由 **0.75 改为 1.0**：PLAN 要求马赛克是真实像素化，0.75 会保留 25% 原像素、细看仍能辨认原文字。强度仍可 0–1 调整。
  - App 新增 `src/ZeepCut/Editor/AnnotationExportRenderer.cs`（**T17 导出渲染**）：
    - `Render(source, selection, screenBounds, dpi, document)`：`SelectionCrop.ToImageRect` 换算出选区在冻结图内的裁剪矩形 → `CaptureCropper.Crop` → 转 BGRA32；按文档 Z 序遍历，遇到马赛克先把累积标注合成为位图、再对**当前像素**做块平均（所以"马赛克盖在矩形上"与预览一致）；最后校验 `PixelWidth/PixelHeight` **必须等于选区物理像素**，否则抛异常。
    - DPI：`RenderTargetBitmap(width, height, dpi, dpi, Pbgra32)` + 绘制上下文压 `ScaleTransform(96/dpi)`，于是 1 绘制单位 = 1 物理像素；125% 屏幕不会输出 80% 的 DIP 尺寸。
    - 只画"选区内的冻结图 + 标注"两样东西；遮罩压暗、选区白框、手柄、尺寸文字、工具栏从不参与渲染。
  - App `Editor/AnnotationRenderer.cs`：`Draw` 增加接收 `Func<MosaicAnnotation, BitmapSource?>` 的重载；马赛克优先铺**真实像素化位图**，拿不到时退回块状示意（仅兜底）。预览与导出共用同一套绘制代码。
  - App `Editor/AnnotationLayer.cs`：新增可选参数 `sourceImage` / `cropRect` 与马赛克位图缓存（键 = 可见区域 + 块大小 + 强度），按 `MosaicPixelator` 对冻结图原像素做像素化；`InvalidateMosaicCache()` 供选区变化时清缓存。预览马赛克从此也是真实像素化。
  - App `Capture/CaptureOverlayWindow.cs`（T16/T17 主战场）：
    - 会话改为 `new AnnotationSession(new PixelRect(0, 0, image.Width, image.Height))`；新增 `_editorPointerDown`、`_editingText`/`_editingTextOriginal`、双击时间戳字段。
    - 新增选中态层 `_editorCanvas`（虚线框 `_editorOutline` + 手柄 `_editorHandles`），叠在标注之上、工具栏之下，`IsHitTestVisible=false`；`UpdateEditorVisuals()` 走 `AnnotationCanvasTransform.ToDipRect` 换算。
    - 鼠标：选择模式（无绘制工具）+ 选区已定稿 + 文档非空时，`BeginAnnotationEditAt` 先试手柄、再试文档命中；拖拽走 `DragTo`/`EndDrag` 并写日志（方式/手柄/终点/包围盒/记录/栈深度）；`LostMouseCapture` 用 `EndDrag` 收尾。
    - 键盘：`Delete`/`Back` 在有选中时删除（没有选中不吞键）；**Esc 优先级**为 草稿 → 拖拽中取消 → 有选中先取消选中（遮罩保持打开）→ 才是取消截图；双击文字进入重编辑时 Enter/Esc 归输入框（与 T15 同一套两层拦截）。
    - 双击：`RegisterClick()` **每次按下都记录**时间/位置（400ms、3px 容差），消费后清零避免三击；双击已选/命中的文字标注 → 把原文放回输入框，提交时 `SetSelectedText` 记一条"修改文字"，取消则还原原文本。
    - 输出：`CropSelection` 替换为 `RenderSelection`，**复制/另存为/贴图三条路径全部走合成渲染**；日志记录 `标注=N 个（马赛克=M 个）` 与 `尺寸匹配选区物理像素=True`。`ICaptureSink` 仍只在成功路径调用一次。
  - 新增验证脚本：
    - `scripts/verify-t16t17.ps1`（6 个场景：`export`/`edit`/`textedit`/`save`/`pin`/`sizes`）——全程物理像素坐标、UIA 按 `AutomationProperties.Name` 点按钮、独立进程读剪贴板（`scripts/clipboard.ps1`）、保存对话框用"点击文件名字段 + 剪贴板粘贴 + `BM_CLICK`"驱动。
    - `scripts/texture-window.ps1`（验证专用，允许 WinForms）：在指定物理矩形画 12px 彩色块 + 密集文字，让马赛克像素化有可比对的高频纹理；用 `SetWindowPos(-4 DPI aware)` 强制物理尺寸。
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
- 2026-10-18 **T9 框选交互**：
  - Core 新增 `src/ZeepCut.Core/Selection/SelectionOverlayLayout.cs`（纯逻辑）：`FormatSize`（物理像素 `640 × 480`）、`PlaceToolbar`（下方优先 / 上方回退 / 水平夹取 / 上下都放不下时贴边）、`PlaceSizeBadge`（右上方优先 / 下方回退 / 夹取）、`ToPhysical`（窗口内 DIP → 屏幕物理像素，用于日志与自动化点击）。**`SelectionController` 未改动**，UI 全部复用其状态机与几何。
  - `Capture/CaptureOverlayWindow.cs` 重写为交互遮罩：`PreviewMouseLeftButtonDown/Up` + `MouseMove` + 鼠标捕获 + 丢失捕获兜底；鼠标 DIP 先按目标屏 DPI 换算成物理像素（`Bounds.Left/Top + DipToPixel`）再喂 Controller，Controller 结果用 `WindowCandidateSelector.ToDipBounds` 画回 DIP。覆盖悬停预览、拖拽、抬手定稿、整体移动、八向调整、屏幕夹取。
  - 视觉：选区外半透明黑（`GeometryGroup` + `FillRule.EvenOdd` 挖洞；无选区时整屏压暗）；悬停候选 3 DIP 蓝框；定稿/调整时 1 DIP 白框 + 8 个 8 DIP 白色手柄；拖动/调整时在选区右上方显示物理像素尺寸标签（定稿后由工具栏显示尺寸）。
  - 光标按命中的手柄切换（Cross / SizeAll / SizeWE / SizeNS / SizeNWSE / SizeNESW），手柄命中容差 6 物理像素，最小选区 4×4。
- 2026-10-18 **T10 截图工具栏**：
  - `Selected` 且达到最小尺寸时显示遮罩内浮层工具栏（尺寸文本 + 复制/另存为/贴图/取消）；优先选区下方，下方放不下放上方，水平方向夹取在屏幕内；无选区/选区过小时不显示。
  - 按钮本轮全部只写日志占位（复制/另存为/贴图分别标注 T11/T18）；**“取消”真实关闭遮罩**。键盘 Enter → 复制占位，Esc → 取消关闭；按钮 `Focusable=false`，键盘焦点始终留在遮罩；点击工具栏区域不会触发选区状态机。
  - 工具栏显示时把工具栏与四个按钮的物理矩形写入日志（便于真实桌面自动化点击）。UIA 按“进程 id + 标题前缀 `Zeep Cut`”找遮罩窗口，按 `ControlType.Button` + `Name="取消"` 定位按钮。
  - 验证脚本：新增 `scripts/verify-t9t10.ps1`（`selection` / `reverse` / `window-click` / `tiny` / `toolbar-above` / `handles` / `drag-badge` 七种场景，自动 UIA 点击“取消”）；`scripts/mouse.ps1` 增加 `-ToX/-ToY` 真实拖动；`scripts/screenshot-physical.ps1` 增加 `-Primary` / `-DeviceName`（屏幕枚举顺序不保证与 DISPLAY 编号一致）。

- 2026-10-18 **T11 输出动作**：
  - Core 新增 `src/ZeepCut.Core/Output/`：
    - `SelectionCrop.ToImageRect`：屏幕绝对物理像素选区 → 冻结图内裁剪矩形，**减去目标屏幕原点**（主屏 0,0；副屏 -1080,0）；与屏幕无交集返回空；`IsInsideImage` 按半开区间校验。
    - `CaptureFileName`：默认名 `截图_yyyy-MM-dd_HHmmss.png`（JPEG 为 `.jpg`）；Windows 非法字符/控制字符替换为 `_`、去掉结尾点/空格、超长截断、空名回退“截图”；`TryGetFormatFromExtension` 识别 .png/.jpg/.jpeg；`CaptureImageFormat` 枚举。
  - App 新增 `src/ZeepCut/Output/`：
    - `ICaptureSink` + `CaptureCompletion`（动作、裁剪图、选区、屏幕、文件路径、本地时间、耗时）+ `LoggingCaptureSink`：**成功路径**写一条 INFO（动作/选区/尺寸/耗时/文件/时间）；T24 只需替换 sink 实现即可接历史。
    - `CaptureCropper`：Core 换算 + `CroppedBitmap`，校验选区与冻结图越界，结果冻结。
    - `CaptureImageEncoder`：PNG 保留 alpha；JPEG 先转 Bgr24 去 alpha，质量 90。
  - `Capture/CaptureOverlayWindow.cs`：复制/另存为/贴图按钮与 Enter 接**真实动作**；成功才写完成记录并关闭遮罩；工具栏内联状态文字（失败橙色、取消绿色）；复制用 `Clipboard.SetImage` 并单独 catch `COMException`；另存为用 `SaveFileDialog`（PNG/JPEG 过滤器、默认本地时间名）；取消对话框只写取消日志并留在截图状态。
  - 新增 `Pins/PinWindow.cs`：T11 最小贴图占位（无边框、Topmost、1:1 显示裁剪图、右上 × 与 Esc 关闭、按目标屏 DPI 定位 + Loaded 物理矩形校正）；完整贴图能力留 T18–T22。
  - App 创建 `LoggingCaptureSink` 并传给遮罩；`OnCaptureOverlayClosed` 记录完成动作。
  - **SaveFileDialog + Topmost 遮罩的真实 bug 与修复**：遮罩 `Topmost=true` 时，Win32 保存对话框会在显示后自行返回（实测出现过默认名在 1.5–44 秒后静默保存，`DialogResult=True`；表现为“对话框自己关了/没点保存却生成了文件”）。修复：`ShowDialog` 期间 `Topmost=false`，`finally` 恢复并在取消后重新激活遮罩；修复后对话框稳定等待用户点“保存/取消”。日志新增“保存对话框已打开/已关闭”。

- 2026-10-18 **T12 Core 标注模型**（纯 Core，无 WPF/Win32/System.Drawing 依赖）：
  - `src/ZeepCut.Core/Geometry/PixelPoint.cs`：整数像素点（平移、距离、格式化）。
  - `src/ZeepCut.Core/Annotations/`：
    - `AnnotationColor`：`uint` ARGB 的 record struct（Alpha/Red/Green/Blue、`FromArgb`/`FromRgb`、默认亮红 `#FFFF3B30`），可直接存整数列。
    - `Annotation` 抽象基类：`Id`（Guid，反序列化可显式传入）、`Kind`、`Color`、`Thickness`（1–20 钳制）、`CreationOrder`（文档加入时从 1 递增，撤销删除后保留）、抽象 `Bounds`、`HitTest(PixelPoint, tolerance)`、`Move`；`GetRenderBounds()` 按描边半宽外扩供渲染裁剪；`CanResize`/`Resize` 默认不支持（抛 `NotSupportedException`）。
    - 七种具体类型：`RectangleAnnotation`/`EllipseAnnotation`（区域 + 非填充，支持 Resize）、`ArrowAnnotation`/`LineAnnotation`（共享 `SegmentAnnotation`：起终点、端点包围盒、点到线段距离命中）、`FreehandAnnotation`（点列表 + `Smooth` 开关 + `AddPoint`）、`TextAnnotation`（锚点/文本/字号 8–72/可选背景色，确定性估算包围盒）、`MosaicAnnotation`（区域 + 块大小 4–64 + 强度 0–1，NaN 回退默认）。
    - `AnnotationDefaults`：默认值（粗细 3、字号 18、块 12、强度 0.75）与静默钳制规则。
    - `AnnotationDocument`：按 Z 序（底→顶）的有序集合，`Add`/`Insert`/`Remove`/`RemoveAt`/`Clear`（返回被移除项）、`IndexOf`/`Contains`、`MoveToIndex`/`BringToFront`/`SendToBack`、`HitTest` 取最上层、`Bounds` 并集、实现 `IEnumerable<Annotation>`；同一对象不可重复加入，删除后插回保留 `CreationOrder`。
  - 坐标约定：标注使用**相对截图图像左上角的物理像素**（不是屏幕绝对坐标、不是 DIP），T15 导出时可直接按图像渲染。
  - 命中语义：矩形/椭圆/马赛克命中内部 + 容差（椭圆额外排除包围盒四角）；直线/箭头/画笔按点到线段距离 + 容差 + 描边半宽；文字按包围盒。负容差按 0 处理。退化图形（单点画笔、零长度箭头、零宽线）包围盒为零尺寸但仍可命中。
- 2026-10-18 **T13 Core 撤销重做**：
  - `src/ZeepCut.Core/Editing/IEditCommand.cs`：`Description` + `Execute`（首次/重做）+ `Undo`（恢复执行前状态）。
  - `EditHistory`：撤销栈 + 重做栈（`List` 末尾为栈顶），`Capacity` 默认 100，超出丢弃最旧（栈底）；`Execute` 成功后入栈并清空重做栈，命令抛异常时历史不变；`CanUndo`/`CanRedo`/`UndoCount`/`RedoCount`/`NextUndoDescription`/`NextRedoDescription`；空栈 `Undo()`/`Redo()` 返回 false 不抛异常；`Clear()` 清空两条栈。
  - `AnnotationCommands.cs`：`AddAnnotationCommand`（撤销=移除，重做=同一对象插回原层级）、`RemoveAnnotationCommand`（撤销=恢复原索引/原顺序/原 CreationOrder）、`MoveAnnotationCommand`（撤销=反向平移）、`ClearAnnotationsCommand`（撤销=按原顺序全部插回）、`AnnotationPropertyCommand<T>`（构造时记录旧值，撤销写回旧值，适用于颜色/粗细/文字/字号等）。
  - `AnnotationEdits` 工厂：`Move`/`SetColor`/`SetThickness`/`SetText`/`SetFontSize`/`SetTextBackground`/`SetMosaicBlockSize`/`SetMosaicStrength`/`SetFreehandSmooth`，避免 UI 层手写读写委托。
  - xUnit 覆盖：各类型 Bounds（反向坐标/单点/零长度/多行/全角）、命中/不命中的边界与容差、Move/Resize 与顺序保持、属性默认值与上下限钳制、文档 Z 序与命中取最上层、五类命令往返、容量丢弃最旧、新操作清空重做栈、空栈与抛异常命令。

- 2026-10-18 **T14 标注画布 + T15 七种标注工具**（消费 T12/T13，未改动 Core 已有类型）：
  - Core 新增 `src/ZeepCut.Core/Annotations/AnnotationSession.cs`：会话状态机，把“文档 + 撤销历史 + 工具设置 + 草稿/文字输入”收在一起，UI 只做换算/绘制/键盘，因此可单测。
    - 状态互斥：空闲 / 拖拽草稿（`BeginDraft`→`UpdateDraft`→`CommitDraft`|`CancelDraft`）/ 文字输入（`BeginText`→`UpdateTextPreview`→`CommitText`|`CancelText`）；同时开两种抛 `InvalidOperationException`。
    - 草稿提交走 `AddAnnotationCommand`（首次执行即加入文档），退化操作（单击矩形/零长线）直接丢弃、不污染撤销栈；文字内容空白同样不产生标注。
    - `ActiveTool` 为 `null` 表示“选择”模式；非空时同步 `Settings.Tool`，新建标注使用当前颜色/粗细/字号。
    - `Preview` 依次返回草稿标注 / 待提交文字，供绘制层实时预览（预览与最终结果同一套渲染代码）。
    - `IsSelectionLocked` = 文档有标注 或 草稿/文字输入中；`Undo`/`Redo` 在草稿或输入期间返回 false（不打断当前操作），全部撤销后自动解锁。
  - `Capture/CaptureOverlayWindow.cs` 扩展（T14/T15 主战场）：
    - 叠层顺序：冻结图 → 压暗层 → **标注宿主（随选区摆放，`AnnotationLayer` 实例）** → 选区白框/手柄/尺寸标签 → 工具栏 → 文字输入层/锁定标签。`Grid` 不支持 Canvas 定位，标注宿主外包了一层 `Canvas`。
    - 坐标唯一入口是 `AnnotationCanvasTransform`（屏幕物理 − 选区原点 ↔ 窗口 DIP），窗口里不再另写比例；拖拽终点用 `ToImageClamped` 允许正好落在选区右/下边。
    - 工具栏改两行：上行 选择/矩形/椭圆/箭头/直线/画笔/文字/马赛克（选中项蓝底加粗 + `AutomationProperties.Name`=`工具矩形` 等），下行 尺寸文字 + 6 色块 + 4 档粗细 + 撤销/重做/复制/另存为/贴图/取消。
    - 选区锁定：`IsSelectionLocked` 后隐藏 8 个手柄、禁止移动/调整（`UpdateHandles`/`OnPreviewMouseLeftButtonDown`/`UpdateCursor` 三处一致），并在选区右上方显示 `选区已锁定（N 个标注）` 标签；只选工具未落笔时仍允许调整选区。
    - 鼠标：选区定稿 + 有绘制工具 + 在选区内按下 → 开始草稿并 `CaptureMouse`；`MouseMove` 更新草稿并 `InvalidateVisual`（实时预览）；抬起提交并记一条 INFO（工具/起终点/包围盒/画笔点数/是否提交/栈深度/锁定状态）；`LostMouseCapture` 用最后位置兜底提交。
    - 键盘：**文字输入期间 Enter/Esc 归输入框**（Enter=确认文字、Esc=取消文字，Shift+Enter 换行），绝不触发复制/关闭遮罩；Ctrl+Z/Ctrl+Y 走 `EditHistory`；无草稿时 Esc 才是取消截图，有草稿时 Esc 只丢弃草稿。
    - 文字：点击选区 → 在点击点显示深色输入框（字宽随选区夹取，字号按 `96/dpi` 换算成 DIP，所见即所得），`TextChanged` 实时预览；提交后 `RestoreOverlayFocus` 把焦点还给遮罩；点其它位置也会先提交当前文字。
    - 马赛克：拖拽区域，预览是块状示意（半透明压暗 + 交替块），真实像素化留 T17。
  - 新增验证脚本 `scripts/verify-t14t15.ps1`（11 个场景：rect/ellipse/arrow/line/freehand/text/mosaic/all/undo-redo/lock/color/buttons），UIA 按 `AutomationProperties.Name` 点工具/颜色/粗细/撤销重做按钮，全程物理像素坐标。

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
- 2026-10-18 **T9/T10 构建+测试（最终代码）**：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 298, Skipped: 0, Total: 298`（本轮新增 24）；末尾 `BUILD+TEST OK`。
- 2026-10-18 **主屏框选 / 移动 / 八向 / 工具栏（真实桌面，DISPLAY1 0,0 2560×1440 DPI=120/125%，pid 58740）**：
  - Alt+A 后遮罩物理 `(0,0) 2560x1440`、DIP `2048x1152`。物理拖动 `(300,300)→(940,780)`，日志
    `鼠标按下：指针物理=(300,300) DIP=(240,240)` →
    `选区定稿（框选定稿）：... 物理=(300,300) 640x480 尺寸=640 × 480（物理像素） DIP=(240,240) 512x384`，
    工具栏在选区下方 `物理=(300,790) 426x50`；截图 `artifacts/t9t10/primary-selection.png`
    （read_image：选区外压暗、选区内原图、1px 白框、8 个白色手柄、工具栏显示 `640 × 480` 与四个按钮）。
  - 整体移动：从中心 `(620,540)` 拖到 `(820,640)` → `选区定稿（调整完成）：物理=(500,400) 640x480`；截图 `artifacts/t9t10/primary-moved.png`。
  - 八向调整：依次抓住 `Right / Left / Top / Bottom / TopLeft / TopRight / BottomRight / BottomLeft`，日志每条都记录对应 `手柄=...`，
    最终 `物理=(260,240) 750x610`，工具栏 `(260,860)`；截图 `artifacts/t9t10/primary-handles.png`。
  - 右下角调整：按 `(1138,878)` 拖到 `(1238,978)` → `物理=(500,400) 738x578 尺寸=738 × 578`（按下点比原右下角内缩 2px，
    Controller 以指针为边，所以少 2px；尺寸文字与实际选区一致）；截图 `artifacts/t9t10/primary-resized.png`。
  - 拖动中尺寸标签：按住左键拖到 640×480 时截图，标签在选区右上方显示 `640 × 480`，此时无工具栏/手柄；
    `artifacts/t9t10/primary-drag-badge.png`；松开后工具栏出现 `primary-drag-badge-released.png`。
  - 任意方向拖动：`(940,780)→(300,300)` 仍得到 `物理=(300,300) 640x480`；截图 `artifacts/t9t10/primary-reverse-drag.png`。
  - 窗口候选单击：鼠标 `(1200,700)` 单击命中 Z#0 DSH 窗口，采用候选 `物理=(534,245) 1603x1033`，工具栏 `(534,1288) 426x50`；
    截图 `artifacts/t9t10/primary-window-click.png`。
  - 最小尺寸：在没有窗口候选的顶部任务栏空白带做 `3×3` 拖动 `(1000,30)→(1003,33)`，日志
    `框选未达最小尺寸（4px）：... 结果=(0,0) 0x0，状态=None`，无工具栏、整屏压暗；截图 `artifacts/t9t10/primary-tiny-drag.png`。
  - 工具栏贴底：拖动 `(400,1150)→(1100,1400)` 得 `700×250`，日志 `工具栏显示（选区上方）... 物理=(400,1090) 426x50`；
    截图 `artifacts/t9t10/primary-toolbar-above.png`。UIA 找到“取消”按钮 `物理=(753,1099) 57x32` 并自动点击 →
    日志 `取消本次截图（工具栏取消按钮）` → `遮罩窗口已关闭 ... 已取消=True`；截图 `artifacts/t9t10/primary-after-cancel-button.png`
    （PIL 采样左区 RGB≈255，确认无压暗、遮罩已关闭）。
  - 工具栏水平夹取：选区 `(2200,700) 300×450`，工具栏被夹到 `DIP=(1707,928)` → `物理=(2134,1160) 426x50`
    （右边缘正好 2560，“取消”按钮右边缘 2544 未越界）；截图 `artifacts/t9t10/clamp/primary-toolbar-clamped-right.png`。
  - 选区限制在屏幕内：把选区整体拖 `(3000,2000)`，结果被夹到 `物理=(1920,960) 640x480`（右/下正好贴 2560/1440）；
    截图 `artifacts/t9t10/clamp/primary-moved-clamped.png`。
  - Enter：日志 `键盘 Enter：复制到剪贴板（占位，T11 实现）：物理=(500,400) 738x578 尺寸=738 × 578`；
    `primary-after-enter.png` 与 `primary-resized.png` SHA-256 相同（`020cc2bb...`），证明占位不改变画面。
  - Esc：日志 `取消本次截图（Esc）` → `遮罩窗口已关闭 ... 已取消=True`；`primary-after-esc.png` 显示未压暗桌面；当时进程 58740 仍在运行（收尾重新构建前才停止）。
- 2026-10-18 **副屏框选 / 移动 / 调整（真实桌面，DISPLAY2 -1080,0 1080×1920 DPI=96/100%）**：
  - 鼠标 `(-540,960)` 触发，日志 `屏幕 \\.\DISPLAY2 ... 物理矩形=(-1080,0) 1080x1920 DPI=96`、
    `冻结遮罩 ... DIP=(-1080,0) 1080x1920`。
  - 拖动 `(-980,300)→(-340,780)`：`鼠标按下：指针物理=(-980,300) DIP=(100,300)` →
    `选区定稿 ... 物理=(-980,300) 640x480 尺寸=640 × 480 DIP=(100,300) 640x480`；工具栏 `物理=(-980,788) 341x40`；
    截图 `artifacts/t9t10/secondary-selection.png`（read_image：选区外压暗、白框、8 手柄、工具栏 `640 × 480` 均在 1080×1920 屏内）。
  - 整体移动 `(-660,540)→(-460,640)`：`物理=(-780,400) 640x480`；工具栏 `(-780,888)`。
  - 右下调整 `(-142,878)→(-42,978)`：`物理=(-780,400) 738x578 尺寸=738 × 578 DIP=(300,400) 738x578`；工具栏 `(-780,986) 341x40`；
    截图 `artifacts/t9t10/secondary-resized.png`。
  - Enter 占位、Esc 关闭：日志与主屏一致（`键盘 Enter ... 尺寸=738 × 578` → `取消本次截图（Esc）`）；`secondary-after-esc.png`。
  - 结论：同一套 DIP↔物理换算在两块不同 DPI 屏幕上，尺寸文字与实际选区一致（640×480 / 738×578），副屏负坐标原点正确。
- 2026-10-18 **T11 构建+测试（最终代码）**：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 344, Skipped: 0, Total: 344`（本轮新增 46 个：SelectionCrop 16、CaptureFileName 30）；末尾 `BUILD+TEST OK`。
- 2026-10-18 **T12/T13 构建+测试（最终代码）**：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 512, Skipped: 0, Total: 512`；末尾 `BUILD+TEST OK`。
  本轮新增 168 个测试（过滤实测）：`--filter 'FullyQualifiedName~ZeepCut.Tests.Annotations'` → `Passed: 137`；
  `--filter 'FullyQualifiedName~ZeepCut.Tests.Editing'` → `Passed: 31`。新增测试文件：
  `tests/ZeepCut.Tests/Annotations/{AnnotationBoundsTests,AnnotationHitTestTests,AnnotationMoveResizeTests,AnnotationPropertyTests,AnnotationDocumentTests}.cs`、
  `tests/ZeepCut.Tests/Editing/{EditHistoryTests,AnnotationCommandsTests}.cs`。
- 2026-10-18 **纯 Core 约束核验（真实执行）**：`grep -RnE "System\.Windows|PresentationCore|System\.Drawing|WindowsBase" src/ZeepCut.Core/Annotations src/ZeepCut.Core/Editing src/ZeepCut.Core/Geometry/PixelPoint.cs` 无输出（NO_WPF_REFERENCES），即本轮新增 Core 代码不引用 WPF/PresentationCore/System.Drawing；WPF 项目编译仍为 0 warning。
- 2026-10-18 **主屏复制（真实桌面，DISPLAY1 0,0 2560×1440 DPI=120/125%）**：
  - 把 Notepad 放在物理 (500,350) 作为静态内容，框选 `(560,400) 600×300`；Enter 触发 → 日志
    `开始输出（键盘 Enter）：动作=复制到剪贴板 ...` → `裁剪完成 ... → 图像=600x300 Pbgra32` →
    `截图完成 ... 耗时=4ms，关闭遮罩` → `输出完成：动作=复制到剪贴板 选区=(560,400) 600x300 尺寸=600 × 300（物理像素） 耗时=4ms`；遮罩关闭。该次为 pid 56496（Topmost 修复前的构建，裁剪/剪贴板代码与最终一致）。
  - **独立进程** `scripts/clipboard.ps1` 读剪贴板：`CLIPBOARD IMAGE 600x300`，保存 `artifacts/t11/clipboard-primary-notepad.png`；read_image 查看为 Notepad 选区内容（文字/菜单/状态栏），非黑图/错位。
  - 像素对齐核验：与关闭遮罩后的物理截图 `artifacts/t11/primary-copy-after.png` 按 (560,400,600,300) 裁剪对比，`dx=dy=0` 时顶部文本带（0–120 行）mean diff=0.195、仅 55 像素差异（文本光标闪烁）；偏移 ±1px 时 mean 7.7–16.0。
  - 最终构建（pid 58544）再次主屏复制 600×300（`clipboard-primary.png`）与当次 `primary-copy-after.png` 对比：origin mean=0.021 / 190 个差异像素（内容为实时 DSH 界面），偏移 ±1px 时 mean 3.17–4.14 / 7700+ 像素差异 → 只有 (0,0) 对齐。裁剪原点精确。
  - 当次剪贴板图中出现的 IME/快捷候选条在截图时已消失，整图 12.7% 差异；通过偏移扫描确认不是裁剪错位。
- 2026-10-18 **复制按钮（真实桌面，最终代码）**：UIA 找到工具栏“复制” `物理=(695,719) 58x32` 并点击 → 日志完成、遮罩关闭；独立进程读剪贴板 `600x300`（`artifacts/t11/clipboard-primary-button.png`）。
- 2026-10-18 **主屏右下边缘复制（真实桌面，最终代码）**：框选 `(2200,1100) 359×279`（贴右边缘、接近底部任务栏），Enter → 剪贴板 `359x279`；与关闭遮罩后的截图裁剪 (2200,1100,359,279) 对比 **逐像素完全相同（mean=0, max=0, 0 个差异像素）**；证据 `artifacts/t11/clipboard-primary-edge.png`、`primary-copy-edge-after.png`。
- 2026-10-18 **副屏负坐标复制（真实桌面，DISPLAY2 -1080,0 1080×1920 DPI=96/100%，最终代码）**：
  - Notepad 放在物理 (-1080,250) 640×400；框选 `(-980,320) 580×320`；Enter → 日志
    `开始输出（键盘 Enter）... 选区=(-980,320) 580x320 ... 屏幕=(-1080,0) 1080x1920 DPI=96` →
    `裁剪完成 ... → 图像=580x320 Pbgra32` → `输出完成 ... 尺寸=580 × 320 ... 耗时=5ms`；遮罩关闭。
  - 独立进程读剪贴板 `CLIPBOARD IMAGE 580x320`，保存 `artifacts/t11/clipboard-secondary.png`；read_image 可见 Notepad 文字、状态栏 `Ln 3, Col 7 100% Windows (CRLF) UTF-8` 与右侧壁纸，负坐标未错位。
  - 与关闭遮罩后副屏截图 `secondary-copy-after.png` 的图像内 (100,320,580,320)（= -980 - (-1080)、320）对比：`dx=dy=0` 时 mean=0.189 / max=58 / 7175 个小差异像素（ClearType/重绘），偏移 ±1px 时 mean 2.1–4.2，换算正确。
- 2026-10-18 **另存为 PNG（真实桌面，最终代码）**：点“另存为”（UIA）→ 日志
  `保存对话框已打开：默认名称=截图_2026-10-18_052414.png 目录=C:\Users\19427\Pictures`；
  验证脚本用 UIA 找到文件名字段（DirectUI，无 ValuePattern），点击聚焦后把临时路径**从剪贴板粘贴**进去（中文输入法会把 `C:\...` 改写成 `C：、...`），再点对话框的“保存”（子 Button `BM_CLICK`）→
  `保存对话框已关闭：DialogResult=True 选择文件=C:\...\Temp\zeepcut-t11\save-t11.png` →
  `文件已保存 ... 格式=Png 尺寸=600x300` → `输出完成 ... 耗时=3ms`，遮罩关闭；
  独立校验文件 `600x300, 27352 bytes, PNG magic OK`，副本 `artifacts/t11/primary-saved.png`（read_image 可打开）；截图 `primary-save-dialog.png` / `primary-save-dialog-filled.png` / `primary-save-after.png`。
- 2026-10-18 **另存为 JPEG（真实桌面，最终代码）**：同路径 `.jpg` → 日志 `文件已保存 ... 格式=Jpeg 尺寸=600x300 质量=90`；独立校验 `600x300, 18125 bytes, JPEG magic FF D8`，副本 `artifacts/t11/primary-saved.jpg`（read_image 可打开）；遮罩关闭。
- 2026-10-18 **取消保存对话框（真实桌面，最终代码）**：
  - 点“另存为”后点对话框“取消”（`BM_CLICK` id=2）→ 日志
    `保存对话框已关闭：DialogResult=False` → `另存为取消（工具栏按钮“另存为”）... 留在截图状态，不关闭遮罩、不写入任何记录`；
    窗口枚举仍有 `Zeep Cut 截图遮罩`，截图 `artifacts/t11/primary-cancel-after-dialog.png` 显示 600×300 选区、工具栏与绿色 `已取消保存`；无文件生成；随后 Esc 关闭遮罩。
  - 也验证 Esc 取消：打开对话框后按 Esc → `DialogResult=False` → 取消日志 → 遮罩仍在，截图 `artifacts/t11/primary-cancel-esc.png` 显示 `已取消保存`；第二次 Esc 才关闭（`已取消=True`、`完成动作=无`）。
  - 取消路径日志没有 `输出完成`，也没有图片/历史文件。
- 2026-10-18 **贴图占位（真实桌面，最终代码）**：点“贴图”（UIA `物理=(845,719) 58x32`）→
  `创建贴图窗口：选区=(560,400) 600x300 ... 图像=600x300` → `贴图窗口物理矩形与选区一致 (560,400) 600x300` →
  `输出完成：动作=贴图 ... 耗时=21ms`，遮罩关闭；窗口枚举 `'Zeep Cut 贴图' (560,400) 600x300`；
  截图 `artifacts/t11/primary-pin.png`（放大 `primary-pin-zoom.png` 可见内容与右上 ×）。按 Esc →
  `贴图窗口关闭（Esc）` → `贴图窗口已关闭`，窗口消失；另一次点击右上 × → 只有 `贴图窗口已关闭`、无 Esc 日志（`primary-pin-close-button.png`）。
- 2026-10-18 **剪贴板被占用失败路径（真实桌面，最终代码）**：验证进程 `OpenClipboard` 持有剪贴板 → Enter 复制 → 日志
  `[WARN] 复制到剪贴板失败（留在截图状态）：COMException: OpenClipboard Failed (0x800401D0 (CLIPBRD_E_CANT_OPEN))`；
  遮罩仍在并显示橙色 `复制失败：剪贴板被占用，请稍后重试`（`artifacts/t11/primary-copy-busy.png`）；释放剪贴板后再按 Enter 成功复制并关闭遮罩（`输出完成 ... 耗时=13ms`，`primary-copy-busy-retry.png`）。
- 2026-10-18 **T14/T15 构建+测试（最终代码）**：`scripts/build.ps1` → `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 577, Skipped: 0, Total: 577`；末尾 `BUILD+TEST OK`。新增 `AnnotationSessionTests` 28 个（过滤实测 `Passed: 28`）。
- 2026-10-18 **T14/T15 真实桌面验证（主屏 125%，DISPLAY1 0,0 2560×1440 DPI=120，pid 56636；脚本 `scripts/verify-t14t15.ps1`，证据 `artifacts/t14t15/`）**：
  - 选区固定 `(320,260) 760×560`（图像坐标原点 = 屏幕 −(320,260)）；工具栏两行 `物理=(320,830)`：第一行 8 个工具按钮 53×32，第二行 6 色块 25×25 + 4 档粗细 + 撤销/重做/复制/另存为/贴图/取消。日志逐按钮打印物理矩形，UIA 按名字命中。
  - **矩形**：拖 `(380,320)→(620,480)`，日志 `起点图像=(60,60) 终点图像=(300,220) 包围盒=(60,60) 240x160 已提交=True`；像素探测该截图 `row 400 red=[379-380][619-620]`、`col 500 red=[319-320][479-480]` → 屏幕 (379,319) 240×161，与鼠标轨迹差 ≤1px（125% 下 DIP→物理四舍五入）。
  - **椭圆**：拖 `(380,320)→(640,500)`，日志 `包围盒=(60,60) 260x180`；像素探测 `row400 x=[379-381][638-640]`、`col500 y=[319-321][498-500]`，即屏幕 380–640 × 320–500，与鼠标轨迹完全一致。
  - **箭头**：拖 `(380,320)→(680,490)`，日志 `包围盒=(60,60) 300x170`；红色像素从 (379,319) 连续延伸到 (678,489)，终点处头部三角形最宽 10px（列 640→678 的 y 展宽 3→10 像素），`arrow-head-zoom.png` 放大可见清晰箭头头部。
  - **直线**：拖 `(380,320)→(680,440)`，像素探测 `row320 x=[379-384]`、`col680 y=[439-440]`，端点精确。
  - **画笔**：正弦波拖动 40 点（`FREEHAND 380,650 len=300 amp=60`），像素探测沿 x 采样 y：380→381、420→449、460→378、500→310、540→380、580→449、620→375、700→380，与振幅 60 的两次正弦周期吻合，平滑曲线无断点。
  - **文字**：点 `(380,360)` → 日志 `文字输入开始：图像坐标=(60,100) 锚点DIP=(304,288) 输入框DIP=(304,292) 字号=18`；输入 `Zeep Cut T15`（空格+数字用 SendInput Unicode 逐字输入，SendKeys 在本机 IME 下会吃掉空格/数字）→ 截图 `primary-text-02-typed.png` 显示输入框内实时文字；**按 Enter** → 日志 `文字提交（Enter）：长度=12 内容="Zeep Cut T15" 已提交=True 文档标注数=1`，**且此后直到 Esc 没有任何 `开始输出`/`输出完成` 日志**（复制未触发），遮罩仍在（`primary-text-04-still-open-with-rect.png` 里继续画了矩形，标注数变 2），`primary-text-99-final.png` 中文字渲染为红色 `Zeep Cut T15`。
  - **马赛克**：拖 `(380,320)→(660,490)`，截图 `primary-mosaic.png` 显示半透明压暗 + 12px 交替块棋盘格（`mosaic-zoom.png` 放大可见 12px 方格），无真实像素化（T17）。
  - **七种同时**：`all` 场景在一个选区里依次画矩形→椭圆→箭头→直线→画笔→文字→马赛克，最终 `primary-all-7-mosaic.png` 七种全部可见，锁定标签 `选区已锁定（7 个标注）`。
  - **撤销/重做（键盘）**：画矩形+椭圆后 `Ctrl+Z`×2 → `Ctrl+Y`×2；日志四条 `撤销（Ctrl+Z）/重做（Ctrl+Y）` 的 `文档标注数` 为 2→1→0→1→2，`撤销栈/重做栈` 同步变化；像素统计红色像素 2732→1371→79→1371→2732，完全对称（79 是桌面本身的红色残留）。
  - **撤销/重做（按钮）**：`buttons` 场景点工具栏“撤销”/“重做”，日志 `撤销（工具栏按钮“撤销”）：添加矩形 成功=True 文档标注数=0` → `重做（工具栏按钮“重做”）… 文档标注数=1`；红像素 1872→241→1872。
  - **选区锁定**：画完矩形后点“选择”，再在选区中心拖 `(700,540)→(860,640)`，日志 `选区已锁定（文档中有 1 个标注），忽略选区外点击`，且白框探测三次截图（画完/拖动尝试后/抓角尝试后）都在 `y=540` 的 x=320..1079、`x=540` 的 y=261..819，选区完全没动；手柄同时隐藏（锁定后不再显示 8 个白手柄）。
  - **颜色/粗细**：点蓝色块 `#FF0A84FF` + 粗细 10 后画矩形，日志 `标注颜色切换：#FF0A84FF`、`标注粗细切换：10 像素`；像素统计蓝色像素 10120（此前 897），`primary-color-blue-10.png` 为粗蓝框。
- 2026-10-18 **副屏负坐标验证（真实桌面，DISPLAY2 -1080,0 1080×1920 DPI=96）**：
  - 遮罩 `物理=(-1080,0) 1080x1920 DPI=96`（首次仍按主屏 125% 摆成 (-1350,0)，`SetWindowPos` 校正，T7 已知行为）；选区 `(-1000,260) 700×620`。
  - **矩形**：拖 `(-940,320)→(-700,480)`，日志 `起点图像=(60,60) 终点图像=(300,220) 包围盒=(60,60) 240x160`；截图内应为 x=140..380、y=320..480（= -1000+60+1080 起算），像素探测 `row 400` 左竖边 138–141、右竖边 379–381，`col 300` 上边 318–321、下边 479–481 —— 负坐标换算正确，误差 ≤1px。
  - **箭头**：拖 `(-940,320)→(-640,490)`，日志 `包围盒=(60,60) 300x170`；`secondary-arrow.png`（1080×1920）中箭头从选区内 (140,320) 指向 (440,490)，`secondary-arrow.png` 的 read_image 视图里可见红色箭头与 `选区已锁定（1 个标注）` 标签，位置正确。
  - 两次副屏运行均以 Esc 结束，没有产生任何输出/历史记录。

- 2026-10-18 **T16/T17 构建+测试（最终代码）**：`scripts/build.ps1` → `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 625, Skipped: 0, Total: 625`；末尾 `BUILD+TEST OK`。
  本轮新增 48 个测试：`tests/ZeepCut.Tests/Annotations/AnnotationEditControllerTests.cs`（32 个：选中/命中手柄/移动/缩放/端点/删除/文字/取消/校验/夹取/最小尺寸/一次拖拽一条历史）与 `MosaicPixelatorTests.cs`（16 个：块内均匀、块原点、单像素区域、区域越界、部分越界、块大小上下钳制、不同块大小结果不同、强度 0/NaN/0.5、alpha 不变、越界参数、空区域、`ApplyCopy` 不改输入）。
- 2026-10-18 **T17 主屏 125% 七种标注导出（真实桌面，DISPLAY1 0,0 2560×1440 DPI=120，pid 57904，证据 `artifacts/t16t17/`）**：
  - 选区 `(320,240) 760×560`；七种标注依次绘制，日志逐条 `包围盒`（矩形 55,55 190×110 / 椭圆 275,55 180×120 / 箭头 55,215 200×100 / 直线 285,215 170×80 / 画笔 55,320 280×110 41 点 / 文字 285,365 "Zeep Cut T16" / 马赛克 310,200 175×120）。
  - Enter → 日志 `开始输出（键盘 Enter）... 尺寸=760 × 560（物理像素）` → `导出渲染完成（复制到剪贴板）：... DPI=120 标注=7 个（马赛克=1 个）→ 图像=760x560 Bgra32 尺寸匹配选区物理像素=True` → `耗时=40ms`，遮罩关闭。
  - 独立进程读剪贴板：`CLIPBOARD IMAGE 760x560`，保存 `primary-export-clipboard.png`。read_image 查看：矩形/椭圆/箭头/直线/画笔/文字 **七种全部在**、位置与预览一致；**无压暗、无选区白框、无手柄、无尺寸文字、无工具栏**。
  - **马赛克是真像素化（定量核验）**：`primary-export-clipboard.png` 中马赛克区域 `(310,200) 175×120` 按 12px 网格切块 → **140 个块内部 0 个非均匀**（每块内像素完全相同）；块颜色来自下方原图（彩色块被平均成 `(185,47,47)`、`(70,157,65)`、`(247,247,247)` 等），不是半透明方块示意；马赛克下方的直线被平均成粉色块（原线 `(255,59,48)` → 块内 `(239,149,144)`），证明处理的是"当前像素"而不是覆盖色。
  - **无压暗/无边框定量核验**：把导出图与"马赛克绘制前"的全屏截图按选区原点对齐比较，顶部右侧 60×40 区域 **最大通道差 11、均值 0.35**；底部条带（工具栏可能遮挡处）`0/6480` 像素差异 >12。即导出图就是原图 + 标注。
  - 放大证据：`mosaic-zoom.png`（马赛克 3× 最近邻放大，可见清晰 12px 块）、`primary-export-01-all-seven.png`（遮罩内七种标注 + 工具栏）。
- 2026-10-18 **T17 导出尺寸（主屏 125% 与副屏 100% 各一次，真实桌面）**：
  - 主屏 125%（DPI=120，物理 2560×1440）：选区 `(1200,300) 512×384` → Enter → 日志 `导出渲染完成 ... DPI=120 ... → 图像=512x384 Pbgra32 尺寸匹配选区物理像素=True`；独立进程读剪贴板 **`512x384`**（= 选区物理像素，不是 DIP 的 410×307）。
  - 副屏 100%（DPI=96，物理 1080×1920，原点 **-1080,0**）：选区 `(-1000,240) 700×640` → 日志 `选区=(-1000,240) 700x640 屏幕=(-1080,0) 1080x1920 DPI=96 ... → 图像=700x640 尺寸匹配选区物理像素=True`；独立进程读剪贴板 **`700x640`**，负坐标原点换算正确。
  - 证据：`primary-sizes-clipboard.png`、`secondary-sizes-clipboard.png`。
- 2026-10-18 **T17 另存为 PNG 走合成渲染（真实桌面，主屏 125%）**：画矩形 + 马赛克 → 点工具栏“另存为” → 日志 `保存对话框已打开` → 自动化填入 `...\artifacts\t16t17\primary-save-as.png` 并点保存 → `保存对话框已关闭：DialogResult=True` → `导出渲染完成（另存为）：标注=2 个（马赛克=1 个）→ 图像=760x560 Bgra32 尺寸匹配选区物理像素=True` → `文件已保存 ... 格式=Png 尺寸=760x560` → 遮罩关闭。
  独立校验文件 `760x560, 43300 bytes`，read_image 查看：矩形 + **真实像素化的马赛克**（覆盖在彩色块上，被平均成蓝/黄/白块），无压暗/边框/工具栏。证据 `primary-save-as.png`、`primary-save-as-view.png`。
- 2026-10-18 **T17 贴图走合成渲染（真实桌面，主屏 125%）**：画矩形 + 马赛克 → 点“贴图” → 日志 `导出渲染完成（贴图）：标注=2 个（马赛克=1 个）→ 图像=760x560 ... 尺寸匹配选区物理像素=True` → `创建贴图窗口 ... 图像=760x560` → `贴图窗口物理矩形与选区一致 (320,240) 760x560`；截图 `primary-pin-crop.png` 可见贴图窗口显示的是**合成图**（矩形 + 真实像素化马赛克），不是纯裁剪。
- 2026-10-18 **T16 选中/移动/缩放/删除/撤销（真实桌面，主屏 125%，pid 57904，场景 `edit`）**：
  - 画矩形 `(55,55) 220×150` → 点“选择” → 点标注内部 `(165,130)` → 日志 `开始编辑标注：类型=Rectangle 手柄=内部/平移 ... 包围盒=(55,55) 220x150`（抬起时 `记录=无变化`，选中不产生历史）。截图 `primary-edit-02-selected-crop.png`：**青色虚线框 + 8 个白色手柄**。
  - 拖动移动 `(165,130)→(255,190)` → 日志 `标注编辑抬起：方式=Move ... 包围盒=(145,115) 220x150 记录=移动矩形 撤销栈=2`（= 位移 +90/+60，**一次拖拽只记一条**）。截图 `primary-edit-03-moved.png`。
  - 拖右下手柄 `(365,265)→(425,305)` → 日志 `方式=Resize 手柄=BottomRight ... 包围盒=(145,115) 280x190 记录=调整矩形大小 撤销栈=3`（= +60/+40）。截图 `primary-edit-04-resized-crop.png`：虚线框与手柄跟随新尺寸。
  - `Delete` → 日志 `删除标注（Delete）：类型=Rectangle 记录=删除矩形 文档标注数=0 撤销栈=4 选区锁定=False`；截图 `primary-edit-05-deleted.png` 标注消失、遮罩仍在。
  - `Ctrl+Z`×3 → 日志依次 `撤销（Ctrl+Z）：删除矩形` → `调整矩形大小` → `移动矩形`（文档标注数始终 1，撤销栈 3→2→1，重做栈 1→2→3）；截图 06/07/08 依次显示"标注恢复 → 尺寸恢复 → 位置恢复"，证明每一步都是一条独立历史。
  - 再次点击选中 → `Esc` → 日志 `Esc：取消标注选中态（遮罩保持打开）`，**没有** `取消本次截图`；截图 `primary-edit-10-esc-deselected.png` 虚线框消失、遮罩与选区仍在；之后第二次 Esc 才 `取消本次截图（Esc）`。**选中态优先于关闭遮罩成立**。
- 2026-10-18 **T16 文字双击重编辑（真实桌面，主屏 125%，场景 `textedit`）**：
  - 文字工具点 `(375,295)` → 输入 `first text` → Enter → 日志 `文字提交（Enter）：长度=10 内容="first text" 已提交=True`。
  - 点“选择”后双击文字 → 日志 `重新编辑文字：图像坐标=(55,55) 原内容="first text" 输入框DIP=(300,236) 宽=360 字号=18`；截图 `primary-textedit-02-dblclick-editor-crop.png` 显示输入框内预填原文。
  - `Ctrl+A` + 输入 `second T16 text` + Enter → 日志 `文字重新编辑提交（Enter）：长度=15 内容="second T16 text" 记录=修改文字 文档标注数=1 撤销栈=2`。
  - `Ctrl+Z` → 截图 `primary-textedit-04-undo-text-crop.png` 文字回到 `first text`；`Ctrl+Y` → `primary-textedit-05-redo-text-crop.png` 又变成 `second T16 text`。文字修改**可撤销/重做**。
- 2026-10-18 **未验证（T16/T17 相关）**：
  - 手柄缩放只在**矩形**上真实验证（椭圆/马赛克走同一套 `Resize` 分支，Core 单测覆盖 `MosaicAnnotation` 缩放，但未在桌面逐一拖马赛克/椭圆手柄）。
  - 箭头/直线的**端点拖动**只有 Core 单测（`SegmentEndpoints_DragStartAndEnd_RecordsOneStep`、`SegmentEndpoints_AreClampedToImage`），未在桌面拖动线段端点。
  - 画笔/文字只支持整体平移，没有缩放（本轮决策）；未验证"拖动时贴边夹取"的桌面手感（Core 单测覆盖 `MoveDrag_IsClampedToImageBounds`）。
  - 未做标注的**属性编辑**（选中后改颜色/粗细/字号），PLAN 只要求"选中、移动、删除及修改文字"，属性编辑留作可选增强。
  - 未做**标注复制/粘贴**、右键菜单、层级调整（置顶/置底）的 UI 入口（Core `BringToFront`/`SendToBack` 已存在）。
  - 马赛克只在**单屏 125%** 下做了像素级定量核验；副屏 100% 只验证导出尺寸（`700x640`）与矩形标注，未在副屏重复马赛克像素核验。
  - JPEG 另存为未在本轮重新跑（T11 已验证编码路径；T17 只验证 PNG 与复制/贴图，三者共用 `RenderSelection`）。
  - 双击判定用 `Environment.TickCount64` + 400ms/3px 阈值；**未**接入 WPF 原生 `ClickCount`（遮罩用 `PreviewMouseLeftButtonDown`，原生双击会给第二次事件 `ClickCount=2`，但本轮选择自实现以便 Core 可测）。三击、慢速双击、不同 DPI 下的手感未逐一实测。
  - 拖拽期间的实时预览只在 125% 主屏观察；`LostMouseCapture` 兜底（Alt+Tab 中断拖拽）只做了代码处理，未真实注入验证。
  - 150% 缩放、三屏未测试（本机只有 125% + 100% 双屏）。
- 2026-10-18 **未验证（T14/T15 相关）**：
  - 没有做“选择/移动/删除已有标注、改已提交文字”——那是 T16；本轮只有新建标注与整体撤销/重做。
  - 画笔只验证了平滑开启（默认 `Smooth=true`）；`Smooth=false` 的折线渲染与导出（T17）未在桌面验证。
  - 文字只验证了单行拉丁文本（`Zeep Cut T15`）；多行（Shift+Enter）只在 Core 单测里验证（`CommitText_MultiLineContent_IsKeptAndCounted`），未在桌面按 Shift+Enter 实测；中文/CJK 文字的渲染与估算包围盒差异未实测。
  - 文字输入框的宽度是按选区夹取的固定上限（160–360 DIP），长文本只靠 `TextWrapping=NoWrap` 横向溢出，没有横向滚动条，也未见验证超长文本。
  - 马赛克只是块状预览；真实像素化、`BlockSize`/`Strength` 的可视效果属于 T17。
  - 导出渲染（T17）未做，因此“标注合并到导出图、不含遮罩与工具栏”未验证；目前复制/另存为/贴图仍是 T11 的纯裁剪（不含标注）。
  - 副屏只验证了矩形与箭头两种工具；其余五种工具在副屏未重复跑（同一套 Core 换算，主屏已逐一验证）。
  - 150% 缩放、三屏以上未测试（本机只有 125% + 100% 双屏）。
  - 触摸/触控笔、标注的键盘微调（方向键移动）、复制粘贴标注未实现也未验证。
  - 标注文字使用 `Segoe UI`，中文会回退到系统字体；字体族与字号没有做成可配置项（PLAN 只要求字号）。
- 2026-10-18 **未验证（T12/T13 相关）**：
  - 本轮是纯 Core，**未接 UI**（T14/T15）：没有在真实截图上绘制/渲染任何标注，描边视觉粗细、箭头头部、文字真实字体排版、马赛克像素处理均未验证。
  - 文字 `Bounds` 是确定性估算（半角 `FontSize×0.6`、全角 `FontSize`、行高 `FontSize×1.25`），未与真实字体测量对齐；命中测试允许这个量级的偏差。
  - 未实现真正的序列化/反序列化（只保证 `Id` 可显式传入、集合可遍历、删除后插回保留 `CreationOrder`）；JSON/SQLite 映射留到需要时。
  - 撤销/重做只在 Core 单测中验证；未接 Ctrl+Z/Ctrl+Y（T16），也未验证与 UI 选择态/文字编辑态的联动。
  - 容量 100 与"丢弃最旧"已单测；未做超大历史或性能测试。命令未做线程安全处理（UI 单线程使用）。
- 2026-10-18 **未验证（T11 相关）**：
  - 另存为的失败写入路径（磁盘满/只读目录/被占用）未注入；只验证了成功与取消。
  - 剪贴板失败只注入 `CLIPBRD_E_CANT_OPEN`；其它 COMException/权限失败未注入。
  - JPEG 的过滤器下拉选择未单独用键盘切换验证；JPEG 通过输入 `.jpg` 文件名 + 保存实际验证（扩展名优先；在 JPEG 过滤器下保留默认 `.png` 名仍按 PNG 保存，见已知问题）。
  - 副屏只验证了复制；副屏的另存为/贴图未重复测试（裁剪与写文件同一套代码）。
  - 贴图窗口只验证了主屏 125% 的定位与 Esc/× 关闭；副屏、多贴图、拖动/缩放/透明度/锁定/右键菜单未做（T18–T22）。
  - 保存对话框的 Topmost 临时取消依赖“对话框显示期间用户不会切走”；若切走，遮罩可能被其它窗口覆盖，取消后已恢复 Topmost 与焦点（成功/取消两条路径均实测）。
  - 150% 缩放、三屏、非竖直副屏未测试（本机只有 125%+100% 双屏）。
- 2026-10-18 **未验证（T9/T10 相关）**：
  - 只在 125% + 100% 双屏环境验证；150% 缩放、三屏以上、副屏非竖直排列、触摸/触控笔未测试。
  - 副屏只跑了“框选 + 整体移动 + 右下角调整”；八向全部手柄只在主屏 125% 下逐一实测（逻辑同一套 Core，未在副屏重复跑全八向）。
  - 复制 / 另存为已由 T11 接真实动作；贴图在 T11 是最小占位窗口（完整能力 T18–T22）。
  - 键盘移动/微调选区、双击全屏选择等额外快捷操作未实现，也未验证。
- 2026-10-18 **未验证（历史遗留）**：
  - 非 1409 的热键注册失败分支（如权限/参数异常）未注入。
  - 通知气球/Toast 的视觉呈现未能在截图中确认（见上）；`MessageBox` 兜底已真实验证。
  - 150% 缩放、三屏以上、竖屏以外的特殊排列未测试（本机只有 125%+100% 双屏）。
  - 窗口过滤中的“最小化 / `WS_EX_TOOLWINDOW` / cloaked”没有单独注入验证；只验证了真实桌面上枚举结果正确（主屏 8–9 个候选、副屏 0/1 个候选）且本进程遮罩未进入候选。
  - 遮罩聚焦后被新启动的窗口（如测试用 Notepad）抢焦点时，Esc 不会送到遮罩；验证时先点击遮罩再按 Esc。正常截图过程中不会新建窗口，但这是已知的焦点限制。
  - 仅验证了 Esc 关闭；Alt+F4 / 托盘退出关闭遮罩路径只从代码上处理，未逐一真实验证（退出时 `DisposeCaptureOverlay` 会视作取消）。
  - 受保护内容、HDR、安全桌面等特殊捕获场景按 PLAN 不承诺，未测试。
## 已知问题 / 风险

- **T14 决策：选区一旦开始标注就锁定**。`AnnotationSession.IsSelectionLocked` 为真（文档里有标注，或正在拖草稿/输入文字）后，遮罩隐藏 8 个手柄、忽略选区外按下、光标不再显示移动/调整箭头，并在选区右上方显示 `选区已锁定（N 个标注）`。原因：标注坐标是“相对选区图像原点”的，移动/调整选区会让已有标注与画面错位。全部标注被撤销（文档空且无草稿）后自动解锁。
- **T16 决策：选中态是"第三态"，优先于选区操作与 Esc**。`AnnotationSession.ActiveTool == null`（工具栏“选择”）+ 选区已定稿 + 文档非空时，点选区内会进入编辑：先命中选中标注的手柄（半径 10px），再走 `AnnotationDocument.HitTest`（容差 4px）。此时 Esc 的顺序是 **草稿 → 拖拽中取消 → 取消选中 → 取消截图**；有选中时 Esc 只取消选中，遮罩保持打开（已实测）。
- **T16 支持范围（有意收窄）**：矩形/椭圆/马赛克支持八向缩放；箭头/直线支持整体平移 + 拖两端点；**画笔/文字只支持整体平移**（不给缩放，避免点串被拉伸、字形被压扁）。选“选择”以外的工具会自动取消选中（`SetActiveTool` 里做的），所以"想编辑已有标注"必须先点“选择”。
- **T16 一次拖拽 = 一条历史**：拖拽过程中 `DragTo` 直接改标注做实时预览、不写历史；`EndDrag` 发现几何变化时先**回滚到初始几何**，再用 `ResizeAnnotationCommand`/`MoveAnnotationCommand`/`SetSegmentEndpointsCommand` 应用一次最终几何。这样 `Execute`/`Undo` 永远对称，且撤销一次就整段回退（已实测：撤销 3 次依次回退删除/缩放/移动）。改这段时不要图省事在 `DragTo` 里 `History.Execute`。
- **T16 双击是自实现（400ms / 3px）**：`RegisterClick()` 必须在**每一次**按下时调用（第一次也要记时间戳，否则第二次进不了判定窗口 —— 本轮踩过这个坑，第一版只记录文字分支，导致双击永远不成立）。消费后清零避免三击触发两次。
- **T17 导出坐标只有一个原点**：标注坐标是"相对选区左上角"的物理像素（T14 的 `AnnotationCanvasTransform`），而裁剪图左上角也正好是选区左上角，所以导出**不需要**再减 `imageRect.Left/Top`。本轮第一版多减了一次，会在选区原点非 (0,0) 时整体偏移 —— 已在 `AnnotationExportRenderer.Composite` 里去掉并加注释。
- **T17 马赛克按 Z 序处理"当前像素"**：走到马赛克时，先把之前累积的标注合成为位图，再对这份像素做块平均；所以"马赛克盖在矩形/直线上"会把这些标注一起糊掉（与预览一致，已实测直线被平均成粉色块）。如果以后要求"只糊原图、不糊标注"，需要改成两遍合成（先算原图马赛克层、再按 Z 序合成），并补测试。
- **T17 马赛克默认强度改为 1.0**（原 0.75）：0.75 会保留 25% 原像素，密排文字细看仍可辨认，削弱打码效果。块平均本身是"块的算术平均"，不是模糊；强度 <1 就是与原像素的线性混合。需要"轻打码"时显式设小，`AnnotationDefaults.ClampMosaicStrength` 仍钳 0–1。
- **T17 预览马赛克与导出共用同一算法**：`AnnotationLayer` 拿冻结图 + 选区裁剪矩形，按 `MosaicPixelator` 算出区域的像素化位图并缓存（键 = 可见区域 + 块大小 + 强度），再由 `AnnotationRenderer` 按 1:1 铺上去。也就是说预览不再是"半透明棋盘示意"，看到什么导出就是什么。缓存不会自动失效 —— 选区变化会重建 `AnnotationLayer`，但**同一选区内修改块大小/强度**目前只能靠 `InvalidateMosaicCache()`，UI 还没有调整入口（T16 未做属性编辑）。
- **T17 导出 DPI 做法**：`RenderTargetBitmap` 的像素尺寸直接取选区物理像素，dpi 参数用目标屏 DPI，绘制上下文压 `ScaleTransform(96/dpi)`。**不要**用 DIP 尺寸建 RenderTargetBitmap 再乘缩放，125% 下会得到 80% 的像素数（本轮验收点就是"导出尺寸 = 选区物理像素"，已在 125%/100% 各实测一次）。
- **验证马赛克必须有高频纹理**：桌面壁纸/纯色背景上做马赛克，像素化与"半透明方块"肉眼无法区分（本轮前两次就落在纯色区，看不出效果）。`scripts/texture-window.ps1` 专门生成 12px 彩色块 + 密集文字，做马赛克验证时先起这个窗口。注意它是 WinForms（验证脚本允许），且 Windows 在 125% 下会把 WinForms 尺寸按 DIP 缩，必须用 `SetWindowPos` 强制物理矩形。
- **视觉层不只选区白框会漏进导出**：导出只画"裁剪图 + 标注"，`_dim`/`_border`/`_handles`/`_sizeBadge`/`_toolbar`/`_editorCanvas`/`_lockBadge` 都是遮罩窗口的兄弟元素，不参与 `AnnotationExportRenderer`。已用"导出图 vs 遮罩出现前的全屏截图"逐像素比对确认（底部条带 0/6480 差异 >12）。
- **选了绘制工具后就禁止拖动改选区**（即使还没落笔），必须先点工具栏“选择”才能重新框选/调整。这是为了避免“想画一笔却把选区挪走”。代价是切换多一次点击；如果以后觉得碍事，可以改成“只有已落笔才禁止改选区”，但要同时处理“选区变了已有标注怎么跟着变”。
- **标注层是选区大小的独立元素**（`AnnotationHost` 内有 `AnnotationLayer`），绘制在 `OnRender` 里压入 `ScaleTransform(96/dpi)`，所以 125% 屏幕上 100 物理像素 = 80 DIP；这与 T11 的裁剪（纯物理像素）是两套坐标，改渲染时不要混。
- **文字输入框的 Enter 拦截有两层**：遮罩 `PreviewKeyDown`（先收到）和 `TextBox.PreviewKeyDown`（兜底），都是为了满足 PLAN 的“文字输入时 Enter 优先用于文本编辑”。两层都只处理 `Key.Enter`，Shift+Enter 不拦截（留给换行）。改键盘处理时两层要一起看。
- **验证脚本的输入法坑**：本机 `SendKeys` 会吃掉空格和数字（`Zeep Cut T15` 曾变成 `ZeepCutT5`），所以 `verify-t14t15.ps1` 改用 `SendInput` + `KEYEVENTF_UNICODE` 逐字输入，并且 `INPUT` 结构体必须 `Size = 40`（x64）否则 `SendInput` 静默失败。另外 `SetCursorPos` 在遮罩激活的瞬间可能被丢弃，脚本会在每次移动后用 `GetCursorPos` 回读校验并重试。
- 工具栏现在有两行（约 723×71 物理像素，副屏 100% 下约 723×71 DIP），比 T10 的单行更宽更高；`PlaceToolbar` 仍只做“下方优先/上方回退/水平夹取”，极小选区下两行工具栏可能盖住选区内容（未做自动收纳/折叠）。
- 标注撤销历史与截图输出是分开的：按 Esc 取消后标注全部丢弃（不写历史），复制/另存为/贴图成功后遮罩关闭、标注也随之丢失（T17 会把它们渲染进输出图）。这不是 bug，是 T14/T15/T17 的分工。

- 托盘图标默认进入 Windows 11 的“显示隐藏的图标”溢出面板，不在任务栏直接可见；用户可手动固定到任务栏。
- 菜单占位：从剪贴板贴图仍是空方法（仅写日志）；显示/隐藏全部贴图、历史、设置、检查更新弹“尚未实现”。截图菜单已接真实捕获与冻结遮罩。
- `capture-debug` 是 T6 临时调试产物；T7 起 Alt+A 不再自动落盘，`ScreenCapture.SaveDebugPng` 仅保留为诊断方法，目录仍由 `AppPaths.EnsureCreated` 创建。
- 本机是**混合 DPI**（主屏 125%、副屏 100%）。T7 实测：主屏 WPF 按目标 DIP 摆放一次成功（物理 (0,0) 2560×1440、DIP 2048×1152）；副屏 WPF **首次按主屏 125% 摆放成 (-1350,0) 1350×1942**，由 `Loaded` 里的 `GetWindowRect` 复核 + `SetWindowPos` 校正为 (-1080,0) 1080×1920。当前做法已真实验证正确，但依赖“显示后校正”这一步；若以后 WPF 行为变化或出现可见闪烁，优先检查这里。
- T9/T10 已接入框选与工具栏；Esc/Enter 仍要求遮罩处于键盘焦点。若截图期间有新窗口抢焦点，Esc 可能收不到，需要先点一下遮罩。
- T9 手柄沿用 Controller 的“边跟随指针”语义：按在边缘内侧（容差内最多 6px）时，按下后该边缘会立即对齐到指针位置，因此按下点比原边缘内缩多少，选区就先缩小多少（实测内缩 2px → `738×578` 而不是 `740×580`）。尺寸文字与实际选区一致，不是换算错误；若以后需要“零跳动”手感，应在 Core 增加按增量调整的模式并补测试，而不是在 UI 里另算。
- 在窗口候选上做小于最小尺寸的拖动会被 Controller 视为单击窗口并采用候选（T2 既有语义）。验证“过小框选无效”必须在没有窗口候选的区域（本机为顶部任务栏空白带）进行。
- 本机 DSH 的 `powershell.exe` 运行在沙箱视图中：`Get-Content %LOCALAPPDATA%\ZeepCut\logs\...` 可能读到旧内容，`FindWindow` 按完整中文标题也不可靠。真实日志通过 WSL `/mnt/c/Users/19427/AppData/Local/ZeepCut/logs/app-YYYYMMDD.log` 读取；自动化找遮罩窗口要用“进程 id + 标题前缀 `Zeep Cut`” + UI Automation（工具栏显示后才能找到按钮，且按钮在 `Visibility.Collapsed` 时不会出现在 UIA 树里）。
- 主屏物理 2560×1440、逻辑 2048×1152。若按旧文档期待“2048×1152 PNG”会误判；产品按 PLAN/ARCHITECTURE 使用物理像素，捕获结果 2560×1440 是正确行为。
- Windows 通知横幅在本机自动截图中不可见；热键冲突已用启动 MessageBox 兜底，不影响另外的热键注册。
- 本机 Windows PowerShell 5.1 的 `Start-Process` 会因环境变量 `NO_PROXY`/`no_proxy` 大小写重复键抛 `Item has already been added`；且 GUI 进程继承 WSL 管道会让调用一直等待。`scripts/run.ps1` 已改用 `Invoke-CimMethod Win32_Process Create`（立即返回、不继承管道）。GUI 自动化时 UIA 坐标与 `scripts/mouse.ps1` 坐标一致（本机托盘按钮 1747,81,50,50）。
- `scripts/screenshot.ps1` 是 DPI-unaware 的 WinForms 脚本：在本机主屏上截到的是逻辑 2048×1152，而产品捕获为物理 2560×1440。需要物理像素基准时用新增的 `scripts/screenshot-physical.ps1`。
- T7 起 `scripts/mouse.ps1` 自己声明 PerMonitorV2 并回读 `GetCursorPos`；`scripts/window.ps1` 用物理坐标摆放测试窗口，`scripts/screenshot-physical.ps1` 截物理像素。T9 起 `mouse.ps1` 支持 `-ToX/-ToY` 真实拖动，`screenshot-physical.ps1` 支持 `-Primary`/`-DeviceName`（屏幕枚举顺序不固定），`verify-t9t10.ps1` 提供七种交互场景。
- Inno Setup 未安装，阶段四需尝试 winget 安装；失败则只交付便携版并说明。
- 尚无真实更新渠道地址（`AppInfo.UpdateManifestUrl` 为空），实现时必须显示“尚未配置更新渠道”。
- 无代码签名证书，安装包将为未签名。

- **SaveFileDialog + Topmost 所有者（T11 已修复）**：遮罩 `Topmost=true` 时，Win32 保存对话框会在显示后自行返回（实测出现过默认名在 1.5–44 秒后静默保存，`DialogResult=True`；表现为“没点保存却生成了文件”）。现在 `ShowDialog` 期间临时 `Topmost=false`，`finally` 恢复并在取消后重新激活遮罩；修复后对话框稳定等待用户操作。若以后 WPF/系统行为变化，优先检查这里；日志有 `保存对话框已打开/已关闭`。
- **另存为格式解析是“扩展名优先”**：`.png`→PNG、`.jpg/.jpeg`→JPEG；其它/无扩展名时按对话框过滤器并在末尾补正确扩展名。因此在“JPEG 图片”过滤器下直接保存默认的 `.png` 名称仍会得到 PNG；输入 `.jpg` 或去掉扩展名才按 JPEG 保存（实测 `.jpg` 成功）。若要求“过滤器决定格式”，应在 `FileOk` 里改写扩展名并补测试。
- 保存失败与取消使用工具栏内联文字提示（不抢键盘焦点）；失败时遮罩保持可重试。失败/取消只写 WARN/ERROR/取消 INFO，不写“输出完成”。
- 贴图占位窗口未纳入 App 的贴图集合管理（退出时由 WPF 关闭全部窗口）；T18–T22 接入集合、全局显示/隐藏、拖动/缩放等。
- 本轮验证产生的临时文件（`Pictures\截图_2026-10-18_*.png`、`%TEMP%\zeepcut-t11`、临时排查脚本）已清理；`artifacts/t11/` 保留截图证据（`artifacts/` 在 `.gitignore` 中，不入库）。
- **T12 标注坐标是图像坐标，不是屏幕坐标**：`Annotation` 的像素坐标相对截图图像左上角（副屏截图同样从图像 (0,0) 开始）。T14 把鼠标 DIP 换算成图像坐标时，要减去目标屏幕原点并把 DIP 转物理像素，不能直接把遮罩窗口坐标当标注坐标；T17 导出也不需要再减屏幕原点。
- **T12 命中语义是“内部命中”**：矩形/椭圆/马赛克点内部即可选中（椭圆排除四角）；直线/箭头/画笔按到线段距离。若以后要求“只点边框才选中”（例如内部要穿透到下层标注），需要改各类型的 `HitTest` 并补测试，而不是在 UI 里另写命中逻辑。
- **T12 文字包围盒是估算**：`TextAnnotation.Bounds` 用固定系数（半角 0.6、全角 1.0、行高 1.25），与实际 WPF 字体测量会有偏差；T15 渲染/导出若按真实字形排版，选中框和点击范围可能略有出入。需要精确时应在 Core 注入测量委托（`Func<string,int,int>`），不要依赖 WPF 类型进 Core。
- **T13 `EditHistory` 不持有文档**，命令对象持有文档/标注引用；跨窗口（截图编辑窗口与历史再编辑窗口）要各自建 `EditHistory`，不要把两个窗口的命令混进同一条历史。
- **T13 命令不是线程安全的**，默认 UI 单线程调用；`EditHistory` 只在命令成功 `Execute` 后才入栈，命令抛异常不会污染历史（已单测）。

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
- T9 UI 只做换算/绘制/输入映射，状态与几何全部由 Core `SelectionController` 决定；换算入口固定为 `DpiTransform`（DIP→物理）与 `WindowCandidateSelector.ToDipBounds`（物理→DIP），窗口里不再另写比例。
- T9 渲染顺序：冻结图 → 压暗层（EvenOdd 挖洞）→ 白框/悬停蓝框 → 8 手柄 → 尺寸标签；工具栏放在单独的、可命中测试的 Canvas 层，按钮 `Focusable=false`，保证 Enter/Esc 始终作用于遮罩。
- T9 交互事件用 `PreviewMouseLeftButtonDown/Up` + `CaptureMouse`，`LostMouseCapture` 时用最后位置定稿，避免 Alt+Tab 后卡在 Dragging。
- T10 工具栏只在 `IsCaptureReady`（Selected + 最小尺寸）显示；拖动/调整期间用右上尺寸标签，松开后由工具栏承担尺寸显示。
- T10 工具栏与尺寸标签的摆放算法下沉到 Core `SelectionOverlayLayout`（下方优先、上方回退、水平夹取、超大贴边），并补了 xUnit；尺寸文字固定用物理像素 + ` × `。
- T10 按钮本轮全部日志占位（复制/另存为/贴图），只有“取消”真实关闭；Enter 走复制占位，Esc 取消。
- 点击工具栏矩形不进入选区状态机（`PreviewMouseLeftButtonDown` 先判断是否落在工具栏内），避免点按钮时把已定稿选区清掉重新框选。
- 遮罩把工具栏和四个按钮的物理矩形写入日志；UIA 用“进程 id + 标题前缀”找遮罩、按按钮名定位，自动化验证不硬编码坐标。
- T11 裁剪与命名下沉 Core `ZeepCut.Core.Output`：`SelectionCrop.ToImageRect` 只做“屏幕绝对物理选区 − 屏幕原点”的换算（DPI 不参与）；`CaptureFileName` 显式列出 Windows 非法字符（不用 `Path.GetInvalidFileNameChars`，避免 Unix 实现只返回少数几个字符）。
- T11 成功记录用 `ICaptureSink` 接缝：遮罩只在复制/另存为/贴图**成功后**调用一次；`LoggingCaptureSink` 写动作/选区/尺寸/耗时/文件/时间；T24 换成历史 sink 即可，不动遮罩与动作代码。
- T11 失败/取消不写完成记录：复制失败（COMException）与保存失败只写 WARN/ERROR 并在工具栏内联提示；取消保存只写取消 INFO，遮罩保持；成功才关闭遮罩。
- T11 另存为默认 PNG、可选 JPEG；JPEG 编码前转 Bgr24 去 alpha、质量 90；格式按扩展名解析，未知/无扩展名时按过滤器并在末尾补正确扩展名。
- T11 贴图仅占位窗口（无边框/Topmost/1:1/×+Esc），按目标屏 DPI 摆放并做 Loaded 物理校正；不纳入集合管理，T18–T22 替换。
- T11 SaveFileDialog：**显示期间临时取消遮罩 Topmost**，否则 Topmost 所有者会让 Win32 对话框自行返回；`finally` 恢复并重新激活遮罩（修复前实测会静默保存/关闭，修复后稳定等待用户操作）。
- T11 验证脚本 `scripts/verify-t11.ps1` 覆盖 copy/copy-button/copy-edge/save-png/save-jpeg/cancel-save/pin/copy-busy；`scripts/clipboard.ps1` 在独立进程读剪贴板（`-Save` 落盘、`-Clear` 清空、`-HoldSeconds` 占用剪贴板复现失败）。
- T11 保存对话框的自动化：文件名字段是 DirectUI（UIA 无 ValuePattern，子 Edit 的 `SetWindowText` 不改变保存结果），改为点击聚焦 + **剪贴板粘贴**路径；保存/取消按钮用对话框真实子 Button 的 `BM_CLICK`。中文输入法会把 `C:\...` 改写成 `C：、...`，禁止用 SendKeys 直接键入路径。
- T12 标注模型放 `ZeepCut.Core.Annotations`，坐标是**截图图像内的物理像素**（相对图像左上角），不引入 WPF 颜色/几何类型；颜色用 `uint` ARGB 的 `AnnotationColor`，可直接存整数列。
- T12 `Annotation.Bounds` 是**不含描边的几何包围盒**，另给 `GetRenderBounds()`（按描边半宽外扩、至少 1 像素）用于渲染裁剪；退化图形（单点/零长度/零宽）包围盒为零尺寸但命中测试仍有效。
- T12 命中语义：矩形/椭圆/马赛克命中内部 + 容差（椭圆排除四角），直线/箭头/画笔按点到线段距离 + 容差 + 描边半宽，文字按包围盒；负容差按 0。选中标注时不必精确点中描边，后续若需要“只点边框”再改 Core 并补测试。
- T12 属性越界一律**静默钳制**（粗细 1–20、字号 8–72、块 4–64、强度 0–1，NaN 强度回退默认），不抛异常；这样 UI 滑杆、配置导入、旧数据都不会因一个越界数字失败。
- T12 `AnnotationDocument` 的列表顺序就是 Z 序（末尾最上层），`CreationOrder` 由文档分配（从 1 递增）并在删除后插回时保留；提供 `MoveToIndex`/`BringToFront`/`SendToBack` 与取最上层的 `HitTest`。
- T13 撤销/重做用**命令模式**而不是整文档快照：`IEditCommand` 保存执行所需旧值，命令直接持有文档/标注引用；`EditHistory` 只管两条栈、容量和顺序，不持文档，便于截图编辑与历史再编辑复用。
- T13 命令集：添加（重做插回同一对象与原层级）、删除（恢复原索引/顺序/CreationOrder）、移动（反向平移）、清空（按原顺序全部插回）、属性泛型命令（构造时捕获旧值，撤销写回）；`AnnotationEdits` 工厂提供颜色/粗细/文字/字号/背景/马赛克/平滑等常用命令。
- T13 容量默认 100，超出**丢弃栈底最旧**的一步（被丢弃的无法再撤销）；新命令执行成功后清空重做栈；空栈 `Undo`/`Redo` 返回 false 不抛异常；命令 `Execute` 抛异常时不入栈。
- T14 **选区锁定**（本轮核心决策）：一旦开始标注（文档里有标注，或正在拖草稿/输入文字），选区就不能再移动/调整，也隐藏 8 个手柄、不显示移动光标，并在选区右上方显示 `选区已锁定（N 个标注）`。理由：标注坐标相对选区图像原点，选区块一变已有标注就与画面错位。全部撤销后自动解锁。另外“选中绘制工具”本身也禁止拖动改选区（要先点“选择”），避免画一笔时误挪选区。
- T14 会话状态与 UI 解耦：新增 Core `AnnotationSession` 承载“文档 + `EditHistory` + 工具设置 + 草稿/文字输入 + 锁定判定”，遮罩只做 DIP↔像素换算、绘制和键盘。这样草稿提交/退化丢弃/撤销阻塞/锁定语义都能单测（28 个测试），不用靠桌面操作验证逻辑。
- T14 坐标换算**不再新增**第二套：全部走 T12 留下的 `AnnotationCanvasTransform`（`ToImage`/`ToImageClamped`/`ToDip`/`ToDipLength`/`SelectionDip`），窗口里没有出现 `dpi/96` 之类的散落常量。绘制层 `AnnotationLayer` 内部压一个 `ScaleTransform(96/dpi)`，因此渲染代码拿到的是“图像物理像素”。
- T14 工具栏做两行：上行工具（选择 + 七种），下行尺寸/颜色（6 个预设）/粗细（1·3·6·10）/撤销/重做/复制/另存为/贴图/取消。按钮 `Focusable=false` 保证键盘永远属于遮罩；同时给每个按钮 `AutomationProperties.Name`（`工具矩形`/`颜色#FF0A84FF`/`粗细10`），UIA 自动化不再依赖硬编码坐标。
- T15 文字工具走独立输入框（不是拖拽草稿）：`AnnotationDraftFactory.Begin(Text)` 仍返回 null，UI 用 `AnnotationSession.BeginText/UpdateTextPreview/CommitText`，输入框字号按 `96/dpi` 换算成 DIP 做所见即所得，`TextChanged` 实时预览；空白内容提交等于取消、不产生空标注。
- T15 **文字 Enter 两层拦截**：遮罩 `PreviewKeyDown` 先判断 `_session.IsTextEditing`，为真时 Enter/Esc 只归文字（Shift+Enter 留给换行）；`TextBox.PreviewKeyDown` 再兜一层。实测输入 `Zeep Cut T15` 后按 Enter，日志只有 `文字提交（Enter）… 已提交=True`，之后没有任何 `开始输出/输出完成`，遮罩保持打开并继续画了矩形 —— PLAN 的“文字输入时 Enter 不误触发复制”成立。
- T15 马赛克只做块状预览（半透明压暗 + `BlockSize` 交替块），真实像素化放 T17；画笔默认 `Smooth=true` 用二次贝塞尔中点平滑；箭头头部长度 `max(10, thickness*4)` 且不超过线段长度，杆画到头部底边前 35% 避免粗线穿出尖端。
- T15 撤销/重做有两个入口（Ctrl+Z/Ctrl+Y 与工具栏按钮），都统一走 `AnnotationSession.Undo/Redo` → `EditHistory`；草稿/文字输入期间返回 false，不打断当前操作。日志会打印来源（`Ctrl+Z` / `工具栏按钮“撤销”`）与栈深度。
- T16 编辑状态机放 Core `AnnotationEditController`，与 `AnnotationSession` 用**同一个** `AnnotationDocument` 与 `EditHistory` 实例（`AnnotationSession(PixelRect imageBounds)` 构造时创建）。这样选中/拖动/缩放/删除与"画标注"共用一条撤销栈，Ctrl+Z 不需要区分来源。**不要**为编辑再建第二个 `EditHistory`（会与 T13 的"各自建历史"建议冲突，但这里同一窗口同一文档，共用才对）。
- T16 的 `EndDrag` 用"回滚 + 命令"两段式来保证"一次拖拽一条历史"且命令对称；`CancelDrag` 只回滚、不写历史。`Session.Undo/Redo` 在拖拽中也返回 false（不打断当前操作）。
- T16 手柄语义与选区分开：`AnnotationHandle`（标注）≠ `SelectionHandle`（选区）。选区手柄的"边跟随指针"特性（T9 已知会内缩）**不适用于**标注缩放：标注缩放以拖拽开始时的包围盒为基准，不会因为按下点内缩而先跳一下。
- T17 导出渲染放 `src/ZeepCut/Editor/AnnotationExportRenderer.cs`（WPF 层），马赛克像素算法放 Core `MosaicPixelator`（可单测）。分工理由：`RenderTargetBitmap`/`DrawingVisual` 必须有 WPF，而块平均只是字节运算，放 Core 才能覆盖边界条件。
- T17 把"预览马赛克 = 导出的马赛克"当成硬要求：`AnnotationRenderer` 接受 `Func<MosaicAnnotation, BitmapSource?>`，预览层用冻结图算、导出层用当前合成位图算，绘制代码只有一份。拿不到位图时退回块状示意仅作兜底（正常路径不会走到）。
- T17 输出三条路径（复制/另存为/贴图）统一走 `RenderSelection` → `AnnotationExportRenderer.Render`；`ICaptureSink` 的调用点与"只在成功路径调用一次"的语义没变（T11 的接缝保持完好）。
- T17 导出前做**尺寸断言**：`PixelWidth/PixelHeight` 必须等于选区物理像素，否则抛异常并留在截图状态。这比"输出后再检查"更早暴露 DPI 换算错误。
- T17 验证新增 `scripts/verify-t16t17.ps1`（6 场景）与 `scripts/texture-window.ps1`（高频纹理，仅验证用）。保存对话框仍走 T11 的"点击文件名字段 + 剪贴板粘贴 + `BM_CLICK`"路径（DirectUI 无 ValuePattern，IME 会改写 `C:\...`）。
- T15 验证脚本的两个本机坑（写进脚本注释）：`SendKeys` 在本机 IME 下会吃掉空格/数字，改为 `SendInput + KEYEVENTF_UNICODE` 逐字输入且 `INPUT` 必须 `Size=40`；`SetCursorPos` 在遮罩激活瞬间会被丢弃，移动后必须 `GetCursorPos` 回读校验并重试。
