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

        internal static void DrawCredit()
        {
            if (creditPortrait == null)
                creditPortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/net.l1zerox.radialinventory/Editor/Artwork/Rize.png");
            if (creditPortrait == null) return;

            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                // Display the original sheet's first idle frame without animation or modifying the artwork.
                var portraitRect = GUILayoutUtility.GetRect(96f, 104f, GUILayout.Width(96f), GUILayout.Height(104f));
                GUI.DrawTextureWithTexCoords(portraitRect, creditPortrait, new Rect(0f, 10f / 11f, 1f / 8f, 1f / 11f), true);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(180f)))
                {
                    GUILayout.Space(30);
                    GUILayout.Label("リゼ", Title);
                    GUILayout.Label("dot アバター", Hint);
                }
                GUILayout.FlexibleSpace();
            }
        }

        static void EnsureStyles()
        {
            if (label != null && dark == EditorGUIUtility.isProSkin) return;
            dark = EditorGUIUtility.isProSkin;
            label = new GUIStyle(EditorStyles.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            title = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = ControlHeight, padding = new RectOffset(12, 12, 5, 5) };
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
