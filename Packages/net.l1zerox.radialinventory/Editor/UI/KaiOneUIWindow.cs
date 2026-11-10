#if RISV4_JSON
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YagihataItems.RadialInventorySystemV4
{
    // Keep the established preview and brush event handling inside its own IMGUI context.
    // The shell owns layout and scrolling; no global EditorStyles or skin are modified.
    internal static class KaiOneUIWindow
    {
        internal static void Build(EditorWindow window, string title, string subtitle, Action draw)
        {
            var root = window.rootVisualElement;
            root.Clear();
            var sheet = KaiPackageResources.Load<StyleSheet>("Editor/UI/WardrobeWindow.uss");
            if (!root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            KaiPackageResources.Load<VisualTreeAsset>("Editor/UI/ToolWindow.uxml").CloneTree(root);
            var shell = root.Q<VisualElement>("tool-shell");
            shell.EnableInClassList("light", !EditorGUIUtility.isProSkin);
            shell.Q<Label>("tool-title").text = title;
            shell.Q<Label>("tool-subtitle").text = subtitle;
            var content = new IMGUIContainer(() =>
            {
                float labelWidth = EditorGUIUtility.labelWidth;
                try { draw(); }
                finally { EditorGUIUtility.labelWidth = labelWidth; }
            });
            content.style.flexGrow = 1;
            shell.Q<ScrollView>("tool-content").Add(content);
        }
    }
}
#endif
