# DesktopDockMac native plugin (experimental, untested)

Source for `DesktopDockMac.bundle`, the native half of Desktop Dock's macOS platform (`Runtime/Platform/macOS`).
It has never been compiled or run: it was written without a Mac.

Build on a Mac with the Xcode command line tools:

```sh
sh DesktopDock/Plugins~/macOS/build.sh            # -> DesktopDock/Plugins/macOS/DesktopDockMac.bundle
```

Unity ignores this `Plugins~` folder, so the source never ends up in a build. The package README's
"macOS (experimental, untested)" section covers Player Settings, what each feature maps to, and what to check first.
