# Zeep Cut

A screenshot and desktop pin tool for Windows 10 22H2 / Windows 11 x64, built with C#, .NET 10 and WPF. Includes Simplified Chinese and English, light/dark/system themes.

## Install

Run `ZeepCut-0.1.11-Setup-x64.exe` for a per-user installation, or extract `ZeepCut-0.1.11-win-x64.zip` and run `ZeepCut.exe`. Both releases include the .NET runtime. Keep all files and the Resources folder together. The installer is unsigned.

Zeep Cut lives in the system tray, possibly inside the hidden icons panel. Closing History or Settings keeps the application running; select Exit from the tray menu to stop it. Startup at sign-in is optional and disabled by default. A short guide appears on first launch.

## Capture and annotate

- **Alt+A** freezes the monitor under your pointer. Hover and click to capture a window, or drag to select a region.
- Resize or move the selection before drawing. Tools include rectangle, ellipse, arrow, line, pen, text, and mosaic.
- The capture region locks once annotation begins; undoing all annotations unlocks it.
- **Enter** copies; **Esc or right-click** cancels the entire capture. In a text editor, Enter commits the text and Shift+Enter adds a new line.
- **Ctrl+Z / Ctrl+Y** undo and redo. Select annotations to move or delete them; double-click text to edit.
- Copy, save as PNG/JPEG, or pin. History is created only after a successful action.
- Multiple monitors, negative screen coordinates and mixed scaling are supported. Each capture stays within one monitor.

## Desktop pins

**Alt+S** pins an image from the clipboard. You can also pin captures and history images.

Drag to move, scroll to zoom, Ctrl+scroll for opacity (minimum 20%). The context menu offers original size, copy, save, lock/unlock, and close. Locking prevents movement while keeping the context menu accessible. Hide/show all pins through the tray menu. Pins are not restored after exit.

## Quick view

Left-click the tray icon to browse the latest 20 images. Right-click keeps the existing menu. The default quick-view shortcut is **Alt+Q**, which can be changed or cleared in Settings. The shortcut aligns the panel's top-left corner with the pointer position captured at activation, constrained to the monitor's work area.

Inside quick view, wheel up goes to the previous/newer image and wheel down to the next/older image. Arrow keys also browse; click a thumbnail to jump. Enter copies and closes, while Esc or clicking outside dismisses the panel. Drag the main image or a thumbnail into another app or folder. Pin, edit, and Full history actions reuse the existing tools; opening full history selects the current image.

## History and settings

History provides date groups, name search, favorites, rename, preview, copy, export, pin, delete, and additional annotations. Regular records expire 30 days after creation; favorites are retained. Renaming and editing do not extend retention.

Drag a history thumbnail or the full preview image into another application or folder to transfer the original PNG as a standard file. Only copying is allowed; the history original is not moved. Press Esc to cancel. The destination application determines whether it accepts, imports, or sends the image.

“Save and overwrite” replaces the same history image without keeping older versions. Existing annotations are flattened pixels. Canceling leaves the image unchanged. Already-open pins keep their original snapshot.

Settings provides configurable shortcuts, default capture mode, language, theme, and startup at sign-in. Choose “Draw a rectangle” for manual selection without window detection, or “Detect windows” to select windows on hover/click while retaining manual dragging. The choice is saved automatically and applies to the next capture. Images stay in `%LOCALAPPDATA%\ZeepCut`. Automatic cleanup does not delete manually exported files. Uninstall keeps data by default, with an explicit optional deletion prompt. The update channel has not been configured; update checks report that state.

## Build

Requires Windows and .NET 10 SDK.

```powershell
dotnet build ZeepCut.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
# Optional explicit installer compiler:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1 -InnoCompiler 'C:\Path\To\ISCC.exe'
```

Test projects, validation scripts, and temporary development records have been removed. `scripts/build.ps1` builds only; `scripts/publish.ps1` produces release files. Latest packages are in `artifacts/release/`; the complete self-contained application is in `artifacts/publish/win-x64/`. Publishing compiles and packages only; it does not run tests or launch the application.

Retained directories: `src/` (application sources), `installer/` (installer definition), `scripts/` (build and publish), and `artifacts/` (latest outputs).

Scrolling capture, OCR, recording, cloud sync and cross-monitor selections are outside this release.
