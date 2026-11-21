# Changelog

## [Unreleased]

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
