#if DESKTOPDOCK_UGUI
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DesktopDock.UI
{
    /// <summary>Builds the flat uGUI controls used by the package's ready-made UI, without any sprite assets.</summary>
    internal static class DockUI
    {
        // None of these are pure black, which the window would show as see-through.
        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.11f, 1f);
        public static readonly Color ControlColor = new Color(0.17f, 0.18f, 0.22f, 1f);
        public static readonly Color TrackColor = new Color(0.27f, 0.29f, 0.34f, 1f);
        public static readonly Color AccentColor = new Color(0.33f, 0.56f, 0.96f, 1f);
        public static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color MutedTextColor = new Color(0.6f, 0.62f, 0.67f, 1f);

        private static Font s_font;

        public static Font Font
        {
            get
            {
                if (s_font != null) return s_font;
#if UNITY_2022_2_OR_NEWER
                s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
                return s_font;
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float bottom = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static Image Image(string name, Transform parent, Color color, bool raycast = true)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Text(Transform parent, string value, int size, TextAnchor anchor, Color? color = null)
        {
            var text = Rect("Text", parent).gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color ?? TextColor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, out Text text, int fontSize = 12)
        {
            var img = Image(label, parent, ControlColor);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            if (onClick != null) button.onClick.AddListener(onClick);
            text = Text(img.transform, label, fontSize, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 2, 2);
            return button;
        }

        public static Toggle Toggle(Transform parent, string label, UnityAction<bool> onChanged, out Text text)
        {
            var bg = Image(label, parent, ControlColor);
            var toggle = bg.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = bg;

            var box = Image("Box", bg.transform, TrackColor, raycast: false);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f);
            brt.pivot = new Vector2(0, 0.5f);
            brt.sizeDelta = new Vector2(16, 16);
            brt.anchoredPosition = new Vector2(8, 0);

            var check = Image("Check", box.transform, AccentColor, raycast: false);
            Stretch(check.rectTransform, 3, 3, 3, 3);
            toggle.graphic = check;

            text = Text(bg.transform, label, 12, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 32, 4);
            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        public static Slider Slider(Transform parent, string name, float labelWidth, out Text label)
        {
            var cell = Image(name, parent, ControlColor);
            label = Text(cell.transform, name, 12, TextAnchor.MiddleLeft);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.sizeDelta = new Vector2(labelWidth, 0);
            lrt.anchoredPosition = new Vector2(8, 0);

            var root = Rect("Slider", cell.transform);
            root.anchorMin = new Vector2(0, 0.5f);
            root.anchorMax = new Vector2(1, 0.5f);
            root.offsetMin = new Vector2(labelWidth + 12, -8);
            root.offsetMax = new Vector2(-10, 8);
            var slider = root.gameObject.AddComponent<Slider>();

            var track = Image("Background", root, TrackColor);
            Stretch(track.rectTransform, 0, 0, 5, 5);

            var fillArea = Rect("Fill Area", root);
            Stretch(fillArea, 0, 0, 5, 5);
            var fill = Image("Fill", fillArea, AccentColor, raycast: false);
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Rect("Handle Slide Area", root);
            Stretch(handleArea, 5, 5);
            var handle = Image("Handle", handleArea, TextColor);
            handle.rectTransform.sizeDelta = new Vector2(10, 0);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            return slider;
        }
    }
}
#endif
