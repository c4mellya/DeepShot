# DeepShot UI “自制感”诊断

## 1. 一句话结论

问题不在“不够好看”，而在三处断层：
- **设计系统停在 Token 文件里**，没有落到控件层。
- **没有跟 Windows 平台对齐**，用户熟悉的系统行为缺失。
- **反馈与文案是写给开发者看的**：操作没有回应，界面直接露出注册表、日志、ARGB、硬编码中文。

再加上动漫插画图标、两个以别家公司命名的主题，用户第一眼看到的东西就带着“个人项目”的标签。

---

## 2. 根因

### 根因 A：Token 定义了却没人用，控件层各写各的

DesignTokens 写着“不再用 12.5/13.5 这种差不多的数”，但用户看到最多的那层控件没有遵守。

- **全局控件样式是硬编码 XAML 字符串**：`Ui/UiStyles.cs:44` 是普通 `const string`，圆角 11/8/10/6/12、Padding `14,6`/`16,7`/`10,7`、时长 0.08~0.24s 全是字面量。`Capture/OverlayChrome.cs:94-115` 已经用 `$$"""` 注入 `{{Fast}}`，说明正确做法在代码库里已经有了。
- **状态层 Token 基本没人用**：`AccentHover`、`PressedBackground`、`FocusRing` 没有使用方，`HoverBackground` 只在 `SettingsPage.cs:943` 用了一次。各控件 hover 自己定透明度：Button 0.07（`UiStyles.cs:114`）、ComboBoxItem 0.06（l.363）、ComboBox 0.03（l.399）、Caption 0.09（l.673）。MenuItem 却用 AccentSoft（l.614），两种菜单行为不一样。设置导航的选中态（F4F4F4）比 hover（约 EDEDED）还浅。
- **控件高度有两套，外加 ±n**：`DesignTokens.cs:72-82` 同时有 28/34/40 和 28/32/36。具体位置：
  - `UiKit.cs:322` Compact+2
  - `SegmentedControl.cs:71` PageControl+4
  - `SettingsPage.cs:55` 私有 `const 32`
  - `HistoryQuickViewWindow.cs:228` Large-2

  图库顶栏里搜索框 32、Tab 条 36、图标按钮 32 放在同一行，高度对不齐。
- **字号偏离阶梯**：
  - `DateJumpPopup.cs:230/192`、`DateIndexRail.cs:152` 用 10px，后者紧挨着写着“11px”的注释。
  - `UiKit.cs:814/1145` 用 11.5，`UiKit.cs:324/327` 用 12.5。
  - `Tokens.Text.Eyebrow=11` 一次都没被引用。
- **设置页自建排版**：`SettingsPage.NewText` 绕过 `TextRole`。页标题 15（l.131）只比行标题 13（l.842）大 2px，层级几乎看不出来。`GroupLabel`（l.727-766）把每个字拆成一个 TextBlock 来模拟字距：Inter 的字偶距被破坏，中文也被加了字距，代码和注释（11/16、三级色）也对不上。
- **阴影没有 Token 化，还破坏了自己定的规则**：`DateJumpPopup.cs:102-108` 和 `DateIndexRail.cs:63-69` 把 DropShadowEffect 直接挂在内容的祖先上，整棵子树被位图化，ClearType 丢失、文字发虚。项目里明明已有 `UiKit.ShadowPlate`。各处阴影参数也各不相同（`UiStyles.cs:428/556/591`、`UiKit.cs:932`）。
- **同一组件有多个版本**：
  - 复选框两种：`HistoryCard.cs:101-116` 是 16px/r4，`UiKit.cs:423-435` 是 22px/r6，而且同屏出现在图库里。
  - 焦点环固定 `CornerRadius=11`（`UiStyles.cs:54`），不跟随控件圆角。
  - 禁用态透明度有 0.45 和 0.5 两种。
  - 捕获层另有一套白色 alpha 玻璃色阶，加上 3 个跟 AppIcons 画法不同的图标（`OverlayChrome.cs:45-72`）。

### 根因 B：视觉语言是“借来的 + 网页模板式”，缺少自己的克制

- **应用图标是动漫插画**：`Assets/app.ico` 的 16/32px 帧是 Q 版女孩贴纸缩小后的蓝白色块。它出现在标题栏（`AppChrome.cs:171`）、托盘、任务栏、Toast（`ToastWindow.cs:67`）、安装器。这是“个人项目感”最强的单一信号。
- **主题直接叫“DeepSeek / Claude”，分组叫“品牌”**：`SettingsPage.cs:169-170`、`Strings.en.xaml:337-340`、`zh-Hans:340-347`。没有自己的身份，还有商标风险。
- **强调色用得过多且带特效**：
  - 主按钮下有模糊光晕（`UiStyles.cs:83-90`，Blur 14，opacity 0.42 到 0.7）。
  - 按下时缩放 0.97，再用 BackEase 回弹（l.136-158）。这套继承到所有 Ghost 按钮，包括整行的设置导航和日历单元格。
  - 开关拇指也有 BackEase（l.262）。
  - SegmentedControl 选中项是实心强调色胶囊（`SegmentedControl.cs:27/55`）。
  - TextBox、ComboBox、开关的 hover 边框都变成强调色。
  - `UiKit.BrandMark`、`HeaderWash` 还有强调色光晕和渐变。
- **处处 Hand 光标**：Button/CheckBox/ComboBoxItem/ComboBox/MenuItem 全部设了 `Cursor=Hand`（`UiStyles.cs:71/214/341/378/609`）。这是网页习惯，Win11 只在超链接上用手形。
- **配色对比度与层级**：
  - Claude 浅色主题：强调色 D97757 配白字约 3.1:1（`ThemePalettes.cs:33`）。三级文字 91888A 约 3.45:1，却用于所有 12px 说明文字。控件边框 E9E9E9 对白约 1.2:1，输入框和开关几乎看不见。
  - ClaudeDark 的 Danger 按钮是白字压浅珊瑚色（约 2.8:1），和 `UiStyles.cs:185` 的注释正好相反。
  - 窗口与卡片同为白色，只靠 F3F3F3 发丝线分隔。DeepSeekLight（默认主题）同样如此。
  - 中性色冷暖混杂：表面是纯灰，文字偏紫灰。
- **中文 + Inter 混排**：`AppFonts.cs:45` 依赖逐字回退到雅黑。雅黑没有 600 字重，SemiBold 下中文显示为 Bold 或合成粗体，与 Inter 600 的拉丁字母字重、字面大小都不匹配。全局没有设置 Language，行高也是各处手填。

### 根因 C：没对齐 Windows 平台，系统行为缺失

- **标题栏**（`AppChrome.cs:154-205`）：
  - 自绘的最大化按钮让 Win11 的 **Snap Layouts 失效**，代码里没有 `WM_NCHITTEST → HTMAXBUTTON`。
  - 窗口失去焦点时标题不变暗。
  - 无障碍名称硬编码为“最小化/最大化/关闭”，英文界面也读中文。
  - 标题栏高 40，不是标准值（32 或 48）。
- **托盘菜单**（`TrayIcon.cs:323-414`）：原生 Win32 菜单，深色模式下仍是浅色；没有 `\t` 快捷键提示，没有默认项，没有图标。托盘 tooltip 固定为 “DeepShot”。
- **贴图窗口**（`PinWindow.cs:261-270`）：用的是 XP 时代的 `CS_DROPSHADOW`（`GetWindowLong(GCL_STYLE)` 的读取方式本身也有误）。没有 DWM 阴影，没有焦点态边框，关闭按钮常驻盖在图片上。
- **窗口管理**：
  - 窗口尺寸和位置从不记忆（`HistoryGalleryWindow.xaml.cs:167-171`、`SettingsWindow.cs:73-77`）。
  - 图库 `MinWidth=940` 等于默认宽度，笔记本上无法左右半屏 Snap。
  - 第二次启动只弹窗让用户“去找隐藏图标”（`App.xaml.cs:679`），不会激活已运行的实例。
- **材质与安装**：没有 Mica。安装器是英文向导 + 默认 Inno 美术图 + 无签名 + 没有发布者 URL（`installer/DeepShot.iss`）。

### 根因 D：反馈闭环缺失：操作没有回音，状态没有过渡

- **图库所有操作结果只写日志**：`RunAction` 的注释说“在状态栏显示结果”，实际调用的 `LogResult` 只有 `AppLog.Info`（`HistoryGalleryWindow.xaml.cs:1190-1206, 1727-1733`）。复制成功、所有失败（剪贴板被占用、保存失败、文件丢失）都不提示，而 App 其他地方是有 Toast 的。
- **截图主流程**：普通复制和另存为后覆盖层直接消失（`CaptureOverlayWindow.cs:2116`），只有快速复制会弹 Toast。
- **Toast 只有一种**：
  - 不区分成功和错误（`ToastWindow.cs:30/67/120`），固定 2.2s，关闭没有淡出，多条会重叠。
  - `PinWindow.cs:718` 直接显示 `ex.Message`。
  - 检查更新用这个 2.2s Toast 充当 loading，但请求可能持续 10s。
  - 全项目没有 ProgressRing 或骨架屏。
- **删除**：确认弹窗之后仍是永久删除（`HistoryRepository.cs:882` 用 `File.Delete`），没有撤销，也不进回收站。
- **刷新**：收藏、改名、删除都会触发全量 `Reload`，并且无条件 `ScrollToTop`（l.720）。翻到 200 张之后点个收藏就被弹回顶部。
- **设置页**：每次切换都在页脚显示一条常驻的绿色“设置已保存”（`SettingsPage.cs:980/994`），而旁边就写着“更改自动保存”。
- **细节**：
  - 日期弹窗的空月份被双重变暗（约 0.2），它的“无记录” tooltip 永远不会出现（`DateJumpPopup.cs:165-190`）。
  - Segmented 控件用交叉淡入，而不是滑动指示器。
  - 滚动是原生逐格跳动，滚动条常驻 12px。
  - hover 有的带动画、有的直接跳变。

### 根因 E：文案是开发者口吻，i18n 只做了一半

- **英文界面漏出中文**：
  - `CaptureOverlayWindow.cs:1230/1284/1413/1433/1900-2020`
  - `HistoryActions.cs:107/140/192/211`，其中“已保存到 {path}”在正常成功时也会出现
  - `ClipboardImage.cs` 全部失败消息，同时出现在 Toast 和托盘气泡
  - `HistoryEditWindow.cs:442-505` 的无障碍名称
  - 默认文件名常量 `截图`（`CaptureFileName.cs:22`），每张卡片标题都是“截图_2026-10-27_143012”
  - 与此同时，`S.Message.ClipboardEmpty/NotImage/Failed` 这些现成的键没有被使用。
- **把实现细节露给用户**：
  - 开机启动行的副标题是 `HKCU\...\Run`（`Strings.en.xaml:286`），切换后提示“Registry startup entry written.”
  - 关于页显示原始 GitHub API URL（`SettingsPage.cs:1464-1467`）。
  - 错误文案里有 “Win32 error {1}”、“BitBlt 失败”，还有约 8 处“see log”，但界面上没有打开日志的入口。
  - 颜色 tooltip 显示 `#FFFF3B30`（`CaptureOverlayWindow.cs:2291`、`HistoryEditWindow.cs:434`）。
- **用词不统一**：
  - 同一个东西有多个名字：library / history / gallery / records / items / captures。中文有 历史/历史库/图库/最近，量词有 条/张/项，改名/重命名并存。
  - 英文复数偷懒：`item(s)`、`1 records`（l.107/116/195-198/223/233/243/368/369）。
  - 保留期可配置，提示却写死“超过 30 天”（`Strings.en.xaml:161`）。
  - 时间一律 `yyyy-MM-dd HH:mm:ss`，带秒、不跟随区域设置（`HistoryCard.cs:563-571`）。
  - 说明文字讲的是实现规则：“按 Enter 或离开输入框保存”、“若被占用则回滚”。
  - RELEASE_NOTES 写的是 `FitControlHeight`、wght=400/600 这类工程细节。

### 根因 F：核心截图体验缺少“专业工具手感”

- **没有放大镜、坐标和取色**（`CaptureOverlayWindow.cs:209-237`，只有暗化层 + 矩形 + 手柄）。Snipaste、PixPin 首屏最有辨识度的就是这个。
- **一次 Esc 或右键就丢掉整个截图和所有标注**：`OnPreviewKeyDown` 在判断文字编辑状态之前就 `Cancel`（l.515-525）；`CaptureInputGuard` 在系统层也会取消；右键直接取消（l.326-335）。
- **贴图透明度失效**：`AllowsTransparency=false` 却设置 `Window.Opacity`（`PinWindow.cs:111/386`），结果是变暗变灰，不是半透明。这是贴图的招牌功能。
- **标注**：
  - 粗细和字号是物理像素（`AnnotationTool.cs:49`），200% 缩放下线条和文字只有一半大。
  - 文字编辑不是所见即所得：输入时在暗色框里写白字，提交后才变成所选颜色，位置还偏移约 7-8px（l.2404-2418）。
  - 选区画了标注后被锁定，还用“选区已锁定（N 个标注）”徽章把内部限制展示给用户。
  - 改颜色和粗细只对之后的新标注生效，已选中的标注不变（l.2347-2360）。
- **工具栏与快捷键**：
  - 工具栏两行约 620 DIP，颜色和粗细常驻显示；放不下时直接盖在选区上（`SelectionOverlayLayout.cs:84`）。
  - 快捷键只有 Esc/Enter/Ctrl+Z/Y/Del：没有工具字母键、方向键微调、Ctrl+C/S。撤销栈为空时按钮不置灰，而是弹出一句状态提示。
  - 箭头是细楔形头 + 圆头等粗杆（`AnnotationRenderer.cs:104-105`）。

---

## 3. 优先级修复清单

### P0：改动小、感知收益最大（建议先做这一批）

| # | 改什么 | 在哪 | 预期效果 |
|---|---|---|---|
| 1 | Esc/右键分层回退：先退出文字编辑 → 再取消选中标注 → 再回到选择工具 → 再清空选区 → 最后才退出。有标注时需要二次确认或在 1s 内再按一次 | `CaptureOverlayWindow.cs:515-525, 326-335`，`CaptureInputGuard` | 不再误毁标注，这是最伤信任的问题 |
| 2 | 贴图透明度改用 `WS_EX_LAYERED` + `SetLayeredWindowAttributes(LWA_ALPHA)`，`Window.Opacity` 保持 1，调节时显示一个“60%”浮层 | `PinWindow.cs:111, 386, OnSourceInitialized` | 招牌功能从“坏的”变成能用的 |
| 3 | 图库 `LogResult` 接上窗口内的 Toast/InfoBar（成功/错误两种类型），复制按钮闪一下 ✓；普通复制、另存为也加反馈，另存为附带“在文件夹中显示” | `HistoryGalleryWindow.xaml.cs:1727`、`CaptureOverlayWindow.cs:2116` | 消除“点了没反应”这个最强的自制感信号 |
| 4 | 主题改中性命名（如 Cool/Warm、Ocean/Clay），分组改叫“强调色”，删掉提示文案 | `Strings.*.xaml:337-347`、`SettingsPage.cs:169` | 去掉借来的身份，也消除商标风险 |
| 5 | 去掉“网页模板”特效：删除主按钮光晕、按下缩放和 BackEase；Segmented 选中项改为中性的 Surface 抬起块；hover 边框改中性色；删除所有 `Cursor=Hand`（真正的超链接除外） | `UiStyles.cs:71-158, 214, 262, 341, 378, 609`、`SegmentedControl.cs:27/55` | 一次改动影响所有按钮，立刻更接近 Fluent 的克制感 |
| 6 | 文案清理第一轮：删除注册表路径和“registry entry written”；关于页不显示 API URL；不再显示 `ex.Message`；“see log”旁加“打开日志文件夹”；设置页去掉常驻“已保存”；颜色 tooltip 改为颜色名 | `Strings.*.xaml:286-303`、`SettingsPage.cs:980/1464`、`CaptureOverlayWindow.cs:2291` | 不再像 debug 构建 |
| 7 | 所有硬编码中文改走 StringsUi；默认文件名前缀本地化（Screenshot/截图）；加一个单测扫描 `src/DeepShot` 中非日志字符串里的 CJK 字符 | 见根因 E 的文件清单 | 英文界面不再夹杂中文 |
| 8 | 托盘菜单：加上 `\t快捷键` 提示，用 `SetMenuDefaultItem` 设默认项，调用 `SetPreferredAppMode` + `FlushMenuThemes` 支持深色 | `TrayIcon.cs:371-414` | 最常用的入口与应用主题一致 |
| 9 | 图标重画：做一个简单的矢量标志，按 16/20/24/32 逐帧手工微调；托盘用单色 glyph；吉祥物只留在欢迎页、关于页等大尺寸场景 | `Assets/app.ico`、`AppChrome.cs:171`、`TrayIcon` | 工作量中等，但这是第一印象最强的信号 |

### P1：系统性收口（约 1-2 周）

- **Token 落地到控件层**：
  - 把 UiStyles 改成 `$$"""` 字符串注入 Token。
  - 高度收敛为一套：Compact 28 / Standard 32 / Large 40，删掉所有 ±n；Segmented 外框高度等于 Standard。
  - 统一状态层：Hover 0.05 / Pressed 0.09 / Disabled 0.4。
  - 设置导航选中项加一条 3×16 的强调色指示条。
- **调色板**：
  - 新增 `AccentFill`（深一档的品牌色，配白字 ≥4.5:1）、`OnDanger`、`ControlStroke`（约 C8C8C8 / 4A4A4A）。
  - 三级文字提到 ≥4.5:1。
  - 窗口底色和卡片之间做出可见的层级差，中性色统一一个色相。
  - Hover、SurfaceAlt、Divider 三者取不同值。
  - 加对比度单元测试，覆盖 4 套调色板。
- **排版**：
  - 设置页改用 `UiKit.Text(TextRole)`，新增 PageTitle（22-24）。
  - 删除逐字拆分 TextBlock 的 GroupLabel，改用 `TextRole.Overline`。
  - 10、11.5、12.5 归到 Eyebrow 或 Caption。
  - 中文做 CompositeFont（雅黑 + Inter + ScaleFactor），并在窗口根节点设置 Language。
- **平台行为**：
  - 标题栏：通过 `WM_NCHITTEST` 返回 `HTMAXBUTTON` 恢复 Snap Layouts；加非活动态变暗；按钮名称本地化，最大化/还原随状态切换。
  - 窗口位置用 `WINDOWPLACEMENT` 持久化；图库 MinWidth 降到约 580，顶栏做响应式。
  - 第二次启动通过 IPC 激活已运行实例。
  - 贴图改用 DWM 阴影，活动时显示强调色边框；关闭按钮只在 hover 时出现。
  - 两个弹层的阴影移到 ShadowPlate，Elevation 做成 Token。
- **图库交互**：
  - 卡片加右键菜单，复用现有的 ContextMenu 样式。
  - 键盘模型：方向键、Enter、Del、F2、Ctrl+A、Ctrl+C。
  - 支持 Ctrl/Shift 多选。
  - 收藏、改名、删除就地更新，不再 Reload 回顶。
  - 删除进回收站，并在 Toast 里提供撤销。
- **Toast 系统**：区分 Success/Info/Warning/Error/Progress，时长按字数计算，有淡出，单实例，位置固定。检查更新改成持续显示的进度状态。
- **截图核心**：
  - 加放大镜：像素网格、坐标、HEX，按 C 复制色值。
  - 粗细和字号按 DIP 乘以 dpi/96。
  - 文字工具改为所见即所得的透明编辑框。
  - 改颜色和粗细时作用于选中的标注，并支持撤销。
  - 撤销/重做按钮按栈状态置灰。
  - 加工具字母快捷键和方向键微调，tooltip 里显示快捷键。
- **文案第二轮**：
  - 建一份术语表：中文“图库/截图/贴图/收藏”，量词“张”；英文 Library / Capture。
  - 加复数辅助函数。
  - 修正写死的“30 天”。
  - 时间改成友好的本地化格式，不带秒。
  - 说明文字改为讲用户能得到什么；关于页重做（图标、版本、链接、许可）。

### P2：打磨与长期项

- Mica 背景（22H2+ 可用，保留不透明兜底）；平滑滚动加悬浮自动隐藏的滚动条；Segmented 改为滑动指示器。
- 统一 Tooltip：设置延时和淡入，可带快捷键键帽；捕获层用深色版本；DateIndexRail 不再反复切 `IsOpen`。
- 合并两套复选框；焦点环圆角跟随控件；捕获层玻璃色阶并入调色板，图标统一用 AppIcons。
- 截图工具栏收成单行图标，属性改为按当前工具弹出的子栏；选区边框按设备像素绘制（修掉双重内缩）；箭头改为渐粗杆加宽头。
- 解除选区锁定（标注改为绝对坐标）；多显示器覆盖层；覆盖层预热。
- 图库虚拟化；托盘图标按 DPI/主题重新加载，tooltip 带快捷键；托盘菜单换成 WPF 自绘。
- 新手引导改成可交互（显示热键状态、开机启动默认开、“立即截一张”）；后台静默检查更新，支持应用内安装。
- 安装器加中文、品牌美术图、签名、发布者 URL；RELEASE_NOTES 拆成用户版和开发版。
- 窗口标题改为 “DeepShot”；移除多余的刷新按钮和“点刷新”文案。

---

## 4. 做得好的地方

- **体系骨架已经有了**：DesignTokens、TextRole、`UiKit.ShadowPlate`（含“阴影只放兄弟层”的规则）、OverlayChrome 的 Token 注入写法都已存在。主要是“推广到所有地方”的问题，不需要推倒重来。
- **底层工程扎实**：历史预览在 UI 线程外加载并缓存；窗口设置了 `UseLayoutRounding`/`SnapsToDevicePixels`；圆角和深色标题栏已经通过 DWM 接入；C# 侧的动画大多已经使用 `Tokens.Motion`。
- **局部交互已到位**：QuickView 有完整的键盘支持（方向键、Home/End、Enter、Esc）；图库卡片网格会按宽度自动调整列数；搜索框有图标、清除按钮，Esc 先清空文字。