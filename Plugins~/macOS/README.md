# DesktopDockMac native plugin (experimental)

Source for `DesktopDockMac.bundle`, the native half of Desktop Dock's macOS platform (`Runtime/Platform/macOS`).
The compiled, universal bundle ships in `Plugins/macOS/`. It's only lightly tested on a Mac.

To rebuild it after changing the source, on a Mac with the Xcode command line tools:

```sh
sh DesktopDock/Plugins~/macOS/build.sh            # -> DesktopDock/Plugins/macOS/DesktopDockMac.bundle
```

Unity ignores this `Plugins~` folder, so the source never ends up in a build. The package README's
"macOS (experimental)" section covers Player Settings, what each feature maps to, and what to check first.
