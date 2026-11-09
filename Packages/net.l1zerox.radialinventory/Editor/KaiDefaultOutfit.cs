#if RISV4_JSON
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiDefaultOutfit
    {
        public static bool Enabled(Avatar data) => data != null && data.KaiUseDefaultGroup && data.MenuMode != RIS.MenuModeType.Simple;
        public static bool Default(Avatar data, Group group, Prop prop) => Enabled(data) ? group.KaiDefaultGroup : prop.IsDefaultEnabled;
        public static void Select(Avatar data, Group group)
        {
            if (group != null && (!data.Groups.Contains(group) || !KaiGroupSwitch.Enabled(data, data.Groups.IndexOf(group))))
                throw new InvalidOperationException("排他設定のあるグループは「グループ内の排他を解除」をONにしてから初期衣装に選んでください。");
            data.KaiUseDefaultGroup = true;
            foreach (var item in data.Groups) item.KaiDefaultGroup = item == group;
            Enforce(data);
        }
        public static void Enforce(Avatar data)
        {
            if (!Enabled(data)) return;
            var chosen = data.Groups.FirstOrDefault(g => g.KaiDefaultGroup);
            foreach (var group in data.Groups)
            {
                group.KaiDefaultGroup = group == chosen;
                foreach (var prop in KaiPropBundle.Expand(group.Props)) prop.IsDefaultEnabled = group == chosen;
            }
        }
        public static void Draw(Avatar data)
        {
            if (data == null || data.MenuMode == RIS.MenuModeType.Simple) return;
            var labels = new[] { "個別の初期状態を使う", "なし（すべてOFF）" }.Concat(data.Groups.Select(g => g.Name)).ToArray();
            int index = !data.KaiUseDefaultGroup ? 0 : data.Groups.FindIndex(g => g.KaiDefaultGroup) + 2;
            int next = EditorGUILayout.Popup(new GUIContent("初期衣装", "最初に表示するグループ。このグループ内だけ初期状態をONにします。"), index, labels);
            if (next != index)
            {
                try { if (next == 0) data.KaiUseDefaultGroup = false; else Select(data, next == 1 ? null : data.Groups[next - 2]); }
                catch (Exception e) { EditorUtility.DisplayDialog("初期衣装", e.Message, "閉じる"); }
            }
            if (Enabled(data)) EditorGUILayout.HelpBox("選んだ衣装で開始します。前回の着脱状態より、この初期衣装を優先します。", MessageType.None);
            Enforce(data);
        }
        public static void DrawSaved(Avatar data, Prop prop)
        {
            using (new EditorGUI.DisabledScope(Enabled(data)))
            {
                bool saved = EditorGUILayout.Toggle(new GUIContent(RISStrings.GetString("save_status"), "初期衣装を選んでいる間は、再読み込み時に初期衣装を優先します。"), !Enabled(data) && prop.UseSaveParameter);
                if (!Enabled(data)) prop.UseSaveParameter = saved;
            }
        }
        public static string OutfitName(Avatar data, IEnumerable<GameObject> targets)
        {
            var root = data.GetAvatarRoot();
            if (root == null) return null;
            var first = new HashSet<Transform>();
            foreach (var target in targets.Where(t => t != null))
            {
                if (target == root.gameObject || !target.transform.IsChildOf(root.transform)) return null;
                var top = target.transform;
                while (top.parent != root.transform) top = top.parent;
                first.Add(top);
            }
            return first.Count == 1 ? first.Single().name : null;
        }
        public static void DrawName(Avatar data, Group group)
        {
            var root = data.GetAvatarRoot();
            string name = root == null ? null : OutfitName(data, group.Props.SelectMany(p => p.TargetObjects).Select(t => t?.GetObject(root.gameObject)));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(name)))
                if (GUILayout.Button(new GUIContent("衣装の親名に合わせる", name ?? "同じ衣装の親に属するプロップを登録すると使えます。"))) group.Name = name;
        }
    }
}
#endif
