# Zeep Cut — Agent 工作手册

## 用户最新指令（2026-10-19，优先于下文旧流程）

本次收尾禁止测试、禁止子代理，由 Lead 直接完成实现。不要运行 build.ps1（它会执行测试）或桌面验收脚本。仅使用 scripts/publish.ps1 编译与打包；测试源码保留。发布状态见 STATE.md 和 ACCEPTANCE.md。下文的逐轮测试要求属于历史流程，不适用于本次收尾。

本文件是每一轮新会话的**第一份必读材料**。每轮开始时按顺序读：

1. `AGENTS.md`（本文件，环境与规则）
2. `STATE.md`（当前进度、下一步、已知问题）
3. `ROADMAP.md`（任务清单与验收项）
4. `ARCHITECTURE.md`（模块划分与关键技术决策）
5. `PLAN.md`（产品需求，唯一需求来源）

## 硬性规则

- 需求以 `PLAN.md` 为准。不要重新做需求调研，不要扩大首版范围（不做长截图、OCR、录屏、云同步、跨屏框选、控件级识别、鼠标穿透、贴图恢复）。
- 每轮结束前必须更新 `STATE.md`（做了什么、验证结果、下一步、遗留问题）。这是下一位接手者的唯一记忆。
- 只报告真实执行过的验证。编译通过 ≠ 桌面行为已验证。无法验证的项明确写“未验证”并说明原因。
- 每轮做**一个可交付的完整小步**，保持仓库可编译、测试通过。宁可小步完成，不要大范围半成品。
- 不要删除或重写 `PLAN.md`。不要修改其他 agent 未完成的半成品而声称完成。
- 提交前必须跑 `scripts/build.ps1`（构建 + 测试）并通过。

## 环境事实（重要）

- 工作区是 WSL 路径：`/home/camellya/workspace/Zeep-Cut`，Windows 侧等价路径为
  `\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut`。
- **WSL 里没有 dotnet**。所有构建、测试、运行必须通过 Windows 端 dotnet：
  ```bash
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File '\\wsl.localhost\Ubuntu\home\camellya\workspace\Zeep-Cut\scripts\build.ps1'
  ```
- Windows 已安装 .NET SDK 10.0.100；`net10.0-windows` WPF 项目可直接在 UNC 路径上构建。
- 构建产物：`src/ZeepCut/bin/Debug/net10.0-windows/ZeepCut.exe`（Debug 不启用 RID）。
- 桌面显示器（真实环境，2026-10-18 T5/T6 用 PerMonitorV2 探针复测）：
  - `\\.\DISPLAY1` 主屏：**物理 2560×1440**，原点 (0,0)，DPI 120（125%，逻辑 2048×1152）
  - `\\.\DISPLAY2` 副屏：**物理 1080×1920**，**原点 (-1080,0)**（负坐标，可用于验证多屏定位），DPI 96（100%）
  - 本机当前是 **125% + 100% 混合 DPI**；按物理像素捕获时主屏输出 2560×1440，2048×1152 只是逻辑/DIP 尺寸。
- 桌面环境可交互：GUI 能启动，可用脚本截屏、发送按键。参考 `scripts/` 下：
  - `screenshot.ps1`：截取整屏/指定屏到 PNG（可自行 read_image 查看）
  - `keys.ps1`：向前台窗口发送按键（如 `-Keys '^%a'` = Ctrl+Alt+A）
  - `run.ps1`：启动已构建程序并报告进程状态
- Inno Setup 当前**未安装**（winget 可用）。阶段四再尝试安装；失败则交付便携版并如实说明。

## 使用 Powershell 的坑

- `powershell.exe` 的当前目录是 UNC 路径，dotnet 无法在 UNC 下工作。用 `-File <UNC绝对路径>` 调用脚本，脚本内部用 `$PSScriptRoot` 定位，不要依赖 cwd。
- 本机 PowerShell 5.1 的 `Start-Process` 会因环境变量 `NO_PROXY`/`no_proxy` 大小写重复键报错（`Item has already been added`）；GUI 进程还会继承 WSL 管道导致调用挂起。启动应用一律用 `scripts/run.ps1`（内部用 `Win32_Process.Create`）。
- 托盘图标通常位于 Windows 11 的“显示隐藏的图标”溢出面板；验证用 `scripts/tray.ps1`（查图标 rect / 点击）和 `scripts/mouse.ps1`（屏幕坐标点击）。
- WPF 项目**不要**启用 `UseWindowsForms`：会和 `System.Windows.Application` 名称冲突。托盘用 Win32 `Shell_NotifyIcon` P/Invoke 实现。
- 脚本文件用 UTF-8 **带 BOM** + CRLF，避免 Windows PowerShell 5.1 中文乱码；含中文的临时 PowerShell 脚本同样需要 BOM。

## 提交约定

- 用 `git` 提交，信息格式：`feat(capture): ...` / `fix(pins): ...` / `test(core): ...`。
- 每次提交前确认 `git status` 无意外大文件；`bin/`、`obj/` 已在 `.gitignore`。
