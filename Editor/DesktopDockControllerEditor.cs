using UnityEditor;
using UnityEngine;

namespace DesktopDock.Editor
{
    [CustomEditor(typeof(DesktopDockController))]
    internal sealed class DesktopDockControllerEditor : UnityEditor.Editor
    {
        // Keep the live values current while playing.
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var transparent = serializedObject.FindProperty("defaults.transparentBackground");
            var mode = serializedObject.FindProperty("defaults.transparencyMode");
            bool colorKey = transparent == null || mode == null
                            || (transparent.boolValue && mode.enumValueIndex == (int)DockTransparencyMode.ColorKey);
            var issues = DockPlayerSettings.FindIssues(colorKey);
            if (issues.Count > 0)
            {
                EditorGUILayout.HelpBox("Player Settings need changes for a docked Windows build:\n• " +
                                        string.Join("\n• ", issues), MessageType.Warning);
                if (GUILayout.Button("Apply Recommended Player Settings"))
                    DockPlayerSettings.ApplyRecommended(colorKey);
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();

            if (!Application.isPlaying) return;

            var dock = (DesktopDockController)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("State", DesktopDockController.CurrentState?.ToString() ?? "—");
                EditorGUILayout.Toggle("Docking active", dock.IsDockingActive);
                EditorGUILayout.Toggle("Hidden", dock.IsHidden);
                EditorGUILayout.Toggle("Fullscreen app active", dock.IsFullscreenAppActive);
                EditorGUILayout.Toggle("Click-through now", dock.IsClickThrough);
                EditorGUILayout.LabelField("Transparency in use", dock.ActiveTransparencyMode.ToString());
            }
            if (!dock.IsDockingActive)
                EditorGUILayout.HelpBox("The Editor never restyles its own window; docking runs in Windows builds. " +
                                        "Layout components still receive the configured edge here.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            foreach (DockEdge edge in System.Enum.GetValues(typeof(DockEdge)))
                if (GUILayout.Button(edge.ToString()))
                    dock.SetEdge(edge);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(dock.IsHidden ? "Show" : "Hide")) dock.SetHidden(!dock.IsHidden);
            if (GUILayout.Button("Reset To Defaults")) dock.ResetToDefaults();
            EditorGUILayout.EndHorizontal();
        }
    }
}
