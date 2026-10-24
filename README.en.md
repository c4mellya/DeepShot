# DeepShot

DeepShot is a screenshot and desktop pin tool for Windows 10 22H2 / Windows 11 x64, built with C#, .NET 10 and WPF. Includes Simplified Chinese and English, light/dark/system themes.

Repository: [c4mellya/DeepShot](https://github.com/c4mellya/DeepShot) · [简体中文](README.md)

## Install

Run `DeepShot-0.2.1-Setup-x64.exe` for a per-user installation, or extract `DeepShot-0.2.1-win-x64.zip`, open the `DeepShot` folder, and run `DeepShot.exe`. Both releases include the .NET runtime. Keep all files and the Resources folder together. The installer is unsigned.

DeepShot lives in the system tray, possibly inside the hidden icons panel. Closing History or Settings keeps the application running; select Exit from the tray menu to stop it. Startup at sign-in is optional and disabled by default. A short guide appears on first launch.

## Capture and annotate

- **Alt+A** freezes the monitor under your pointer. Hover and click to capture a window, or drag to select a region.
- **Alt+Shift+A** starts quick capture. Drag to select a rectangle, then release to copy and close immediately. Esc or right-click cancels; no Enter confirmation is needed.
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

History provides date groups, name search, favorites, rename, preview, copy, export, pin, delete, and additional annotations. Regular records expire 30 days after creation by default. In Settings → Data folder, set retention to 1–3650 days; press Enter or leave the field to save. The new value is used at next startup or daily cleanup. Favorites are retained, and renaming or editing does not extend retention.

Drag a history thumbnail or the full preview image into another application or folder to transfer the original PNG as a standard file. Only copying is allowed; the history original is not moved. Press Esc to cancel. The destination application determines whether it accepts, imports, or sends the image.

“Save and overwrite” replaces the same history image without keeping older versions. Existing annotations are flattened pixels. Canceling leaves the image unchanged. Already-open pins keep their original snapshot.

Settings provides configurable shortcuts for capture, quick capture, pins and quick view, plus default capture mode, language, theme and startup at sign-in. Two- and three-key combinations support Ctrl / Alt / Shift / Win plus a letter, digit, function, arrow, space, numpad or punctuation key. Shortcut conflicts are reported. Choose “Draw a rectangle” for manual selection without window detection, or “Detect windows” to select windows on hover/click while retaining manual dragging. The choice is saved automatically and applies to the next capture. Images stay in `%LOCALAPPDATA%\DeepShot`. Automatic cleanup does not delete manually exported files. Uninstall keeps data by default, with an explicit optional deletion prompt. Update checks query the [latest stable GitHub Release](https://github.com/c4mellya/DeepShot/releases/latest), compare versions, and display release notes with a link to the release page. Network failures are reported. Updates are not downloaded or installed automatically.

## Font credit

The UI uses Xiaomi's [MiSans](https://hyperos.mi.com/font/) typeface (Copyright © 2020-2025 Beijing Xiaomi Mobile Software Co.,Ltd.) under the MiSans free commercial license; the Regular and Semibold weights are embedded unmodified. MiSans remains the property of Xiaomi; the notice ships with the app as `Assets\Fonts\MiSans-NOTICE.txt` in the install folder.
