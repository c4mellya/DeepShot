# Zeep Cut — 当前状态

## 0.1.1 修复（2026-10-19）

- 用户反馈 Alt+A 后 Esc 和右键无法直接退出；新增截图窗口专属 Esc 热键，解决初次唤起未取得焦点时 Esc 无效，并兼容 SystemKey / ImeProcessedKey。
- PreviewMouseRightButtonDown 直接取消整次截图；Esc 同样直接取消，不再优先清除标注选中或文字编辑状态。
- 保存对话框显示时暂时注销截图 Esc，关闭对话框再恢复；截图关闭后注销并移除消息钩子。
- 发布为 0.1.1，安装版与 ZIP 均由 publish.ps1 生成。依用户既有要求不运行测试或桌面验收。


## 最新交付（2026-10-19，本节优先于下方历史记录）

- 首版实现与分发已交付；无需继续派发任务。当前使用用户要求的单 Agent 收尾方式。
- T29–T32：接手全部设置、主题、资源字典与更新代码，修复深色 ComboBox 的完整模板与切换时选项容器配色。既有快捷键/开机启动/图库/工具栏/提示接线保留。
- T33：WelcomeGuide 首次引导，中英文按当前语言显示；介绍快捷键、标注、贴图、托盘与 30 天规则，关闭后写一次性标记。
- T34：Inno Setup 6.7.3 编译当前用户 x64 安装包；包含快捷方式、卸载、默认保留数据与显式清理选项。自包含 ZIP 同时交付。
- T35：README.md / README.en.md / RELEASE_NOTES.md 完成。
- T36：依用户最新指令取消最终测试、桌面验收与安装验证；ACCEPTANCE.md 如实记录历史证据与最终未验证项。未把编译通过当成验收通过。
- 构建：scripts/publish.ps1 → dotnet publish Release / win-x64 / self-contained 成功；Inno Setup 输出 Successful compile。该脚本不调用测试项目、不启动应用。
- 产物：artifacts/release/ZeepCut-0.1.0-Setup-x64.exe（46,264,261 bytes）、ZeepCut-0.1.0-win-x64.zip（61,003,353 bytes），均附 SHA-256 文件。
- 发布目录：artifacts/publish/win-x64，包含 ZeepCut.exe、运行时、原生依赖及 Resources/Lang。
- 本轮没有测试结果。此前最新完整测试为 1124 项通过，仅属历史记录，不能代表最终修改已验证。
- 仍需发布者信息：真实更新渠道与签名证书。未配置渠道时据实提示；当前安装包未签名。

---

以下内容为早期轮次记录，不代表当前待办：

> 每轮工作结束时更新本文件。下一位接手者先读这里。


## 本轮快照

- 日期：2026-10-18
- 当前阶段：阶段三（历史与设置）→ **T27 30 天清理 + T28 追加标注覆盖已完成**
- 本轮任务：**T27 30 天清理 + T28 追加标注覆盖**
- 下一步任务：**T29 设置窗口**（快捷键修改与冲突提示、语言、主题、开机启动、数据目录、
  30 天清理规则说明）。T29 现在必须知道的底座：
  - 清理规则文案已放在 Core：`HistoryRetentionPolicy.DescribeRule()`（"普通记录保留最近 30 天
    （按创建时间计算，编辑与重命名不延长期限）；收藏的记录不会被自动清理。"），设置窗口
    直接调用它，不要另写一份；保留天数常量 `HistoryRetentionPolicy.DefaultRetentionDays = 30`。
  - 数据目录就是 `AppPaths.DataRoot`（`%LOCALAPPDATA%\ZeepCut`），
    `AppPaths.EnsureCreated()` 已在启动时建好 `logs/ images/ thumbs/`。
  - 应用层已经有一个"一次性提示/偏好"的落盘先例：`src/ZeepCut/History/HistoryUiState.cs`
    （`history-ui.json`，JSON + 失败不抛）。T29 的设置文件（T29 规划的 `AppSettings` JSON）
    可以照这个写法，但**不要把设置塞进 history-ui.json**，那是历史模块的提示状态。
  - 快捷键目前是 `App.xaml.cs` 里的常量 `ScreenshotShortcutText = "Alt+A"` /
    `PinShortcutText = "Alt+S"`，注释里已写明"T29 设置窗口接入后改为从设置读取"。
- 构建状态：✅ 编译通过，**0 warning 0 error**
- 测试状态：✅ **1014 个测试通过**（上轮 958；本轮 +56：
  `HistoryRetentionPolicyTests` 26、`HistoryRetentionRepositoryTests` 16、`HistoryOverwritePlanTests` 14）
- 桌面验证：✅ 主屏 125%（2560×1440 @ (0,0)）真实桌面实测。证据 `artifacts/t27t28/`，
  驱动脚本 `scripts/verify-t27t28.ps1`（新）+ `scripts/tray-menu-direct.ps1`（新，托盘菜单回退）。
  真实观察到的关键数字（细节见下面“验证证据 / T27/T28 桌面实测”）：
  - **T27 启动清理**：启动前 6 行 / `images` 6 / `thumbs` 6（含 31 天前 1 条、29 天前 1 条、
    40 天前收藏 2 条）→ 启动后 **5 行 / 5 / 5**；日志
    `30 天清理（启动）：… 清理前=6 过期=1 已删除=1 失败=0 清理后=5`。
    被删的正是 `T27_31天前_应清理`（图片、缩略图文件同时消失），29 天与两条 40 天收藏都还在。
  - **T27 取消过期收藏**：40 天前收藏点星标 → 真实 Win32 对话框（标题“取消收藏”）出现，
    正文含“创建于 40 天前，已经超过 30 天保留期…取消收藏后…会在下次清理（启动或跨日）时被删除”；
    点“取消”→ 状态栏“已保持收藏…（收藏不会被自动清理）”，星标仍是 ★（未误删）。
  - **T28 覆盖编辑**：图库卡片“标注”→ 编辑窗口（真实 WPF 窗口，旧标注已在背景像素里）→
    选“矩形”拖一笔 → “保存并覆盖” → **首次说明对话框**（“覆盖编辑说明…旧版本图片与旧标注
    「不会被保留」…”）→ 确定。结果：**记录数 5→5（不新增）**，同一条记录
    `Id=704d94d9…`、名称 `截图_昨天验证`、`CreatedAtUtc=2026-10-17T16:27:45.000Z`、
    `IsFavorite=0` 全部不变，**`UpdatedAtUtc` 16:39:12Z → 17:31:09Z 变了**；
    图片仍 760×480、**diff 包围盒 (60,89)-(297,233) 共 2965 像素变化**（新矩形，见
    `artifacts/t27t28/t28-image-after.png`），缩略图 sha256 也变了；
    `images/` 与 `thumbs/` 文件数 **5→5 不变**（原地覆盖，无 `.tmp` 残留）。
  - **T28 取消编辑**：编辑窗口里画一个椭圆 → “取消” → 确认对话框 → 图片与缩略图
    sha256 与编辑前**完全一致**，文件数不变。
  - **T28 编辑期间记录被删**：编辑窗口开着时在图库删掉这条记录，再点“保存并覆盖” →
    弹出“已被删除，本次修改没有保存（原记录不存在，也不会新建一条）”提示，
    **进程不崩**、不写文件、不新建记录。

### 本轮决策（值得留给下一位）

- **T27 规则放 Core**：新增 `src/ZeepCut.Core/History/HistoryRetentionPolicy.cs`（纯逻辑）：
  `IsExpired` / `ShouldPurge`（= 过期 且 非收藏）/ `SelectExpired` / `GetExpiryUtc` /
  `GetRemainingDays` / `DescribeRule` / `DescribeFavoriteRemovalWarning`。
  **ROADMAP/ARCHITECTURE 里写的类名是 `HistoryRules`，实际落地叫 `HistoryRetentionPolicy`**
  （与"保留策略"语义一致，且不影响任何已通过类型）；下一位若找 `HistoryRules` 请按这个名字找。
- **边界**：`now >= CreatedAtUtc + 30 天` 即过期（正好 30 天算过期；29 天 23 小时仍保留；
  30 天 1 秒清理）。**只看 `CreatedAtUtc`**，改名/收藏/覆盖编辑都只动 `UpdatedAtUtc`，不延期 ——
  这条写成测试锁住（`WithImage_DoesNotExtendRetention`、`ShouldPurge_RecentlyUpdatedOldRecord_StillExpires`）。
- **清理复用同一条删除路径**：`HistoryRepository.PurgeExpired(nowUtc, retentionDays)` 先
  `GetAll()` → `SelectExpired` → 逐条 `Delete(id)`（删行 + `images/` + `thumbs/` 内部文件）。
  文件已缺失时 `DeleteQuietly` 视为成功，只删行不报错（`PurgeExpired_MissingFiles_DeletesRowOnly`）。
  单条失败只进 `HistoryRetentionResult.Failures`，不打断其余记录。
- **启动清一次 + 跨日再清**：`src/ZeepCut/History/HistoryRetentionScheduler.cs`，
  `DispatcherTimer` 每 1 分钟比较**本地日期**是否变化（不是"每 24 小时"：跨日才是用户能理解的定义，
  也不受休眠影响），两次清理之间至少隔 5 分钟（防手动改系统时间抖动）。
  清理**只写日志**，不弹窗（启动时清理是静默家务活）；只有图库开着时才刷新它并写一行状态栏说明。
- **自动清理不碰用户文件**：删除路径只由记录里的相对路径决定，只可能落在
  `<数据根>/images`、`thumbs`；用户"另存为"到别处的文件不在库里
  （`PurgeExpired_DoesNotTouchFilesOutsideDataRoot` 用"另一个目录里的同名文件"验证）。
- **过期收藏取消收藏要二次确认**：文案在 Core `DescribeFavoriteRemovalWarning`，
  只有**已过期**（`IsExpired`，与收藏无关）的收藏才弹；点"取消"= 保持收藏、什么都不改
  （`MessageBoxResult.Cancel` 是默认按钮）。未过期的收藏取消收藏**不弹**（不打扰）。
- **T28 复用标注组件**：编辑窗口 `src/ZeepCut/History/HistoryEditWindow.cs` 用同一个
  `AnnotationSession` / `AnnotationLayer` / `AnnotationRenderer` / `AnnotationExportRenderer.Composite`，
  背景 = 记录当前图片（所以旧标注已在像素里，命中测试碰不到 → "不可拆分修改"就是覆盖语义）；
  会话文档从空开始，**本次只追加**。没有引入任何新的标注模型。
- **覆盖写回同一条记录**：走既有的 `HistoryRepository.UpdateImage(id, bytes, w, h, thumb)`：
  `.tmp` → 校验长度 → `File.Move(overwrite)` → 写缩略图 → `Update` 行（`HistoryRecord.WithImage`
  保留 Id/名称/创建时间/收藏/来源）。**不生成新记录**，因此图库条数不变、30 天期限也不延长。
  写 tmp 失败时旧图不动（`UpdateImage_WhenTemporaryWriteFails_OriginalStaysIntact`）。
- **编辑期间被改名/收藏**：保存前重新 `GetById`，用**库里最新那一版**作基底
  （`HistoryOverwritePlan.MergeLatest`），避免把编辑器打开时的旧名称/旧星标写回去。
  记录已被删则返回提示、不写文件、不新建（`DescribeDeleted`）。
- **"保存并覆盖"按钮文案**（PLAN 硬性要求）+ **首次解释**：`HistoryOverwritePlan.DescribeOverwriteExplanation`
  在 Core 里（可单测"说明里必须提到不保留旧版本/不新增记录/保留字段"）。说明**跨启动只出现一次**：
  显示过之后写 `%LOCALAPPDATA%\ZeepCut\history-ui.json`（`HistoryUiState`，JSON 坏了就当没解释过，
  从不抛异常）。编辑窗口里点"取消"对话框则不保存、留在编辑状态。
- **双击/多条编辑**：每条记录同时只开一个编辑窗口（再点"标注"= 激活已有窗口）；
  图库关闭时一并关掉编辑窗口（等价取消，原图不动）。
- **贴图不受覆盖影响**（PLAN）：贴图窗口持有创建时解码的冻结位图
  （`PinWindow._image`，`CreateFromHistory` 只在创建时 `TryLoad` 一次），与磁盘文件无共享引用；
  覆盖只替换 `images/` 下那一个文件，所以已打开的贴图保持原样。**代码层面确认，未做"贴图开着时
  覆盖"的桌面实测**（见遗留问题）。

### 本轮修掉的真实 bug（都是桌面实测撞出来的）

1. **`HistoryRepository.Delete` 先查行再开事务 → 可能"行删了、文件没删"**。原来是
   `GetById`（另一条连接）→ 开事务删行；在 WAL 下多一条连接会改变快照，实测出现过
   `行已删除=False` 但行其实已经删掉、图片/缩略图变成孤儿（图库删除记录时观察到）。
   已改成**同一事务里 SELECT + DELETE，提交后再删文件**，返回值与事实一致。
   （T27 清理与 T25 删除都走它，所以这个修复同时覆盖两条路径。）
2. **历史卡片小按钮把中文画成一条**：`HistoryUi.Text` 默认带 `TextTrimming.CharacterEllipsis`，
   放进 Grid 自动列/按钮内容里会被量成很窄（实测“标注”按钮 49px，字被裁掉），
   一度看起来像"按钮不见了/点了没反应"。已给 `HistoryUi.Text` 加
   `measureNaturalWidth` 参数（去掉省略号）+ `Small()` 里 `Measure` 后设 `MinWidth`。
3. **`HistoryUi.Button` 的裸 `Content=string`** 在按钮模板里会走默认字体（缺中文字形时画方块），
   已统一改成 `TextBlock`（带本类字体族）。

### 本轮：残留代码如何接手（重要）

上一轮 agent 在写 `HistoryGalleryWindow` 时崩溃，留下 10 个未提交文件、约 1940 行，
`dotnet build` 只有 3 个错误（`HistoryGallery.cs` 引用不存在的 `HistoryGalleryWindow`）。
**处理方式是"补齐 + 小改"，没有推倒重来**：

| 残留文件 | 处理 | 说明 |
| --- | --- | --- |
| `src/ZeepCut.Core/History/HistoryGrouping.cs` | **原样保留** | 日期分组（今天/昨天/`yyyy-MM-dd 星期X`）+ 组内倒序，直接接入图库，未改一行 |
| `src/ZeepCut.Core/History/HistoryRename.cs` | **原样保留** | 重命名校验（空名/非法字符/collapse 后为空都拒绝），被 `HistoryRenameDialog` 调用 |
| `src/ZeepCut.Core/History/HistoryView.cs` | **原样保留** | 视图（搜索 + 只看收藏 + 排序 + 页大小）→ `HistoryQuery`；空状态文案 `DescribeEmptyState` 直接给图库用 |
| `src/ZeepCut/History/HistoryActions.cs` | **保留 + 1 行 using** | 复制/另存为/贴图/删除/收藏/重命名动作；只补了 `using ZeepCut.Capture;`（`MonitorLayout` 在 `Capture` 命名空间，残留代码漏了这个 using） |
| `src/ZeepCut/History/HistoryCard.cs` | **保留 + 1 个新方法** | 缩略图卡片；新增 `SetDragScroll(ScrollViewer)`（在卡片空白处按下拖动 = 滚动图库），其余未动 |
| `src/ZeepCut/History/HistoryGallery.cs` | **保留 + 3 处可见性** | 单实例宿主已写好；把 `Current`/`Show` 由 `public` 改 `internal`（`HistoryGalleryWindow` 的构造函数是 internal，public 方法无法返回它；`CloseIfOpen` 仍 public） |
| `src/ZeepCut/History/HistoryUi.cs` | **原样保留** | 图库配色/文字/按钮工厂 |
| `tests/ZeepCut.Tests/History/{HistoryGrouping,HistoryRename,HistoryView}Tests.cs` | **原样保留** | 上一轮已写好且全绿（254 个 History 测试中包含它们） |

**新写的文件**（缺口就在这里）：
- `src/ZeepCut/History/HistoryGalleryWindow.xaml.cs`（约 700 行，**纯代码构建 UI，没有 XAML 文件**：
  与仓库里遮罩/贴图/Toast 的写法一致，避免再引入一套 XAML 命名空间与 partial 类；
  文件后缀沿用宿主 `HistoryGallery.cs` 指名的类型名 `HistoryGalleryWindow.xaml.cs`）。
- `src/ZeepCut/History/HistoryTabStrip.cs`：顶部“全部 / 只看收藏”两段式开关（RadioButton + 自绘模板）。
- `src/ZeepCut/History/HistoryRenameDialog.cs`：重命名对话框（Enter 确定、Esc 取消、非法输入就地报错不关闭）。
- `src/ZeepCut/History/HistoryPreviewWindow.cs`：双击预览大图（1:1 + 滚轮缩放 + 适应窗口）。
- `src/ZeepCut/Pins/PinWindows.cs`：`CreateFromHistory` 去掉“T25 尚未实现”的日志文案（行为未变，
  仍固定 `PinSource.History` → 不新增历史）。
- `src/ZeepCut/App.xaml.cs`：托盘“历史”从 `ShowNotImplemented` 换成 `ShowHistoryGallery`
  （历史库启动失败时说明原因而不是弹“尚未实现”）；`ExitFromTray`/`OnExit` 增加 `DisposeHistoryGallery()`。
- `scripts/verify-t25t26.ps1`：真实桌面验证驱动（11 个场景，见下）。

**接手时发现的残留代码 bug（都修了）**：
1. 分组标题用 `Width = double.PositiveInfinity` 想做整行 → WPF 直接抛
   `ArgumentException: '∞' is not a valid value for property 'Width'`，图库一打开就显示“无法读取历史记录”。
   改成：标题前插一个宽到必然换行的 1px 空占位。
2. `HistoryCard` 的“重命名”按钮显示文字是 `改名`，但残留的动作条没有 AutomationId 前缀统一
   （`history-action-改名-N`），验证脚本一开始按“重命名”找不到 —— 已按实际文字修正脚本。
3. 预览窗口用 `SendKeys '{ESC}'` 关不掉（本机已知坑，T23 记录过）：改为 `PostMessage(WM_KEYDOWN/UP)`
   到预览窗口 hwnd，窗口自己的 `PreviewKeyDown` 才走得到。

### 本轮决策（值得留给下一位）

- **图库是普通窗口**（有边框、进任务栏），关闭只是关窗口，进程与托盘继续运行（PLAN：关闭普通窗口留在托盘）。
- **收藏切换后重查库**（`Reload`）而不是只改卡片颜色：这样“只看收藏”开着时取消收藏，
  记录会立刻从列表消失——这是过滤语义的正确表现，而不是 bug。
- **预览是 1:1 物理像素**（`Stretch.None` + ScrollViewer），不是“缩放到窗口”：本机主屏 2560×1440，
  图库里基本都是整屏截图，缩放到窗口会糊；滚轮 / ±/ 适应窗口 / 实际大小都给了。
- **贴图摆放在光标附近**（复用 `PinPlacement.NearCursor` + 光标所在屏幕），与 Alt+S 剪贴板贴图一致。
- **删除一律带确认**（`MessageBox` OKCancel，默认按钮是取消），收藏记录文案额外提示“这是一条收藏记录”。
- **重名允许**（PLAN 只要求按名称搜索，Id 才是主键）；新建记录才走 `CreateUniqueName` 的 ` (2)` 规则。
- **搜索用 Core 的 `HistoryView` 翻译成 SQL**（`LIKE + COLLATE NOCASE`），不在 UI 里做内存过滤：
  分页“加载更多”才不会漏掉后面的匹配项。
- **所有会改列表的操作最后都走 `Reload()`**（重新查库 + 重建卡片）。图库量级很小，重建最不容易出错；
  代价是滚动位置会回到顶部（见“未验证/遗留”）。

## 已完成

- 2026-10-18 **T27 30 天清理 + T28 追加标注覆盖**（不重写任何已通过的 Core 类型；只新增类型 +
  既有类型上加成员）：

  **Core `src/ZeepCut.Core/History/`**
  - `HistoryRetentionPolicy.cs`（新，纯逻辑）：`DefaultRetentionDays=30`、`RetentionPeriod`、
    `IsExpired` / `ShouldPurge` / `IsCreatedAtExpired` / `SelectExpired` / `GetExpiryUtc` /
    `GetRemainingDays` / `DescribeRule` / `DescribeFavoriteRemovalWarning`。
    只按 `CreatedAtUtc` 判定；收藏豁免；边界含"正好 30 天"；非法参数（负天数/null 记录）抛明确异常。
  - `HistoryOverwritePlan.cs`（新，纯逻辑）：`EditState{Available,Deleted}` + `ResolveState`、
    `MergeLatest`（用库里最新一版作覆盖基底，Id 不一致直接抛）、`WasModifiedElsewhere`、
    `DescribeSaved` / `DescribeDeleted` / `DescribeOverwriteExplanation`（三个面向用户的文案）。
  - `HistoryRepository.cs`（只加成员 + 修 1 个 bug，未改既有签名语义）：
    - 新 `GetAll()`（按创建时间倒序全量读）。
    - 新 `PurgeExpired(nowUtc, retentionDays)` → `HistoryRetentionResult`（清理前/过期数/已删 Id/
      失败列表/现在/天数 + `TotalAfter`、`IsEmpty`、`ToString`），内部**逐条走 `Delete(id)`**。
    - `UpdateImage` 增加可选 `updatedAtUtc`（测试可注入）与空字节拒绝，文档写清覆盖语义
      （保留 Id/名称/创建时间/收藏/来源/相对路径，只换图片/缩略图/宽高/修改时间）。
    - **修复 `Delete`**：同一事务内 SELECT + DELETE，提交后再删文件（见"本轮修掉的真实 bug"#1）。

  **App `src/ZeepCut/History/`**
  - `HistoryRetentionScheduler.cs`（新）：`Start()` = 立刻清一次 + 起 `DispatcherTimer`；
    每 `CheckInterval`(1 分钟) 比较本地日期，跨日且距上次清理 ≥ `MinimumInterval`(5 分钟) 再清；
    `Purge(trigger)` 吞掉所有异常只写日志；`LastResult` / `IsRunning` / `LastPurgeUtc` 供诊断。
  - `HistoryEditWindow.cs`（新，约 900 行，纯代码构建 UI）：标题“Zeep Cut 编辑历史 · 名称”，
    顶部记录信息 + “旧标注已合并进图片像素，只能继续追加（不可拆分修改）”，中间
    `ScrollViewer`+`Image(Stretch=None)` 1:1 显示原图，右侧工具栏（选择/矩形/椭圆/箭头/直线/
    画笔/文字/马赛克、颜色、粗细、撤销/重做/删除选中、缩放 −/＋/1:1/适应窗口、
    **保存并覆盖**、取消），底部状态行。
    - 鼠标：绘制工具 = 拖拽草稿（`AnnotationSession.BeginDraft/UpdateDraft/CommitDraft`）；
      文字工具 = 窗口内输入框（Enter 提交 / Esc 取消）；选择模式 = 手柄缩放 / 整体平移 /
      双击文字重新编辑（全部走 `AnnotationEditController`）。
    - 键盘：Ctrl+Z / Ctrl+Y、Delete / Back、Esc（草稿 → 拖拽 → 选中 → 取消窗口）、Ctrl+S 保存。
    - 保存：`GetById` 确认记录还在（不在 → 提示 + 不写文件）→ 首次弹说明 → `Compose()`
      （`AnnotationExportRenderer.Composite`，尺寸必须等于原图）→ `EncodePng` + 缩略图 →
      `UpdateImage` → 日志（新尺寸/字节数/追加标注数/保留的创建时间/收藏/历史条数前后）。
    - 取消：画过东西才确认（`OKCancel` 默认取消），从不写文件。
  - `HistoryUiState.cs`（新）：`history-ui.json` 里记 `OverwriteExplained`，读失败按"没解释过"，
    写失败只 WARN；`Load()` / `MarkOverwriteExplained()`。
  - `HistoryGalleryWindow.xaml.cs`：新增 `EditRecord`（打开/激活编辑窗口、关闭上一个）、
    `OnEditWindowClosed`（保存过才 `Reload()` 并写状态栏）、`Edit` 属性、图库关闭时关编辑窗口、
    `ShowRetentionNotice` / `RetentionNotice`（自动清理后的一行说明）、
    `ToggleFavorite` 加**过期收藏取消收藏的二次确认**（文案来自 Core）、底部提示补一句"标注"。
  - `HistoryGallery.cs`：新增 `ReloadIfOpen(reason)`（App 清理后调用）。
  - `HistoryCard.cs` / `HistoryUi.cs`：卡片操作条新增“标注”按钮（`history-action-标注-N`）；
    `HistoryUi.Text` 加 `measureNaturalWidth`、`Button` 内容改 TextBlock（修 bug #2/#3）。
  - `Editor/AnnotationLayer.cs`：新增 `DpiOverride`（窗口 `Loaded` 后拿到真实 DPI 再交给标注层，
    125% 屏上文字/线宽不再偏差），`_dipPerPixel` 由 readonly 改为可写。
  - `App.xaml.cs`：`StartHistoryRetention()`（历史不可用就跳过）、`OnHistoryPurged`（图库开着才刷新）、
    `DisposeHistoryRetention()`（托盘退出与 `OnExit` 都调）、启动时 `HistoryUiState.Load()` +
    `HistoryEditWindow.ApplyPersistedExplanation(...)` + `OnExplanationShown = MarkOverwriteExplained`。

  **测试（+56）**：`tests/ZeepCut.Tests/History/HistoryRetentionPolicyTests.cs`（26）、
  `HistoryRetentionRepositoryTests.cs`（16，含"文件缺失只删行""不碰数据目录外文件""写 tmp 失败原图不动"、
  覆盖后字段/文件数/删 .tmp 等）、`HistoryOverwritePlanTests.cs`（14）。

  **脚本**：`scripts/verify-t27t28.ps1`（新，场景 `diagnose/gallery/unfav/editor/overwrite/cancel/all`，
  UIA 按 AutomationId 点到每个按钮、读 Win32 MessageBox 正文、点标准按钮）、
  `scripts/tray-menu-direct.ps1`（新，托盘菜单回退：`PostMessage(WM_TRAYICON)` 打开真实菜单再点第 N 行；
  原因见"遗留问题"）。

- 2026-10-18 **T25 历史图库 + T26 历史管理**（接手上一轮崩溃留下的残留代码，补齐缺口而不是重写；
  详细“怎么接手”见上面的本轮快照表格）：

  **图库窗口 `src/ZeepCut/History/HistoryGalleryWindow.xaml.cs`（新写，纯代码构建 UI）**
  - 顶部：`历史记录` 标题 + 搜索框（`history-search`，250ms 防抖，Enter 立即搜，Esc 清空）+ 
    `全部 / 只看收藏` 两段开关（`HistoryTabStrip`）+ `刷新`；下面一行状态栏
    （`共 N 条历史记录` / `共 N 条匹配（库内总计 M 条）` / 上次操作结果）。
  - 主体：`ScrollViewer` + `WrapPanel`，按 `HistoryGrouping.GroupByDay` 分组——
    每组先一行分组标题（`今天` / `昨天` / `2026-10-16 星期五`，带条数），标题前插一个宽度等于可见区的
    空占位强制换行（`WrapPanel` 没有"整行"概念）；卡片用残留的 `HistoryCard`（缩略图 + 名称 + 时间/尺寸/来源 +
    星标 + 选中后展开 复制/贴图/改名/删除/另存为/预览 操作条）。
  - 交互：单击卡片选中（操作条展开）、双击打开预览、卡片空白处拖动 = 滚动列表、滚到底自动加载下一页
    （每页 60，`PageSize`）、F5 刷新。所有会改动列表的操作最后都走 `Reload()`（重新查库 + 重建卡片）。
  - 空状态：`totalCount<=0` → `还没有历史记录。按 Alt+A 截个图…`；有筛选没命中 → `没有匹配的记录` +
    Core 给出的具体说明 + `清除筛选` 按钮。文案全部来自 `HistoryView.DescribeEmptyState`。
  - 历史库初始化失败时托盘“历史”不再弹“尚未实现”，而是说明本次启动历史不可用（截图/贴图不受影响）。
  - 退出时（托盘退出 / `OnExit`）关闭图库窗口，不动数据库。

  **重命名 `HistoryRenameDialog`**：单行输入框 + 提示 + 错误行 + 确定/取消；Enter 提交、Esc 取消；
  校验用 Core `HistoryRename.TryNormalize`（空名/全非法字符直接拒绝并就地显示原因，不关对话框）；
  `HistoryActions.Rename` 只改 `Name` + `UpdatedAtUtc`（`CreatedAtUtc` 不变 → 不延长 30 天）。

  **预览 `HistoryPreviewWindow`**：非模态（可同时操作图库），1:1 显示原图 + 滚动条，
  滚轮缩放（0.1×–4×）、`实际大小`/`适应窗口` 按钮、`复制`/`贴图`（复用 `HistoryActions`），Esc 关闭；
  元数据栏显示名称/像素尺寸/本地时间/来源/当前缩放。图片走 `HistoryStore.TryLoad`（读完即释放句柄），
  因此预览打开时仍可删除该记录（`DeleteRecord` 会先关掉对应预览）。

  **T26 收藏**：星标点击 → `HistoryRepository.SetFavorite` → 就地更新星标 + `Reload()`；
  “只看收藏”开着时取消收藏会让记录立刻从列表消失（过滤语义，不是 bug）。

  **T26 搜索**：`HistoryView` → `HistoryQuery`（`LIKE + COLLATE NOCASE`，`BuildLikePattern` 转义 `%`/`_`），
  大小写不敏感、子串匹配，与“只看收藏”是**与**关系；分页也带着同一份条件。

  **T25 动作**：复制（`Clipboard.SetImage`，`COMException` 单独提示）/ 另存为（真实 Win32 对话框，
  显示期间临时取消 Topmost —— T11 的老坑）/ 贴图（`PinWindows.CreateFromHistory`，固定 `PinSource.History`，
  **不新增历史**；摆放用 `PinPlacement.NearCursor` + 光标所在屏幕）/ 删除（`MessageBox` 确认，默认按钮取消；
  收藏记录额外提示）→ `Repository.Delete` 删行 + 图片 + 缩略图文件。

  **接线**：`App.xaml.cs` 的 `ShowHistoryGallery(trigger)`（取代 `ShowNotImplemented("历史")`）、
  `DisposeHistoryGallery()`（`ExitFromTray` 与 `OnExit` 都调）；`PinWindows.CreateFromHistory` 的日志
  去掉"T25 尚未实现"的占位文案。

- 2026-10-18 **T23 历史存储 + T24 历史写入**（全新 Core 存储层 + 替换 T11/T22 的两个日志占位实现；
  未改动任何已通过的 Core 类型，也没有动 PLAN 范围之外的东西）：

  **Core `src/ZeepCut.Core/History/`（纯逻辑 + SQLite，可无桌面单测）**
  - `HistoryRecord.cs`：不可变 `record`（`Id`/`Name`/`CreatedAtUtc`/`UpdatedAtUtc`/`IsFavorite`/
    `RelativeImagePath`/`RelativeThumbnailPath`/`Width`/`Height`/`Source`）。
    - 构造即校验：空 Guid / 空图片路径抛 `ArgumentException`；名称为空回退 `截图`；
      空缩略图路径归一为 `null`；负宽高夹到 0；本地时间自动转 UTC。
    - **时间精度统一到毫秒**：`NormalizeUtc` 截断到 ms，因为 SQLite 里只存 3 位小数 ——
      不截断会出现“写进去的对象与读回来的对象不相等”，所有记录比较都会假失败。
    - 修改都返回新实例：`WithName` / `WithFavorite` / `WithImage`（T28 覆盖编辑用，保留 Id/名称/创建时间/收藏）。
    - `CreateDefaultName(本地时间)` = `截图_yyyy-MM-dd_HHmmss`（复用 T11 的 `CaptureFileName`，去掉扩展名）。
    - `FormatUtc`/`TryParseUtc`：库里的 ISO-8601（`2026-10-18T12:30:15.456Z`）。
  - `HistoryName.cs`：默认名称（`截图_` / 剪贴板用 `贴图_`）、名称清洗（Windows 非法字符→`_`、
    去掉结尾点/空格、超长截断到 80、**顺手去掉用户输入的 `.png` 等已知图片扩展名**）、
    `CreateUniqueName`（同名时追加 ` (2)`/` (3)`，大小写不敏感如 Windows）、`MatchesSearch`
    （大小写不敏感子串；空关键字命中全部）。
  - `HistoryPaths.cs`：布局常量（`images/`、`thumbs/`）+ 文件命名
    `{yyyyMMdd-HHmmss-fff}-{guid8}.png`、缩略图 `xxx.thumb.png`、`ToAbsolute(数据根, 相对路径)`、
    `Normalize`（统一 `/`）、`IsSafeImageRelativePath`（拒绝绝对路径/盘符/`..` 逃逸 ——
    库被手工改坏时图库不会去读 `C:\Windows\…`）、`EnsureUniqueRelativePath`（文件名占用时 `-2`/`-3`）。
  - `HistorySource.cs`：`HistorySource{Screenshot, Clipboard, History}` + 中文描述 + 存储值
    （枚举名，稳定可读）。**与贴图的 `PinSource` 是两套枚举，不要合并**：前者说“这条历史怎么来的”，
    后者说“这次贴图要不要新增历史”。
  - `HistorySchema.cs`：**版本机制**——版本号存 `PRAGMA user_version`（本版 `CurrentVersion = 1`）；
    新库直接建表 + 建索引 + 写版本；老库版本低时按 `Migrations` 顺序补齐；版本**高于**程序支持时
    只 WARN、不降级、不改数据（回退装旧版的场景）。建表/建索引/迁移在**同一个事务**里。
    索引 4 条：`CreatedAtUtc DESC`、`IsFavorite`、`Name`、`(IsFavorite, CreatedAtUtc DESC)`。
    连接字符串集中在 `HistoryRepositoryConnection`：`ReadWriteCreate` + `Private` 缓存 +
    `DefaultTimeout=10`，打开后 `PRAGMA journal_mode=WAL` + `busy_timeout=10000`。
    **加列方式**：`CurrentVersion` +1、往 `Migrations` 里补一条 SQL，**不要改 v1 的建表语句**
    （否则老库与新库结构会分叉）。
  - `HistoryRepository.cs`：存储层本体（**只管字节与行，不碰 WPF 图像**）。
    - `Initialize()`：建 `images/`、`thumbs/`、`<root>/history.db`，返回
      `HistorySchemaResult{VersionBefore, VersionAfter, Created, MigratedSteps}` 供启动日志用；幂等。
    - `Add(record, imageBytes, thumbnailBytes?)`：先写图片（可选缩略图），再插元数据；
      路径被占用时自动顺延文件名；**入库失败会删掉刚写的文件**（不留孤儿）；
      缩略图字节为 null 时 `RelativeThumbnailPath` 置 null（图库要能容忍）。
    - 查询：`GetList(HistoryQuery)`（名称 Like 搜索 + 收藏过滤 + 排序 + `LIMIT/OFFSET`，
      返回 `HistoryPage{Items, TotalCount, HasMore, NextPage()}`）、`GetById`、`GetAllNames`、`Count`。
    - 修改：`Update`（整行元数据）、`SetFavorite`、`UpdateImage`（**先写 `.tmp` → 校验长度 →
      `File.Move(overwrite)` → 再改元数据**，失败时旧图还在，T28 直接复用）。
    - 删除：`Delete(id)` 先删行再删两个文件（文件已不在也算成功）。
    - **句柄**：写用 `FileShare.None` 的短命 `FileStream`；读一次性读成 `byte[]` 立刻关闭
      （`ReadImageBytes`/`ReadThumbnailBytes` 都不返回流）。**不要**在这里返回 `BitmapImage`
      或长期持有的流 —— 那会锁住文件，覆盖/清理会失败（已用真实桌面改名/删除验证）。
    - 所有失败都包成 `HistoryStoreException`，调用方只记日志。
  - `HistoryQuery.cs`：`HistoryQuery`（`SearchText`/`FavoriteFilter`/`Sort`/`Offset`/`Limit`，
    默认第 1 页 100 条、创建时间倒序）、`HistorySort`、`HistoryFavoriteFilter`、
    `BuildLikePattern`（转义 `%`/`_`/`\`，否则搜“100%”会匹配全库）、`HistoryPage`。

  **App `src/ZeepCut/History/`（WPF 图像编解码，不参与 Core 单测）**
  - `HistoryThumbnail.cs`：`MaxEdge = 320`；`EncodePng`；`Scale`（`TransformedBitmap` 等比缩放，
    小图不放大）；`CreatePngBytes`。返回 `byte[]`，不留流。
  - `HistoryStore.cs`：`Add(BitmapSource, name, createdAtLocal, source, createThumbnail)` =
    编码 PNG + 缩略图 + 仓储写入 + 一条明细日志；**缩略图生成失败只 WARN 并置空，不让整条历史失败**。
    `TryLoad(相对路径)` 用 `BitmapCacheOption.OnLoad` + `IgnoreImageCache` 读成冻结位图
    （读后即可删文件）；`TryLoadPreview(record)` 优先缩略图、回退原图；`GetPage`/`Count` 给 T25 用。

  **T24 把两个日志占位实现换成真实写入**
  - `src/ZeepCut/Output/HistoryCaptureSink.cs`（`ICaptureSink` 实现）：
    1) 仍写 T11 那条 `输出完成：动作=…` INFO（挂掉历史也有成功记录）；
    2) 再写一条历史，名称 `截图_…`、`Source=Screenshot`、同名自动 ` (2)`；
    3) **吞掉所有异常**，写 ERROR + `LastFailure`，绝不向遮罩抛（否则用户会看到“复制失败”，
       而剪贴板里其实已经有图了）。
  - `src/ZeepCut/Pins/HistoryPinRecorder.cs`（`IPinHistoryRecorder` 实现）：只有
    `PinSource.Clipboard` 才写（规则仍由 Core `PinSourceText.ShouldRecordHistory` 判定），
    名称 `贴图_…`、`Source=Clipboard`；内部截图与历史贴图只写一条“不记录”的日志（与 T22 一致）。
  - `src/ZeepCut/App.xaml.cs`：新增 `InitializeHistory()`（启动时建库 + 接两个 sink；
    失败则整条历史功能降级为 `LoggingCaptureSink` + `LoggingPinHistoryRecorder` 并弹一次提示）；
    `NotifyHistoryFailureIfAny(action)` 在遮罩关闭后检查 `LastFailure`，弹 Toast
    “X 已成功，但历史保存失败（详见日志）”。`_captureSink` 从 `readonly` 改为可替换字段。
  - `LoggingCaptureSink` / `LoggingPinHistoryRecorder` **保留**：前者是历史不可用时的降级实现，
    其 `Describe` 仍被遮罩与历史 sink 复用；后者是降级与调试旁路。

  **测试**（`tests/ZeepCut.Tests/History/`，全部用独立临时目录 + 独立 history.db，测完连 WAL/SHM 一起删）：
  - `HistoryRecordTests`（44）：字段/校验/名称清洗/时间归一与 UTC 往返/不可变修改方法/
    `WithImage` 保留身份/相等性/来源解析（含 `"99"` 越界数字）/`RecordFactory`。
  - `HistoryNameTests`（38）：默认名称、前缀、非法字符、扩展名剥离、超长截断、
    同名顺延（大小写不敏感）、搜索匹配、路径生成与回环、`IsSafeImageRelativePath` 拒绝逃逸。
  - `HistoryRepositoryTests`（43）：建库（含 images/thumbs）、**重复初始化幂等且不丢数据**、
    未来版本不回退、写图片可读回、缩略图有/无两路、全字段往返、空字节被拒、
    路径撞名顺延、时间倒序/正序/名称排序、搜索（含 `100%` 转义）、收藏过滤、
    搜索+收藏组合、分页（offset/limit/NextPage）、limit 与 offset 钳制、空库空页、
    改名/收藏/覆盖图片、删除（行+两个文件）、文件已丢时的删除、
    **`FilesAreNotLockedAfterAddAndRead`（写完读完能立即改名/复制/删除）**、跨连接重开、参数校验。
  - `HistoryQueryTests`（31）：默认值、空白/负数/上限钳制、`BuildLikePattern` 转义、
    `HistoryPage` 的 HasMore/NextPage/Empty、`ToString`。

## 已完成

- 2026-10-18 **T20 贴图全局操作 + T21 剪贴板贴图 + T22 历史联动接缝**（在 T18/T19 的
  `PinWindow`/`PinWindows` 上扩展，未重写已通过的类型）：
  - Core 新增 `src/ZeepCut.Core/Pins/PinCollectionState.cs`（**T20 纯逻辑**，不可变、无 WPF/Win32）：
    由每张贴图的可见性构造；`Count`/`VisibleCount`/`HiddenCount`/`HasVisible`/`AllHidden`；
    `DecideBulkAction()` 给出 `PinBulkAction`（`None`/`HideAll`/`ShowAll`）；`HideAll()`/`ShowAll()`/`ToggleAll()`/`Resolve()`
    返回新状态（已是目标状态时原样返回，便于 UI 跳过无谓的窗口操作）；`WithVisible(index, bool)` 改单张。
    **语义**：空集合 → `None`（既不算“有可见”也不算“全隐”）；**有可见就全隐**（混合状态首次 toggle 是隐藏）；
    全隐才全显。
  - Core 新增 `src/ZeepCut.Core/Pins/PinPlacement.cs`（**T21 摆放逻辑**）：`NearCursor(cursor, w, h, screenBounds)`
    默认放在光标右下 +16px，右侧放不下翻到左边、下方放不下翻到上方（两轴独立），最后用 `PixelRect.Clamp` 夹进屏幕；
    贴图比屏幕大时按屏幕尺寸缩小并贴左上角。`NearCursor(DisplaySpace, ...)` 重载按 `DisplaySpace.FindByPoint`
    找屏幕（兼容副屏负坐标）。常量 `CursorOffset = 16`。
  - Core 新增 `src/ZeepCut.Core/Pins/PinSource.cs`（**T22 规则**）：`PinSource{Clipboard,History,Screenshot}` +
    `PinSourceText.Describe/ShouldRecordHistory`。**只有 `Clipboard` 返回 true**（内部截图已由 T11 的 `ICaptureSink` 记录，
    历史贴图的原记录已存在）。
  - App 新增 `src/ZeepCut/Pins/PinHistoryRecorder.cs`（**T22 接缝**）：
    `IPinHistoryRecorder.OnPinCreated(PinHistoryEntry{Source,Image,Bounds,CreatedAtLocal})` +
    `LoggingPinHistoryRecorder`（本轮实现：写一条 INFO，含建议文件名 `贴图_yyyy-MM-dd_HHmmss.png`，明确标注
    “T24 接真实历史，本轮仅日志占位”）。记录规则下沉到 Core，实现方不必各写一份。
  - App `src/ZeepCut/Pins/PinWindows.cs` 扩展（**唯一贴图注册表**，未改 `Create` 的既有调用契约）：
    - `Create(..., PinSource source = PinSource.Screenshot)` 新增可选来源参数；创建后统一走 `RecordHistory`。
    - **T20**：`VisibleCount`/`HasVisible`/`AllHidden`/`Snapshot()`；`HideAll()`（先 `pin.CaptureSnapshot()` 记下
      `PinWindowSnapshot(State, Dpi)` 再 `HideForBulk()`，只把 `Visibility` 设为 `Collapsed`，**不 Close、不丢实例**，
      已被隐藏的跳过并计数）、`ShowAll()`（用快照 `ShowFromBulk()` 恢复，再 `ApplyStateToWindow` + `CorrectPhysicalBounds`
      复核物理矩形）、`ToggleAll()` 返回实际执行的动作供提示。`NotifyClosed`/`CloseAll` 顺手清掉快照表。
    - **T21**：`CreateFromClipboard(cursor, display)` → `ClipboardImage.TryRead()` → `PinPlacement.NearCursor` →
      `Create(..., PinSource.Clipboard)`；返回 `ClipboardPinResult{Success,Status,Message,Pin,Format,PixelWidth,PixelHeight}`，
      失败不抛异常。
    - **T22**：`SetHistoryRecorder(IPinHistoryRecorder)`；`CreateFromHistory(...)`（本轮无调用方，历史图库 T25/T28 用，
      固定 `PinSource.History` → 不记录）。
  - App 新增 `src/ZeepCut/Platform/ClipboardImage.cs`（**T21 剪贴板读取**）：按 `Bitmap/DIB` → `PNG` 流 → `FileDrop`
    三种格式依次尝试，全部失败时区分 **Empty / NotAnImage / Busy / Failed** 四种状态并给中文原因；
    `COMException`（剪贴板被占用）重试 5 次 × 60ms 后返回 `Busy`，**不抛异常**。读到的位图统一转 Pbgra32 并复制成
    独立 `WriteableBitmap` 再 `Freeze`（剪贴板 DIB 属于对方进程，直接持有会变黑图）；文件拖放用
    `BitmapCacheOption.OnLoad` 立刻释放文件句柄。
  - App 新增 `src/ZeepCut/Capture/MonitorLayout.cs`：`EnumDisplayMonitors` + `GetMonitorInfo` + `GetDpiForMonitor`
    构造 `DisplaySpace`（含副屏负坐标与每屏 DPI），失败时用 `GetSystemMetrics` 虚拟桌面兜底。
  - App 新增 `src/ZeepCut/Platform/ToastWindow.cs`（**轻量提示**，T20/T21 用）：光标附近一行短消息，2.2 秒自动消失；
    无边框、`AllowsTransparency`、`WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`、不抢焦点、不参与命中；按目标屏 DPI 换算并夹进工作区。
    理由：托盘气球通知在本机常被系统“通知/专注”设置抑制（T5 已知），这类**必须看见**的反馈需要可靠通道。
  - App `src/ZeepCut/Platform/TrayIcon.cs`：`BuildMenu` 改为实例方法；新增
    `ToggleAllPinsTextProvider`（文案随状态变化：`隐藏全部贴图 (2)` / `显示全部贴图 (2)`）与
    `ToggleAllPinsDisabledProvider`（无贴图时 `MF_GRAYED` 灰显）；每次弹菜单求值并写一条日志
    `托盘菜单“隐藏全部贴图 (2)”：禁用=False`（便于自动化核对）。
  - App `src/ZeepCut/App.xaml.cs`：
    - `PasteFromClipboard(trigger)`（取代占位方法）：`GetCursorPos` → `MonitorLayout.ReadDisplaySpace()` →
      `PinWindows.CreateFromClipboard`；失败走 `NotifyPinFailure`（Toast + 托盘通知 + WARN 日志）。Alt+S 与托盘菜单共用。
    - `ToggleAllPins(trigger)`：空集合 → Toast“当前没有贴图…”；否则 `PinWindows.ToggleAll()` 并按动作提示
      `已隐藏 N 张贴图…` / `已显示 N 张贴图`。
    - `DescribeToggleAllPinsText()` 接托盘文案；`PinWindows.Count == 0` 时禁用菜单项。
  - App `src/ZeepCut/Pins/PinWindow.cs`：新增 `Image`（T22 记录用）、`IsShown`、`CaptureSnapshot()`、
    `HideForBulk()`（拖动中先收尾）、`ShowFromBulk(snapshot)`（恢复状态 + DPI + 重摆 + 复核）；新增
    `internal readonly record struct PinWindowSnapshot(PinState State, int Dpi)`。
  - App `src/ZeepCut/Capture/CaptureOverlayWindow.cs`：`TriggerPin` 显式传 `PinSource.Screenshot`（T22：内部来源不重复记）。
  - `src/ZeepCut/Platform/NativeMethods.cs` 新增 `MF_GRAYED`、`EnumDisplayMonitors` + `MonitorEnumProc`。
  - 新增测试：`tests/ZeepCut.Tests/Pins/PinCollectionStateTests.cs`（22）、`PinPlacementTests.cs`（18）、
    `PinSourceTests.cs`（7）——隐藏/显示往返、空集合、混合状态 toggle 语义、幂等、索引越界、相等性；
    光标附近/四角翻转/屏幕夹取/超大图/副屏负坐标/DisplaySpace 重载/非法入参；三种来源的记录规则。
  - 新增验证脚本：
    - `scripts/verify-t20t22.ps1`（场景 `toggle`/`clipboard`/`empty`/`text`/`busy`/`formats`/`exit`）。
    - `scripts/tray-menu.ps1`（**新工具**：Win11 托盘图标在“显示隐藏的图标”溢出面板里，脚本用 UIA 点
      “Show Hidden Icons” 展开、按名字找到 Zeep Cut 图标右键；Win32 `#32768` 弹出菜单**不暴露任何 UIA 项**，
      因此改为扫描菜单位图里的文字行（band）再点第 N 行；`-List` 只列不点、`-Shot` 存带菜单的截图）。

- 2026-10-18 **T18 贴图基础 + T19 贴图增强**（在 T11 的 `Pins/PinWindow.cs` 占位上**扩展**，未推翻重写；未改动已通过的 T12–T17 类型）：
  - Core 新增 `src/ZeepCut.Core/Pins/PinState.cs`（**T18/T19 状态与限制规则**，纯逻辑、无 WPF/Win32 依赖、不可变 record struct）：
    - 字段：`SourceWidth/SourceHeight`（原图物理像素）、`Scale`、`TopLeft`（屏幕物理像素，副屏可为负）、`Opacity`、`IsLocked`。派生：`DisplayWidth/DisplayHeight`、`Bounds`（`PixelRect`）、`MinimumScale`（由“宽高都 ≥ 40px”反推）、`CanDrag`、`IsOriginalSize`。
    - 常量：`MinimumDisplaySize = 40`、`MaximumScale = 8.0`、`MinimumOpacity = 0.2`、`MaximumOpacity = 1.0`、`OpacityStep = 0.1`、`ZoomStep = 1.1`。
    - 缩放：`ScaleTo(scale, anchor)` / `ZoomBy(factor, anchor)` —— **以锚点（鼠标位置）为基准保持画面点不动**；锚点先夹进旧矩形（指针在窗口外按最近边处理，不会甩飞）；倍率非法（NaN/∞/≤0）或已在上/下限时原样返回；2×2 这类极小原图**最小尺寸优先于 8 倍上限**（否则会缩到看不见）。
    - 原始大小：`ResetToOriginal()` 以**当前中心点**为锚点回到 1:1，避免贴图跳走。
    - 透明度：`WithOpacity` / `AdjustOpacity` 静默夹到 [0.2, 1.0]；NaN/∞ 保持原值。
    - 锁定：`WithLock` / `ToggleLock`；`DragTo`/`DragBy` 在锁定时**返回原状态**（拒绝移动），`TryDragTo` 额外报告是否真的移动；`PlaceAt` 是程序化摆放，不受锁定限制。
  - App `src/ZeepCut/Pins/PinWindow.cs` 扩展（无边框、`Topmost`、`ShowInTaskbar=false`、`WS_EX_TOOLWINDOW`、`CS_DROPSHADOW` 轻微投影 + 1 DIP 深色边框）：
    - 1:1 物理像素：窗口位置尺寸按 `DpiTransform(当前屏 DPI)` 从物理像素换算；`Loaded` 与每次拖动结束用 `GetWindowRect` + `SetWindowPos` 复核物理矩形。**跨屏拖动时用 `MonitorFromWindow` 重新取 DPI**（实测 120 → 96 后物理尺寸仍为 300×240）。
    - 拖动：左键按下记起点（屏幕物理坐标），`MouseMove` 走 `PinState.DragTo` 后一次性应用物理矩形；锁定状态下按下直接拒绝并写日志；`LostMouseCapture` 兜底收尾。
    - 滚轮缩放：以**光标位置**为锚点（T18 决策），每档 ×1.1，日志记录倍率/锚点/尺寸/位置，到限时记“已到限制”。
    - Ctrl+滚轮透明度：每档 ±0.1，下限 20%（日志记录前后值），到下限继续滚只记日志不变。
    - 锁定视觉：边框变橙色 2 DIP + 左上角 `已锁定 W×H` 徽标 + 光标改箭头；**锁定后仍响应滚轮/Ctrl+滚轮/右键菜单**。
    - 右键菜单（WPF `ContextMenu`，即时按状态生成）：原始大小（勾选项）/ 锁定位置·解锁位置（`IsChecked` 反映状态）/ 复制 / 另存为… / 关闭。复制用 `Clipboard.SetImage` 并单独 catch `COMException`；另存为复用 T11 的 `CaptureImageFileName` + `CaptureImageEncoder`，显示期间临时 `Topmost=false`（T11 的已知坑）。
    - 关闭：Esc（`PreviewKeyDown`）或右上 × 按钮；贴图小于 3 倍按钮尺寸（<66px）时自动隐藏 × 按钮，Esc/右键仍可关闭。
    - 标题改为 `Zeep Cut 贴图 {pid}`：窗口标题带 pid，验证脚本不会把它和遮罩（`Zeep Cut 截图遮罩`）混为一谈。
  - App 新增 `src/ZeepCut/Pins/PinWindows.cs`（**贴图集合**，T20/T21 从这里取实例）：`Create(image, selection, screenBounds, dpi)` 创建 + Show + 登记；`All`/`Count`；`NotifyClosed`（窗口关闭时自动注销）；`CloseAll()`（退出时用）。
  - `Capture/CaptureOverlayWindow.cs`：`TriggerPin` 改调 `PinWindows.Create`（不再直接 `new PinWindow`），成功路径与焦点交接不变。
  - `App.xaml.cs`：托盘“退出”与 `OnExit` 都调用新增的 `DisposePins()` → `PinWindows.CloseAll()`（PLAN：退出不恢复贴图）。
  - `Platform/NativeMethods.cs` 新增：`MonitorFromWindow`、`GetSystemMetrics` 常量、`GCL_STYLE`/`CS_DROPSHADOW`、`SetWindowClassStyle`、`GetWindowStyle`/`SetWindowStyle`（64 位走 `Get/SetWindowLongPtrW`，32 位回退）、`WS_POPUP`/`WS_EX_NOACTIVATE`、`SWP_NOMOVE`/`SWP_NOSIZE`/`HWND_TOP`。
  - `Capture/ScreenCapture.cs`：把原来的私有 `GetEffectiveDpi` 拆出公开的 `TryGetMonitorDpi(monitor, out dpi)`，遮罩与贴图共用同一个 DPI 读取入口（贴图跨屏时靠它重新取 DPI）。
  - 新增 `tests/ZeepCut.Tests/Pins/PinStateTests.cs`（32 个测试）。
  - 新增验证脚本 `scripts/verify-t18t19.ps1`（7 个场景：`basic`/`lock`/`menu`/`multi`/`limits`/`crossscreen`/`keep`）。
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

## 未验证 / 遗留问题（T25/T26 本轮）

- **图库只在主屏 125% 实测过**：副屏（-1080,0 / 100%）没有单独打开图库验证定位与缩放；
  窗口尺寸按 `SystemParameters.WorkArea` 夹取（主屏工作区），副屏上打开时的初始位置是
  `WindowStartupLocation.CenterScreen`（落在光标所在屏），没有专门验证。
- **“加载更多”（每页 60 条，滚到底自动追加）未在真实桌面验证**：本轮库里最多 4 条，
  分页逻辑只有代码路径；`HistoryRepositoryTests` 覆盖了 `HistoryQuery` 的 offset/limit。
- **滚动位置**：任何操作都会 `Reload()` 重建卡片，滚动条会回到顶部（记录多时要重新往下滚）。
  本轮选择“重建比增量维护更不容易出错”，若后续觉得碍事再改成增量更新。
- **重命名的非法输入提示**（空名称 / 只有 `???`）只在代码里走 `HistoryRename` 校验，**没有在桌面
  输入非法字符实测**；Core 单测（`HistoryRenameTests`）覆盖了规则本身。
- **收藏记录删除的额外提示文案**只对收藏项弹不同文案，本轮删除的是非收藏记录，收藏项的
  那条提示没有实测点击。
- **重命名/删除后“只看收藏”开着时的组合行为**只验证了“取消收藏 → 从列表消失”，
  没有验证“在收藏视图里重命名后仍然留在收藏视图”（逻辑上会留下，因为收藏没变）。
- **双击判定**用 WPF 原生 `ClickCount`（卡片 `MouseLeftButtonDown` 的 `e.ClickCount >= 2`），
  本轮用两次 45ms 间隔的真实双击验证成功；三击、慢双击未测。
- **预览的滚轮缩放 / 适应窗口 / 实际大小按钮**都没有在桌面点过（只截到按钮；缩放日志
  `历史预览缩放：…` 已写好）。预览对**超大记录**（如整屏 2560×1440）的滚动表现未实测。
- **列表为空的历史库（真正的空库）**没有截图：本轮空状态都是在“有 2~3 条但被筛掉”的情况下
  看到的（`没有匹配的记录`），`还没有历史记录。按 Alt+A 截个图…` 这条文案（`totalCount<=0` 分支）
  **未实测**。
- 图库窗口没有“删除全部 / 清空历史”入口（PLAN 没要求；T27 的自动清理是另一回事）。
- 主题（T30）未接：图库颜色写死在 `HistoryUi`，浅色下正常，深色模式下不跟随（符合 T30 之前的状态）。
- **构建前必须先退出应用**：应用运行时 `scripts/build.ps1` 会报
  `MSB3021/MSB3027 … ZeepCut.Core.dll is being used by another process`（被 `Zeep Cut (pid)` 锁住），
  看起来像编译错误其实是文件占用。本轮结尾就踩了一次：`Stop-Process ZeepCut` 后再跑即 0 warning 0 error。

## 未验证 / 遗留问题（T27/T28 本轮）

- **跨日清理没有等真实跨日**：`HistoryRetentionScheduler` 的"日期变化 → 再清一次"逻辑只做了
  代码走查（`DispatcherTimer` 每分钟比较 `DateTime.Now.Date`），**没有把系统时间调到第二天实测**
  （改系统时间会污染其它验证）。启动清理是真实实测的；跨日路径的规则本身由
  `HistoryRetentionPolicyTests` 覆盖（给不同的 `now`）。
- **调度器"清理一次"没有直接测"开着应用跨过零点"**：同上，未实测。
- **"贴图开着时覆盖该记录"未做桌面实测**：代码层面确认贴图是创建时的冻结快照
  （`PinWindow._image`，只在 `CreateFromHistory` 时 `TryLoad` 一次），覆盖不触碰它；但没有
  "先贴一张 → 再覆盖同一条记录 → 观察贴图不变"的真实桌面截图。
- **编辑窗口的缩放按钮 / Ctrl+滚轮缩放 / 适应窗口**没有逐个点过（打开时自动 `FitToWindow`
  走到了日志 `编辑窗口缩放：86%`/`158%`）；标注的**马赛克、画笔、文字、箭头、直线、椭圆**在编辑窗口里
  没有逐个桌面实测（本轮实测了矩形；这些工具与截图遮罩共用同一套 `AnnotationSession` 渲染代码，
  T15/T16/T17 已实测过）。
- **文字输入框在编辑窗口里的 Enter/Esc**（新增/双击改字）未实测，只做了代码走查。
- **多屏**：编辑窗口只在主屏 125% 打开过（副屏 100% / 负坐标未测）。
- **UIA 点击在图库卡片**上本轮遇到一次"点了没反应"（`InvokePattern` 与真实鼠标都试过），
  最终用**真实鼠标点 + `InvokePattern` 组合**跑通；`verify-t27t28.ps1` 里已加入
  "点击前把图库提到最前 + 用 `WindowFromPoint` 断言点击目标"的保护，但仍建议下一位用脚本
  点卡片时先跑 `-Scenario diagnose` 看 `TOP AT …` 是不是 `Zeep Cut 历史`。
- **托盘图标 UIA 名字有时很久不可用**：本轮重启后 `scripts/tray-menu.ps1`（按 UIA 名字找图标）
  连续失败，改为 `scripts/tray-menu-direct.ps1`（`PostMessage(WM_TRAYICON)` 打开真实菜单）
  才稳定。验证脚本已自动回退，但**托盘图标本身的可访问性问题仍未查明**
  （可能是 Windows 11 重启后通知区域缓存还没建好）。
- **WAL 与"WSL 侧直接改库"**：本轮在 WSL 侧用 `python3` 直接写 `/mnt/c` 下的 `history.db`
  时遇到两类坑，记下免得下一位再踩：
  1. 应用运行中**不能**直接对库做写操作，也不能只替换 `history.db` —— 应用自己那条连接还开着
     `-wal`，新的行会读不到/看不到旧行（旧 WAL 帧仍生效）。要造数据请**先退出应用**，
     并且**连 `history.db-wal`/`-shm` 一起删掉**再启动。
  2. `cp` 到 `/tmp` 后用 python `sqlite3` 读副本时，**必须连 `-wal` 一起 cp**，否则看到的是
     过时的主文件（本轮一度以为"行没被删"，其实 WAL 里已删）。
  3. 沙箱/9p 偶尔会让 python 读 `/mnt/c/...` 报 `PermissionError`/`disk I/O error`，
     用 shell 的 `cp` 拷到 `/tmp` 后一切正常（`cp` 本身没问题）。
- **`HistoryRepository.Delete` 的修复**是"读+删同事务"，`PurgeExpired` 与图库删除都受益；
  但**图库删除按钮**在修复后的代码上只做了单元测试（`HistoryRetentionRepositoryTests`），
  没有重新在桌面点一遍"删除 → 图片/缩略图同时消失"（上一次 T25 桌面验证时走的是修复前的实现）。
- 清理**不会**提示用户（只写日志/刷新图库）。若 PLAN 之后要求"清理后提示"，接在
  `App.OnHistoryPurged`（现在只刷新图库）里即可。
- 构建前必须先退出应用（MSB3021/MSB3027）这条规则**本轮又踩了一次**，已按 AGENTS.md 处理。

## 验证证据

- 2026-10-18 **T27/T28 构建+测试（最终代码）**：先 `Get-Process ZeepCut` 确认**应用已退出**
  （应用运行时踩到过 MSB3021 `ZeepCut.exe is being used by another process`，按 AGENTS.md 先退出再构建），
  再 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` →
  `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 1014, Skipped: 0,
  Total: 1014`；末尾 `BUILD+TEST OK`。

- 2026-10-18 **T27 真实桌面实测（主屏 \\.\\DISPLAY1 0,0 2560×1440 DPI=120/125%；证据 `artifacts/t27t28/`）**。
  造数据的方式（**先退出应用**，然后 WSL 侧用 `cp` 把 `history.db` 拉到 `/tmp`，用 python `sqlite3`
  写行 + 写 PNG 文件，再 `cp` 回 `%LOCALAPPDATA%\ZeepCut\`，**连 `-wal`/`-shm` 一起删掉**）：
  6 行 = `T27_40天前收藏_保留`(fav, 2026-09-08) / `T27_40天前收藏_保留2`(fav) /
  `T27_31天前_应清理`(2026-09-17) / `T27_29天前_保留`(2026-09-19) / 昨天的 `截图_昨天验证` /
  今天的 `Abc 报告`(fav)；`images/` 6 个、`thumbs/` 6 个。快照存档
  `artifacts/t27t28/db-T27-step0-before-launch.db`。
  - **启动清理**（`scripts/run.ps1` 冷启动）：日志
    `30 天清理（启动）：清理过期历史：保留=30 天 现在=2026-10-18T17:28:23.725Z 清理前=6 过期=1 已删除=1 失败=0 清理后=5`
    与 `30 天清理已删除 1 条记录`；`30 天清理调度器已启动：每 1 分钟检查日期变化，当前日期=2026-10-18`。
  - **结果核对**（WSL 侧：`cp history.db* images/ thumbs/` 到 `/tmp` 后读副本，**必须带 WAL**）：
    行 **6 → 5**，`images/` **6 → 5**、`thumbs/` **6 → 5**；消失的正是 `T27_31天前_应清理`
    这一行 + `images/20261018-172715-817-1f6e4724.png` + 同名 `.thumb.png`；
    `T27_29天前_保留`（29 天，未到期）与 `T27_40天前收藏_保留*`（40 天，收藏）**都还在**，
    收藏行的 `IsFavorite=1` 未变。存档 `artifacts/t27t28/db-T27-step1-after-start.db`。
  - **取消过期收藏的二次确认**（`scripts/verify-t27t28.ps1 -Scenario unfav -CardIndex 3`，
    当时库里有 4 条、卡片 3 是 40 天前收藏）：点星标 → 真实 Win32 对话框
    标题 `取消收藏`、按钮 `1:OK | 2:Cancel`，正文（读 STATIC 控件）：
    `“T27_40天前收藏_保留”创建于 40 天前，已经超过 30 天保留期。\n\n取消收藏后，这条记录会在下次清理（启动或跨日）时被删除，图片、缩略图与记录都会永久移除。\n\n确定取消收藏吗？选择“取消”可以保持收藏、什么都不改。`
    截图 `artifacts/t27t28/unfav-warning.png`（可见图库 + 该对话框）。
    点 **Cancel(IDCANCEL=2)** → 状态栏 `已保持收藏“T27_40天前收藏_保留”（收藏不会被自动清理）。`，
    该卡片星标仍是 `取消收藏`（= 仍为收藏），日志 `取消过期收藏已放弃：用户选择保留收藏`。
    截图 `artifacts/t27t28/after-unfav-cancel.png`。
  - **"正好 30 天 / 29 天 23 小时 / 30 天 1 秒"三个边界**由单元测试锁住
    （`HistoryRetentionPolicyTests.IsExpired_ExactlyThirtyDays_IsExpired` /
    `…_TwentyNineDaysTwentyThreeHours_IsNotExpired` / `…_ThirtyDaysPlusOneSecond_IsExpired`），
    桌面侧实测的是 31 天（删）/ 29 天（留）/ 40 天收藏（留）。

- 2026-10-18 **T28 真实桌面实测（同上环境；证据 `artifacts/t27t28/`）**。被测记录：
  `截图_昨天验证`，`Id=704d94d9-c0a3-473c-8e94-a2fabdade02e`，760×480，收藏=0，
  `CreatedAtUtc=2026-10-17T16:27:45.000Z`，`UpdatedAtUtc=2026-10-18T16:39:12.779Z`。
  - **打开编辑窗口**：图库卡片 1 选中后用 `InvokePattern` 点 `history-action-标注-1`（UIA 名
    `追加标注并覆盖这条记录`）；日志 `历史编辑窗口已创建：… 图片=760x480 收藏=False
    创建=2026-10-17T16:27:45.000Z …（旧标注已在像素中，只能追加）`、`编辑窗口缩放：86%`。
    窗口截图 `artifacts/t27t28/editor-open.png` / `editor-open-primary.png`
    （标题栏 `Zeep Cut 编辑历史 · 截图_昨天验证`，右侧 8 个工具 + 颜色 + 粗细 + 撤销/重做/删除 +
    缩放 + “保存并覆盖”/“取消”）。
  - **追加标注**：点 `history-edit-tool-矩形` → 在图像内真实拖拽 → 日志
    `编辑窗口开始画标注：工具=Rectangle 起点图像=(62,91) 图片=760x480`、
    `编辑窗口标注抬起：工具=Rectangle 已提交=True 文档标注数=1 撤销栈=1`、
    `编辑窗口状态：已追加 1 个标注…`。截图 `t28-editor-annotated.png`（图像上出现红框）。
  - **首次“保存并覆盖”解释**：点 `history-edit-save` → 日志 `首次使用覆盖编辑，显示覆盖语义说明`，
    真实 MessageBox 标题 `保存并覆盖`，正文含
    `“截图_昨天验证”现在的标注已经合并进图片像素，不能再拆分修改，只能在这张图上继续追加标注。`
    `点“保存并覆盖”会用新图替换这条记录原来的图片：· 名称、创建时间与收藏状态保持不变，不会新增一条历史记录；· 旧版本图片与旧标注「不会被保留」，无法撤销回编辑前的样子。`
    `想保留一份原图，请先取消，在图库里用“另存为”导出。`
    截图 `artifacts/t27t28/overwrite-explanation.png` / `-crop.png`（可见编辑器里已有的红框 +
    对话框）。点 OK 后日志 `已记录“覆盖说明已解释”到 C:\Users\19427\AppData\Local\ZeepCut\history-ui.json`
    （文件内容 `{"OverwriteExplained":true}`）。
  - **保存结果**（日志）：
    `编辑窗口保存并覆盖成功：名称=截图_昨天验证 Id=704d94d9-… 新尺寸=760x480 图片字节=32176
    缩略图字节=18129 追加标注=1 创建时间保留=2026-10-17T16:27:45.000Z 收藏保留=False
    修改时间=2026-10-18T17:31:09.217Z … 历史条数 5 → 5（不新增记录） 图片文件=images/20261018-122702-740-704d94d9.png`
    以及 `历史编辑覆盖完成：… 历史条数 5 → 5：创建时间=… 收藏=False 修改时间=…`。
    **WAL 快照对比**（`artifacts/t27t28/db-T28-before.db` vs `db-T28-after.db`）：
    **行数 6 → 6、Id 集合完全相同**；逐列比对后只有 `UpdatedAtUtc` 变化
    （`17:20:34.962Z → 17:31:09.217Z`）；`Id / Name / CreatedAtUtc / IsFavorite / RelativeImagePath /
    RelativeThumbnailPath / Width / Height / Source` **全部一致**。
    **文件**：`images/` **5 → 5**、`thumbs/` **5 → 5**（原地覆盖，没有多出 `.tmp` 文件）；
    `images/20261018-122702-740-704d94d9.png` 的 sha256 `93ca87b3…` → `a8233d33…`，
    缩略图 `4b47110b…` → `13a1f951…`（对比记录 `artifacts/t27t28/t28-before-sha.txt`）。
    **像素**：新旧图都是 **760×480**；diff 包围盒 `(60,89)-(297,233)`、**2965 个像素变化**，
    即新画的矩形；图里原有的界面内容完全没变（`artifacts/t27t28/t28-image-after.png`，
    看得出两个红框：一个是本次画的、一个是上一次验证遗留的）。
  - **取消编辑不改原图**：编辑窗口里选 `history-edit-tool-椭圆` 拖一个椭圆（日志
    `编辑窗口标注抬起：工具=Ellipse 已提交=True 文档标注数=1`）→ 点 `history-edit-cancel` →
    MessageBox 标题 `取消编辑`，正文 `放弃这次追加的标注吗？原图不会被修改（取消编辑不改变这条历史记录）。`
    点 OK → 日志 `编辑窗口取消编辑：… 追加标注=1 个已丢弃，原图未修改` 与
    `历史编辑窗口已关闭（未保存）：… 原图未修改`；**图片与缩略图 sha256 与编辑前逐字节相同**，
    `images/`、`thumbs/` 文件数不变。截图 `cancel-confirm.png`。
  - **编辑期间记录被删除**：编辑窗口开着时，在图库对同一条记录点 `history-action-删除-2` 并确认
    （这一步同时暴露了下面 bug #1），再回编辑窗口点“保存并覆盖” → 日志
    `[WARN] 编辑窗口保存失败（侧栏按钮）：记录已被删除 Id=cffafdc0-… 名称=T27_29天前_保留，不写任何文件`，
    真实 MessageBox 标题 `保存并覆盖`，正文
    `“T27_29天前_保留”在编辑期间已被删除，本次修改没有保存（原记录不存在，也不会新建一条）。`
    **进程未崩溃**、没有新行、没有新文件。截图 `save-deleted-warning.png`。
  - **图库端佐证**：保存后 `历史图库刷新：… 命中=5 显示=5 分组=今天=1 | 昨天=1 | 2026-09-19 星期六=1 |
    2026-09-08 星期二=2 总记录=5`，卡片 1 的 meta 仍是 `2026-10-17 12:27:45 · 760×480 · 截图`
    （时间/尺寸不变），缩略图重新生成（`t28-thumb-after.png` 320×202）。


- 2026-10-18 **T25/T26 构建+测试（最终代码）**：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` → `Build succeeded.`、
  `0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 958, Skipped: 0, Total: 958`；末尾 `BUILD+TEST OK`。
  其中历史相关过滤实测 `--filter 'FullyQualifiedName~ZeepCut.Tests.History'` → `Passed: 254`。

- 2026-10-18 **T25/T26 真实桌面实测（主屏 \\.\DISPLAY1 0,0 2560×1440，DPI=120/125%；
  脚本 `scripts/verify-t25t26.ps1`；证据 `artifacts/t25t26/`）**。数据准备：先清空
  `history.db*` 与 `images/*.png`、`thumbs/*.png`，再用脚本 `-Scenario seed` 做 3 条真实历史：
  两次 Alt+A 框选 + Enter 复制（选区 640×420 / 760×480）、一次 Alt+S 外部剪贴板贴图（300×200）。
  WSL 侧先用 `cp history.db* /tmp/hdb/` 再 `sqlite3` 核对（WAL 模式不能用 python3 直接开 `/mnt/c`），
  然后**把其中两条的创建时间改成"昨天"与"前天"**（只改数据库行，图片文件照旧）并 checkpoint，
  让日期分组真正有 3 个不同的日期可验证。最终 3 行：
  `贴图_2026-10-18_122704`(Clipboard,300×200) / `截图_昨天验证`(Screenshot,760×480) / `截图_前天验证`(Screenshot,640×420)。
  - **托盘“历史”打开图库**（`-Scenario gallery`）：`scripts/tray-menu.ps1 -Index 3` 真实点开托盘菜单并点“历史”，
    日志 `菜单命令: History` → `历史图库触发：托盘菜单` → `历史图库窗口已创建` →
    `历史图库刷新：搜索=- 只看收藏=False 排序=CreatedAtDescending 每页=60 → 命中=3 显示=3
    分组=今天=1 | 昨天=1 | 2026-10-16 星期五=1 总记录=3 用时=28ms`；截图 `gallery-01-opened.png`
    （窗口标题 `Zeep Cut 历史`，顶部搜索框 + 全部/只看收藏 + 刷新，三组标题
    **`今天 (1)` / `昨天 (1)` / `2026-10-16 星期五 (1)`**，每张卡片是缩略图 + 名称 + `今天 12:27:04 · 300×200 · 剪贴板贴图`）。
    UIA 树核对（脚本 `Report-Gallery`）：3 个 `history-group-*` 分组节点、3 张卡片，每张卡片的
    `history-name-N` / `history-meta-N` / `history-star-N` 都在，星标 UIA 名字是 `收藏`。
    **单实例**：重复点托盘“历史”不会开第二个窗口（日志 `历史图库已打开，本次只激活已有窗口`）。
  - **搜索（名称子串、大小写不敏感、与收藏组合）**（`-Scenario search`）：
    - 先重命名一条为 `Abc 报告`，搜 `abc` → `共 1 条匹配（库内总计 3 条）`，只剩 `Abc 报告`；
      再搜 `ABC`（全大写）→ **同样命中 1 条**（大小写不敏感）；截图 `search-01-keyword.png`、`search-02-uppercase.png`。
    - 搜 `zzz-no-such-name` → 0 条，空状态文案 `没有名称包含“zzz-no-such-name”的记录。换个关键字试试，或清空搜索。`
      （文案来自 Core `HistoryView.DescribeEmptyState`）；截图 `search-03-empty.png`。
    - 清空后点“只看收藏” → `共 0 条匹配（库内总计 3 条）`、空状态
      `还没有收藏的记录。点击缩略图左上角的星标即可收藏。`；截图 `search-04-favorites-only.png`。
      收藏了一条之后再点“只看收藏” → `共 1 条匹配（库内总计 2 条）`，列表只剩被收藏的那条。
    - 空状态截图 `empty-01-no-match.png` 上可见“没有匹配的记录”“清除筛选”按钮；
      点“清除筛选”后回到 `共 3 条历史记录`（`empty-02-cleared.png`）。
  - **收藏 + 重启持久化**（`-Scenario favorite` 然后 `-Scenario restart`）：
    - 点第 1 张卡片的星标 → UIA 该星标名字由 `收藏` 变 `取消收藏`，状态栏
      `已收藏“截图_昨天验证”（不会被 30 天清理）。`，日志
      `历史收藏切换：名称=截图_昨天验证 … 收藏=False → True`；截图 `favorite-01-starred.png`
      （放大 `zoom-stars-after-restart.png` 可见卡片 1 是**实心金色星标**、卡片 0 是空心）。
    - WSL 侧查库：该行 `IsFavorite=1`，且 `CreatedAtUtc` 仍是 `2026-10-17T16:27:45.000Z`（只有 `UpdatedAtUtc` 变），
      即收藏**不延长 30 天期限**（PLAN 要求）。
    - 托盘“退出”（`tray-menu.ps1 -Index 6`）→ 脚本确认 `APP ALIVE AFTER EXIT: 0` → `scripts/run.ps1`
      重新启动（新 pid）→ 重新从托盘打开图库 → UIA 读到该卡片星标仍是 `取消收藏`；
      再反过来（取消收藏 A、收藏 B）重启一次，读到 `CARD 0 star=[取消收藏] CARD 1 star=[收藏]`，
      与数据库 `[('Abc 报告', 1), ('截图_昨天验证', 0)]` 完全一致 —— **收藏重启后保持**。
  - **重命名生效**（`-Scenario rename`）：选中卡片 0（操作条展开，UIA 逐个确认
    `复制 / 贴图 / 改名 / 删除 / 另存为 / 预览` 的物理矩形）→ 点“改名” → 对话框
    `history-rename-dialog` 出现，输入框 UIA 名字 `历史记录名称`；截图 `rename-01-dialog.png`。
    全选后输入 `Abc 报告` → 点“确定重命名” → 状态栏 `已重命名为“Abc 报告”。`，
    卡片名称变成 `Abc 报告`（`rename-03-after.png`），日志
    `历史重命名成功：Id=… 贴图_2026-10-18_122704 → Abc 报告 （创建时间不变=…）`；
    数据库该行 `Name='Abc 报告'`，`CreatedAtUtc` 未变。
  - **双击预览大图**（`-Scenario preview`）：双击缩略图（物理 788,462）→ 预览窗口
    `预览 · Abc 报告`，元数据栏 `Abc 报告 · 300×200 px · 2026-10-18 12:27:04 · 剪贴板贴图 · 缩放 100%`，
    截图 `preview-01-opened.png` 里图片就是 1:1 的 300×200（绿底 + 黄块），底部有
    缩小/放大/实际大小/适应窗口/复制/贴图；向预览窗口 PostMessage ESC → `PREVIEW STILL OPEN: False`
    （截图 `preview-02-closed.png`），即**双击能开大图、Esc 能关**。
  - **从历史贴图不新增历史**（`-Scenario pin`，卡片 2 = `截图_前天验证`）：
    点“贴图” → 贴图窗口 `0xFA0930 854,685 640x420 [Zeep Cut 贴图 38296]`，`PINS before=0 after=1`；
    文件计数 `images=3 thumbs=3` → `images=3 thumbs=3`（**不变**）；日志
    `贴图历史联动：来源=历史记录 不记录（…），尺寸=640x420 位置=(854,685) 640x420` →
    `从历史贴图完成：名称=截图_前天验证 … 贴图总数=1 历史记录数 3 → 3（不新增历史）`；
    截图 `pin-01-created.png`。之后点贴图右上 × 关闭，`PINS after close: 0`。
  - **删除：行 + 图片 + 缩略图同步删除**（`-Scenario delete`，卡片 2）：
    点“删除” → 真实确认框 `0xB20544 1066,672 446x232 [删除历史记录]`（截图 `delete-01-confirm.png`）→
    点“确定” → 日志 `历史删除：名称=截图_前天验证 … 行已删除=True
    图片=images/20261018-122656-330-ae2daf10.png 图片仍在=False
    缩略图=thumbs/20261018-122656-330-ae2daf10.thumb.png 缩略图仍在=False 剩余记录数=2`。
    脚本侧文件计数 `images 3 → 2`、`thumbs 3 → 2`（各少一个，且是同一记录的两个文件）；
    WSL 侧 `ls images/ thumbs/` 只剩另两条记录的两个文件，`sqlite3` 查询行数 3 → 2、
    被删行的 Id 已不存在；图库刷新为 2 个分组 2 张卡片（`delete-02-after.png`）。
  - **复制到剪贴板**（`-Scenario copy`）：点“复制” → 独立读取本进程剪贴板
    `CLIPBOARD IMAGE 300x200`（与记录尺寸一致，保存 `copy-clipboard.png`），状态栏
    `已复制“Abc 报告”（300×200）。`。
  - **另存为（真实 Win32 对话框）**（`-Scenario saveas`，760×480 的记录）：
    点“另存为” → 对话框 `历史图片另存为`（截图 `saveas-01-dialog.png`）→ 用剪贴板把路径贴进文件名框
    （中文 IME 会改写 `C:\` 的老坑）→ 点“保存” → `SAVED FILE …\saveas-history.png bytes=31391 size=760x480`
    （PNG 可被本进程重新打开，尺寸与记录一致），截图 `saveas-02-filled.png` / `saveas-03-after.png`。
  - **脚本注意事项（写给下一位）**：托盘菜单的自动打开在本机有一条不稳定路径 ——
    直接 `PostMessage(WM_TRAYICON, WM_RBUTTONUP)` 给托盘隐藏窗口**有时不弹菜单**
    （取决于当时哪个窗口是前台，本轮遇到过几次 `NO MENU`）；`scripts/tray-menu.ps1`
    （UIA 展开“显示隐藏的图标” → 右键 Zeep Cut 图标 → 扫描菜单文字行 → 点第 N 行）**稳定可用**，
    所以 `verify-t25t26.ps1` 直接调用它。菜单文字行的 y 坐标会随弹出位置变化，**不要写死**。
  - **WPF 弹窗的 UIA 位置**：本机 WPF 拥有的 owned 窗口（重命名对话框、预览窗口）
    在 UIA 里是**图库窗口的子孙**，不是桌面 RootElement 的直接子节点 —— 脚本改用
    `AutomationId` 从 RootElement 全树查找（`history-rename-dialog` / `history-preview-window`）。


- 2026-10-18 **T23/T24 构建+测试（最终代码）**：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` → `Build succeeded.`、
  `0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 860, Skipped: 0, Total: 860`；末尾 `BUILD+TEST OK`。
  本轮新增 156 个测试（过滤实测 `--filter 'FullyQualifiedName~ZeepCut.Tests.History'` → `Passed: 156`）。
- 2026-10-18 **T23/T24 真实桌面验证（主屏 DISPLAY1 0,0 2560×1440 DPI=120/125%，脚本
  `scripts/verify-t23t24.ps1`，证据 `artifacts/t23t24/`）**：每次场景前杀掉进程、删掉
  `history.db*` 与 `images/*.png`、`thumbs/*.png`，再 `scripts/run.ps1` 重启，保证“冷启动 + 空库”。
  - **首次运行建库**（pid 58484/44868/59864）：日志
    `历史库已就绪：C:\Users\19427\AppData\Local\ZeepCut\history.db 版本 1 → 1 新建=True 迁移步数=0 记录数=0 图片目录=…\images`；
    目录下出现 `history.db`（+ WAL/SHM）、`images/`、`thumbs/`。
    `PRAGMA user_version` = **1**；`sqlite_master` 里 `history` 表 10 列与 ARCHITECTURE 约定一致
    （`Id TEXT PRIMARY KEY NOT NULL, Name, CreatedAtUtc, UpdatedAtUtc, IsFavorite INTEGER DEFAULT 0,
    RelativeImagePath, RelativeThumbnailPath NULL, Width, Height, Source`），
    索引 **4 条**（`IX_history_CreatedAtUtc`、`IX_history_IsFavorite`、`IX_history_Name`、
    `IX_history_Favorite_Created`）。**重复启动不会重建/清空**（重启后记录数与图片文件都在）。
  - **截图 → 复制（Enter）= 恰好 1 条**（场景 `copy`，选区 `(360,320) 640×420`）：
    日志 `输出完成：动作=复制到剪贴板 …` → `历史写入完成：名称=截图_2026-10-18_084354 来源=截图
    尺寸=640x420 图片=images/20261018-084354-099-0c075c79.png 缩略图=thumbs/…thumb.png …`
    → `截图已记录到历史：动作=复制到剪贴板 …`。数据库（python3 sqlite3 从 WSL 直接查）：
    `('截图_2026-10-18_084008','2026-10-18T12:40:08.337Z',0,640,420,'Screenshot','images/20261018-084008-337-00dc8005.png','thumbs/…thumb.png')`
    —— **名称/尺寸/UTC 时间/来源/两个相对路径全部正确**（本地 08:40:08，UTC 12:40:08）。
    `images/*.png` 实测 **640×420 @ 48824 bytes**，`thumbs/*.png` **320×210 @ 26612 bytes**
    （最长边正好 320，等比不变形）。read_image 打开历史图片 `artifacts/t23t24/history-image-copy.png`：
    内容就是选区里的 DSH/资源管理器画面，非黑图/非缩略图。
  - **Esc 取消不记录**（场景 `cancel`）：拖选后 Esc → 日志
    `取消本次截图（Esc）… 不写入历史` → `遮罩窗口已关闭 … 已取消=True` → `截图结束 … 完成动作=无`；
    **数据库行数不变**（3→3），**images/thumbs 文件数不变**，且整段日志里没有
    `输出完成` / `历史写入完成`。
  - **截图 → 另存为 = 只有 1 条（不是两条）**（场景 `save`，真实 Win32 对话框）：
    `保存对话框已打开` → 自动化填入路径并保存 → `文件已保存 … 格式=Png 尺寸=640x420`
    → `输出完成：动作=另存为 …` → **只有一条** `历史写入完成：名称=截图_2026-10-18_084024 …`。
    数据库该轮新增 **1 行**（不是 2），另存为文件本身也真实存在。
  - **截图 → 贴图 = 只记 1 条（不重复）**（场景 `pin`）：日志顺序是
    `贴图历史联动：来源=内部截图 不记录（…）` → `输出完成：动作=贴图` → `历史写入完成：名称=截图_…`；
    数据库新增 **1 行**（Source=Screenshot），不是 2 行。贴图窗口 `0x16E0E9C 360,320 640x420`。
  - **Alt+S 外部剪贴板贴图 = 1 条 Source=Clipboard**（场景 `clipboard`，剪贴板放 300×200 PNG）：
    日志 `剪贴板图片读取成功：格式=Bitmap/DIB 尺寸=300x200` → `贴图历史联动：来源=外部剪贴板 已记录一次
    名称=贴图_2026-10-18_084332 …`；数据库该行
    `('贴图_2026-10-18_084332','…T12:43:32.425Z',0,300,200,'Clipboard','images/20261018-084332-425-6f120e91.png','thumbs/…')`
    —— **名称前缀是 `贴图_`、来源是 `Clipboard`、尺寸 300×200 与原图一致**；
    图片内容 read_image 核对为蓝底 + 黄/红块 + `T23 CLIP` 文字（`artifacts/t23t24/history-clipboard.png`）。
  - **一次会话串起全部动作（场景 `all`，4 步累计计数全部符合预期）**：复制 → 取消 → 另存为 → 贴图 → 外部剪贴板，
    每步后 `images`/`thumbs` 计数依次 **1 / 1 / 2 / 3 / 4**，即
    **取消不增、另存为 +1、贴图 +1、剪贴板 +1**；结束时 2 张贴图并存
    （`0x1C1AD2 360,320 640x420`、`0x2001330 1416,816 240x160`）。数据库最终 4 行：
    3×`Screenshot` + 1×`Clipboard`，名称/尺寸/时间/来源/两个相对路径逐行核对无误。
  - **图片文件没有被锁（PLAN：读图注意释放句柄）**（场景 `lock`，应用持续运行）：
    `LOCK PROBE RENAME IMAGE: OK` → `LOCK PROBE COPY IMAGE: OK 48648 bytes` →
    `LOCK PROBE DELETE IMAGE: OK (no lock held by the app)`；缩略图 `LOCK PROBE COPY THUMB: OK 26500 bytes`。
    即写入/读取完成后，历史图片与缩略图都能被**另一个进程改名、复制、删除**。
  - **历史写失败不影响主操作（真实失败注入）**：
    - **启动初始化失败**：把 `%LOCALAPPDATA%\ZeepCut\history.db` 换成一个**同名目录**再启动 →
      日志 `[ERROR] [History] 历史库初始化失败，本次运行不记录历史（截图/贴图不受影响）：
      HistoryStoreException: 初始化历史库失败：SQLite Error 14: 'unable to open database file'.`，
      并弹出提示框（`#32770 [Zeep Cut]`，截图 `artifacts/t23t24/dbfail-startup-dialog.png`）；
      **应用仍照常启动、托盘与热键正常**，截图 sink 自动降级为 `LoggingCaptureSink`。
      恢复真库文件后重启 → `历史库已就绪 … 新建=True`，功能立即恢复。
    - **单次写入失败**：把 `images/` 换成同名文件后做一次复制 →
      日志 `[ERROR] [History] 历史保存失败（复制到剪贴板 本身已成功，不影响剪贴板/文件/贴图）：
      IOException: Cannot create '…\images' because a file or directory with the same name already exists.`
      → `[WARN] [History] 复制到剪贴板已成功，但历史保存失败：…` → Toast
      **`复制到剪贴板已成功，但历史保存失败（详见日志）。`**（截图 `artifacts/t23t24/writefail-toast.png`）。
      **复制本身没有失败**：独立进程 `scripts/clipboard.ps1` 立刻读到 `CLIPBOARD IMAGE 640x420`。
      还原 `images/` 后再次复制 → 正常 `历史写入完成`（自愈，无需重启）。
  - **数据库查询方式（WSL 侧）**：`history.db` 处于 WAL 模式，Windows 侧（PowerShell）读到的视图不可靠；
    本轮所有数据库核对都是先 `cp history.db history.db-wal history.db-shm` 到 `/tmp` 再用
    `python3 -c "import sqlite3; …"` 查（`/mnt/c` 上直接开库会报 `disk I/O error`）。
    **下一位接手者核对历史库照这个做法**，不要用 python3 直接开 `/mnt/c/...`。


- 2026-10-18 **T20/T21/T22 构建+测试（最终代码）**：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'` → `Build succeeded.`、
  `0 Warning(s)`、`0 Error(s)`；测试 → `Passed! - Failed: 0, Passed: 704, Skipped: 0, Total: 704`；末尾 `BUILD+TEST OK`。
  本轮新增 47 个测试（`--filter 'FullyQualifiedName~ZeepCut.Tests.Pins'` → `Passed: 79`，其中 `PinCollectionStateTests` 22、`PinPlacementTests` 18、`PinSourceTests` 7；覆盖）：隐藏/显示往返、空集合（`DecideBulkAction=None`、
  三个批量方法都是原样返回的空操作）、全可见→全隐、全隐→全显、混合状态 toggle 先全隐、连续 toggle 到全显、单张贴图两个方向、
  幂等（`HideAll`/`ShowAll` 在目标状态时 `Assert.Same`）、`WithVisible` 单张改动、索引越界抛异常、相等性/哈希、
  `ToString` 计数；光标右下默认摆放、图像尺寸不变、右边缘翻左、下边缘翻上、右下角双轴翻转、角落空间不足夹回屏幕、
  超大图缩到屏幕、副屏负坐标摆放/贴左右上边、`DisplaySpace` 重载命中主/副屏、`DisplaySpace` 为 null、非正尺寸与空屏幕抛异常；
  三种 `PinSource` 的记录规则（只有 Clipboard 为 true）与中文描述。
- 2026-10-18 **T20 全局隐藏/显示（真实桌面，DISPLAY1 0,0 2560×1440 DPI=120/125%，pid 24280，
  `scripts/verify-t20t22.ps1 -Scenario toggle`，证据 `artifacts/t20t22/toggle/`）**：
  - 先贴两张（300×220 @ 360,320 与 340×260 @ 700,360），再对第一张**滚轮放大 2 档 + Ctrl+滚轮降到 0.7**
    （让“恢复”这件事有可验证的内容）：窗口枚举 `0x691A4C 352,312 363x266`、`0x15B16DA 700,360 340x260`；
    截图 `primary-toggle-02-zoomed-faded.png`。
  - 托盘菜单**文案随状态变化**：打开菜单时日志 `托盘菜单“隐藏全部贴图 (2)”：禁用=False`；菜单截图
    `primary-toggle-02b-tray-menu-hide.png` 可见该项文字为 `隐藏全部贴图 (2)`。
  - 点该项 → 日志 `全局显隐触发：托盘菜单，当前 贴图集合=2 张（可见 2 张，隐藏 0 张）` →
    `已隐藏贴图：(352,312) 363x266 缩放=1.21x 透明度=0.7 锁定=False` →
    `已隐藏贴图：(700,360) 340x260 缩放=1x 透明度=1 锁定=False` →
    `隐藏全部贴图完成：隐藏 2 张，集合仍有 2 张（实例未销毁）` → `全局显隐结果：已隐藏全部贴图，仍保留 2 张实例`；
    Toast `已隐藏 2 张贴图（再次点击托盘菜单可显示）`（截图 `primary-toggle-03-hidden-toast.png`）。
    **窗口枚举 `visible count=0`**（两张都不可见），截图 `primary-toggle-04-hidden.png` 上两张贴图消失；
    像素测量：原贴图区域 (360,320) 340×330 平均亮度 **196.81 → 249.00**（变成桌面背景）。
  - 再次打开菜单 → 日志 `托盘菜单“显示全部贴图 (2)”：禁用=False`（**文案已切换**）；点该项 → 日志
    `全局显隐触发：托盘菜单，当前 贴图集合=2 张（可见 0 张，隐藏 2 张）` →
    `已显示贴图：(352,312) 363x266 缩放=1.21x 透明度=0.7 状态已恢复=True` →
    `已显示贴图：(700,360) 340x260 缩放=1x 透明度=1 状态已恢复=True` → `显示全部贴图完成：显示 2 张，当前可见 2 张`；
    Toast `已显示 2 张贴图`（截图 `primary-toggle-05-shown.png`）。
  - **恢复后完全一致**：窗口签名 `0x691A4C:352,312 363x266 | 0x15B16DA:700,360 340x260` 与隐藏前**逐项相同**
    （hwnd 同一实例、位置、尺寸、缩放后的宽高都不变），脚本输出 `RESTORED IDENTICAL (hwnd+position+size): True`；
    像素级比对：`primary-toggle-02-zoomed-faded.png` 与 `-05-shown.png` 在贴图区域 (360,320,340,330) **完全相同
    （mean 196.81 / 196.81、`identical=True`）**，即透明度 0.7 与 1.21x 缩放都被恢复；截图 `primary-toggle-06-shown-clean.png`。
  - **无贴图时菜单项灰显**：关闭全部贴图后打开托盘菜单，日志
    `托盘菜单“显示/隐藏全部贴图”：禁用=True`（08:10:37 与 08:10:47 两次），
    截图 `primary-nopins-menu.png` 中该项为默认文案 `显示/隐藏全部贴图` 且呈灰色；
    有贴图时同一项为 `隐藏全部贴图 (2)` / `显示全部贴图 (2)` 且 `禁用=False`（对照上面两条日志），
    即**文案与可用状态都随集合变化**。
- 2026-10-18 **T21 剪贴板贴图（真实桌面，主屏，pid 56432，场景 `clipboard`）**：
  - 用 `scripts` 内置的 WinForms 夹具把一张 300×200 PNG（蓝底 + 黄/红块 + 文字 `T21 CLIP`）放进剪贴板；
    光标定位到物理 (900,600)，发 `Alt+S`。日志：
    `热键触发：Alt+S（id=2）` → `从剪贴板贴图触发：全局热键 Alt+S` →
    `剪贴板图片读取成功（第 1 次尝试）：格式=Bitmap/DIB 尺寸=300x200` →
    `创建贴图窗口：选区=(916,616) 300x200 ... 期望物理=(916,616) 300x200` →
    `贴图已加入集合：当前 1 张，来源=外部剪贴板 物理=(916,616) 300x200 ...` →
    `剪贴板贴图完成：光标=(900,600) 屏幕=\\.\DISPLAY1 DPI=120 摆放=(916,616) 300x200 ... 格式=Bitmap/DIB`。
  - 窗口枚举 `0x951BE8 916,616 300x200`：位置正是**光标 +16/+16**（`|dx|=16 |dy|=16`）、尺寸 300×200 与原图一致、
    `inside=True`（完全在屏幕内）；截图 `artifacts/t20t22/clipboard/primary-clipboard-01-created.png`
    可见蓝底黄块红块的贴图就在光标右下方、右上 × 按钮正常。
  - **副屏（负坐标）**：`-Target secondary -CursorX -900 -CursorY 900` → 窗口 `-884,916 300x200`
    （= -900+16 / 900+16），`inside=True`；截图 `artifacts/t20t22/secondary/secondary-clipboard-01-created.png`
    （1080×1920 竖屏）。证明 `PinPlacement` + `DisplaySpace` 在负坐标屏幕上正确。
  - **贴右/下边缘自动翻转**：光标 (2530,1410)（主屏右下角附近，剩余空间 30×30 放不下 300×200）→
    贴图落在 `2214,1194 300x200`（翻到光标左上方），`inside=True`，没有被截断。
  - **三种剪贴板格式（场景 `formats`，pid 49344）**：`Bitmap/DIB`（上面）、
    `PNG`（`SetData("PNG", MemoryStream)`）→ 日志 `格式=PNG 尺寸=240x160`、窗口 `916,616 240x160`；
    `FileDrop`（资源管理器复制 PNG 文件）→ 日志 `格式=FileDrop:png 尺寸=240x160`、窗口 `1036,696 240x160`。
    截图 `primary-formats-01-png.png` / `primary-formats-02-filedrop.png`。
- 2026-10-18 **T21 失败路径（真实桌面，主屏）**：
  - **空剪贴板**（场景 `empty`）：`CLIPBOARD CLEARED` 后 Alt+S → 日志
    `从剪贴板贴图失败：状态=NotAnImage ...`（清空后 Win11 仍保留 UnicodeText 格式，属正常）→
    Toast 窗口 `0x11C1A3C 923,623 293x47`，UIA 读到文本 **`剪贴板是空的，请先复制一张图片。`**；
    截图 `artifacts/t20t22/errors/primary-empty-01-toast.png`（可见深色圆角提示在光标右下方）。
    `EXPECT APP STILL RUNNING: True`、`EXPECT 0 PINS: True`（没有新建贴图）。
  - **纯文本剪贴板**（场景 `text`）：`SetText(...)` 后 Alt+S → 日志 `状态=NotAnImage 格式=UnicodeText`；Toast 文本
    **`剪贴板里没有图片（当前格式：UnicodeText）。`**；截图 `primary-text-01-toast.png`；无贴图、进程存活。
  - **剪贴板被占用（COMException）—— 未能在真实桌面复现**：用独立进程 `OpenClipboard` 持锁 25 秒
    （`holdrun.ps1` 确认输出 `HOLDER HOLDS CLIPBOARD`，另有探针在同一时刻 `OpenClipboard` 失败），
    再发 Alt+S，结果**仍读到数据**（`状态=NotAnImage 格式=UnicodeText`）——因为 WPF/OLE 的
    `Clipboard.GetDataObject()` 通过 OLE 快照读取，不走 `OpenClipboard`，所以持锁不构成 COMException。
    因此“剪贴板被占用 → 提示且不崩溃”这条**只有代码路径与单测/日志覆盖，未在真实桌面复现**。
    本轮为它保留了完整实现：`ClipboardImage.TryRead` 对 `COMException` 重试 5 次 × 60ms 后返回 `Busy`，
    提示文案 `剪贴板被其他程序占用，暂时读不到图片，请稍后重试。`，调用方只提示、不抛异常。
- 2026-10-18 **T22 历史联动规则（真实桌面日志核对，`artifacts/t20t22/{toggle,clipboard}/*-log.txt`）**：
  - **外部剪贴板 → 记录一次**：每次 Alt+S 成功都有一条
    `贴图历史联动：来源=外部剪贴板 应记录一次（T24 接真实历史，本轮仅日志占位） 图像=300x200 位置=(916,616) 300x200
    时间=2026-10-18 08:05:54 建议名称=贴图_2026-10-18_080554.png`（PNG 流与 FileDrop 各一条，共 3 条）。
  - **内部截图 → 不记录**：每次截图“贴图”都有一条
    `贴图历史联动：来源=内部截图 不记录（内部截图已由 ICaptureSink 记录 / 历史贴图不重复记录），尺寸=300x220 位置=(360,320) 300x220`，
    同一次操作里只有 T11 的 `输出完成：动作=贴图 ...` 一条，**没有第二条历史**（PLAN：内部操作避免重复记录）。
  - **历史来源（`PinSource.History`）不记录**：本轮无历史图库，`CreateFromHistory` 无调用方，
    该规则的判定已在 Core `PinSourceText.ShouldRecordHistory` 单测覆盖（`History → false`）。
- 2026-10-18 **T20 退出关闭全部贴图（真实桌面，场景 `exit`，pid 49344）**：两张贴图打开时点托盘“退出” →
  日志 `菜单命令: Exit` → `退出：用户选择托盘菜单“退出”` → `退出：关闭全部贴图 2 张` →
  两条 `贴图窗口已关闭：(360,320) 300x220 ...` / `(700,360) 340x260 ...` → 热键注销 → 托盘图标移除 →
  `退出完成 (exit code=0)`；脚本输出 `EXPECT APP EXITED: True`，`Get-Process ZeepCut` 无结果。
  截图 `artifacts/t20t22/errors/primary-exit-01-two-pins.png` 与 `primary-exit-02-after-exit.png`。
- 2026-10-18 **T18/T19 回归（真实桌面，场景 `lock`，同一最终构建）**：锁定后拖动被拒绝
  （`LOCK BEFORE=360,320 640x420 AFTER-DRAG-ATTEMPT=360,320 640x420`）、锁定仍可缩放
  （`→ 347,307 852x559`）、解锁后拖动生效（`→ 447,377 852x559`）、× 关闭 `count=0`；与 T19 记录一致，无回归。
- 2026-10-18 **T18/T19 构建+测试（最终代码）**：`scripts/build.ps1` → `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`；测试 →
  `Passed! - Failed: 0, Passed: 657, Skipped: 0, Total: 657`；末尾 `BUILD+TEST OK`。
  本轮新增 32 个测试（`--filter 'FullyQualifiedName~ZeepCut.Tests.Pins'`）：缩放 1x/2x/0.5x 尺寸跟随、超上限钳到 8x、低于下限钳到 40px、
  2×2 原图最小尺寸优先、NaN/∞/≤0 倍率原样返回、已到限原样返回、锚点在左上/中心/内部/窗口外（夹到最近边）四组、`ZoomBy` 连续 100/200 档停在上下限、

  `ResetToOriginal` 保持中心、透明度 0 钳到 0.2、>1 钳到 1、NaN 保持原值、连续 -0.1/+0.1 停在 0.2/1.0、透明度不影响 Bounds、
  锁定后 `DragTo`/`DragBy` 被拒绝、`TryDragTo` 报告是否移动、锁定仍可缩放/调透明度、`ToggleLock` 幂等、`ClampScaleFor` 双方向取较大者、负坐标副屏。
- 2026-10-18 **T18/T19 主屏 125%（DISPLAY1 0,0 2560×1440 DPI=120，pid 59188，脚本 `scripts/verify-t18t19.ps1`，证据 `artifacts/t18t19/primary/`）**：
  - **基本贴图（`basic`，选区 (360,320) 640×420）**：点工具栏“贴图”后窗口枚举
    `hwnd=0x7D1222 360,320 640x420 exstyle=0x00000088(TOOLWINDOW)` —— 位置尺寸与选区**完全一致**；
    `WS_EX_TOOLWINDOW` 置位（不在任务栏/Alt+Tab 出现按钮），且 `ShowInTaskbar=false`。日志 `贴图窗口：物理=(360,320) 640x420 DPI=120（WPF PixelsPerDip=1.25）`。
    截图 `primary-basic-01-created.png`；放大 `zoom-pin-corner.png` 可见 1px 深灰边框（像素探测：x=360 为 113,113,114，x=361 回到图片内容 216,216,218；右边缘 x=999 为 104,104,105）。
  - **拖动**：从 (680,530) 拖到 (800,620) → 窗口 rect `480,410 640x420`（位移 **+120/+90**，与鼠标完全一致），日志 `拖动结束（左键抬起）` + `拖动结束物理矩形=(480,410) 640x420 与状态一致=True`；截图 `primary-basic-02-dragged.png`。
  - **滚轮缩放（光标锚点）**：锚点 (600,500) 反向滚 2 档 → `1x → 0.909x → 0.826x`、尺寸 `640x420 → 529x347`、位置 `(480,411) → (501,426)`（左上角按锚点比例外移）；
    正向滚 4 档 → `0.826x → 1.331x`、`774x508`。每档日志都记录 `锚点=(600,500)` 与尺寸/位置；截图 `primary-basic-03-zoom-out.png`、`-04-zoom-in.png`。
  - **Ctrl+滚轮透明度（定量）**：从 1.0 连续下调，日志逐条 `1 → 0.9 → … → 0.2`，到 0.2 后继续滚 4 次全部输出 `0.2 → 0.2（下限=0.2）`（**不会变成完全看不见**）；
    同一屏幕区域 (470,410) 300×200 的平均亮度实测：正常 `238.0` → 0.4 时 `142.9` → 到下限 `47.5` → 调回后 `238.0`（截图中贴图明显变淡但内容仍可辨认）。
    证据 `primary-basic-05-opacity-04.png` / `-06-opacity-floor.png` / `-07-opacity-restored.png`。
  - **右键菜单**：五项目均通过 UIA 真实点中（`MENU ITEM '原始大小' 847,650 166x27` 等）：
    - `原始大小`：1.21x `774x508` → 1x `640x420`（窗口 rect `522,436 640x420`），中心点不变。
    - `复制`：独立进程读剪贴板 → **`CLIPBOARD IMAGE 640x420`**（`primary-basic-clipboard.png`）。
    - `另存为…`：真实 Win32 对话框（点击文件名字段 + 剪贴板粘贴 + `BM_CLICK`）→ **`PIN SAVED FILE ... 640x420 3941 bytes`**；`primary-menu-pin-saved.png` 与同次截图的贴图区域（去掉 6px 边框）逐像素比较 **mean 通道差 0.23、62000 采样中仅 106 个 >30 的差异像素**，即贴图就是原图 1:1，没有缩放模糊。
    - `关闭` / `锁定位置` / `解锁位置`：见下。
  - **Esc 关闭**：`SENT {ESC}` 后 `PINS after-esc: count=0`；日志 `贴图窗口关闭（Esc）` → `贴图窗口已关闭` → `贴图已从集合移除：剩余 0 张`；截图 `primary-basic-09-after-esc.png`。
- 2026-10-18 **T18/T19 锁定（主屏 125%，场景 `lock`）**：
  - 右键 `锁定位置` → 日志 `右键“锁定位置”：锁定 False → True，位置=(360,320)`；截图 `primary-lock-02-locked.png`，放大 `zoom-locked-corner.png` 可见橙色边框 + `已锁定 640×420` 徽标。
  - **锁定后拖动被拒绝**：从窗口中心拖 (680,530)→(840,650)，日志 `贴图已锁定，忽略拖动`，窗口 rect 前后都是 `360,320 640x420`（**完全没动**，已用窗口 rect 对比，不是靠肉眼看截图）；`primary-lock-03-drag-rejected.png`。
  - **锁定后仍可缩放**：滚 3 档 → `640x420 → 852x559`（`primary-lock-04-zoom-while-locked.png`），说明锁定只禁用拖动。
  - 右键 `解锁位置` → `锁定 True → False`；再拖 (773,586)→(873,656) → rect `347,307 852x559` → `447,377 852x559`（**位移 +100/+70 生效**）。
  - 右上 × 点击后 `count=0`，日志只有 `贴图窗口关闭（右上 ×）`、没有 Esc 记录；`primary-lock-07-after-close.png`。
- 2026-10-18 **T18/T19 多张并存（主屏 125%，场景 `multi`）**：
  - 依次创建 320×240 @ (360,320) 与 360×260 @ (720,360) → `PINS pin-2: count=2`，两张独立窗口（不同 hwnd、不同 WPF 窗口类 GUID）；截图 `primary-multi-02-second.png`。
  - 拖动第二张 (900,490)→(820,630) → 第二张 `640,500 360x260`（-80/+140），**第一张仍是 `360,320 320x240`（未受影响）**；`primary-multi-03-dragged-second.png`。
  - 点第一张的 × → `PINS after-close-first: count=1`，只剩第二张；`primary-multi-04-closed-first.png`。再 Esc 关掉最后一张 → `count=0`。
- 2026-10-18 **T19 尺寸上下限（主屏 125%，场景 `limits`，原图 200×150）**：
  - 连续反向滚 24 档 → 窗口停在 **`53x40`**（高度正好 40px 下限，宽度按比例 53px，没有再缩小也没有消失）；`primary-limits-02-min.png`。
  - 连续正向滚 60 档 → 窗口停在 **`1600x1200`（= 200×150 × 8）**，日志重复输出 `滚轮缩放已到限制：缩放=8x 尺寸=1600x1200（下限=40px 上限=8x）`；`primary-limits-03-max.png`。
  - 右键 `原始大小` → `200x150`（位置 `539,332`，中心点保持），再点 × 关闭 → `count=0`。
- 2026-10-18 **T18 跨屏拖动与 DPI 重算（主屏 125% → 副屏 100%，场景 `crossscreen`）**：
  - 副屏创建贴图（场景 `basic -Target secondary`）：`hwnd=0xDC12C8 -1000,320 640x420` —— 负坐标原点正确，尺寸 = 选区物理像素；
    拖动 (680,530)→(800,620) 得到 `-880,410 640x420`；滚轮/透明度/菜单/剪贴板（640×420）与主屏表现一致；证据 `artifacts/t18t19/secondary/`（截图尺寸 1080×1920，来源为 `-1080,0` 副屏）。
    副屏贴图边框像素探测：x=80 为 `140,71,71`、x=81 起为图片内容；右边缘 x=719 为 `62,51,50`。
  - 主屏 (120,400) 300×240 的贴图**拖到副屏**：拖动结束 rect **`-380,460 300x240`**（物理尺寸完全不变，只移动 -500/+60）；
    日志 `拖动：贴图所在屏幕 DPI 120 → 96，按新 DPI 重新换算 DIP 尺寸` → `拖动结束物理矩形=(-380,460) 300x240 与状态一致=True`。
    在副屏上再正向滚 2 档 → `363x290`（= 300×240 × 1.21，物理像素仍然正确）；证据 `primary-cross-01/02/03-*.png`。
- 2026-10-18 **退出关闭全部贴图（真实桌面）**：先创建 2 张贴图（`exit-01-two-pins-before-exit.png`），再用托盘菜单“退出”（UIA/坐标点击真实菜单项）→
  日志 `退出：用户选择托盘菜单“退出”` → `退出：关闭全部贴图 2 张` → 两条 `贴图窗口已关闭` → `退出完成 (exit code=0)`，`Get-Process ZeepCut` 无输出（进程结束）。
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
- 2026-10-18 **T16/T17 最终冒烟（提交后重新构建 + 重启，真实桌面）**：重新构建 `0 Warning(s)` / `625 passed`，启动 pid 8624。主屏 125% 七种标注 + 马赛克 → Enter → 独立进程读剪贴板 **`760x560`**，马赛克区域 **140 个块 0 个非均匀**（证据 `artifacts/t16t17/final-smoke/primary-export-clipboard.png`）；副屏 100% 选区 `(-1000,240) 640×480` → 剪贴板 **`700x640`**（脚本里副屏默认 700×640 的选择，与 `-SelW/-SelH` 的组合一致）。提交后行为与提交前一致。
- 2026-10-18 **未验证（T18/T19 相关）**：
  - **全局显示/隐藏（T20）未做**：托盘“显示/隐藏全部贴图”仍是“尚未实现”提示；`PinWindows` 只有 `Create`/`All`/`Count`/`CloseAll`，没有 `HideAll`/`ShowAll`。按本轮范围留给 T20。
  - **剪贴板贴图（T21）未做**：Alt+S 与托盘“从剪贴板贴图”仍是占位日志（`PasteFromClipboardPlaceholder`）。贴图自身的右键“复制”已验证，但“从剪贴板创建贴图”没有入口。
  - **历史联动（T22）未做**：贴图不写历史（T24 才有历史），也没有“从历史贴图”入口。
  - **透明度只在 20%/40%/100% 三个点做了像素级亮度定量**（238.0 → 142.9 → 47.5 → 238.0）；中间档位（0.3/0.5/0.7/0.9）只从日志确认数值，没有逐档截图。
  - **贴图窗口的投影（`CS_DROPSHADOW`）只在主屏截图中确认**：贴图右/下外沿能看到一条比桌面略暗的过渡带，但投影是系统绘制的，没有做像素级基线对比；贴图靠 1px 深色边框与桌面区分这一点是逐像素验证过的。
  - **滚动缩放的锚点在“窗口外”的分支只在 Core 单测覆盖**（`ScaleTo_AnchorOutside_ClampsToNearestEdge`），桌面验证时鼠标一直在窗口内。
  - **缩放上限 8x 下超屏的贴图没有被夹回屏幕**：`limits` 场景把 200×150 放大到 1600×1200 时窗口左上角跑到 `-161,-193`（部分在屏幕外）。这是**有意为之**（缩放以鼠标为锚点，不做屏幕夹取，否则贴图会在放大时被推着走）；若以后要求“贴图不能移出屏幕”，应在 `PinState` 加屏幕夹取并补测试，而不是改 UI。
  - **贴图右键菜单里没有“透明度”项**：PLAN 的右键菜单只要求原始大小/复制/另存为/锁定/关闭，透明度走 Ctrl+滚轮，没有做菜单快捷项。
  - **另存为只验证了 PNG**：贴图另存为沿用了 T11 的编码器（PNG/JPEG 同一入口），本轮只跑 PNG。
  - **多贴图上限未测**：本轮最多同时 3 张（`keep` 场景 2 张 + 遗留 1 张），没有做大数量（10+）的内存/性能测试。
  - **贴图不响应 Alt+F4/系统关闭菜单**：无系统菜单（`WindowStyle=None`），只有 Esc/×/右键关闭；Alt+F4 由 WPF 默认处理，未单独验证。
  - **锁定徽标只覆盖左上角小区域**，贴图很小（40px）时徽标会盖住大半画面（这时 × 按钮会自动隐藏），未做自动收纳。
  - 150% 缩放、三屏以上未测试（本机只有 125% + 100% 双屏）。
  - **验证脚本的两个坑（写进脚本注释）**：①`Find-PinWindows` 返回 1 个元素时 PowerShell 会把数组拆开，所有调用点必须用 `@(...)` 包住再用 `.Count`/`[0]`（本轮踩过：`$pins[0]` 取到的是 PSCustomObject 的属性值 `0x5A`，被当成 hwnd 打印出 `hwnd=0x5A rect?`）；②自定义 C# 辅助类里**不能同时**定义 `public const uint WHEEL` 和 `public static void Wheel(...)`，PowerShell 解析 `[Class]::Wheel` 会失败并报“does not contain a method named 'Wheel'”。
  - **托盘菜单的自动化仍不稳**：`scripts/tray.ps1` 未声明 PerMonitorV2，返回的 icon rect 是虚拟化 DIP（本机 1871,30 对应物理 2339,37）；用真实物理坐标或 `PostMessage(WM_TRAYICON, WM_RBUTTONUP)` 打开菜单后，本轮用“菜单窗口 `#32768` 的 rect 底部 -12px”点击“退出”成功，但这是位置推算而不是 UIA 命中菜单项。
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

- **T23 时间精度必须截到毫秒**：SQLite 文本里只有 `yyyy-MM-ddTHH:mm:ss.fffZ` 三位小数，而 `DateTime`
  有 100ns 精度。`HistoryRecord.NormalizeUtc` 统一截断，否则“写进去的 record 与读回来的 record”
  永远不相等（单测 `NormalizeUtc_TruncatesToMilliseconds_BecauseThatIsWhatSqliteStores` 是这条规则的护栏）。
  改时间格式时必须同步这条规则。
- **T23 数据库版本只升不降**：`HistorySchema.EnsureSchema` 遇到 `user_version` 高于
  `CurrentVersion` 时**不降级、不改数据**，只写 WARN。加列请走 `Migrations`（版本 +1），
  **不要直接改 `CreateTableV1`** —— 老用户的库不会重跑建表语句，改了只会让新老库结构分叉。
- **T23 图片与缩略图必须“用完即关”**：仓储只返回 `byte[]`，App 侧读图一律
  `BitmapCacheOption.OnLoad`。如果以后图库为了滑动性能缓存 `BitmapImage` 或 `FileStream`，
  会重新锁住文件，导致 T27 清理与 T28 覆盖删除失败。**不要**在 `HistoryStore.TryLoad` 之外另开
  读文件的路子；要更高性能就缓存已冻结的 `BitmapSource`（它不再持有文件句柄）。
- **T23 相对路径要校验**：`HistoryPaths.IsSafeImageRelativePath` 只允许 `images/...` 且拒绝
  `..`/盘符/绝对路径。图库读文件前应走 `HistoryPaths.ToAbsolute`，不要自己 `Path.Combine`
  用户可控字符串。
- **T24 历史失败一律吞掉，只提示**：`HistoryCaptureSink` / `HistoryPinRecorder` 里
  `catch (Exception)` 是**有意**的——PLAN 要求“历史缺失不阻塞本地功能”，而遮罩的
  `CompleteCapture` 之后就要 `Close()`。如果让异常冒出去，用户会看到“复制失败”但实际上图已经在剪贴板里。
  真实注入验证过（`images/` 被占）复制仍成功、Toast 明确说“已成功，但历史保存失败”。
- **T24 “一次完成只一条”依赖单点调用**：只有 `CaptureOverlayWindow.CompleteCapture` 调
  `_sink.OnCaptureCompleted`，且遮罩成功路径只走一次；`PinWindows.RecordHistory` 对内部截图
  直接跳过（Core 规则 `PinSourceText.ShouldRecordHistory`）。**新增输出动作时不要另外再写历史**，
  否则会出现一次操作两条记录（本轮专门验证了另存为与贴图各只 +1）。
- **T24 名称去重是“查一遍已有名称再顺延”**：`HistoryCaptureSink`/`HistoryPinRecorder` 调
  `GetAllNames()` + `HistoryName.CreateUniqueName`。同一秒内并发写入理论上可能撞名（单机 UI 单线程，
  实际不会发生）；若要绝对唯一，应在库里给 `Name` 加唯一索引并重试。查名失败时**直接用默认名称继续写**
  （不因为查不到名字而丢记录）。
- **验证脚本的 Esc 坑（T23/T24 新增，重要）**：本机用 `SendKeys '{ESC}'` 或 `SendInput(VK_ESCAPE)`
  **送不到遮罩**（Enter 却能到），表现为“Esc 没反应”。`scripts/verify-t23t24.ps1` 改用
  `PostMessage(WM_KEYDOWN/WM_KEYUP, VK_ESCAPE, lParam 带 scan=0x01)` 直接投递给遮罩 hwnd，
  走的是同一条 WPF `PreviewKeyDown` 路径，结果确定。**下一位写 Esc 相关验证时直接用
  `Send-EscToOverlay`**（脚本里已有），不要再试 SendKeys。另外 `$Target` 是 `ValidateSet` 参数，
  PowerShell 变量名大小写不敏感，**不要把临时变量叫 `$target`**（本轮踩过：赋值直接抛
  `ValidateSetFailure`）。
- **历史库在 WSL 侧核对要先复制**：`history.db` 是 WAL 模式，`python3` 直接在 `/mnt/c/...` 上开库
  会 `disk I/O error`；Windows 侧 PowerShell 读到的视图也不可靠。正确做法：
  `cp history.db history.db-wal history.db-shm /tmp/x/` 再查（本轮全部数据库证据都是这样取的）。

## 未验证（T23/T24 相关）

- **副屏（负坐标 100%）没有跑历史写入**：本轮所有场景都在主屏 125% 完成。负坐标只影响
  **选区裁剪**（T11/T17 已验证），历史写入只用输出图与本地时间，不接触屏幕坐标 ——
  但“副屏截图写出的历史尺寸/时间正确”这条**未实测**。
- **缩略图生成失败的降级路径只有代码与单测**：真实注入缩略图编码失败（例如内存不足）未做，
  `TryCreateThumbnailBytes` 的 `catch → null` 分支没有桌面证据。
- **`HistoryStore.TryLoad` / `TryLoadPreview` 没有 UI 调用方**（图库是 T25），因此
  “`BitmapCacheOption.OnLoad` 读图后文件不被锁”这条只在仓储层的字节路径上验证过
  （`FilesAreNotLockedAfterAddAndRead` + 桌面改名/删除）。等 T25 真的用 `TryLoad` 渲染缩略图时，
  需要再实测一次“图库打开着某条记录、同时删除它的文件”。
- **数据库损坏（`SQLITE_CORRUPT`）与只读目录未注入**：只验证了“库路径不可打开”
  （同名目录 → `SQLite Error 14`）这一种初始化失败。损坏库的恢复策略（备份+重建）尚未实现。
- **多进程并发写历史未验证**：单实例 Mutex 保证只有一个应用进程，但“图库窗口读取时后台写入”
  这种同进程并发也没压测过（WAL + 10s busy_timeout 只是配置层面的保证）。
- **1000+ 条记录的性能未测**：分页与索引已就位，本轮最多 5 条记录。
- **`HistoryQuery.SearchText` 的 `LIKE '%x%'` 用不上索引**：几千条规模无感，T26 若变慢需改 FTS；
  本轮没有做性能基线。
- **`Name` 排序用 `COLLATE NOCASE`**：只对 ASCII 大小写不敏感，中文按 UTF-8 码点排序，
  没有做 ICU/拼音排序（PLAN 未要求）。
- **覆盖编辑（`WithImage` / `UpdateImage`）只有单测，没有 UI 与桌面验证**：那是 T28。


- **T14 决策：选区一旦开始标注就锁定**。`AnnotationSession.IsSelectionLocked` 为真（文档里有标注，或正在拖草稿/输入文字）后，遮罩隐藏 8 个手柄、忽略选区外按下、光标不再显示移动/调整箭头，并在选区右上方显示 `选区已锁定（N 个标注）`。原因：标注坐标是“相对选区图像原点”的，移动/调整选区会让已有标注与画面错位。全部标注被撤销（文档空且无草稿）后自动解锁。
- **T16 决策：选中态是"第三态"，优先于选区操作与 Esc**。`AnnotationSession.ActiveTool == null`（工具栏“选择”）+ 选区已定稿 + 文档非空时，点选区内会进入编辑：先命中选中标注的手柄（半径 10px），再走 `AnnotationDocument.HitTest`（容差 4px）。此时 Esc 的顺序是 **草稿 → 拖拽中取消 → 取消选中 → 取消截图**；有选中时 Esc 只取消选中，遮罩保持打开（已实测）。
- **T16 支持范围（有意收窄）**：矩形/椭圆/马赛克支持八向缩放；箭头/直线支持整体平移 + 拖两端点；**画笔/文字只支持整体平移**（不给缩放，避免点串被拉伸、字形被压扁）。选“选择”以外的工具会自动取消选中（`SetActiveTool` 里做的），所以"想编辑已有标注"必须先点“选择”。
- **T16 一次拖拽 = 一条历史**：拖拽过程中 `DragTo` 直接改标注做实时预览、不写历史；`EndDrag` 发现几何变化时先**回滚到初始几何**，再用 `ResizeAnnotationCommand`/`MoveAnnotationCommand`/`SetSegmentEndpointsCommand` 应用一次最终几何。这样 `Execute`/`Undo` 永远对称，且撤销一次就整段回退（已实测：撤销 3 次依次回退删除/缩放/移动）。改这段时不要图省事在 `DragTo` 里 `History.Execute`。
- **T16 双击是自实现（400ms / 3px）**：`RegisterClick()` 必须在**每一次**按下时调用（第一次也要记时间戳，否则第二次进不了判定窗口 —— 本轮踩过这个坑，第一版只记录文字分支，导致双击永远不成立）。消费后清零避免三击触发两次。
- **T17 导出坐标只有一个原点**：标注坐标是"相对选区左上角"的物理像素（T14 的 `AnnotationCanvasTransform`），而裁剪图左上角也正好是选区左上角，所以导出**不需要**再减 `imageRect.Left/Top`。本轮第一版多减了一次，会在选区原点非 (0,0) 时整体偏移 —— 已在 `AnnotationExportRenderer.Composite` 里去掉并加注释。
- **T17 马赛克按 Z 序处理"当前像素"**：走到马赛克时，先把之前累积的标注合成为位图，再对这份像素做块平均；所以"马赛克盖在矩形/直线上"会把这些标注一起糊掉（与预览一致，已实测直线被平均成粉色块）。如果以后要求"只糊原图、不糊标注"，需要改成两遍合成（先算原图马赛克层、再按 Z 序合成），并补测试。
- **T17 马赛克默认强度改为 1.0**（原 0.75）：0.75 会保留 25% 原像素，密排文字细看仍可辨认，削弱打码效果。块平均本身是"块的算术平均"，不是模糊；强度 <1 就是与原像素的线性混合。需要"轻打码"时显式设小，`AnnotationDefaults.ClampMosaicStrength` 仍钳 0–1。
- **贴图的所有几何规则都在 Core `PinState`，UI 不另算**：缩放倍率、显示尺寸、最小 40px / 最大 8 倍、透明度下限 20%、锁定拒绝拖动、锚点比例，全部在 `src/ZeepCut.Core/Pins/PinState.cs`。`PinWindow` 只做 DIP 换算、`SetWindowPos`、事件接线和视觉。**不要在 `PinWindow` 里写 `Math.Clamp` 之类的新规则**，否则桌面行为与单测会分叉。改规则时先改 `PinState` 并补 `PinStateTests`。
- **贴图的坐标是真·物理像素，位置尺寸都按“当前所在屏幕”的 DPI 换算**：窗口物理矩形由 `PinState.Bounds` 决定，`DpiTransform(当前屏 DPI)` 换算成 WPF DIP；每次拖动/缩放后用 `GetWindowRect` + `SetWindowPos` 复核。**拖动跨屏时必须重新取 DPI**（`MonitorFromWindow` → `ScreenCapture.TryGetMonitorDpi`），否则从 125% 屏拖到 100% 屏会整体放大 25%。已在桌面实测 120→96 后物理尺寸不变。
- **贴图滚轮缩放的锚点是鼠标位置，且不把贴图夹回屏幕**：这是 T18 的明确决策（另一种做法是以窗口中心为锚，无法“放大鼠标下的细节”）。副作用是放大到很大时窗口可以伸出屏幕（实测 8x 时左上角到了 `-161,-193`），这是有意的 —— 夹取会让贴图在放大过程中被推着走。`ResetToOriginal` 反过来以**中心点**为锚，避免“原始大小”把贴图甩走。
- **贴图锁定只禁止拖动，不禁用其它一切**：PLAN 要求“锁定贴图仍响应鼠标，通过右键解锁，不穿透鼠标”。所以锁定后滚轮缩放、Ctrl+滚轮透明度、右键菜单全部照常，只有 `DragTo`/`DragBy` 返回原状态。视觉上用橙色边框 + `已锁定 W×H` 徽标提示，光标从 `SizeAll` 变回 `Arrow`。
- **贴图窗用 `CS_DROPSHADOW` + 1 DIP 边框区分桌面**：不启用 `AllowsTransparency`（会让内容变成分层窗口，1:1 像素显示与性能都更麻烦），也不自绘阴影。窗口类样式里加 `CS_DROPSHADOW` 拿系统投影，内容层再叠一个 1 DIP 深色边框（实测像素：边缘 113,113,114，紧邻内容 216,216,218）。
- **贴图窗口标题带 pid**（`Zeep Cut 贴图 {pid}`）：验证脚本靠标题前缀区分遮罩（`Zeep Cut 截图遮罩`）与贴图；不带 pid 时按前缀匹配会把两者混在一起。另外 `Find-PinWindows` 必须过滤掉 `GetWindowRect` 失败/零尺寸的窗口（WPF 还有若干同名隐藏辅助窗口）。
- **`PinWindows` 是贴图唯一入口**：`CaptureOverlayWindow.TriggerPin` 走 `PinWindows.Create`，退出走 `PinWindows.CloseAll`，窗口 `Closed` 里回调 `NotifyClosed` 自动注销。T20/T21 不要再 `new PinWindow(...)`，否则集合会漏登记，全局隐藏/关闭会漏掉那张。
- **另存为对话框必须临时取消 Topmost**（贴图与遮罩同一个坑）：贴图本身是 `Topmost`，直接 `ShowDialog(this)` 会让 Win32 文件对话框自行返回。`PinWindow.SaveAs` 里 `Topmost=false` → `ShowDialog` → `finally` 恢复并 `Activate()`。
- **贴图很小（<66px）时自动隐藏右上 × 按钮**：22 DIP 的按钮在 40px 贴图上会盖住大半内容。Esc 与右键“关闭”仍然可用，所以不会出现关不掉的小贴图。
- **T17 预览马赛克与导出共用同一算法**：`AnnotationLayer` 拿冻结图 + 选区裁剪矩形，按 `MosaicPixelator` 算出区域的像素化位图并缓存（键 = 可见区域 + 块大小 + 强度），再由 `AnnotationRenderer` 按 1:1 铺上去。也就是说预览不再是"半透明棋盘示意"，看到什么导出就是什么。缓存不会自动失效 —— 选区变化会重建 `AnnotationLayer`，但**同一选区内修改块大小/强度**目前只能靠 `InvalidateMosaicCache()`，UI 还没有调整入口（T16 未做属性编辑）。
- **T17 导出 DPI 做法**：`RenderTargetBitmap` 的像素尺寸直接取选区物理像素，dpi 参数用目标屏 DPI，绘制上下文压 `ScaleTransform(96/dpi)`。**不要**用 DIP 尺寸建 RenderTargetBitmap 再乘缩放，125% 下会得到 80% 的像素数（本轮验收点就是"导出尺寸 = 选区物理像素"，已在 125%/100% 各实测一次）。
- **验证马赛克必须有高频纹理**：桌面壁纸/纯色背景上做马赛克，像素化与"半透明方块"肉眼无法区分（本轮前两次就落在纯色区，看不出效果）。`scripts/texture-window.ps1` 专门生成 12px 彩色块 + 密集文字，做马赛克验证时先起这个窗口。注意它是 WinForms（验证脚本允许），且 Windows 在 125% 下会把 WinForms 尺寸按 DIP 缩，必须用 `SetWindowPos` 强制物理矩形。
- **视觉层不只选区白框会漏进导出**：导出只画"裁剪图 + 标注"，`_dim`/`_border`/`_handles`/`_sizeBadge`/`_toolbar`/`_editorCanvas`/`_lockBadge` 都是遮罩窗口的兄弟元素，不参与 `AnnotationExportRenderer`。已用"导出图 vs 遮罩出现前的全屏截图"逐像素比对确认（底部条带 0/6480 差异 >12）。
- **选了绘制工具后就禁止拖动改选区**（即使还没落笔），必须先点工具栏“选择”才能重新框选/调整。这是为了避免“想画一笔却把选区挪走”。代价是切换多一次点击；如果以后觉得碍事，可以改成“只有已落笔才禁止改选区”，但要同时处理“选区变了已有标注怎么跟着变”。
- **标注层是选区大小的独立元素**（`AnnotationHost` 内有 `AnnotationLayer`），绘制在 `OnRender` 里压入 `ScaleTransform(96/dpi)`，所以 125% 屏幕上 100 物理像素 = 80 DIP；这与 T11 的裁剪（纯物理像素）是两套坐标，改渲染时不要混。
- **文字输入框的 Enter 拦截有两层**：遮罩 `PreviewKeyDown`（先收到）和 `TextBox.PreviewKeyDown`（兜底），都是为了满足 PLAN 的“文字输入时 Enter 优先用于文本编辑”。两层都只处理 `Key.Enter`，Shift+Enter 不拦截（留给换行）。改键盘处理时两层要一起看。
- **验证脚本的输入法坑**：本机 `SendKeys` 会吃掉空格和数字（`Zeep Cut T15` 曾变成 `ZeepCutT5`），所以 `verify-t14t15.ps1` 改用 `SendInput` + `KEYEVENTF_UNICODE` 逐字输入，并且 `INPUT` 结构体必须 `Size = 40`（x64）否则 `SendInput` 静默失败。另外 `SetCursorPos` 在遮罩激活的瞬间可能被丢弃，脚本会在每次移动后用 `GetCursorPos` 回读校验并重试。
- 工具栏现在有两行（约 723×71 物理像素，副屏 100% 下约 723×71 DIP），比 T10 的单行更宽更高；`PlaceToolbar` 仍只做“下方优先/上方回退/水平夹取”，极小选区下两行工具栏可能盖住选区内容（未做自动收纳/折叠）。
- **T20 全局隐藏只折叠窗口，禁止改成 `Close()` + 重建**：`PinWindows.HideAll` 把快照（`PinState` + DPI）存进 `HiddenSnapshots` 再 `Visibility=Collapsed`；`ShowAll` 用同一份快照恢复。重建窗口会丢掉用户调好的缩放/透明度/锁定状态，也会换 hwnd，且会闪一下。改这里时注意 `NotifyClosed` 与 `CloseAll` 都要清 `HiddenSnapshots`（已做），否则字典会留下已销毁窗口。
- **T20 的“有可见就全隐”是 Core 里的规则，不要挪到 UI**：`PinCollectionState.DecideBulkAction()` 决定混合状态先隐藏。`PinWindows.ToggleAll()` 只是执行它。如果以后要改成“有隐藏就全显”，改 Core 并补 `PinCollectionStateTests`（现有测试会立刻失败，这是有意设计的回归保护）。
- **T21 剪贴板读取必须处理“读取期间用户又复制了别的东西”**：现在每次 `TryRead` 都是一次独立的 `Clipboard.GetImage()/GetData()` 序列，中间可能抛出 `COMException`（重试覆盖）。**不要**把读到的 `DataObject` 缓存起来供后续多次使用，剪贴板内容随时会变。
- **T21 剪贴板图片一定要复制成独立位图**（详见决策段）：`ClipboardImage.ToIndependentBitmap` 负责 `FormatConvertedBitmap → WriteableBitmap → Freeze`。直接 `Clipboard.SetImage`/`GetImage` 的返回值交给 `PinWindow` 长期持有，会在设置剪贴板那个进程退出后变成黑图。
- **“剪贴板被占用”的真实复现方式未找到**：`OpenClipboard` 持锁**不会**让 WPF/OLE 的 `Clipboard.GetDataObject()` 失败（OLE 走自己的快照通道），本轮实测持锁 25 秒期间 Alt+S 仍能读到数据。想真实验证 `ClipboardImageStatus.Busy` 分支，可能需要一个真正长期占住 OLE 的进程（例如另一个 WPF/WinForms 程序在 `Clipboard.GetImage()` 期间挂起），本轮未做到。**代码路径已实现且不会崩溃**（`COMException` → 重试 5 次 → 返回 `Busy` → Toast 提示），但“真实桌面验证”这一条标注为**未验证**。
- **T20/T21 的提示用自绘 Toast，不要换回托盘气球**：T5 已实测本机 `NIF_INFO` 通知被系统抑制（截图看不到），`Shell_NotifyIcon` 却返回成功。Toast 是唯一能截图证明“用户确实看到了提示”的通道，验证脚本也是按窗口标题 `Zeep Cut 提示 {pid}` + UIA Text 来找它。托盘通知仍并行发一份，但不要指望它能被看见。
- **托盘菜单的自动化只能靠“文字 band + 固定顺序”**：Win32 `#32768` 弹出菜单**不暴露任何 UIA MenuItem**（`FindAll` 返回 0），且 Win11 把图标放进溢出面板（`Shell_NotifyIconGetRect` 给的 rect 与实际位置不符）。`scripts/tray-menu.ps1` 的做法是：UIA 点 `Show Hidden Icons`（panel 是 toggle，失败会重试 3 次）→ 桌面里按名字找 `Zeep Cut` → 右键 → 扫菜单位图文字行 → 点第 N 行。**菜单项顺序变更必须同步更新 `tray-menu.ps1` 的注释与调用方的 `-Index`**。
- **本机日志核对必须从 WSL 做**：`powershell.exe` 读 `%LOCALAPPDATA%\ZeepCut\logs\app-YYYYMMDD.log` 会拿到一个**陈旧的 907 字节视图**（同一路径在 WSL 侧已是 1.1MB），所以验证脚本里的日志检查全部改成打印 `grep` 提示，真正核对由 WSL 侧执行。写验证脚本时不要再用 PowerShell 读日志判断成功与否。

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
- T18/T19 贴图的缩放/透明度/锁定/锚点规则全部下沉到 Core `PinState`（32 个单测），`PinWindow` 只做 DIP 换算、Win32 摆放与事件接线。理由：这些规则是"贴图手感"的核心，靠桌面截图对比很难回归；放 Core 后改一条上限/下限就有测试兜住。
- T18 选定**鼠标位置为缩放锚点**（不是窗口中心）：滚轮时"鼠标下的像素不动"更符合贴图的使用方式（放大看细节）。右键"原始大小"是唯一以**中心点**为锚的操作，避免恢复尺寸时贴图跳走。缩放**不做屏幕夹取**（放大到 8x 允许伸出屏幕）。
- T19 锁定语义按 PLAN 收窄成"只禁止拖动"：滚轮缩放、Ctrl+滚轮透明度、右键菜单在锁定下全部可用（已在桌面逐项实测）；锁定状态用橙色边框 + `已锁定 W×H` 徽标表达，光标从 `SizeAll` 变 `Arrow`。
- T18 贴图集合 `PinWindows` 是唯一注册表（`Create`/`All`/`Count`/`CloseAll`/`NotifyClosed`），托盘退出与 `OnExit` 都调 `CloseAll()`（PLAN：退出不恢复贴图）。T20 的全局隐藏/显示、T21 的剪贴板贴图、T22 的历史联动都从这里取实例。
- T18 贴图窗口不启用 `AllowsTransparency`，靠 `CS_DROPSHADOW`（系统投影）+ 1 DIP 深色边框与桌面区分；这样内容仍是 1:1 物理像素直出，不受分层窗口影响。
- T18 验证脚本 `scripts/verify-t18t19.ps1` 用 `Find-PinWindows`（标题前缀 + 可见 + 非零矩形 + 按屏幕位置排序）识别贴图窗口，右键菜单项通过 UIA 按 `Name` 命中（原始大小/复制/另存为…/锁定位置/解锁位置/关闭），不硬编码菜单坐标。
- T20 全局隐藏**只折叠窗口**（`Visibility=Collapsed`），不 `Close()`、不从集合移除、不重建图片：隐藏前用
  `PinWindow.CaptureSnapshot()` 记下 `PinState` + 当时屏幕 DPI，显示时原样恢复再走一遍 `ApplyStateToWindow` +
  `CorrectPhysicalBounds`。这样"隐藏再显示"的 hwnd/位置/尺寸/缩放/透明度全部不变（已像素级实测），
  也不会因为重建窗口而闪一下或丢掉用户调好的缩放。
- T20 的"有可见就全隐、全隐就全显"语义下沉到 Core `PinCollectionState.DecideBulkAction()`，而不是写在
  `PinWindows` 或托盘代码里：混合状态的取舍（隐藏优先）是最容易被后续改动破坏的规则，放 Core 才有单测兜住。
  `HideAll`/`ShowAll` 在已是目标状态时**原样返回同一实例**，UI 据 `PinBulkAction` 决定是否提示"没有可隐藏的贴图"。
- T20 托盘菜单项**文案随状态变化**（`隐藏全部贴图 (2)` / `显示全部贴图 (2)`）且**无贴图时 `MF_GRAYED` 灰显**：
  灰显让"点了没反应"不再发生，文案里的数字让用户知道会影响几张。两者都由 `App` 提供的 provider 委托在
  每次弹菜单时求值（`TrayIcon` 不依赖 `PinWindows`，保持 Platform 层干净）。
- T21 剪贴板读取顺序固定为 **Bitmap/DIB → PNG 流 → FileDrop**：DIB 是截屏工具/画图/浏览器"复制图片"的主流格式；
  PNG 流常见于浏览器与 Win11 截图工具；FileDrop 覆盖"在资源管理器里复制一个图片文件"。三者都失败时才去
  `GetFormats` 区分"空"与"非图片"，以便给出准确提示（实测得到 `剪贴板是空的…` 与 `剪贴板里没有图片（当前格式：UnicodeText）`）。
- T21 读到的位图**必须复制成独立 `WriteableBitmap` 再 `Freeze`**：剪贴板 DIB 的内存属于设置剪贴板的进程，
  直接长期持有 `Clipboard.GetImage()` 的 `BitmapSource` 在对方释放后会变黑（与 T6 的 GDI 位图同一类陷阱）。
- T21 摆放（`PinPlacement`）默认**光标右下 +16px**，右/下放不下时**分别翻到左侧/上方**（两轴独立），最后 `Clamp` 进屏幕：
  比"永远贴左上角"更接近用户预期，也比"只夹取"少了半张贴图在屏幕外的观感问题。贴图比屏幕还大时按屏幕缩小（与 T18 的 8x 缩放允许出屏一致）。
- T20/T21 的用户反馈用**自绘 Toast（`ToastWindow`）而不是托盘气球**：T5 已实测本机 `Shell_NotifyIcon(NIF_INFO)`
  返回成功但通知横幅被系统抑制、截图里看不到；"剪贴板没有图片"这类反馈必须让用户真看见，所以走一个
  2.2 秒自动消失的无边框提示窗口（`WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`，不抢焦点、不挡鼠标）。
  托盘通知仍然并行发送一份（双通道，任一可见即可），并同时写日志。
- T22 记录规则**下沉到 Core `PinSourceText.ShouldRecordHistory`**（只有 `Clipboard` 为 true），而不是在
  `LoggingPinHistoryRecorder` 里写 if：T24 换成 SQLite 实现后仍要遵守同一套规则，规则留在 Core 才有单测
  （`PinSourceTests`）兜住"内部截图不重复记、历史贴图不重复记"。
- T22 的 `IPinHistoryRecorder` 在 `PinWindows` 里用 `SetHistoryRecorder` 注入（默认 `LoggingPinHistoryRecorder`），
  `RecordHistory` 外层 try/catch：历史写失败只记 ERROR，**绝不影响贴图本身**（PLAN：历史缺失不阻塞本地功能）。
- **本机验证环境坑（务必注意）**：
  1. `powershell.exe` 进程**看不到应用新写入的日志内容**（读到的 `app-YYYYMMDD.log` 只有 907 字节，
     而 WSL 侧同一路径已有 1.1MB）。因此日志核对**必须从 WSL 侧 grep**（`/mnt/c/Users/.../logs/app-*.log`），
     验证脚本里的 `Show-LogHint` 只打印一条提示命令，不再自己读日志（否则永远 0 命中）。
  2. Win11 托盘图标在“显示隐藏的图标”溢出面板里，`Shell_NotifyIconGetRect` 给的 rect（1851,10）与真实图标
     位置（1747,81）不一致，**直接点 rect 会点到别的应用图标**。已新增 `scripts/tray-menu.ps1`：
     UIA 点 `Show Hidden Icons`（名字会变成 `Show Hidden Icons Hide`，按前缀匹配，且面板是 toggle 的所以失败会重试）→
     在整个桌面里按名字找 `Zeep Cut` 图标 → 右键 → 扫 `#32768` 菜单位图里的文字行（band）→ 点第 N 行。
     **Win32 弹出菜单不给 UIA 暴露任何 MenuItem**（`FindAll` 返回 0），所以只能用文字 band + 固定顺序
     （0 截图 / 1 从剪贴板贴图 / 2 显示隐藏全部贴图 / 3 历史 / 4 设置 / 5 检查更新 / 6 退出）。
  3. `Start-Process` 在本机不可用（`NO_PROXY` 重复键）；且 `Win32_Process.Create` 用 `>` 重定向需要
     `cmd.exe /c` 包一层。后台持锁进程要用这两条组合才能拿到输出。
  4. 其他截图/录屏工具（实测 QQ 的 `QQScreenCapture`）会**抢走 Alt+A**，桌面被别的遮罩盖住时 Zeep Cut 的热键
     完全不触发（日志无“热键触发”）。验证前先确认桌面干净；`Alt`/`Ctrl` 被 `keybd_event` 卡住也会导致热键失效
     （表现为托盘图标与热键全无响应），用 `GetAsyncKeyState` 查并补发 key-up 修掉。
