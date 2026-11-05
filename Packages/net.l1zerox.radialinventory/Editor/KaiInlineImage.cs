#if RISV4_JSON
using System;
using UnityEditor;
using UnityEngine;
namespace YagihataItems.RadialInventorySystemV4
{
    internal static class KaiInlineImage
    {
        public static void Draw(Avatar avatar, Prop prop)
        {
            if (prop == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var icon = prop.Icon?.GetObject();
                EditorGUILayout.LabelField("衣装のサムネイル", EditorStyles.boldLabel);
                if (icon != null)
                {
                    var rect = GUILayoutUtility.GetRect(160, 160, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawTextureTransparent(rect, icon, ScaleMode.ScaleToFit);
                }
                else EditorGUILayout.HelpBox("衣装だけを透明背景で撮影します。自動モードでは衣装追加時に作成します。", MessageType.Info);
                if (GUILayout.Button(icon == null ? "撮影する" : "画像を撮り直す", GUILayout.Height(24)))
                {
                    try { KaiThumbnail.Capture(avatar, prop, 0, 1); }
                    catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("撮影", e.Message, "OK"); }
                }
                if (GUILayout.Button("構図…", GUILayout.Width(52), GUILayout.Height(24))) KaiWardrobeWindow.Open(avatar);
            }
        }
    }
}
#endif
