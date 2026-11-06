#if RISV4_JSON
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    // Local style copies keep the original three-column editor and other tools unchanged.
    internal static class KaiEditorUI
    {
        internal const float ControlHeight = 32f;
        internal const float LabelWidth = 148f;
        static bool dark;
        static GUIStyle label, title, button, popup, toggle, tabs, hint, sectionHeading;
        static Texture2D creditPortrait;
        internal const string CreditProfileUrl = "https://x.com/L1ZE_ragram1";
        static GUIStyle profileButton;

        internal static void DrawCredit()
        {
            if (creditPortrait == null)
                creditPortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/net.l1zerox.radialinventory/Editor/Artwork/Rize.png");
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                // Display the original sheet's first idle frame without animation or modifying the artwork.
                var portraitRect = GUILayoutUtility.GetRect(96f, 104f, GUILayout.Width(96f), GUILayout.Height(104f));
                if (creditPortrait != null)
                    GUI.DrawTextureWithTexCoords(portraitRect, creditPortrait, new Rect(0f, 10f / 11f, 1f / 8f, 1f / 11f), true);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(52f)))
                {
                    GUILayout.Space(40);
                    GUILayout.Label("リゼ", Title);
                }
                GUILayout.Space(20);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(224f)))
                {
                    GUILayout.Space(34);
                    EnsureStyles();
                    var linkRect = GUILayoutUtility.GetRect(224f, 36f, GUILayout.Width(224f), GUILayout.Height(36f));
                    if (GUI.Button(linkRect, new GUIContent("@L1ZE_ragram1", "Xのプロフィールをブラウザで開きます\n" + CreditProfileUrl), profileButton))
                        Application.OpenURL(CreditProfileUrl);
                    EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Link);
                    DrawXIcon(new Rect(linkRect.x + 12f, linkRect.y + 8f, 20f, 20f));
                }
                GUILayout.FlexibleSpace();
            }
        }

        static void DrawXIcon(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var previousColor = Handles.color;
            Handles.BeginGUI();
            try
            {
                Handles.color = EditorGUIUtility.isProSkin ? Color.white : new Color(0.12f, 0.12f, 0.12f);
                // Vector outline avoids missing glyphs on fonts without the X brand symbol.
                Vector3 a = new Vector3(rect.x + 1f, rect.y + 1f);
                Vector3 b = new Vector3(rect.x + 6f, rect.y + 1f);
                Vector3 c = new Vector3(rect.x + 19f, rect.y + 19f);
                Vector3 d = new Vector3(rect.x + 14f, rect.y + 19f);
                Handles.DrawAAPolyLine(1.8f, a, b, c, d, a);
                Handles.DrawAAPolyLine(1.8f, new Vector3(rect.x + 19f, rect.y + 1f), new Vector3(rect.x + 11f, rect.y + 9f));
                Handles.DrawAAPolyLine(1.8f, new Vector3(rect.x + 9f, rect.y + 11f), new Vector3(rect.x + 1f, rect.y + 19f));
            }
            finally
            {
                Handles.color = previousColor;
                Handles.EndGUI();
            }
        }

        static void EnsureStyles()
        {
            if (label != null && dark == EditorGUIUtility.isProSkin) return;
            dark = EditorGUIUtility.isProSkin;
            label = new GUIStyle(EditorStyles.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            title = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = ControlHeight, padding = new RectOffset(12, 12, 5, 5) };
            profileButton = new GUIStyle(button) { fixedHeight = 36f, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(42, 12, 5, 5) };
            popup = new GUIStyle(EditorStyles.popup) { fontSize = 14, fixedHeight = ControlHeight, alignment = TextAnchor.MiddleLeft };
            toggle = new GUIStyle(EditorStyles.toggle) { fontSize = 13, fixedHeight = ControlHeight, alignment = TextAnchor.MiddleLeft };
            tabs = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = ControlHeight, padding = new RectOffset(14, 14, 5, 5) };
            hint = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true, padding = new RectOffset(6, 6, 4, 4) };
            sectionHeading = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fixedHeight = 24 };
        }
        internal static GUIStyle Label { get { EnsureStyles(); return label; } }
        internal static GUIStyle Title { get { EnsureStyles(); return title; } }
        internal static GUIStyle Button { get { EnsureStyles(); return button; } }
        internal static GUIStyle Popup { get { EnsureStyles(); return popup; } }
        internal static GUIStyle Toggle { get { EnsureStyles(); return toggle; } }
        internal static GUIStyle Tabs { get { EnsureStyles(); return tabs; } }
        internal static GUIStyle Hint { get { EnsureStyles(); return hint; } }
        internal static GUIStyle SectionHeading { get { EnsureStyles(); return sectionHeading; } }
    }
}
#endif
