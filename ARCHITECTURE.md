# Zeep Cut — 架构与关键技术决策

## 解决方案结构

```
ZeepCut.slnx
├── src/ZeepCut.Core/         纯 .NET 10 类库，无 WPF/Win32 依赖，可单元测试
├── src/ZeepCut/              WPF 应用（net10.0-windows），UI + Win32 互操作
└── tests/ZeepCut.Tests/      xUnit，只测 Core 与可测逻辑
```

**Core 必须保持纯逻辑**：坐标换算、选区状态机、历史到期规则、标注模型、撤销栈、名称生成都放 Core，这样能在无桌面的情况下跑测试。

## Core 层规划（每项都要有单元测试）

| 类型 | 职责 |
| --- | --- |
| `PixelRect` | 整数像素矩形（Left/Top/Width/Height），提供 Intersect、Contains、Clamp、IsEmpty |
| `MonitorInfo` | 屏幕物理矩形、工作区、DPI。`DisplaySpace` 持有屏幕列表，做点→屏幕查找 |
| `DpiTransform` | 物理像素 ↔ WPF DIP 换算（scale = dpi/96），以及"目标屏幕内把越界选区夹回"的规则 |
| `SelectionState` | 框选状态机：Idle → Hover → Dragging → Selected → Adjusting。产出规范化矩形（拖拽任意方向都得到正宽高）|
| `Annotation` 体系 | 矩形/椭圆/箭头/直线/画笔/文字/马赛克的纯数据模型（像素坐标）+ 颜色/粗细/字号 |
| `EditHistory` | 撤销/重做栈（增删移动标注、修改属性）|
| `HistoryRules` | 30 天到期判定（按 CreatedAtUtc，收藏豁免）、名称生成、搜索匹配 |
| `AppSettings` | 设置模型 + 默认值 + 快捷键字符串解析/校验 |
| `ShortcutSpec` | 快捷键解析与格式化（"Alt+A" ↔ 修饰键+虚拟键），冲突检测 |

## WPF 层规划

| 目录 | 职责 |
| --- | --- |
| `App/` | `App.xaml(.cs)`：单实例互斥体、托盘、生命周期、全局异常处理 |
| `Capture/` | 热键注册（RegisterHotKey）、屏幕捕获（GDI BitBlt）、窗口枚举（DWM 边框）、遮罩窗口、选区交互 |
| `Editor/` | 标注工具栏、标注绘制与命中测试、渲染输出（RenderTargetBitmap / DrawingVisual） |
| `Pins/` | 贴图窗口（无边框置顶）、拖动/缩放/透明度/锁定、集合管理 |
| `History/` | 图库窗口、缩略图生成、SQLite 仓储 |
| `Platform/` | Win32 P/Invoke 集中处：user32/gdi32/dwmapi/shell32、DPI、剪贴板、开机启动、外部链接 |
| `Resources/Lang/` | 中英文资源字典（`Strings.zh-Hans.xaml` / `Strings.en.xaml`） |

## 关键实现决策

1. **托盘**：Win32 `Shell_NotifyIcon` + 隐藏的 message-only 窗口接收回调。不要引入 WinForms。
2. **冻结画面**：热键触发后先 `BitBlt` 抓目标屏幕（物理像素），再把 `BitmapSource` 铺到全屏遮罩窗口；遮罩窗口本身不参与截图，窗口枚举在遮罩显示前完成。
3. **窗口识别**：`EnumWindows` 按 Z 序，过滤不可见/最小化/工具窗口/自身进程/`GetWindowRect` 为空；用 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 取真实边框；只取与目标屏幕的交集。
4. **DPI**：`app.manifest` 已声明 PerMonitorV2。用 `MonitorFromPoint(..., MONITOR_DEFAULTTONEAREST)` + `GetDpiForMonitor` 取每屏 DPI。**WPF 的 Left/Top 在 PerMonitorV2 下是 DIP**，所以全屏遮罩要按目标屏幕的 DIP 尺寸铺放，输出图片用物理像素裁剪。
5. **单实例**：命名 Mutex，二次启动通过 `WM_COPYDATA` 或命名事件通知已有实例（首版允许简单提示后退出）。
6. **剪贴板**：`Clipboard.GetImage()`；必须 `try/catch` 处理 `COMException`（剪贴板被占用）。
7. **历史存储**：`LocalAppData\ZeepCut\`，`history.db` (SQLite) + `images/` + `thumbs/`；内部路径存相对路径。
8. **覆盖保存**：写临时文件 → 校验可读 → 替换 → 更新元数据与缩略图 → 释放所有 `BitmapImage` 句柄（用 `BitmapCacheOption.OnLoad`）。
9. **配色/主题**：`ThemeManager` 切换资源字典；默认跟随系统（读注册表 `AppsUseLightTheme`）。
10. **语言**：启动读系统 `CultureInfo`，非中文默认英文；切换后重新加载资源字典。

## 测试策略

- Core：xUnit，覆盖坐标夹取、多屏查找（含负坐标）、选区反向拖拽、到期规则、收藏豁免、名称生成、撤销重做、快捷键解析与冲突。
- WPF：不写自动化 UI 测试。改为在真实桌面上用 `scripts/` 手动/脚本化验证，并在 `STATE.md` 记录证据（截图路径、观察到的事实）。
- 混合 DPI：本机 100% 无法真实覆盖，只做 Core 换算单测；在文档中标注"真实混合 DPI 未验证"。
