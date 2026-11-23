# Desktop Dock

Turns a Unity standalone player into a borderless, transparent strip docked to an edge of the desktop, the layout used by idle "desktop companion" games: the game lives in a band along the bottom (or top or side) of the screen while you keep working above it.

- **Dock to any edge**: bottom, top, left or right, on any monitor. Thickness in pixels or as a percentage of the screen. The dock can span the whole edge or part of it (start, center or end).
- **Transparent background**, two ways (see [Transparency modes](#transparency-modes)):
  - **ColorKey** (default): pure-black pixels, such as a camera background of `(0,0,0,0)`, show the desktop through. All other pixels are opaque. Free.
  - **PerPixelAlpha** (opt-in): the backbuffer's alpha blends with the desktop, so soft shadows, glows and anti-aliased edges look right. Works with Direct3D11, Direct3D12 and the flip-model swapchain. Costs a GPU readback per frame.
- **Click-through**: clicks on empty areas go to the window behind. Hit testing checks UI, 2D colliders, 3D colliders, whether the pixel under the cursor is visible, and your own providers.
- **Always on top**, optional **taskbar button**, and **stays clear of the taskbar**.
- **Steps aside for fullscreen apps**: when a game or fullscreen video takes over the dock's monitor, the dock drops behind it (or hides), then comes back.
- **Reserve screen space** (optional): registers as a Windows app bar, so maximized windows stop at the dock the way they stop at the taskbar.
- **Auto-hide** (optional): the dock slides almost out of sight when the cursor leaves, like an auto-hiding taskbar, and comes back when the cursor touches the sliver left at the screen edge.
- **Global hotkey**: a shortcut such as Ctrl+Alt+D shows or hides the dock from any app.
- **Tray icon** (optional): show/hide the dock, change edge, pin, auto-hide, start with Windows, your own items, and Quit.
- **Resizable**: a grip on the dock's inner edge to drag or scroll, an API to resize from your own input, and a `Resized` event as the size changes, so your content can follow (zoom in as the dock gets taller, for example).
- **Drag to another edge**: press a Move handle and drag; the dock jumps to the nearest edge of whichever monitor the cursor is on.
- **Settings panel**: a ready-made in-game panel for every dock setting, laid out for horizontal and vertical docks.
- **Start with Windows** (opt-in), through the standard per-user startup list.
- **Follows display changes**: re-docks when a resolution, monitor or taskbar changes.
- **Layout helpers**: swap between horizontal and vertical layouts, fit an orthographic camera to the dock (with a pixel-perfect option), and set up a transparent camera.
- **Remembers** changes the player makes (edge, size, monitor) in PlayerPrefs.
- **Low idle cost**: a lower frame rate when unfocused, an optional render interval (scripts keep running, frames are drawn less often), and a very low frame rate while the dock can't be seen.

**Platform:** Windows standalone (Mono or IL2CPP, x64/x86). **macOS standalone is experimental**: the package includes the compiled native plugin, and on a first run on a Mac the tests passed and the sample docked as a transparent strip that takes clicks, but most features haven't been checked there yet (see [macOS](#macos-experimental)). The Editor and other platforms only simulate docking: layout code still gets the configured edge, but no window is touched. The platform layer is behind `IDockPlatform`.

Requires Unity 2021.3 or newer.

Tested with:
- **Unity 2022.3.20f1** and **Unity 6000.3.29f1**, Windows x64 Mono builds, built-in render pipeline.
- In Unity 6, with the legacy Input Manager and with Input System only.
- **URP 17.3** (Unity 6000.3) with the Universal and 2D renderers, post-processing on and off, and ACES tonemapping. Transparency and click-through were checked on screen.
- Reserve screen space, hiding, the tray icon and menu, stepping aside for a fullscreen window, drag-to-resize, the settings panel in both orientations, and start with Windows were checked in a Unity 6000.3 build.
- Auto-hide (tucking away, coming back from the sliver, sliding under the taskbar), the global hotkey, dragging to another edge and back, the frame pacing, and the Basic Dock sample's idle game (buying, offline earnings) were checked in a Unity 6000.3 build.
- PerPixelAlpha (Unity 6000.3, built-in pipeline): Direct3D11 without flip model and Direct3D12, Gamma and Linear color space. Blending was measured on screen against white and black backgrounds, along with click-through, switching modes at runtime, edge changes, hiding, and pinning.
- Resizing with the grip (drag and wheel) and with `ResizeBy` from game code, the `Resized` event while dragging, and clamping to the thickness limits were checked in a Unity 6000.3 build.
- IL2CPP (x64, Unity 6000.3): the Basic Dock sample docks with its transparent background, and the global hotkey hides and shows it, with no errors in the player log.
- 119 EditMode tests (see [Tests](#tests)).

Not tested yet: IL2CPP x86 builds, the tray icon under IL2CPP, more than one monitor, display scaling other than 100%, PerPixelAlpha with Vulkan, with Direct3D11 plus the flip-model swapchain, and with URP.

**Unity 6 note:** new Unity 6 projects default to Direct3D12, fullscreen and the flip-model swapchain. Fullscreen breaks the dock, and Direct3D12 and the flip model break ColorKey transparency. **Tools → Desktop Dock → Apply Recommended Player Settings** fixes all three. With PerPixelAlpha you can keep Direct3D12: use **Apply Recommended Player Settings (PerPixelAlpha, keep graphics API)** instead. Either way, a build that can't do ColorKey falls back to PerPixelAlpha by itself.

## Install

Pick one:

- **Git (recommended):** Package Manager → **+** → *Add package from git URL…*, then enter the URL below. The `#v0.4.3` pins a release; leave it off to follow the latest version.

  ```
  https://github.com/jamebesa/desktop-dock.git#v0.4.3
  ```
- **Local folder:** download or clone the package, then Package Manager → **+** → *Add package from disk…* → select its `package.json`.
- **Embedded:** copy the package folder into your project's `Packages/` folder.

## Quick start

1. **Tools → Desktop Dock → Apply Recommended Player Settings.** This sets:
   - Windowed mode, resizable window
   - Run In Background and Visible In Background on
   - Alt+Enter fullscreen switch off
   - **Direct3D11** as the only/first graphics API
   - **flip-model swapchain off**

   ColorKey transparency (the default) needs the last two; PerPixelAlpha doesn't. A build without them uses PerPixelAlpha automatically. The `DesktopDockController` inspector warns you if any of these drift. For a PerPixelAlpha game, use **Apply Recommended Player Settings (PerPixelAlpha, keep graphics API)** (`DockPlayerSettings.ApplyRecommended(colorKey: false)`) so your graphics API choice is kept.
2. Add **Desktop Dock → Desktop Dock Controller** to a GameObject in your first scene. It survives scene loads.
3. Add **Desktop Dock → Dock Transparent Camera** to your camera. It sets the background to `(0,0,0,0)`, and in URP it also turns camera dithering off.
4. Build for Windows and run.

### URP

URP works with both the Universal and 2D renderers, and post-processing can stay on. Two things to watch:

- **Dithering** speckles the see-through area with dark dots. `DockTransparentCamera` turns it off for you.
- **Bloom** (and anything else that lifts pure black above zero, such as film grain or a color-grading lift) makes the affected pixels opaque. In testing, a strong bloom filled the whole strip with dark glow. Keep these out of the dock camera's Volume profile, or keep them subtle enough that the background stays exactly black.

HDR and post-processing make URP discard the alpha channel. In ColorKey mode that doesn't matter, because neither the window's transparency nor the click-through test uses alpha. PerPixelAlpha needs the alpha channel; see [Transparency modes](#transparency-modes).

For a ready-made scene, import the **Basic Dock** sample from the Package Manager. Put `DockDemo` on an empty GameObject in an empty scene and build. You get a tiny idle game with clickable critters, plus **Move**, **Buy**, **Settings** and **Quit** buttons, the settings panel (edge, size, always on top, auto-hide and more), a resize grip, a tray icon and a hotkey.

> A docked window has no title bar or close button, so **give players a way to quit** (and to change settings) inside the game.

## Transparency modes

`DockConfig.transparencyMode` picks how the transparent background is made. It is saved with the other settings and can change at runtime; the Basic Dock sample's settings panel has a **Soft transparency** toggle for it.

| | **ColorKey** (default) | **PerPixelAlpha** |
|---|---|---|
| What is see-through | Exactly the pure-black pixels | Whatever the alpha channel says, blended |
| Soft shadows, glows, smooth edges | Drawn against black, shown opaque | Blend with the desktop |
| Pure black in your art | See-through (use `#010101`) | Fine |
| Graphics API | Direct3D11, flip-model swapchain off (otherwise PerPixelAlpha is used instead) | Direct3D11 or Direct3D12, flip model on or off |
| Per-frame cost | None | A GPU copy and readback, plus about 1 ms of main-thread time for a 3840×170 dock |
| Latency | None | The window shows each frame one or two frames late |
| `VisiblePixel` hit test | Pixel isn't pure black (1×1 readback) | Alpha above `visibleAlphaThreshold` (default 0.5), from the frame on screen |

```csharp
dock.Modify(c => c.transparencyMode = DockTransparencyMode.PerPixelAlpha);
var inUse = dock.ActiveTransparencyMode;   // what is actually used
bool canColorKey = dock.IsColorKeySupported;
```

**Automatic fallback:** ColorKey only works on Direct3D11 with the bitblt-model swapchain. With Direct3D12, Vulkan or the flip model it would show a black strip, so the dock uses PerPixelAlpha instead and logs it once. The saved setting doesn't change. The player finds the swapchain model in its `boot.config`: builds made with the flip model off contain `force-d3d11-bitblt-model`. `ActiveTransparencyMode` reports the mode in use, and the settings panel's **Soft transparency** toggle shows as on and locked while ColorKey isn't possible.

What PerPixelAlpha needs:

- **A transparent clear and real alpha.** Clear the camera to `(0,0,0,0)`, which `DockTransparentCamera` does. Anything drawn with normal alpha blending over it ends up correctly premultiplied, including uGUI. If every frame is opaque everywhere, the dock logs a warning once.
- **URP: keep the alpha channel.** Turn on *Post-processing → Alpha Processing* in the URP asset, and either turn HDR off or set *HDR Precision* to 64 bits. Otherwise URP drops alpha and the dock shows as a solid block. (Not tested with URP yet.)
- **Gamma and Linear color space both work.** In Linear projects, colors are converted to display-space premultiplied alpha on the GPU (shader `Hidden/DesktopDock/AlphaPrepare`, loaded from `Resources`), because Windows blends in display space. Half-transparent pixels therefore blend as they would in a Gamma project.
- **Cost grows with the dock's area.** Measured on a 3840×170 bottom dock in a Direct3D11 build with no frame cap: about 2.4 ms per frame against about 1.5 ms with ColorKey. The main thread spends about 1 ms of that copying and presenting, on both Direct3D11 and Direct3D12. Readbacks are skipped while the dock is hidden, and frames are dropped instead of queued if the GPU falls behind. Combine it with the lower unfocused frame rate.

Choosing the threshold: at 0.5, shadows, glows and the outer half of anti-aliased edges let clicks through. Lower `visibleAlphaThreshold` to make faint pixels clickable. Colliders, UI and your own providers count as content whatever their alpha.

## Runtime API

```csharp
using DesktopDock;

var dock = DesktopDockController.Instance;
dock.SetEdge(DockEdge.Left);
dock.SetThicknessPixels(260);           // or SetThicknessPercent(0.18f); clamped to dock.ThicknessLimits
dock.SetLength(0.6f, DockAlignment.End);
dock.SetMonitor(1);                     // -1 = primary; see dock.Monitors
dock.SetAlwaysOnTop(false);
dock.SetReserveScreenSpace(true);
dock.Modify(c => { c.edge = DockEdge.Top; c.showInTaskbar = false; }); // several at once, one re-dock
dock.Modify(c => c.thicknessPixels = 300, save: false); // for sliders and drags...
dock.SaveSettings();                    // ...then save once at the end (unsaved changes are also saved on quit)
dock.SetHidden(true);                   // hide completely; give players a way back, such as the tray icon
dock.SetAutoHide(true);                 // slide away when the cursor leaves; see autoHideDelay, autoHidePeekPixels
dock.HoldAutoHide(this);                // keep it on screen while your menu is open...
dock.ReleaseAutoHide(this);             // ...and let it tuck away again afterwards
dock.RevealAutoHide(3f);                // bring a tucked-away dock back for a few seconds
dock.ResetToDefaults();                 // also clears what was saved in PlayerPrefs

DesktopDockController.Docked += state =>
{
    Debug.Log(state);                   // edge, window rect, monitor, IsSimulated
    if (state.Orientation == DockOrientation.Vertical) { /* rearrange */ }
};
var current = DesktopDockController.CurrentState; // for late subscribers

DesktopDockController.HiddenChanged += hidden => { /* pause music? */ };
DesktopDockController.FullscreenAppChanged += active => { /* a fullscreen app covers the dock's monitor */ };
DesktopDockController.AutoHideChanged += tucked => { /* sliding away (true) or coming back (false) */ };

if (DockStartup.IsSupported)            // Windows builds, and macOS 13+ builds
    DockStartup.SetEnabled(true);       // opt-in: ask the player first. Also: DockStartup.IsEnabled
```

What the dock does while another app is fullscreen on its monitor is `DockConfig.fullscreenBehavior`: **DropBehind** (default) moves it behind the fullscreen app until that app closes or loses focus, **Hide** hides it, and **Ignore** leaves a pinned dock on top.

### Resizing

The dock's *thickness* is its height for top and bottom docks and its width for left and right docks. Players can change it with the resize grip, by dragging it or turning the mouse wheel over it, or with the settings panel's Size slider. Your code can change it too:

```csharp
var dock = DesktopDockController.Instance;
dock.SetThicknessPixels(300, save: false);  // set it (save: false for changes many times a second)
dock.ResizeBy(+20, save: false);            // grow or shrink by a step; steps add up even before the window catches up
dock.SaveSettings();                        // save once the player is done
int now = dock.Thickness;                   // current thickness in pixels
Vector2Int limits = dock.ThicknessLimits;   // x = smallest, y = largest allowed on this monitor

// Raised whenever the window's size changes: on every step of a drag, on each ResizeBy, on edge or monitor changes.
DesktopDockController.Resized += state =>
{
    float t = Mathf.InverseLerp(limits.x, limits.y, state.Thickness);
    mapCamera.orthographicSize = Mathf.Lerp(6f, 3f, t); // zoom in as the dock gets taller
};
```

For example, resize while a key is held and the wheel turns, from your own input code (the Basic Dock sample does exactly this with Ctrl):

```csharp
if (Input.GetKey(KeyCode.LeftControl) && Input.mouseScrollDelta.y != 0)
    dock.ResizeBy(Input.mouseScrollDelta.y > 0 ? 20 : -20, save: false);
```

The wheel only reaches the game over visible content: over empty areas the dock is click-through, so the wheel goes to the window behind it.

All of these stay within `DockConfig.minThicknessPixels` (default 100) and `maxThicknessPercent` (default 0.5 of the monitor). `Resized` is raised once the window has its new size, so during a fast drag several steps can arrive as one. In the Editor it's raised when the Game view changes size.

To have the camera follow the size automatically, use `DockCameraFitter` (see [Layout components](#layout-components)). Its default fit mode keeps the same amount of world across the dock's thickness, so a taller dock zooms in. `FixedScale` keeps the zoom, so a taller dock shows more of the world.

### Auto-hide

**Auto-hide** (`DockConfig.autoHide`, off by default) waits `autoHideDelay` seconds (default 1) after the cursor leaves, then slides the dock toward its outer edge until only `autoHidePeekPixels` (default 4) remain where it was. On the taskbar's edge it slides under the taskbar. While tucked away it lets every click through. Touching the sliver brings it back. It never slides away while a mouse button is held, while the settings panel, the resize grip or the move handle is in use, or while anything else holds it with `HoldAutoHide`. An auto-hiding dock never reserves screen space.

### Tiling

Several Desktop Dock games can run side by side. Docks on the same edge of the same monitor share it: each gets a share of the edge's length in proportion to its **Length** setting, so two docks at 100% get half each. Each keeps its own thickness. A dock alone on its edge uses Length and Alignment as before. This is on by default (`DockConfig.tileWithOtherDocks`, **Tile with other docks** in the settings panel). A dock with it off ignores the others and may overlap them.

- **Finding each other.** Every dock writes a small heartbeat file once a second to `%LOCALAPPDATA%\DesktopDock\Peers` (on macOS, `~/.local/share/DesktopDock/Peers`). A dock that's closed leaves the row at once. One that crashes drops out after about 4 seconds. The other docks then spread out to fill the gap.
- **Order.** The row's order is remembered per game (by company and product name), so the same games line up the same way next time. Newcomers join at the end. Dragging the **Move** handle along the edge swaps places with the dock under the cursor; `MoveTile(index)` does the same from code. `TileCount` and `TileIndex` say how many docks share the edge and where this one is.
- **Reserved space.** Windows stacks reserved strips on one edge instead of putting them side by side, so tiled docks don't reserve screen space. A dock reserves again once it has the edge to itself.
- **One hotkey for all.** Only the first game to start gets a given global shortcut. With `DockHotkey.affectAllDocks` on (the default), its show/hide applies to every running dock, so `Ctrl+Alt+D` shows or hides them all. The other games keep trying the shortcut every few seconds, and one takes over when the holder quits.
- **Content.** A tile can be much shorter than the whole edge, so lay content out for any length (`DockCameraFitter` helps). The `Resized` event fires when a dock joins or leaves and the tile changes size.

### Frame pacing

**Frame pacing** picks a frame rate and render interval from the situation:

| Situation | Frame rate | Rendering |
|---|---|---|
| The game has focus | `focusedFrameRate` (60) | every frame |
| Another app has focus, cursor over the dock | `backgroundFrameRate` (30) | every frame |
| Another app has focus, cursor elsewhere | `backgroundFrameRate` (30) | every `backgroundRenderInterval`th frame (1 = every frame) |
| The dock can't be seen: hidden, tucked away, or behind a fullscreen app | `hiddenFrameRate` (10) | every frame |

The render interval uses `OnDemandRendering.renderFrameInterval`: `Update` still runs every frame, so game logic keeps its pace and only drawing is skipped. A value of 2–4 suits most idle games.

Treat an invisible but clickable region as content:

```csharp
class DragHandle : MonoBehaviour, IDockHitTestProvider
{
    void OnEnable()  => DesktopDockController.Instance.AddHitTestProvider(this);
    void OnDisable() => DesktopDockController.Instance?.RemoveHitTestProvider(this);
    public bool IsContentAt(Vector2 screenPos) => /* your test */ false;
}
```

## Layout components

| Component | What it does |
|---|---|
| `DockLayoutSwitcher` | Turns `horizontalOnly` objects on for top/bottom docks and `verticalOnly` objects on for left/right docks. Raises `onEdgeChanged` and `onOrientationChanged`. |
| `DockCameraFitter` | Sizes an orthographic camera to the dock, updating as it's resized. `fitMode`: **Thickness** (default) keeps `worldUnitsAcross` world units across the dock's thickness, so a taller dock zooms in; **Length** keeps `worldUnitsAlong` units along the dock; **FixedScale** keeps `screenPixelsPerUnit`, so a taller dock shows more. Optional whole-number pixel scaling for pixel art. |
| `DockTransparentCamera` | Clears the camera to transparent black and, in URP, turns off camera dithering. Uses an opaque color when transparency is off. |

## Ready-made pieces

| Component | What it does |
|---|---|
| `DockTrayIcon` | An icon in the Windows notification area (the menu bar on macOS), using the game's icon from Player Settings. Left-click shows or hides the dock. Right-click opens a menu: Show/Hide, Dock to (edge), Always on top, Auto-hide, Start with Windows, your items (`AddMenuItem(label, onClick, isChecked)`), Quit. Put it on the controller's GameObject. |
| `DockResizeHandle` (uGUI) | A grip on the dock's inner edge. Dragging it, or turning the mouse wheel over it (`scrollStep` pixels per notch, default 20), changes the thickness within the dock's `ThicknessLimits`. `Resized` is raised as it changes, and the size is saved when the drag ends or the wheel stops. `DockResizeHandle.Create(canvasTransform)` makes one. |
| `DockHotkey` | A system-wide shortcut, such as `Ctrl+Alt+D`, that works while another app has focus. The default action shows a hidden dock, brings back a tucked-away one, or else hides it; `onPressed` lets you add your own. Needs Ctrl, Alt, Shift or Win (except F13–F24). If another app already has the combination, Windows refuses it and a warning is logged; `SetShortcut("Ctrl+Shift+F9")` changes it at runtime. If another Desktop Dock game holds it, that game's press shows or hides this dock too (`affectAllDocks`), and this one takes the shortcut over when the other quits. One component per shortcut. Windows only. |
| `DockMoveHandle` (uGUI) | Put it on any UI element, such as a "Move" button. Pressing and dragging it moves the dock to the nearest edge of the monitor under the cursor, live, and saves when released. `hysteresis` (48 px) stops it flickering between two edges near a corner. |
| `DockSettingsPanel` (uGUI) | Edge, size, length, alignment, monitor, always on top, taskbar button, auto-hide, reserve screen space, step aside for fullscreen, soft transparency (PerPixelAlpha), start with Windows, and reset. Covers the dock while open; fills columns in a horizontal dock and rows in a vertical one, and scrolls if needed. `DockSettingsPanel.Create(canvas)`, then `Open()`, `Close()` or `Toggle()`. |

The uGUI pieces need a Screen Space Overlay canvas and an EventSystem. They're built from code with no assets, so restyle them or use them as a starting point for your own UI.

**Testing in the Editor:** make Game view resolutions such as `1920×220` and `220×1080`. The controller simulates the configured edge, and in play mode the inspector has edge buttons.

## How it works (Windows)

1. Finds the player's `UnityWndClass` window.
2. Restyles it as a borderless `WS_POPUP`.
3. **ColorKey:** makes it layered with a black color key and calls `DwmExtendFrameIntoClientArea(-1)`. In testing, exactly the pure-black pixels were see-through and Windows ignored the alpha channel.
   **PerPixelAlpha:** makes the game window layered with a uniform alpha of 1/255. That makes it invisible, but it is still the window that gets the mouse and keyboard. A second layered window, owned by the game window and always click-through (`WS_EX_TRANSPARENT`), sits directly above it. After each frame, `ScreenCapture.CaptureScreenshotIntoRenderTexture` copies the backbuffer (overlay UI included). A small shader pass converts it to premultiplied alpha, `AsyncGPUReadback` reads it back as BGRA, and `UpdateLayeredWindow` hands it to the overlay window. The desktop compositor then blends it per pixel.
   Why two windows: with a flip-model swapchain, which Direct3D12 and Vulkan always use, DWM draws the game window's swapchain opaque on top of anything `UpdateLayeredWindow` gives that window, and ignores a color key. A uniform layered alpha is still applied, though. Direct3D11 without flip model could use `UpdateLayeredWindow` on the game window directly, but one path for every API is simpler.
4. Places it with `SetWindowPos`, using `HWND_TOPMOST` when pinned.
5. Click-through: each frame it reads the cursor with `GetCursorPos` (Unity gets no mouse messages while click-through) and hit-tests that point. Then it toggles `WS_EX_TRANSPARENT`, but never while a mouse button is held.
6. Reserved space uses `SHAppBarMessage` (`ABM_NEW`/`QUERYPOS`/`SETPOS`). It is released when the game quits or the dock is hidden.
7. Fullscreen apps: twice a second it checks whether another process's focused window covers the dock's monitor (a maximized window with a title bar doesn't count). If so it sends the dock to the bottom of the z-order (`HWND_BOTTOM`) or hides it.
8. The tray icon uses `Shell_NotifyIcon` with a hidden window of its own. Unity's message loop delivers its messages; menu commands run on the next frame.
9. Start with Windows writes the per-user `Run` registry key, and treats an entry turned off in Task Manager as off.
10. Auto-hide moves the window with `SetWindowPos` (no resize, so no new backbuffer) and puts it just below `Shell_TrayWnd` in the z-order while it's away.
11. Hotkeys use `RegisterHotKey` with a hidden message-only window that receives `WM_HOTKEY`.
12. When click-through switches off, the dock posts one `WM_MOUSEMOVE` with the cursor's current position to the game window. Otherwise Unity would still have the position from before the window became click-through, and a click before the next real move would land in the wrong place.

### Known limitations

- **ColorKey: pure black `(0,0,0)` pixels become see-through and click-through** because black is the color key. Use near-black such as `#010101` in art and UI.
- **ColorKey: no partial transparency.** Semi-transparent pixels (soft shadows, glows, anti-aliased edges) are drawn against black and shown fully opaque, so they look darker. Hard-edged art, such as pixel art, looks exactly right. Use PerPixelAlpha if you need soft edges.
- **ColorKey: Direct3D11 only.** D3D12 and Vulkan always use the flip-model swapchain, and that renders the transparent background black. The dock therefore switches to PerPixelAlpha by itself in those builds.
- **ColorKey: the `VisiblePixel` hit test reads back one pixel per frame.** If your content all has colliders or UI, drop `VisiblePixel` from `hitTestSources` to skip it.
- **PerPixelAlpha: frames reach the screen one or two frames late**, and a readback costs time in proportion to the dock's area. Fine for a dock strip, wasteful for a huge window.
- **PerPixelAlpha: the see-through areas are darkened by at most one level out of 255.** That comes from the nearly invisible game window underneath.
- **PerPixelAlpha: window captures and previews show the game window itself**, which is nearly invisible. Display capture is fine. Taskbar and Alt+Tab thumbnails are likely to look empty (not checked).
- **Full-screen raycast-target UI images** (such as an invisible background panel) count as content and block click-through. Turn off *Raycast Target* on them.
- **A crash leaves the reserved strip in place** if *Reserve Screen Space* was on. Windows reclaims the space when Explorer notices the window is gone, or you can restart Explorer.
- **Window size is in physical pixels.** Use `PercentOfScreen` if you want the dock to scale with the display.
- **The game pauses while the tray menu is open**, because Windows runs the menu on the game's main thread.
- **Auto-hide on a monitor edge shared with another monitor**: the part of the dock that slides away shows on the neighbouring monitor.
- **Global hotkeys are Windows only.** On macOS, `DockHotkey` does nothing yet.
- **Start with Windows points at the current executable.** If the game moves (for example a Steam library move), `IsEnabled` reports false until `SetEnabled(true)` is called again.

## macOS (experimental)

> **Only lightly tested.** The macOS platform was written on Windows without a Mac. The compiled plugin now ships in `Plugins/macOS/DesktopDockMac.bundle` (universal: arm64 and x86_64, signed ad hoc). On a first run (Apple M4, macOS 26.7, Unity 6000.3.10f1) all EditMode tests passed and the Basic Dock sample docked as a transparent, borderless, full-width strip along the bottom of the screen that takes clicks. Nothing else in this section has been checked yet. Please report what works.

### Rebuild the native plugin

The package ships the compiled plugin, so a Mac build works as is. You only need to rebuild it after changing its source, `Plugins~/macOS/DesktopDockMac.m`. On a Mac with the Xcode command line tools (`xcode-select --install`), run this from the folder that contains `DesktopDock`:

```sh
sh DesktopDock/Plugins~/macOS/build.sh
```

It builds a universal (Apple silicon + Intel) `DesktopDock/Plugins/macOS/DesktopDockMac.bundle` for macOS 10.13+, signs it ad hoc, and replaces the shipped one. Unity imports it from there. The bundle's `.meta` already sets its platforms: **Standalone → macOS** (any CPU) only, not the Editor or other platforms, since the native calls only run in a macOS player. Keep that `.meta` when you rebuild, and commit the bundle with it if you share the package.

If the bundle is missing, a macOS build logs a warning and doesn't dock. Everything else keeps working.

### Player Settings (macOS)

- **Fullscreen Mode: Windowed.** **Tools → Desktop Dock → Apply Recommended Player Settings** sets this for every platform. Its graphics-API checks only apply to Windows; Metal is fine.
- **Mac Retina Support** can be on or off. Desktop coordinates are in the pixels Unity renders, so with it on, `thicknessPixels` covers half as many points on a Retina screen.
- **UI on Retina screens:** with Retina Support on, a Constant Pixel Size canvas shows buttons and text at half size. Add **Desktop Dock → Dock Canvas Scale** to the canvas: it multiplies the canvas's scale factor by `DesktopDockController.PixelsPerPoint` (2 on Retina, 1 on Windows and in the Editor). The Basic Dock sample does this.
- **Allow Fullscreen Switch** off, so ⌘F or the green button can't make the dock fullscreen.

### What it does

The source is `Plugins~/macOS/DesktopDockMac.m` (Objective-C). The C# side is `Runtime/Platform/macOS/`.

| Feature | macOS equivalent |
|---|---|
| Find the window | The main window, or a visible window that renders with Metal |
| Borderless | `styleMask = NSWindowStyleMaskBorderless`. The window's class is swapped for a runtime subclass that can still become key, so the keyboard keeps working. |
| Transparent background | `opaque = NO`, clear `backgroundColor`, no shadow, and the view's layers made non-opaque |
| Monitors | `NSScreen.screens`. `workArea` is `visibleFrame`, so the dock stays clear of the menu bar and the Dock. |
| Always on top | `NSFloatingWindowLevel`. This is below the menu bar and the Dock. |
| Taskbar button | The Dock icon and Cmd-Tab entry (`NSApp` activation policy Regular or Accessory) |
| All Spaces | `canJoinAllSpaces`, `stationary`, `ignoresCycle`, `fullScreenAuxiliary` |
| Click-through | `ignoresMouseEvents` |
| Cursor and buttons | `NSEvent.mouseLocation` and `pressedMouseButtons`. Both are global and need no permissions. |
| Show / hide / send to back | `orderFrontRegardless` (doesn't take focus), `orderOut:`, and `orderBack:` at normal level |
| Fullscreen apps | `CGWindowListCopyWindowInfo`: a window of the frontmost app covers the monitor. Any level below the screen saver counts, including games that raise themselves above the menu bar. A zoomed window doesn't count, since it stops at the menu bar. On notched screens, a window may stop below the notch. Only window bounds are read, so no Screen Recording permission is needed. |
| Tray icon | `NSStatusItem` in the menu bar with the app icon. With `leftClickTogglesDock`, left-click toggles and right-click or Control-click opens the menu. Without it, any click opens the menu. |
| Start at login | `SMAppService.mainAppService` (macOS 13+). The menu item is called **Open at Login**. It may need approval in System Settings → General → Login Items. While it waits, the toggle shows it as on, so the player can turn it back off. The game must be a signed `.app`; otherwise macOS reports "not found" and the option is hidden. |
| Reserve screen space | No equivalent. It is ignored, with a log message. |

Coordinates: Cocoa uses points with y up, from the bottom-left of the primary screen. The package uses pixels with y down, from the top-left. `MacCoordinates` converts between them and is unit-tested. It uses one pixels-per-point scale for the whole desktop: whatever Unity renders per point in the window. With monitors of different scales, the dock re-docks when the scale changes.

### Transparency on macOS works differently

On Windows, the default ColorKey mode makes pure black the color key and ignores alpha. On macOS, the window is always composited with the backbuffer's **alpha**, premultiplied, so `DockConfig.transparencyMode` has no effect: it behaves like PerPixelAlpha without the frame copy, and the settings panel's Soft transparency toggle shows it locked on.

- `DockTransparentCamera` clears to `(0,0,0,0)`, which is see-through on both platforms.
- **Partial transparency works**: soft shadows and anti-aliased edges blend with the desktop. Colors must be premultiplied, or semi-transparent pixels look too bright.
- **Pure black content is opaque**, as long as its alpha is 1.
- **Anything that throws away alpha makes the whole strip opaque.** URP does this with HDR or post-processing on. Use the built-in pipeline, or URP with both off on the dock camera, and check that your shaders write alpha.
- The `VisiblePixel` hit test treats a pixel as see-through only when it is `(0,0,0,0)`, which matches what the compositor shows.

### Things to check first on a real Mac

These are the parts most likely to need fixing:

1. **Unity's window size vs. the native frame.** The controller calls `Screen.SetResolution` and then sets the frame itself. Check that Unity doesn't fight it, or put its title bar back, after a resolution change. The placement guard re-applies the style whenever the frame drifts.
2. **Retina scale.** Check that `Screen.width` matches the dock rect, with Retina Support both on and off.
3. **Keyboard focus** after going borderless (the runtime subclass) and after changing the activation policy.
4. **Rendering while hidden.** Check that `orderOut:` doesn't stall Unity's Metal drawable loop.
5. **Over other apps' fullscreen Spaces.** With Fullscreen Behavior set to Ignore, `fullScreenAuxiliary` with the floating level should show the dock there. With Drop Behind or Hide, it should step aside.
6. **Menu callbacks into C#** (IL2CPP reverse P/Invoke), and whether the game keeps rendering while the status menu is open.

## Tests

EditMode tests live in `Tests/Editor` and cover the dock geometry, resize and fullscreen math, config saving, the start-with-Windows helpers, the tray menu, the macOS coordinate conversions, and PerPixelAlpha's hit test and color conversion (the conversion test runs the shader on the GPU). To run them in a project, add the package to `testables` in `Packages/manifest.json` (the Desktop Dock sample project does):

```json
"testables": ["com.desktopdock.unity"]
```

Then open **Window → General → Test Runner → EditMode** and run all.

The PerPixelAlpha shader tests run on the GPU, so leave out `-nographics` when running tests from the command line. Without a GPU they fail with every pixel reading 205.
