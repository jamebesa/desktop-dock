using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace DesktopDock.Editor
{
    /// <summary>
    /// Checks and fixes the Player Settings a docked, transparent Windows build depends on.
    /// Tools > Desktop Dock > Apply Recommended Player Settings.
    /// </summary>
    public static class DockPlayerSettings
    {
        private const string Menu = "Tools/Desktop Dock/";

        /// <param name="colorKey">Include the settings only ColorKey transparency needs (Direct3D11, no flip model).
        /// PerPixelAlpha works with any graphics API and swapchain.</param>
        public static List<string> FindIssues(bool colorKey = true)
        {
            var issues = new List<string>();
            if (PlayerSettings.fullScreenMode != FullScreenMode.Windowed)
                issues.Add("Fullscreen Mode should be Windowed (the dock is a window).");
            if (!PlayerSettings.resizableWindow)
                issues.Add("Resizable Window should be on so the player follows the dock's size.");
            if (!PlayerSettings.runInBackground)
                issues.Add("Run In Background should be on: a desktop idler keeps playing while you work.");
            if (!PlayerSettings.visibleInBackground)
                issues.Add("Visible In Background should be on.");
            if (PlayerSettings.allowFullscreenSwitch)
                issues.Add("Allow Fullscreen Switch should be off so Alt+Enter can't undock the window.");
            if (!colorKey) return issues;
            if (PlayerSettings.useFlipModelSwapchain)
                issues.Add("Use DXGI flip model swapchain must be off for ColorKey transparency (the default). " +
                           "Otherwise the dock falls back to PerPixelAlpha at runtime.");
            if (!UsesD3D11First())
                issues.Add("Windows graphics API should be Direct3D11 (listed first, Auto Graphics API off) for ColorKey transparency (the default). " +
                           "With Direct3D12 or Vulkan the dock falls back to PerPixelAlpha at runtime.");
            return issues;
        }

        private static bool UsesD3D11First()
        {
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64)) return false;
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            return apis.Length > 0 && apis[0] == GraphicsDeviceType.Direct3D11;
        }

        [MenuItem(Menu + "Apply Recommended Player Settings")]
        public static void ApplyRecommendedMenu() => ApplyRecommended();

        [MenuItem(Menu + "Apply Recommended Player Settings (PerPixelAlpha, keep graphics API)")]
        public static void ApplyRecommendedPerPixelMenu() => ApplyRecommended(colorKey: false);

        /// <param name="colorKey">Also make Direct3D11 the first graphics API and turn the flip-model swapchain off,
        /// which ColorKey transparency needs. Pass false for a PerPixelAlpha game to keep its graphics API choice.</param>
        public static void ApplyRecommended(bool colorKey = true)
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = false;
            // Start small so the undocked window barely flashes before the dock takes over.
            PlayerSettings.defaultScreenWidth = 640;
            PlayerSettings.defaultScreenHeight = 160;

            if (!colorKey)
            {
                Debug.Log("[DesktopDock] Applied recommended Player Settings for a docked Windows build (graphics API and swapchain left as they are).");
                return;
            }

            PlayerSettings.useFlipModelSwapchain = false;
            foreach (var target in new[] { BuildTarget.StandaloneWindows64, BuildTarget.StandaloneWindows })
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
                var apis = PlayerSettings.GetGraphicsAPIs(target).Where(a => a != GraphicsDeviceType.Direct3D11).ToList();
                apis.Insert(0, GraphicsDeviceType.Direct3D11);
                PlayerSettings.SetGraphicsAPIs(target, apis.ToArray());
            }

            Debug.Log("[DesktopDock] Applied recommended Player Settings for a docked Windows build.");
        }

        [MenuItem(Menu + "Check Player Settings")]
        public static void Validate()
        {
            var issues = FindIssues();
            if (issues.Count == 0)
            {
                EditorUtility.DisplayDialog("Desktop Dock", "Player Settings look good for a docked Windows build.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog("Desktop Dock",
                    "These Player Settings will stop the dock working properly:\n\n• " + string.Join("\n• ", issues),
                    "Fix them", "Leave them"))
                ApplyRecommended();
        }

        /// <summary>Warns (doesn't fail) when building for Windows with settings that break the dock.</summary>
        private sealed class BuildCheck : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                var t = report.summary.platform;
                if (t != BuildTarget.StandaloneWindows && t != BuildTarget.StandaloneWindows64) return;
                foreach (var issue in FindIssues())
                    Debug.LogWarning("[DesktopDock] " + issue + " (Tools > Desktop Dock > Apply Recommended Player Settings)");
                // Which transparency mode the game uses lives on a component in a scene, so this can't tell; the
                // DesktopDockController inspector only lists the API/swapchain issues when ColorKey is selected.
            }
        }
    }
}
