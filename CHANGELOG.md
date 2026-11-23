# Changelog

## [Unreleased]

## [0.4.3] - 2026-11-23

### Fixed
- macOS: `DesktopDockMac.bundle` had no platform settings in its `.meta`, so Unity fell back to its defaults, and a package installed from git can't change them in the Inspector. It's now set to **Standalone → macOS** (any CPU) only, and excluded from the Editor and other platforms.

## [0.4.2] - 2026-11-23

### Fixed
- README: the macOS section said the package ships the plugin's source, not a binary, right after saying the compiled plugin ships. It now says the compiled `DesktopDockMac.bundle` ships and only needs rebuilding after changing its source. The plugin source's own README no longer says it has never been compiled or run.

### Changed
- README: IL2CPP (x64) is now checked: the Basic Dock sample docks with its transparent background and the global hotkey works, with no errors in the player log. The test count is 119.

## [0.4.1] - 2026-11-22

### Added
- `DockCanvasScale` (uGUI) and `DesktopDockController.PixelsPerPoint`: keeps a canvas's UI the same size in points on a Retina Mac, where it was half size. The Basic Dock sample uses it.

### Fixed
- macOS: resizing was jerky on Retina screens. A thickness of an odd number of pixels made a frame of half a point, which Unity rendered a pixel larger, so each resize step waited for a size that never came and then fell back to `Screen.SetResolution`. Dock rects are now snapped to whole points (`MacCoordinates.SnapToPoints`), and each step lands in one frame. 3 more EditMode tests (119 in all).
- macOS: buttons and text stretched while the dock was resized. Until Unity draws a frame at the new size, Core Animation stretched the last frame to fit the window. The native plugin now shows it unscaled, pinned to the edge that stays put while growing and to the dragged edge while shrinking, so the grip and buttons never squash or get cropped. It also stops re-setting window and layer properties that haven't changed, which redrew the window on every resize step.

## [0.4.0] - 2026-11-21

### Added
- Tiling: docks from different games on the same edge of the same monitor sit side by side, each taking a share of the edge in proportion to its Length. On by default (`DockConfig.tileWithOtherDocks`, **Tile with other docks** in the settings panel). Docks find each other through heartbeat files in `%LOCALAPPDATA%\DesktopDock\Peers`. A closed dock leaves the row at once and a crashed one after about 4 seconds. The row's order is remembered per game. Dragging the Move handle along a shared edge swaps places. `TileCount`, `TileIndex` and `MoveTile(index)` cover the same from code. A tiled dock doesn't reserve screen space: Windows would stack the strips.
- `DockHotkey.affectAllDocks` (on by default): the show/hide shortcut applies to every running dock, so one `Ctrl+Alt+D` works for all the dock games running at once. A game that can't get the shortcut because another holds it tries again every few seconds and takes over when that game quits. It warns once instead of on every attempt.
- `DockShowHide`: what the show/hide shortcut did (show, reveal or hide).
- 17 more EditMode tests (116 in all): tile shares and placement, drag-to-reorder stability, and peers finding each other, ordering, leaving, timing out and messaging.
- macOS: the compiled native plugin ships in `Plugins/macOS/DesktopDockMac.bundle` (universal arm64 + x86_64, built with `Plugins~/macOS/build.sh`, signed ad hoc), so a Mac build no longer needs you to build it first. On a first run on a Mac (Apple M4, macOS 26.7) all 116 EditMode tests passed and the sample docked as a transparent, borderless, full-width strip that takes clicks. Other features are still unchecked on macOS.

## [0.3.0] - 2026-11-20

### Added
- Auto-hide: `DockConfig.autoHide`, `autoHideDelay` and `autoHidePeekPixels`. The dock slides almost out of sight when the cursor leaves and comes back when the cursor touches the sliver. On the taskbar's edge it slides under the taskbar, and it lets every click through while away. `SetAutoHide`, `HoldAutoHide`/`ReleaseAutoHide`, `RevealAutoHide`, `IsTuckedAway`, `CurrentWindowRect` and the `AutoHideChanged` event. The settings panel and tray menu have an Auto-hide option. The settings panel, resize grip and move handle keep the dock on screen while in use.
- `DockHotkey`: a system-wide shortcut (default `Ctrl+Alt+D`) that shows, reveals or hides the dock, with an `onPressed` event and `SetShortcut`. Windows only. `DockShortcut.TryParse` parses the key combinations.
- `DockMoveHandle` (uGUI): press and drag to move the dock to the nearest edge of the monitor under the cursor.
- Frame pacing: `DockConfig.backgroundRenderInterval` renders only every Nth frame while unfocused and not hovered (scripts still run every frame), and `hiddenFrameRate` (default 10) applies while the dock can't be seen.
- `IDockPlatform.MoveWindow` and `PlaceBelowTaskbar`.
- Basic Dock sample: now a small idle game. Critters earn coins over time and while the game is closed (up to 8 hours). Clicking a critter gives a bonus, and coins buy more critters, all saved in PlayerPrefs. It also adds a Move button and the `Ctrl+Alt+D` hotkey.
- Resizing API: `SetThicknessPixels(pixels, save)`, `ResizeBy(delta, save)`, `Thickness` and `ThicknessLimits`, all clamped to the new `DockConfig.minThicknessPixels` (100) and `maxThicknessPercent` (0.5).
- `DesktopDockController.Resized`: raised whenever the window's size changes, including on every step of a drag on the resize grip, so content can follow (for example, zoom a map in as the dock gets taller). In the Editor it follows the Game view's size.
- `DockResizeHandle`: the mouse wheel over the grip resizes the dock (`scrollStep`, 20 px per notch), and keeps working while the grip moves away from the cursor.
- `DockCameraFitter.fitMode`: **Thickness** (the previous behaviour: a taller dock zooms in), **Length**, or **FixedScale** (a taller dock shows more of the world).
- Basic Dock sample: Ctrl+wheel over the dock resizes it through `ResizeBy`, and the new size is shown through `Resized`.
- Basic Dock sample: the edge and Pinned buttons drawn over the game are gone, since the settings panel has them. Move, Buy, Settings and Quit remain, in a 2×2 grid.
- 44 more EditMode tests (99 in all): auto-hide geometry, nearest edge and monitor, frame pacing, shortcut parsing, thickness limits, wheel steps, camera fit modes, and loading 0.2.0 settings.

### Changed
- **Breaking:** `DockResizeHandle.minThickness`/`maxFraction` and `DockSettingsPanel.minThickness`/`maxThicknessFraction` are removed. Both now use the dock's `ThicknessLimits`, set with `DockConfig.minThicknessPixels` and `maxThicknessPercent`.
- `SetThicknessPixels` now clamps to `ThicknessLimits` (it used to allow anything from 16 px).

### Fixed
- The window jumped to the middle of the screen for a frame or two on every resize step: `Screen.SetResolution` re-centred it, sized as if it had a title bar, before the dock put it back. The dock now makes the window borderless and sizes and places it in one move, and Unity's backbuffer follows. `Screen.SetResolution` is only a fallback if the player doesn't follow within 10 frames. Resizing is also smoother: a drag now reaches the window in about four times as many steps.
- A click that came right after click-through switched off, before the cursor moved again, could land where the cursor last was over the window. The dock now posts the current cursor position to the game window when click-through switches off.

## [0.2.0] - 2026-11-20

### Added
- `DockConfig.transparencyMode`: opt-in **PerPixelAlpha** transparency. The backbuffer's alpha blends with the desktop, so soft shadows, glows and anti-aliased edges look right, and it works with Direct3D12 and the flip-model swapchain. Each frame is read back with `AsyncGPUReadback` and shown in a click-through layered window (`UpdateLayeredWindow`) above the game window. The game window is kept at alpha 1/255 so it still gets input. Costs about 1 ms of main-thread time per frame for a 3840×170 dock, and frames show one or two frames late. **ColorKey** stays the default. Pure C#, no native plugin.
- Automatic fallback: where ColorKey can't work (Direct3D12, Vulkan, or Direct3D11 with the flip-model swapchain, read from the player's `boot.config`), the dock uses PerPixelAlpha instead of showing a black strip. The saved setting is kept. `DesktopDockController.ActiveTransparencyMode` and `IsColorKeySupported` report it. In those builds, the settings panel's Soft transparency toggle is locked on.
- `DockPlayerSettings.ApplyRecommended(colorKey: false)` and the menu item **Apply Recommended Player Settings (PerPixelAlpha, keep graphics API)**: set everything except the graphics API and swapchain. The controller inspector's button uses it when the controller is set to PerPixelAlpha.
- `DockConfig.visibleAlphaThreshold` (default 0.5): in PerPixelAlpha mode, the `VisiblePixel` hit test counts pixels with alpha above this as content, read from the frame on screen.
- `Hidden/DesktopDock/AlphaPrepare` shader (in `Runtime/Resources`): converts frames to display-space premultiplied alpha, which Linear color space projects need.
- A warning when PerPixelAlpha only ever sees fully opaque frames (the camera isn't clearing to transparent, or URP is dropping alpha).
- `DockSettingsPanel`: a **Soft transparency** toggle.
- Basic Dock sample: critters cast a soft black shadow. It vanishes in ColorKey mode and blends in PerPixelAlpha mode.
- **Experimental macOS support. It is untested and has never been run on a Mac.** `MacDockPlatform` (`Runtime/Platform/macOS`) is backed by a native Objective-C plugin. You build it from `Plugins~/macOS` with `build.sh`; no binary ships.
  - Borderless, transparent window (composited with alpha).
  - Floating level when pinned.
  - Docks to `NSScreen.visibleFrame`, so it stays clear of the menu bar and the Dock.
  - `ignoresMouseEvents` click-through, global cursor and button polling.
  - Show/hide and send to back.
  - Fullscreen-app detection through `CGWindowListCopyWindowInfo`.
  - The Dock icon as the taskbar button.
  - `DockTrayIcon` as an `NSStatusItem`.
  - `DockStartup` through `SMAppService` (macOS 13+).
  - Reserve Screen Space has no macOS equivalent and is ignored.
  - Transparency always blends with alpha, so `transparencyMode` is ignored (`IDockPlatform.BlendsAlphaNatively`).
  - See the README's macOS section for what to check first.
- `DockStartup.DisplayName`: "Start with Windows", or "Open at Login" on macOS. The tray menu and settings panel use it.
- EditMode tests for the macOS coordinate conversions, scale smoothing and login-item status rules (`MacCoordinatesTests`), bringing the total to 55.
- `DockConfig.fullscreenBehavior`: drop behind (default) or hide while another app is fullscreen on the dock's monitor, or ignore it. `DesktopDockController.IsFullscreenAppActive` and the `FullscreenAppChanged` event report it.
- `DesktopDockController.SetHidden` / `IsHidden` / `HiddenChanged`: hide the dock completely. Reserved screen space is released while hidden.
- `Modify(change, save: false)`, `SaveSettings()` and `HasUnsavedChanges`, for sliders and drags. Unsaved changes are saved on quit.
- `DockTrayIcon`: a notification-area icon with show/hide, edge, always on top, start with Windows, custom items and Quit.
- `DockStartup`: opt-in start with Windows through the per-user Run key. Respects entries turned off in Task Manager.
- `DockResizeHandle` (uGUI): drag the dock's inner edge to resize it.
- `DockSettingsPanel` (uGUI): a ready-made settings panel that fits horizontal and vertical docks.
- EditMode tests (55) in `Tests/Editor`.
- Controller inspector: live hidden, fullscreen and click-through state, and a Show/Hide button.
- Basic Dock sample: Settings button, resize grip and tray icon.

### Changed
- The package now depends on the built-in Screen Capture module (`com.unity.modules.screencapture`).
- The Player Settings check only asks for Direct3D11 without the flip model when the controller uses ColorKey transparency. The build-time warnings now say these settings are for ColorKey, and that the dock falls back to PerPixelAlpha without them.
- **Breaking:** the `PixelAlpha` hit-test source is now `VisiblePixel`. It treats any pixel that isn't pure black as content and ignores alpha. `alphaHitThreshold` is removed.
  - Why: URP discards alpha whenever post-processing or HDR is on, which made the whole strip block clicks. Pure black is also exactly what the window shows as see-through.
- `DockTransparentCamera` turns off URP camera dithering, which speckled the see-through area. It no longer forces HDR off, since alpha isn't used. URP is an optional dependency (`DESKTOPDOCK_URP`).
- `DockTrayIcon` talks to the platform through an internal `ITrayIcon`, so Windows and macOS share it.
- On macOS, the `VisiblePixel` hit test also checks alpha, because the macOS compositor uses it.
- The window is no longer re-shown by every placement (`SWP_SHOWWINDOW` removed), and restyling keeps it hidden if it was hidden.
- Verified with URP 17.3: Universal and 2D renderers, post-processing on and off.
- Verified on Unity 6000.3: the package compiles warning-free, and the Player Settings fixer handles Unity 6 defaults.
- Basic Dock sample: uses `InputSystemUIInputModule` in projects set to Input System only, so it no longer errors in Unity 6 templates.
- Basic Dock sample: replaced the obsolete `FindObjectOfType` call.
- Basic Dock sample: moved the score label so it doesn't overlap the buttons in side docks.

## [0.1.0] - 2026-11-20

### Added
- `DesktopDockController`: dock the Windows player to any edge or monitor. Supports a transparent background, click-through, always-on-top, an optional taskbar button, and app-bar space reservation. Changes the player makes are saved.
- Layout helpers: `DockLayoutSwitcher`, `DockCameraFitter`, `DockTransparentCamera`.
- Editor: Player Settings checker and fixer (Tools > Desktop Dock), a build-time warning, and a controller inspector with live edge buttons.
- Basic Dock sample.
