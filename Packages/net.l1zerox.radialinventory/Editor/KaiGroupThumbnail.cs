#if RISV4_JSON
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    internal static class KaiGroupThumbnail
    {
        sealed class State
        {
            public bool Pending, Manual;
            public string AttemptedSignature;
            public GUIDPathPair<Texture2D> Icon;
            public string IconGuid, Error;
        }
        static readonly ConditionalWeakTable<Group, State> states = new ConditionalWeakTable<Group, State>();
        public static void Draw(Avatar data, int groupIndex, float width)
        {
            if (data == null || groupIndex < 0 || groupIndex >= data.Groups.Count) return;
            var group = data.Groups[groupIndex];
            var root = data.GetAvatarRoot();
            if (root == null) return;
            var state = states.GetValue(group, _ => new State());
            Observe(data, group);
            var icon = group.Icon?.GetObject();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(width - 8)))
            {
                var name = string.IsNullOrEmpty(group.Name) ? "グループ" + groupIndex : group.Name;
                EditorGUILayout.LabelField(name + " の全身画像", EditorStyles.boldLabel);
                var rect = GUILayoutUtility.GetRect(width - 20, Mathf.Clamp(width - 20, 180, 280), GUILayout.ExpandWidth(false));
                if (icon != null) EditorGUI.DrawTextureTransparent(rect, icon, ScaleMode.ScaleToFit);
                else GUI.Box(rect, state.Pending ? "全身を撮影中…" : "アバターの全身画像", EditorStyles.helpBox);
                EditorGUILayout.LabelField("このグループの衣装を着せて全身を撮影", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(state.Pending))
                    if (GUILayout.Button(icon == null ? "全身を撮影" : "全身を撮り直す")) Request(data, group, true);
                if (!string.IsNullOrEmpty(state.Error)) EditorGUILayout.HelpBox(state.Error, MessageType.Warning);
            }
        }
        static void Observe(Avatar data, Group group)
        {
            if (!data.KaiAutoThumbnail || data.GetAvatarRoot() == null) return;
            var state = states.GetValue(group, _ => new State());
            var signature = KaiThumbnail.GroupSignature(data, group);
            var icon = group.Icon?.GetObject();
            // User-supplied images stay untouched. Refresh only missing or tool-owned images.
            bool owned = icon != null && !string.IsNullOrEmpty(group.KaiIconID) && Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(icon)) == group.KaiIconID;
            bool duplicate = !string.IsNullOrEmpty(group.KaiIconID) && data.Groups.Any(g => !ReferenceEquals(g, group) && g.KaiIconID == group.KaiIconID);
            if ((icon == null || (owned && (group.KaiThumbnailSignature != signature || duplicate))) && state.AttemptedSignature != signature)
                Request(data, group, false);
        }
        static void Request(Avatar data, Group group, bool manual)
        {
            var state = states.GetValue(group, _ => new State());
            if (state.Pending) return;
            state.AttemptedSignature = KaiThumbnail.GroupSignature(data, group);
            state.Pending = true;
            state.Manual = manual;
            state.Icon = group.Icon;
            state.IconGuid = group.Icon?.ObjectGUID;
            state.Error = null;
            EditorApplication.delayCall += () => Finish(data, group);
        }
        static void Finish(Avatar data, Group group)
        {
            var state = states.GetValue(group, _ => new State());
            if (!state.Pending) return;
            state.Pending = false;
            // A removed group or a manually replaced icon must not be updated by an old request.
            if (!state.Manual && !data.KaiAutoThumbnail) { state.AttemptedSignature = null; return; }
            if (!data.Groups.Contains(group) || !ReferenceEquals(group.Icon, state.Icon) || group.Icon?.ObjectGUID != state.IconGuid) return;
            try { KaiThumbnail.CaptureGroup(data, group); }
            catch (Exception ex) { state.Error = "撮影できませんでした: " + ex.Message; Debug.LogWarning("[衣装簡単切替ツール] " + state.Error); }
            foreach (var window in Resources.FindObjectsOfTypeAll<MainWindow>()) window.Repaint();
        }
        public static void CompletePending(Avatar data)
        {
            foreach (var group in data.Groups)
            {
                Observe(data, group);
                Finish(data, group);
            }
        }
    }
}
#endif
