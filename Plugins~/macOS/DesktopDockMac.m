// DesktopDockMac: the native half of Desktop Dock's macOS platform (MacDockPlatform.cs).
//
// UNTESTED: written on Windows without a Mac. See README.md in this folder before relying on it.
//
// Conventions for every function here:
// - Rects and points are in Cocoa global coordinates: points (not pixels), origin at the bottom-left of the
//   primary screen (NSScreen.screens[0], the one with the menu bar), y grows upward. The C# side converts to
//   the package's desktop coordinates (pixels, origin top-left, y down). The one exception is
//   DD_IsFullscreenAppActive, which takes a Quartz (CGWindow) rect: points, origin top-left, y down.
// - Booleans cross the boundary as int (0 / 1) so the marshaling is the same under Mono and IL2CPP.
// - Strings come in as null-terminated UTF-8.
// - AppKit must only be touched on the main thread. Unity's macOS player runs scripts on the main thread, so
//   DDOnMain normally runs the block directly; it only hops threads if called from elsewhere.

#import <Cocoa/Cocoa.h>
#import <QuartzCore/QuartzCore.h>
#import <ServiceManagement/ServiceManagement.h>
#import <objc/runtime.h>
#import <unistd.h>

#define DD_EXPORT __attribute__((visibility("default")))

static NSWindow *g_window;

static void DDOnMain(void (^block)(void))
{
    if ([NSThread isMainThread]) block();
    else dispatch_sync(dispatch_get_main_queue(), block);
}

// ============================================================================================ window lookup

static BOOL DDHasMetalLayer(NSView *view)
{
    if (view == nil) return NO;
    if ([view.layer isKindOfClass:[CAMetalLayer class]]) return YES;
    for (CALayer *sub in view.layer.sublayers)
        if ([sub isKindOfClass:[CAMetalLayer class]]) return YES;
    for (NSView *child in view.subviews)
        if (DDHasMetalLayer(child)) return YES;
    return NO;
}

static BOOL DDIsCandidate(NSWindow *w)
{
    if (w == nil || w.contentView == nil || [w isKindOfClass:[NSPanel class]]) return NO;
    // Status items, menus and popovers are windows too; never restyle those.
    NSString *cls = NSStringFromClass([w class]);
    if ([cls hasPrefix:@"NSStatusBar"] || [cls containsString:@"Menu"] || [cls containsString:@"Popover"]) return NO;
    return YES;
}

/// Unity's player window: prefer the main/key window, then any visible window that renders with Metal.
static NSWindow *DDFindPlayerWindow(void)
{
    NSMutableArray<NSWindow *> *list = [NSMutableArray array];
    if (NSApp.mainWindow) [list addObject:NSApp.mainWindow];
    if (NSApp.keyWindow) [list addObject:NSApp.keyWindow];
    [list addObjectsFromArray:NSApp.windows];

    NSWindow *best = nil;
    int bestScore = -1;
    for (NSWindow *w in list)
    {
        if (!DDIsCandidate(w)) continue;
        int score = (DDHasMetalLayer(w.contentView) ? 4 : 0) + (w.isVisible ? 2 : 0);
        if (score > bestScore)
        {
            best = w;
            bestScore = score;
        }
    }
    return best;
}

// ============================================================================================ key-window subclass

static BOOL DDReturnYes(__unused id self, __unused SEL _cmd) { return YES; }

/// Borderless windows refuse to become key, which would cut the game off from the keyboard. Swap the window's
/// class for a runtime subclass that says yes. Only this one window object is affected.
static void DDAllowKeyWindow(NSWindow *w)
{
    Class cls = object_getClass(w);
    const char *name = class_getName(cls);
    if (strncmp(name, "DesktopDock_", 12) == 0) return; // already done
    if (strncmp(name, "NSKVONotifying_", 15) == 0)
    {
        // Re-classing a KVO-observed object breaks KVO. Leave it; only keyboard focus is affected.
        NSLog(@"[DesktopDock] Player window is being observed (KVO); it may not take keyboard focus while borderless.");
        return;
    }

    NSString *subName = [NSString stringWithFormat:@"DesktopDock_%s", name];
    Class sub = NSClassFromString(subName);
    if (sub == Nil)
    {
        sub = objc_allocateClassPair(cls, subName.UTF8String, 0);
        if (sub == Nil) return;
        const char *types = [[NSString stringWithFormat:@"%s@:", @encode(BOOL)] UTF8String];
        class_addMethod(sub, @selector(canBecomeKeyWindow), (IMP)DDReturnYes, types);
        class_addMethod(sub, @selector(canBecomeMainWindow), (IMP)DDReturnYes, types);
        objc_registerClassPair(sub);
    }
    object_setClass(w, sub);
}

// ============================================================================================ lifecycle

DD_EXPORT int DD_Initialize(void)
{
    __block int ok = 0;
    DDOnMain(^{
        g_window = DDFindPlayerWindow();
        ok = g_window != nil;
    });
    return ok;
}

DD_EXPORT void DD_Shutdown(void)
{
    DDOnMain(^{
        if (g_window) g_window.ignoresMouseEvents = NO;
        g_window = nil;
    });
}

// ============================================================================================ screens

DD_EXPORT int DD_GetScreenCount(void)
{
    __block int count = 0;
    DDOnMain(^{ count = (int)NSScreen.screens.count; });
    return count;
}

/// frame = whole screen; visible = minus the menu bar and the Dock.
DD_EXPORT int DD_GetScreen(int index,
                           double *x, double *y, double *w, double *h,
                           double *vx, double *vy, double *vw, double *vh,
                           double *scale)
{
    __block int ok = 0;
    DDOnMain(^{
        NSArray<NSScreen *> *screens = NSScreen.screens;
        if (index < 0 || index >= (int)screens.count) return;
        NSScreen *s = screens[(NSUInteger)index];
        NSRect f = s.frame, v = s.visibleFrame;
        *x = f.origin.x; *y = f.origin.y; *w = f.size.width; *h = f.size.height;
        *vx = v.origin.x; *vy = v.origin.y; *vw = v.size.width; *vh = v.size.height;
        *scale = s.backingScaleFactor;
        ok = 1;
    });
    return ok;
}

/// Height of the primary screen, needed to flip between y-up and y-down coordinates.
DD_EXPORT double DD_GetPrimaryScreenHeight(void)
{
    __block double h = 0;
    DDOnMain(^{
        NSArray<NSScreen *> *screens = NSScreen.screens;
        if (screens.count > 0) h = screens[0].frame.size.height;
    });
    return h;
}

// ============================================================================================ window style

// The style is re-applied on every resize step, so only change what differs: setting a window's or layer's
// opacity or background, even to the same value, makes AppKit redisplay the window, a visible flash mid-drag.
static void DDSetLayersOpaque(NSView *view, BOOL opaque)
{
    if (view == nil) return;
    CALayer *layer = view.layer;
    if (layer != nil)
    {
        if (layer.opaque != opaque) layer.opaque = opaque;
        CGColorRef clear = CGColorGetConstantColor(kCGColorClear);
        if (!opaque && (layer.backgroundColor == NULL || !CGColorEqualToColor(layer.backgroundColor, clear)))
            layer.backgroundColor = clear;
        for (CALayer *sub in layer.sublayers)
            if (sub.opaque != opaque) sub.opaque = opaque;
    }
    for (NSView *child in view.subviews) DDSetLayersOpaque(child, opaque);
}

DD_EXPORT void DD_ApplyStyle(int transparent, int showInDock)
{
    DDOnMain(^{
        NSWindow *w = g_window;
        if (w == nil) return;

        // Borderless. Changing the style mask can reset the first responder; put it back.
        NSResponder *responder = w.firstResponder;
        if (w.styleMask != NSWindowStyleMaskBorderless) w.styleMask = NSWindowStyleMaskBorderless;
        if (responder != nil && w.firstResponder != responder && [responder isKindOfClass:[NSView class]]
            && ((NSView *)responder).window == w)
            [w makeFirstResponder:responder];
        DDAllowKeyWindow(w);

        // A shadow would outline the visible content of a transparent window.
        if (w.hasShadow) w.hasShadow = NO;
        BOOL opaque = transparent ? NO : YES;
        if (w.opaque != opaque) w.opaque = opaque;
        NSColor *background = transparent ? [NSColor clearColor] : [NSColor blackColor];
        if (![w.backgroundColor isEqual:background]) w.backgroundColor = background;
        DDSetLayersOpaque(w.contentView, opaque);
        if (w.ignoresMouseEvents) w.ignoresMouseEvents = NO;

        // On every Space, unaffected by Mission Control, not in the Cmd-` cycle, never fullscreen itself, and
        // allowed over other apps' fullscreen windows (the C# side decides whether to step aside).
        // Some of these flags are mutually exclusive, and AppKit throws on a bad combination, so clear first.
        NSWindowCollectionBehavior b = w.collectionBehavior;
        b &= ~(NSWindowCollectionBehaviorMoveToActiveSpace | NSWindowCollectionBehaviorManaged
               | NSWindowCollectionBehaviorTransient | NSWindowCollectionBehaviorFullScreenPrimary
               | NSWindowCollectionBehaviorFullScreenNone);
        b |= NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorStationary
             | NSWindowCollectionBehaviorIgnoresCycle | NSWindowCollectionBehaviorFullScreenAuxiliary;
        if (w.collectionBehavior != b) w.collectionBehavior = b;

        // The Dock icon and Cmd-Tab entry are the macOS equivalent of the taskbar button.
        NSApplicationActivationPolicy want = showInDock ? NSApplicationActivationPolicyRegular
                                                        : NSApplicationActivationPolicyAccessory;
        if (NSApp.activationPolicy != want)
        {
            BOOL wasVisible = w.isVisible;
            [NSApp setActivationPolicy:want];
            if (wasVisible && !w.isVisible) [w orderFrontRegardless];
        }
    });
}

// ============================================================================================ placement

static void DDCollectMetalLayers(NSView *view, NSMutableArray<CAMetalLayer *> *out)
{
    if (view == nil) return;
    if ([view.layer isKindOfClass:[CAMetalLayer class]]) [out addObject:(CAMetalLayer *)view.layer];
    for (CALayer *sub in view.layer.sublayers)
        if ([sub isKindOfClass:[CAMetalLayer class]]) [out addObject:(CAMetalLayer *)sub];
    for (NSView *child in view.subviews) DDCollectMetalLayers(child, out);
}

/// While the dock is resized, the picture on screen is Unity's last frame, drawn at the previous size: Unity renders at
/// the size it had when the frame started, and during a drag the window changes again before Unity catches up. With
/// Core Animation's default it is stretched to fit, so buttons squash. Pinning it to one edge instead displaces
/// whatever sits at the other edge (the ground sinks off the screen, or the grip is cropped) for as long as the drag
/// lasts. So keep both edges exactly where they are and let a thin band across the middle of the picture, usually
/// empty space in a dock, take up the change: a nine-slice with an unscaled top and bottom (or left and right).
/// Growing stretches a 1-point band; shrinking squeezes a band as tall as the change down to 1 point. Once Unity
/// presents a frame at the window's size, the slicing has no effect.
static void DDKeepEdgesDuringResize(NSWindow *w, NSRect from, NSRect to)
{
    BOOL vertical = from.size.height != to.size.height, horizontal = from.size.width != to.size.width;
    if (!vertical && !horizontal) return;

    CGRect center = CGRectMake(0, 0, 1, 1); // both axes change (a move to another edge): plain stretch
    if (vertical != horizontal)
    {
        CGFloat old = vertical ? from.size.height : from.size.width;
        CGFloat delta = (vertical ? to.size.height : to.size.width) - old;
        CGFloat band = old > 1 ? MIN(old, (delta < 0 ? -delta : 0) + 1) / old : 1;
        center = vertical ? CGRectMake(0, 0.5 - band / 2, 1, band) : CGRectMake(0.5 - band / 2, 0, band, 1);
    }

    NSMutableArray<CAMetalLayer *> *layers = [NSMutableArray array];
    DDCollectMetalLayers(w.contentView, layers);
    [CATransaction begin];
    [CATransaction setDisableActions:YES]; // contentsCenter is animatable; it must change at once
    for (CAMetalLayer *layer in layers)
    {
        if (![layer.contentsGravity isEqualToString:kCAGravityResize]) layer.contentsGravity = kCAGravityResize;
        layer.contentsCenter = center;
    }
    [CATransaction commit];
}

/// Never shows a hidden window. alwaysOnTop uses the floating level, which stays below the menu bar and Dock.
DD_EXPORT void DD_SetFrame(double x, double y, double width, double height, int alwaysOnTop)
{
    DDOnMain(^{
        NSWindow *w = g_window;
        if (w == nil) return;
        NSWindowLevel level = alwaysOnTop ? NSFloatingWindowLevel : NSNormalWindowLevel;
        if (w.level != level) w.level = level;
        NSRect frame = NSMakeRect(x, y, width, height);
        if (NSEqualRects(w.frame, frame)) return;
        DDKeepEdgesDuringResize(w, w.frame, frame);
        [w setFrame:frame display:YES];
    });
}

DD_EXPORT int DD_GetFrame(double *x, double *y, double *w, double *h)
{
    __block int ok = 0;
    DDOnMain(^{
        if (g_window == nil) return;
        NSRect f = g_window.frame;
        *x = f.origin.x; *y = f.origin.y; *w = f.size.width; *h = f.size.height;
        ok = 1;
    });
    return ok;
}

/// The area Unity renders into (equal to the frame once the window is borderless).
DD_EXPORT int DD_GetContentRect(double *x, double *y, double *w, double *h)
{
    __block int ok = 0;
    DDOnMain(^{
        if (g_window == nil) return;
        NSRect c = [g_window contentRectForFrameRect:g_window.frame];
        *x = c.origin.x; *y = c.origin.y; *w = c.size.width; *h = c.size.height;
        ok = 1;
    });
    return ok;
}

/// Pixels per point of the screen the window is on (2 on Retina screens).
DD_EXPORT double DD_GetBackingScale(void)
{
    __block double scale = 1;
    DDOnMain(^{
        if (g_window != nil) scale = g_window.backingScaleFactor;
        else if (NSScreen.mainScreen != nil) scale = NSScreen.mainScreen.backingScaleFactor;
    });
    return scale;
}

DD_EXPORT void DD_SetVisible(int visible)
{
    DDOnMain(^{
        NSWindow *w = g_window;
        if (w == nil) return;
        // orderFrontRegardless shows the window without activating the app or taking keyboard focus.
        if (visible) [w orderFrontRegardless];
        else [w orderOut:nil];
    });
}

DD_EXPORT int DD_IsVisible(void)
{
    __block int visible = 0;
    DDOnMain(^{ visible = g_window != nil && g_window.isVisible; });
    return visible;
}

/// Drop to the normal level and behind every other window; DD_SetFrame restores the level.
DD_EXPORT void DD_SendToBack(void)
{
    DDOnMain(^{
        NSWindow *w = g_window;
        if (w == nil) return;
        w.level = NSNormalWindowLevel;
        if (w.isVisible) [w orderBack:nil];
    });
}

// ============================================================================================ input

DD_EXPORT void DD_SetIgnoresMouseEvents(int ignore)
{
    DDOnMain(^{
        if (g_window != nil) g_window.ignoresMouseEvents = ignore ? YES : NO;
    });
}

/// Global cursor position. Works while the window ignores mouse events, and needs no permissions.
DD_EXPORT void DD_GetMouseLocation(double *x, double *y)
{
    __block NSPoint p = NSZeroPoint;
    DDOnMain(^{ p = [NSEvent mouseLocation]; });
    *x = p.x;
    *y = p.y;
}

/// Bit 0 = left, 1 = right, 2 = middle, higher bits = other buttons. Global, needs no permissions.
DD_EXPORT int DD_GetPressedMouseButtons(void)
{
    __block NSUInteger buttons = 0;
    DDOnMain(^{ buttons = [NSEvent pressedMouseButtons]; });
    return (int)buttons;
}

// ============================================================================================ fullscreen apps

/// Whether the frontmost app (not us) has a window below the screen-saver level covering the given rect, which is a monitor's
/// bounds in Quartz coordinates (points, origin top-left of the primary screen, y down: the coordinate space
/// CGWindowListCopyWindowInfo reports in). Window bounds and owner PIDs need no Screen Recording permission.
/// A zoomed ("maximized") window doesn't count, because it stops at the menu bar; a native-fullscreen window or a
/// borderless fullscreen game does. On a screen with a camera notch, a fullscreen window may stop below the
/// notch, so the top edge is allowed to fall short by the screen's top safe-area inset.
DD_EXPORT int DD_IsFullscreenAppActive(double x, double y, double width, double height)
{
    __block int result = 0;
    DDOnMain(^{
        NSRunningApplication *front = NSWorkspace.sharedWorkspace.frontmostApplication;
        if (front == nil) return;
        pid_t pid = front.processIdentifier;
        if (pid == getpid()) return;

        double topSlack = 0.5;
        if (@available(macOS 12.0, *))
        {
            NSArray<NSScreen *> *screens = NSScreen.screens;
            double primaryHeight = screens.count > 0 ? screens[0].frame.size.height : 0;
            for (NSScreen *s in screens)
            {
                NSRect f = s.frame;
                double quartzY = primaryHeight - (f.origin.y + f.size.height);
                if (fabs(f.origin.x - x) < 1 && fabs(quartzY - y) < 1 && fabs(f.size.width - width) < 1
                    && fabs(f.size.height - height) < 1)
                {
                    topSlack += s.safeAreaInsets.top;
                    break;
                }
            }
        }

        // Borderless fullscreen games often raise their window above the menu bar (NSMainMenuWindowLevel + 1,
        // or the shielding level), so accept any layer from normal up to, not including, the screen saver.
        int maxLayer = (int)CGWindowLevelForKey(kCGScreenSaverWindowLevelKey);

        CFArrayRef list = CGWindowListCopyWindowInfo(
            kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, kCGNullWindowID);
        if (list == NULL) return;
        NSArray<NSDictionary *> *windows = CFBridgingRelease(list);
        for (NSDictionary *info in windows)
        {
            if ([info[(__bridge id)kCGWindowOwnerPID] intValue] != pid) continue;
            int layer = [info[(__bridge id)kCGWindowLayer] intValue];
            if (layer < 0 || layer >= maxLayer) continue;
            NSDictionary *boundsDict = info[(__bridge id)kCGWindowBounds];
            CGRect b;
            if (boundsDict == nil || !CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)boundsDict, &b))
                continue;
            if (b.origin.x <= x + 0.5 && b.origin.y <= y + topSlack
                && CGRectGetMaxX(b) >= x + width - 0.5 && CGRectGetMaxY(b) >= y + height - 0.5)
            {
                result = 1;
                break;
            }
        }
    });
    return result;
}

// ============================================================================================ status item (tray)

typedef void (*DDTrayPopulateFn)(void);
typedef void (*DDTrayCommandFn)(int command);

enum
{
    DDMenuChecked = 1,
    DDMenuDisabled = 2,
    DDMenuSeparator = 4,
    DDMenuHasChildren = 8,
};

@interface DDTrayController : NSObject <NSMenuDelegate>
@property(nonatomic, strong) NSStatusItem *item;
@property(nonatomic, strong) NSMenu *menu;
@property(nonatomic, assign) BOOL leftClickIsCommand;
@end

static DDTrayController *g_tray;
static DDTrayPopulateFn g_trayPopulate;
static DDTrayCommandFn g_trayCommand;
static NSMutableDictionary<NSNumber *, NSMenu *> *g_submenus; // only while the menu is being populated

@implementation DDTrayController

// Rebuilt from C# each time the menu opens, like the Windows tray menu.
- (void)menuNeedsUpdate:(NSMenu *)menu
{
    if (menu != self.menu) return;
    [menu removeAllItems];
    g_submenus = [NSMutableDictionary dictionary];
    if (g_trayPopulate) g_trayPopulate(); // calls DD_TrayMenuAdd
    g_submenus = nil;
}

- (void)itemClicked:(NSMenuItem *)sender
{
    if (g_trayCommand) g_trayCommand((int)sender.tag);
}

- (void)buttonClicked:(__unused id)sender
{
    NSEvent *e = NSApp.currentEvent;
    BOOL secondary = e.type == NSEventTypeRightMouseUp || e.type == NSEventTypeRightMouseDown
                     || (e.modifierFlags & NSEventModifierFlagControl) != 0;
    if (!secondary)
    {
        if (g_trayCommand) g_trayCommand(-1); // left click
        return;
    }
    // Show the menu for a right click: attach it just for this click.
    self.item.menu = self.menu;
    [self.item.button performClick:nil];
    self.item.menu = nil;
}

@end

/// leftClickIsCommand: 1 = a left click reports command -1 and a right click opens the menu (Windows style);
/// 0 = any click opens the menu (the usual macOS behaviour). The icon is the app icon.
DD_EXPORT int DD_TrayCreate(const char *tooltip, int leftClickIsCommand, DDTrayPopulateFn populate, DDTrayCommandFn command)
{
    __block int ok = 0;
    DDOnMain(^{
        if (g_tray != nil) return;
        g_trayPopulate = populate;
        g_trayCommand = command;

        DDTrayController *t = [DDTrayController new];
        t.item = [NSStatusBar.systemStatusBar statusItemWithLength:NSSquareStatusItemLength];
        if (t.item == nil) return;
        NSImage *icon = [NSApp.applicationIconImage copy];
        icon.size = NSMakeSize(18, 18);
        t.item.button.image = icon;
        t.item.button.toolTip = tooltip ? [NSString stringWithUTF8String:tooltip] : nil;

        t.menu = [[NSMenu alloc] initWithTitle:@""];
        t.menu.delegate = t;
        t.menu.autoenablesItems = NO;
        t.leftClickIsCommand = leftClickIsCommand != 0;
        if (t.leftClickIsCommand)
        {
            t.item.button.target = t;
            t.item.button.action = @selector(buttonClicked:);
            [t.item.button sendActionOn:NSEventMaskLeftMouseUp | NSEventMaskRightMouseUp];
        }
        else
        {
            t.item.menu = t.menu;
        }
        g_tray = t;
        ok = 1;
    });
    return ok;
}

DD_EXPORT void DD_TraySetTooltip(const char *tooltip)
{
    DDOnMain(^{
        if (g_tray != nil) g_tray.item.button.toolTip = tooltip ? [NSString stringWithUTF8String:tooltip] : nil;
    });
}

/// Only valid while the populate callback runs. parentId -1 = top level; otherwise the id of an item added
/// earlier with DDMenuHasChildren. Clicking an item reports its id through the command callback.
DD_EXPORT void DD_TrayMenuAdd(int parentId, int itemId, const char *label, int flags)
{
    DDOnMain(^{
        if (g_tray == nil || g_submenus == nil) return;
        NSMenu *parent = parentId < 0 ? g_tray.menu : g_submenus[@(parentId)];
        if (parent == nil) return;

        if (flags & DDMenuSeparator)
        {
            [parent addItem:[NSMenuItem separatorItem]];
            return;
        }

        NSString *title = label ? [NSString stringWithUTF8String:label] : @"";
        NSMenuItem *item = [[NSMenuItem alloc] initWithTitle:title ?: @"" action:nil keyEquivalent:@""];
        item.tag = itemId;
        item.enabled = (flags & DDMenuDisabled) == 0;
        item.state = (flags & DDMenuChecked) ? NSControlStateValueOn : NSControlStateValueOff;
        if (flags & DDMenuHasChildren)
        {
            NSMenu *sub = [[NSMenu alloc] initWithTitle:title ?: @""];
            sub.autoenablesItems = NO;
            item.submenu = sub;
            g_submenus[@(itemId)] = sub;
        }
        else
        {
            item.target = g_tray;
            item.action = @selector(itemClicked:);
        }
        [parent addItem:item];
    });
}

DD_EXPORT void DD_TrayRemove(void)
{
    DDOnMain(^{
        if (g_tray == nil) return;
        [NSStatusBar.systemStatusBar removeStatusItem:g_tray.item];
        g_tray.menu.delegate = nil;
        g_tray = nil;
        g_trayPopulate = NULL;
        g_trayCommand = NULL;
    });
}

// ============================================================================================ start at login

/// SMAppService.mainAppService status: 0 not registered, 1 enabled, 2 requires approval in System Settings,
/// 3 not found. -1 before macOS 13, where SMAppService doesn't exist.
DD_EXPORT int DD_LoginItemStatus(void)
{
    if (@available(macOS 13.0, *)) return (int)SMAppService.mainAppService.status;
    return -1;
}

/// 1 on success, 0 on failure (logged), -1 before macOS 13. Registering can leave the status at "requires
/// approval" until the user allows it in System Settings > General > Login Items.
DD_EXPORT int DD_LoginItemSetEnabled(int enabled)
{
    if (@available(macOS 13.0, *))
    {
        NSError *error = nil;
        BOOL ok = enabled ? [SMAppService.mainAppService registerAndReturnError:&error]
                          : [SMAppService.mainAppService unregisterAndReturnError:&error];
        if (!ok) NSLog(@"[DesktopDock] Login item %@ failed: %@", enabled ? @"register" : @"unregister", error);
        return ok ? 1 : 0;
    }
    return -1;
}
