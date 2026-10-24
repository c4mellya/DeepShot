# DeepShot 功能缺口分析与路线建议

> 范围：4 位分析员共提出 47 条候选，合并后约 30 项。每一项都对照 `master@87beb78` 用 grep 和读代码做了抽查。文件路径相对仓库根目录。

## 0. 抽查结论速览

**候选里说得不准、或者已经实现的（已剔除或修正）：**
- **历史图拖出为文件：已经实现。** `src/DeepShot/History/HistoryActions.cs:53` `DragFile` 用的是 `SetFileDropList`。真正缺的只是把文件放进剪贴板（CF_HDROP）的"复制为文件"，下文第 B6 条可以直接复用这段逻辑。
- **"Select 工具"并不存在**（`AnnotationTool` 只有 7 种工具）。不过点中已有标注就能选中（`_session.Editing.HasSelection`，见 `CaptureOverlayWindow.cs:608`），所以"改已选标注的样式"这一条仍然成立。
- **保留期改成可配置的工作量比候选估的小。** `HistoryRetentionPolicy` 的所有方法已经接受 `retentionDays` 参数，只有 `HistoryRetentionScheduler.cs:122`、`SettingsWindow.cs:292` 和 `HistoryRepository.cs:572` 用的是常量默认值。工作量定为 S，容量上限另算。
- **长路径**：`src/DeepShot/app.manifest:25` 已经开了 `longPathAware`，MAX_PATH 风险比候选说的低。但外部程序未必支持长路径，文件名仍应控制在 260 以内。

**抽查确认属实的关键证据：**
- 三处"另存为"都写死了从"图片"文件夹开始：`CaptureOverlayWindow.cs:2222`、`History/HistoryActions.cs:156`、`Pins/PinWindow.cs:768`。
- `CaptureFileName.DefaultBaseName = "截图"`：英文界面下默认文件名也是"截图"，这本身就是一个小 bug。
- `CaptureImageEncoder.DefaultJpegQuality = 90` 是常量。全仓库搜不到 AutoSave、explorer、/select。
- 截图界面里，右键松开会无条件 `Cancel("鼠标右键")`（`CaptureOverlayWindow.cs:366-370`）。`OnPreviewKeyDown` 只处理 Esc、Enter、Ctrl+Z/Y、Delete/Back。`SelectionController` 只有 `Pointer*`、`Cancel`、`SetWindowCandidates`，没有 Nudge 或 SetSelection。
- `AnnotationEdits.SetColor/SetThickness/SetFontSize/SetTextBackground` 只有定义（`Core/Editing/AnnotationCommands.cs:304-340`），UI 里没有任何地方调用。`SelectColor` 的日志写的是"对之后创建的标注生效"。
- `AppInfo.UpdateManifestUrl` 为空。`origin` 指向 `github.com/c4mellya/DeepShot`。
- 托盘命令只有 7 个（`Platform/TrayIcon.cs:9-17`）。贴图右键菜单只有 5 项。历史卡片没有右键菜单。
- 搜不到 OCR、放大镜、取色、延时截图、命令行参数（e.Args）、AllowDrop、便携模式、长截图、模糊、序号这些功能。
- `HistorySchema.CurrentVersion = 1`，迁移数组为空，但迁移循环已经写好。数据库用 WAL 模式（`HistorySchema.cs:222`）。
- **`DeepShot.slnx` 里没有测试项目**，但不少注释提到了单测。建议在第一个版本里补一个 `DeepShot.Core.Tests`。

---

## 1. 分级清单

### P0：下一步就做（价值高，工作量 S 或 M）

| # | 功能 | 要做什么 / 为什么 | 涉及文件 | 工作量 | 风险 |
|---|---|---|---|---|---|
| A1 | **自定义保存路径、一键保存、文件名模板、自动保存** | 见第 2 节的完整设计。这是你提的 idea，也是 4 位分析员共同的头号建议 | 见第 2 节 | M | 文件夹不可写或离线，见第 2 节 |
| A2 | **右键先清空选区，再右键才退出** | 现在误点一次右键，选区和所有标注就没了，属于数据丢失。改成：有选区时右键回到识别/框选状态；已有标注时先确认（或让这一步可以撤销）；没有选区时才退出。快速截图模式保持原样 | `Capture/CaptureOverlayWindow.cs:366`、`Core/Selection/SelectionController.cs`（复用 `Cancel()`） | S | 有标注时要先确认 |
| A3 | **截图界面的键盘操作** | Ctrl+C 复制、Ctrl+S 保存（配合 A1 一键保存）、Ctrl+Shift+S 另存为、Ctrl+T 贴图、Ctrl+A 选中整屏。方向键微移选区 1px（Shift 时 10px），Ctrl 加方向键调整边。选中标注时方向键改为移动标注，算一步撤销。单字母切换工具（R/E/A/L/P/T/M）。Shift 拖动时线条锁 45°、矩形锁正方 | `Core/Selection/SelectionController.cs`（新增 Nudge、ResizeEdge、SelectAll）、`CaptureOverlayWindow.cs:550`、`Core/Annotations/AnnotationDraft.cs`（约束）、`History/HistoryEditWindow.cs` | S | 要用规范化后的 `key`，不能直接用 `e.Key`（Alt+A 之后会是 Key.System 或 IME 状态）。正在编辑文字（`IsTextEditing`）时不能抢键。选区在开始标注后会锁定，要遵守这个规则 |
| A4 | **改已选标注的样式，同时补上字号和文字底色控件** | 命令已经写好但没接上，几乎白捡。点色块或粗细时，如果当前有选中的标注，就把样式改到这个标注上（一步撤销），同时更新默认值。选中文字工具时，工具栏多出"字号"一组（S/M/L/XL）和"底色"开关 | `CaptureOverlayWindow.cs:2380-2391`、`History/HistoryEditWindow.cs`（约 426/443 行）、`Core/Annotations/AnnotationSession.cs:215`（新建文字时带上底色）、`Capture/OverlayChrome.cs` | S | 工具栏变宽后要检查 `UpdateToolbar` 的定位。字号按物理像素算，在 150% 缩放下会偏小，预设值要乘上 DPI |
| A5 | **记住上次的颜色、粗细、字号（可选：上次用的工具）** | 现在每次截图都会重置为红色、3px、矩形 | `CaptureOverlayWindow.cs:180`、`HistoryEditWindow.cs:137`、`AnnotationSession`（接收初始设置） | S | **不要写进 settings.json**：整份记录覆盖写，会和设置窗口互相覆盖。参照 `history-ui-state` 另存一个 `editor-state.json`，写入时做防抖 |
| A6 | **保留期可配置（7/30/90/365 天或永久）和"清空非收藏历史"** | 有了自定义路径和自动保存之后，30 天写死的问题会更明显。Core 层已经支持传参 | `Core/Settings/AppSettings.cs`、`History/HistoryRetentionScheduler.cs:122`、`Settings/SettingsWindow.cs:292`、`HistoryGalleryWindow`（取消收藏时的提示）、README | S | 改短保留期会立刻删一批记录，保存前要显示将删除的条数并确认 |
| A7 | **启用更新渠道** | 检查更新功能已经全部写好，只是 URL 是空的，菜单项点了等于没用。最省事的做法：每次 Release 附带一个 `update.json`（沿用现有的 `{version,url,notes}` 格式），URL 指向 `releases/latest/download/update.json`，不用解析 GitHub API，也没有每小时 60 次的请求限制 | `Core/AppInfo.cs:13`、`scripts/publish.ps1`（生成 json 和 sha256） | S | 仓库必须公开。不要做自动下载和安装（放到 P2） |

### P1：值得做

| # | 功能 | 要做什么 / 为什么 | 涉及文件 | 工作量 | 风险 |
|---|---|---|---|---|---|
| B1 | **OCR 识别文字** | 国内截图工具里最常用的功能之一。用系统自带的 `Windows.Media.Ocr`，离线、不加依赖。截图工具栏、贴图菜单、历史里都可以触发 | `DeepShot.csproj`（TFM 改为 `net10.0-windows10.0.19041.0`）、新增 `Platform/OcrService.cs`、`CaptureOverlayWindow.cs`、`Pins/PinWindow.cs:668`、`History/HistoryActions.cs`、`ClipboardWriter`（写入文本） | M | 改 TFM 会影响发布和安装包，`scripts/` 和 `installer/` 都要回归测试。英文系统常常没装中文 OCR 语言包，要检测并给出提示。图片边长上限约 1 万 px，超过要先缩小。中文结果里字之间会多出空格，要后处理。识别要放到后台线程 |
| B2 | **放大镜、像素坐标、取色**（C 复制 HEX，Shift+C 复制 RGB） | 能精确对齐边缘，也能顺手取色，是 Snipaste、PixPin 的标配 | `CaptureOverlayWindow.cs`（`OnMouseMove` 和按键）、`OverlayChrome.cs`、新增 `Core/Capture/PixelColorFormat.cs`、`SelectionOverlayLayout`（靠近边缘时翻转位置） | M | 冻结图是物理像素，而界面在 PerMonitorV2 下按 DIP 布局，必须走现有的 PixelPoint 映射，否则在 125%/150% 缩放下会差 1px。鼠标移动时的开销：缓存像素数组，配合 CroppedBitmap 和 NearestNeighbor。放大镜要避开工具栏 |
| B3 | **一键截图（不出选区界面）：当前屏全屏和当前活动窗口** | 截全屏几乎零成本，复用 `CaptureMonitorUnderCursor` 即可。活动窗口用 `GetForegroundWindow` 加上 DWM 扩展边框，再按所在显示器裁剪。结果直接走复制、历史、自动保存这条输出管线 | `App.xaml.cs`、`Capture/ScreenCapture.cs`、`Capture/WindowEnumerator.cs`、`Platform/NativeMethods.cs`、`AppSettings`（新增 2 个快捷键并做冲突检查） | S（全屏）+ S/M（窗口） | 目标窗口句柄要在热键触发那一刻取。跨屏的窗口按占比最大的显示器裁剪。不要用 PrintWindow 或 WGC（GPU 窗口会截成黑屏，还要改 TFM） |
| B4 | **延时截图（3/5/10 秒）** | 用来截悬停提示、下拉菜单这类一按热键就会消失的界面 | `App.xaml.cs`（DispatcherTimer，再按一次取消）、`Platform/TrayIcon.cs`（子菜单）、倒计时角标 | S | 倒计时窗口必须不抢焦点（`WS_EX_NOACTIVATE`），并且不能被截进去（`WDA_EXCLUDEFROMCAPTURE`，或截图前先隐藏）。"已有截图界面"的守卫要把等待中的计时器也算进去。快捷键入口比托盘入口更重要，因为点托盘本身就可能把目标菜单关掉 |
| B5 | **重复上次区域** | 前后对比、做连续文档时很有用。可以提供两种用法：快捷键直接按上次区域输出，或者打开截图界面时预选上次区域。截图界面里再用 `,` `.` 前后翻历史选区 | `SelectionController`（新增 `SetSelection`）、新增 `Core/Selection/SelectionHistory.cs`、`App.xaml.cs`、`MonitorLayout` | S | 只在同一显示器、同一分辨率、同一 DPI 下有效，否则回退到正常截图界面 |
| B6 | **历史右键菜单、"在资源管理器中显示"、复制为文件（CF_HDROP）** | 现在历史图在磁盘上找不到对应文件，聊天软件要传附件也没法直接粘贴。右键菜单把已有的悬停按钮操作也收进来 | `History/HistoryCard.cs`、`HistoryQuickViewWindow.cs`、`HistoryActions.cs`（复用 `DragFile` 的 FileDrop 逻辑）、`Platform/SystemIntegration.cs`（`SHOpenFolderAndSelectItems`）、`ClipboardWriter.cs` | S | 内部文件名不可读（时间戳加哈希）。复制为文件时应导出一个以记录名命名的临时副本。同时放 CF_HDROP 和图片格式时，Word 等程序的粘贴行为会变，所以单独做一个菜单项，不改默认的复制 |
| B7 | **截图后的动作可配置** | 快速截图和 Enter 现在写死为复制，可以改成复制、保存、复制并保存、贴图里任选 | `CaptureOverlayWindow.cs:168/818/616`（`bool quickCopy` 改为枚举）、`App.xaml.cs:266`、`AppSettings` | S | 依赖 A1。保存失败时要保持"界面不关"的现有语义 |
| B8 | **重启后恢复贴图，加上"重开最近关闭的贴图"** | 参考图跨重启保留，误关也能找回 | 新增 `Core/Pins/PinSessionFile.cs`（用 AtomicFile 写）、`Pins/PinWindows.cs`（注释第 26 行写明了"退出不恢复"）、`PinState`（新增 Restore 工厂）、`HistoryPinRecorder`（返回记录 Id）、`TrayIcon` | M | 来源记录可能被保留期清理掉，要么把贴着的记录视为收藏、不参与清理，要么为贴图单独存一份 PNG。显示器布局变了要把位置收回屏内。保存要防抖 |
| B9 | **托盘菜单增强**：打开截图/保存文件夹、关闭所有贴图、暂停热键、切换截图模式 | 改动小，用处大（玩游戏、远程桌面时 Alt+A 会冲突） | `Platform/TrayIcon.cs`、`GlobalHotkey.cs`、`App.xaml.cs` | S | 设置窗口保存时会重新注册热键，暂停状态要在这之后保持住。"最近截图"子菜单先不做 |
| B10 | **序号工具（1、2、3…）** | 写教程、提缺陷报告时最常用 | `Core/Annotations/AnnotationTool.cs`、`AnnotationKind.cs`、新增 `StepMarkerAnnotation.cs`、`Editor/AnnotationRenderer.cs`、两个工具栏 | M | 编号由文档顺序推导出来，不要存进对象里，否则撤销或删除之后会乱 |
| B11 | **荧光笔、矩形和椭圆的填充** | 用来突出文字、整块遮盖 | `RectangleAnnotation`/`EllipseAnnotation`（新增 FillColor）、`AnnotationRenderer.cs:46/83`、`AnnotationDefaults.MaxThickness` | M | 线条自相交的地方透明度会叠加，要用 opacity 分组来画。WPF 没有正片叠底。调整 MaxThickness 会影响所有工具 |
| B12 | **绑定 PrintScreen 键** | 很多人的肌肉记忆 | `Core/Shortcuts/ShortcutSpec.cs:67/182`（只把 PrtSc 和 F 键列入"可以不带修饰键"的白名单）、`GlobalHotkey.cs`、`SettingsWindow.cs` | S | Win11 默认把 PrtSc 交给截图工具。读 `PrintScreenKeyForSnippingEnabled` 给出提示，并提供跳转到对应系统设置页的链接，**不要替用户改这个系统设置** |

### P2：以后再说

| 功能 | 说明 |
|---|---|
| **历史库根目录迁移**（第 2 节方案 C） | 价值高但工作量 L，涉及 WAL、文件占用、卸载程序，放在 A1 之后 |
| 高斯模糊（马赛克的另一种模式） | M。模糊程度要足够大，否则文字能被认出来。要做可分离的快速实现 |
| 自定义颜色和最近使用的颜色 | M。截图界面是置顶全屏窗口，弹出层容易被压在下面或抢走焦点 |
| 贴图增强：旋转/翻转、鼠标穿透、贴图上标注、双击关闭、阴影 | M。鼠标穿透必须始终留一条出路（托盘或热键） |
| 历史批量导出（文件夹或 ZIP）、批量收藏 | M。可以用 `System.IO.Compression`，不加依赖 |
| 历史编辑器里裁剪、旋转 | M。现在保存是直接覆盖，裁剪又是破坏性的，要等非破坏编辑做好以后再做 |
| 非破坏性标注（保存原图和标注 JSON，可以再次编辑） | L。磁盘占用翻倍。原图里有打码前的内容，有隐私问题。需要做数据库 v2 迁移 |
| 单实例激活和命令行参数（`--capture`、`--pin <file>`、`--history`） | M。第二次启动时打开历史（而不是弹"已在运行"）这部分可以先做（S） |
| 设置导入/导出/重置、历史库备份 | M。在方案 C 之后做比较顺 |
| 截进鼠标指针（选项） | S，但实际需求不多 |
| 跨屏或整个虚拟桌面一起截 | L。混合 DPI 下要每个屏幕一个界面窗口、共用一个选区控制器，改动面很大 |
| 长截图 | XL。截图界面要从冻结画面改成实时、可穿透的，拼接时吸顶栏和动画会捣乱。值得做（Snipaste 没有，是差异点），但要排在 OCR 和标注工具之后 |
| 便携模式 | S，但和方案 C 的根目录解析是同一套逻辑，应该一起设计 |

### Skip：不建议做

| 功能 | 理由 |
|---|---|
| WebP 输出 | WIC 只带解码器、没有编码器，得引入 SkiaSharp 或 ImageSharp，安装包会变大，还要做许可证审查，收益很小。PNG/JPEG 加质量设置已经够用 |
| 导出装饰（阴影、圆角、边框） | 很多程序读剪贴板 CF_DIB 时会丢掉 alpha，阴影会变成黑边，JPEG 也会丢 alpha。和"截图工具"的定位关系不大 |
| 把剪贴板文字、颜色贴成图 | 小众需求，还会改变现在 Alt+S"剪贴板里没有图片"的提示语义 |
| 更新时自动静默安装 | 要做签名和哈希校验，还涉及卸载时关闭进程，在 A7 之后视用户量再定。手动点"下载"就够了 |

---

## 2. 自定义保存路径：推荐设计

### 2.1 拆成三层，分别配置

| 层 | 含义 | 现状 | 建议 |
|---|---|---|---|
| **A. 导出目录** | "另存为"对话框的起始目录，以及一键保存（Ctrl+S）的写入目录 | 3 处写死为"图片"文件夹 | P0 |
| **B. 自动保存** | 每次复制、贴图、快速截图时，额外在指定文件夹里落一份文件 | 没有 | P0（和 A 一起做） |
| **C. 历史库根目录** | 整体迁移 `images/`、`thumbs/`、`history.db` | 写死在 `%LOCALAPPDATA%\DeepShot` | P2，单独发版 |

`settings.json`、`logs/`、`history-ui-state`、欢迎页标记**始终留在** `%LOCALAPPDATA%\DeepShot`，保证启动时总能找到设置。

### 2.2 设置字段（`src/DeepShot.Core/Settings/AppSettings.cs`）

```csharp
public const int CurrentSettingsVersion = 3;

public string? SaveDirectory { get; init; }          // null = 系统"图片"文件夹（等于现在的行为）
public bool RememberLastSaveDirectory { get; init; } = true;
public string FileNameTemplate { get; init; } = "{name}_{yyyy-MM-dd_HHmmss}";
public CaptureImageFormat DefaultSaveFormat { get; init; } = CaptureImageFormat.Png;
public int JpegQuality { get; init; } = 90;          // Sanitize 时夹到 1-100

public bool AutoSaveEnabled { get; init; }           // 默认关闭
public string? AutoSaveDirectory { get; init; }      // null = 图片\DeepShot
public AutoSaveTriggers AutoSaveOn { get; init; } = AutoSaveTriggers.Copy | AutoSaveTriggers.QuickCapture; // [Flags]：Copy/Pin/QuickCapture

public string? HistoryLibraryRoot { get; init; }     // 方案 C，null = 默认位置
```

- **上次使用的目录不放进 AppSettings**。AppSettings 是不可变记录、整份覆盖写，每次保存都写一次，会和设置窗口互相覆盖。改为放进单独的 `output-state.json`（参照 `History/HistoryUiState.cs`）。
- **Sanitize 规则**：路径必须是绝对路径。展开 `%USERPROFILE%` 这类环境变量后再判断。拒绝安装目录（`{localappdata}\Programs\DeepShot`）和数据根目录内部的路径。模板要过一遍 `CaptureFileName.SanitizeBaseName`，非法时回退为默认值并记一条警告。旧版程序读到 v3 文件会忽略不认识的字段，没有影响。
- **v2 升到 v3 的迁移**：新字段全部取默认值，行为和现在完全一样，零迁移。

### 2.3 文件名模板（`src/DeepShot.Core/Output/CaptureFileName.cs`，纯函数，可单测）

- 支持的变量：`{name}`（本地化前缀，中文为"截图"，英文为"Screenshot"，顺手修掉英文界面显示"截图"的问题）、`{yyyy}` `{MM}` `{dd}` `{HH}` `{mm}` `{ss}` 或整段 .NET 日期格式、`{w}`、`{h}`、`{counter}`、`{source}`（截图/贴图/快速截图）。`{app}`（前台窗口标题）放到 P1：要在热键触发那一刻就取标题，而且得做脱敏处理。
- 清洗：非法字符、控制字符、结尾的点和空格、保留名（CON、NUL、COM1…）。截断到 `MaxBaseNameLength`，整条路径控制在 260 以内（虽然 manifest 开了长路径，但外部程序不一定支持）。
- 重名：自动加 `_2`、`_3` 后缀。秒级时间戳下连按很容易重名。
- 设置页提供实时预览。

### 2.4 写入流程和边界情况

| 场景 | 一键保存（Ctrl+S） | 自动保存 |
|---|---|---|
| 目录不存在 | 先 `CreateDirectory` | 同左 |
| 驱动器离线、U 盘拔出、网络盘超时 | 在后台检查，**超时 2 秒**（网络路径可能卡住几十秒）。失败就**退回"另存为"对话框**（起始目录改成"图片"）并弹提示，截图界面不关闭（复用现有 `FailOutput` 语义） | **不阻塞复制和关闭界面**。后台编码写入，失败只弹一条提示（参照 `App.xaml.cs` 里"复制成功但历史失败"的处理） |
| 没有权限（UnauthorizedAccessException） | 同上，退回对话框 | 同上，只提示 |
| **Windows 受控文件夹访问**（Defender 防勒索会拦截向"图片"或"文档"写入） | 在提示文案里专门说明这种情况 | 同左 |
| OneDrive 或云同步文件夹 | 可以写，不用处理 | 可以写 |
| 用户在对话框里取消 | 保持现在的行为（界面不关） | 不涉及 |

- 截图界面是置顶窗口，还调用了 `MarkFullscreen`。设置页的文件夹选择器（WPF `OpenFolderDialog`）不在截图界面里弹，不受影响。一键保存走的是同一套"先取消置顶"的逻辑（`TriggerSaveAs` 附近，`CaptureOverlayWindow.cs:1905/1975`）。
- 三处另存为合并成一个 `Output/SaveDialogFactory.cs`，统一读取导出目录和上次目录。
- 保存成功后弹一个**可点击的提示**："打开文件夹"，用 `SHOpenFolderAndSelectItems` 定位到文件。这需要扩展 `Platform/ToastWindow.cs`，让它支持一个动作按钮。
- 自动保存实现成 `ICaptureSink` 的组合：新增 `Output/AutoSaveCaptureSink.cs` 和 `CompositeCaptureSink.cs`，在 `App.xaml.cs:511` 处接上。来源是历史的贴图（`PinSource.History`）不再重复保存。

### 2.5 和保留期、缩略图、HistorySchema 的关系

- **保留期只管历史库**（`images/` 和 `thumbs/`）。A 和 B 写出的文件都是用户自己的文件，**永远不会被清理**。设置页上要写明这一点。
- **A 和 B 都不改 HistorySchema**。历史仍然照常落一份 PNG，作为图库和贴图的来源。
- **方案 C 的数据库已经准备好了**：库里存的都是相对路径（`images/...`、`thumbs/...thumb.png`），`HistoryRepository(dataRoot, databasePath)` 也接受根目录参数，迁移时**不需要改任何一行数据**。

### 2.6 方案 C（历史库根目录迁移）要点，留到 P2

1. **在下次启动时生效**，不要运行中热切换，这样能避开图库、快览、预览缓存、保留期调度器占着文件的问题。具体做法：设置里只写入"待迁移到 X"，下次启动时在 `InitializeHistory` 之前执行。
2. 迁移步骤：先 `PRAGMA wal_checkpoint(TRUNCATE)`（或用 `SqliteConnection.BackupDatabase`），再复制，然后核对文件数，跑 `PRAGMA integrity_check`，最后切换根目录。旧目录先保留，由用户确认后再删。**校验没通过之前绝不切换**。
3. 目标目录里已经有 `history.db` 时，提供"使用已有的库"，并检查 `IsNewerThanSupported`，版本更新的库不能降级打开。
4. **拒绝 UNC 路径和网络驱动器**（`DriveInfo.DriveType == Network`），因为 WAL 在网络共享上不可靠。路径在 OneDrive 或 Dropbox 下面时给出警告（`-wal`、`-shm` 文件可能被同步弄坏）。
5. 启动时根目录不可用（U 盘没插）：**不要悄悄新建一个空库**，进入现有的降级模式（`App.xaml.cs:538` 的 `LoggingCaptureSink` 分支），设置页显示"库不可用"。
6. 需要一起改的地方：`AppPaths.ImagesDirectory/ThumbsDirectory` 被 `SettingsWindow.cs:840`、`SystemIntegration.cs:239` 直接引用，要改成从当前仓库的根目录读取。
7. 卸载程序 `installer/DeepShot.iss:57` 只删除 `{localappdata}\DeepShot`。自定义位置的库会留下来，卸载提示里要说明，或者读取 settings.json 一并处理。
8. 便携模式（exe 旁边有标记文件就用 `<exe>\Data`）走的是同一套根目录解析，可以一起设计。

---

## 3. 版本打包建议

| 版本 | 主题 | 内容 | 预估 |
|---|---|---|---|
| **0.1.16 / 0.2.0** | 截图和编辑的手感 | A2 右键修复、A3 键盘操作、A4 改已选标注样式和字号/底色、A5 记住工具设置、A7 更新渠道，另外**补上 `DeepShot.Core.Tests` 测试项目** | 全是 S，大约 1 到 1.5 周 |
| **0.3.0** | 保存与存储 | A1 导出目录、一键保存、模板、格式和质量、自动保存、"打开文件夹"提示；B7 截图后动作可配置；A6 保留期可配置和清空；B6 历史右键菜单和复制为文件；B9 托盘增强 | 1 个 M 加若干 S，大约 2 周 |
| **0.4.0** | 截图能力 | B3 全屏和活动窗口一键截图、B4 延时截图、B5 重复区域、B12 PrintScreen、B2 放大镜和取色、B8 贴图恢复 | 大约 2 到 3 周 |
| **0.5.0** | 大件 | B1 OCR（单独验证改 TFM 后的发布和安装流程）、B10 序号工具、B11 荧光笔和填充、方案 C 历史库迁移（可以和便携模式一起）。长截图之后视情况再排 | 大约 3 到 4 周 |

**排序理由**：0.2 全是小改动，而且修的都是现有体验里会让人恼火的地方（右键丢数据、颜色改不了、每次重置），风险最低，又能先把测试项目搭起来。0.3 把你提的"自定义路径"做全，并把和它强相关的保留期、在资源管理器中显示、复制为文件一起打包。会改动 TFM 和数据目录这类需要验证安装流程的改动，集中放在最后。