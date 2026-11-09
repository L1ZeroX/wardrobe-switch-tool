#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiPropBundle
    {
        public static bool IsMerged(Prop prop) => prop?.KaiMergedParts != null && prop.KaiMergedParts.Count > 0;
        public static IEnumerable<Prop> Expand(IEnumerable<Prop> props)
        {
            foreach (var prop in props.Where(p => p != null))
            {
                yield return prop;
                if (IsMerged(prop)) foreach (var child in Expand(prop.KaiMergedParts)) yield return child;
            }
        }
        public static IEnumerable<Prop> AllProps(Avatar data) => Expand(data.Groups.SelectMany(g => g.Props));
        static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        public static string Problem(Avatar data, Prop prop)
        {
            if (IsMerged(prop)) return "合体済みです。先に分離してください。";
            if (prop.EnableAnimation?.GetObject() != null || prop.DisableAnimation?.GetObject() != null || prop.MaterialOverrides.Count > 0 ||
                prop.UseResetTimer || prop.AttachedBone != RIS.BoneType.None || prop.Parameters.Count > 0 || prop.PropSets.Count > 0)
                return "独自アニメーション・マテリアル・タイマー・セット等の詳細設定があるため、個別のまま使用してください。";
            var root = data.GetAvatarRoot();
            if (root == null || prop.TargetObjects.Count == 0 || prop.TargetObjects.Any(t => t?.GetObject(root.gameObject) == null)) return "対象オブジェクトの参照を確認してください。";
            if (prop.TargetObjects.Any(t => t.GetObject(root.gameObject) == root.gameObject || !t.GetObject(root.gameObject).transform.IsChildOf(root.transform)))
                return "アバター配下の衣装オブジェクトを指定してください。";
            return null;
        }
        public static Prop Merge(Avatar data, Group group, IEnumerable<Prop> selected, string name)
        {
            if (data.MenuMode == RIS.MenuModeType.Simple || !data.Groups.Contains(group)) throw new InvalidOperationException("基本または詳細のグループを選んでください。");
            var members = selected.Distinct().OrderBy(p => group.Props.IndexOf(p)).ToArray();
            if (members.Length < 2 || members.Any(p => !group.Props.Contains(p))) throw new InvalidOperationException("同じグループから2個以上のプロップを選んでください。");
            foreach (var part in members) { var problem = Problem(data, part); if (problem != null) throw new InvalidOperationException(part.GetPropName(data) + ": " + problem); }
            var first = members[0];
            if (members.Any(p => p.IsLocalOnly != first.IsLocalOnly || p.UseSaveParameter != first.UseSaveParameter || p.ExclusiveGroup != first.ExclusiveGroup))
                throw new InvalidOperationException("ローカル動作・保存・排他グループが同じプロップを選んでください。");
            var before = group.Props.ToList();
            int at = before.IndexOf(first);
            foreach (var part in AllProps(data)) if (string.IsNullOrEmpty(part.KaiPartID)) part.KaiPartID = Guid.NewGuid().ToString("N");
            var merged = new Prop { Name = string.IsNullOrWhiteSpace(name) ? first.GetPropName(data) + " まとめ" : name.Trim(),
                KaiPartID = Guid.NewGuid().ToString("N"), IsDefaultEnabled = members.Any(p => p.IsDefaultEnabled),
                IsLocalOnly = first.IsLocalOnly, UseSaveParameter = first.UseSaveParameter, ExclusiveGroup = first.ExclusiveGroup,
                KaiMergedParts = members.ToList(), KaiMergedOffsets = members.Select(p => before.IndexOf(p) - at).ToList() };
            var root = data.GetAvatarRoot().gameObject;
            merged.TargetObjects = members.SelectMany(p => p.TargetObjects).GroupBy(t => t.GetObject(root)).Select(g => Copy(g.First())).ToList();
            KaiAutoThumbnail.Observe(data);
            group.Props.RemoveAll(p => members.Contains(p)); group.Props.Insert(at, merged);
            Remap(data, group, before, merged, false);
            KaiDefaultOutfit.Enforce(data);
            KaiAutoThumbnail.Observe(data);
            return merged;
        }
        public static void Split(Avatar data, Group group, Prop merged)
        {
            if (!data.Groups.Contains(group) || !group.Props.Contains(merged) || !IsMerged(merged)) throw new InvalidOperationException("分離するプロップを選んでください。");
            if (group.Props.Count - 1 + merged.KaiMergedParts.Count > 255) throw new InvalidOperationException("分離後に255プロップを超えます。先にグループを整理してください。");
            var before = group.Props.ToList(); int at = before.IndexOf(merged);
            KaiAutoThumbnail.Observe(data);
            group.Props.RemoveAt(at);
            for (int i = 0; i < merged.KaiMergedParts.Count; i++)
            {
                int offset = merged.KaiMergedOffsets != null && i < merged.KaiMergedOffsets.Count ? merged.KaiMergedOffsets[i] : i;
                group.Props.Insert(Mathf.Clamp(at + offset, 0, group.Props.Count), merged.KaiMergedParts[i]);
            }
            Remap(data, group, before, merged, true);
            KaiDefaultOutfit.Enforce(data);
            KaiAutoThumbnail.Observe(data);
        }
        static void Remap(Avatar data, Group group, List<Prop> before, Prop merged, bool split)
        {
            int groupIndex = data.Groups.IndexOf(group);
            Func<int, int, string, IEnumerable<IndexPair>> map = (g, p, hint) => {
                if (g != groupIndex || p < 0 || p >= before.Count) return new[] { new IndexPair { GroupIndex = g, PropIndex = p, KaiTargetPartID = hint } };
                var old = before[p];
                if (split && old == merged)
                {
                    var original = merged.KaiMergedParts.FirstOrDefault(x => x.KaiPartID == hint);
                    return (original != null ? new[] { original } : merged.KaiMergedParts.ToArray())
                        .Select(x => new IndexPair { GroupIndex = g, PropIndex = group.Props.IndexOf(x) });
                }
                bool collapsed = !split && merged.KaiMergedParts.Contains(old);
                return new[] { new IndexPair { GroupIndex = g, PropIndex = group.Props.IndexOf(collapsed ? merged : old), KaiTargetPartID = collapsed ? old.KaiPartID : hint } };
            };
            foreach (var prop in AllProps(data).ToArray())
            {
                prop.PropSets = prop.PropSets.Where(p => p != null).SelectMany(p => map(p.GroupIndex, p.PropIndex, p.KaiTargetPartID)).ToList();
                prop.Parameters = prop.Parameters.SelectMany(parameter => {
                    if (parameter == null) return new Parameter[] { null };
                    var match = Regex.Match(parameter.Name ?? "", @"^RISV4-G(\d+)P(\d+)$");
                    if (!parameter.IsRISParam || !match.Success) return new[] { parameter };
                    return map(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), parameter.KaiTargetPartID).Select(pair => {
                        var result = Copy(parameter); result.Name = "RISV4-G" + pair.GroupIndex + "P" + pair.PropIndex; result.KaiTargetPartID = pair.KaiTargetPartID; return result;
                    });
                }).ToList();
            }
        }
        public static void Draw(Avatar data, Group group, Prop selected)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(group == null || group.Props.Count < 2))
                    if (GUILayout.Button("プロップを合体…")) KaiPropBundleWindow.Open(data, group);
                using (new EditorGUI.DisabledScope(!IsMerged(selected)))
                    if (GUILayout.Button("分離する"))
                    {
                        try { Split(data, group, selected); GUIUtility.ExitGUI(); }
                        catch (ExitGUIException) { throw; }
                        catch (Exception e) { EditorUtility.DisplayDialog("プロップの分離", e.Message, "閉じる"); }
                    }
            }
        }
        public static void DrawDetails(Avatar data, Prop prop)
        {
            if (!IsMerged(prop)) return;
            EditorGUILayout.HelpBox(prop.KaiMergedParts.Count + "個のプロップをひとつに合体しています。分離すると合体前の名前・画像・個別設定に戻ります。", MessageType.Info);
            foreach (var part in prop.KaiMergedParts) EditorGUILayout.LabelField("・" + part.GetPropName(data));
        }
    }
    public sealed class KaiPropBundleWindow : EditorWindow
    {
        Avatar data; Group group; readonly HashSet<Prop> selected = new HashSet<Prop>();
        string mergedName = "", error; Vector2 scroll;
        public static void Open(Avatar data, Group group)
        {
            var window = CreateInstance<KaiPropBundleWindow>(); window.data = data; window.group = group;
            window.mergedName = group.Name + " まとめ"; window.titleContent = new GUIContent("プロップを合体"); window.minSize = new Vector2(480, 360); window.ShowUtility();
        }
        void OnGUI()
        {
            if (data == null || group == null || !data.Groups.Contains(group)) { EditorGUILayout.HelpBox("対象グループを選び直してください。", MessageType.Info); return; }
            EditorGUILayout.HelpBox("選んだパーツをひとつのスイッチにまとめます。あとから「分離する」で元に戻せます。", MessageType.Info);
            mergedName = EditorGUILayout.TextField("まとめたプロップ名", mergedName);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var prop in group.Props)
            {
                var problem = KaiPropBundle.Problem(data, prop);
                using (new EditorGUI.DisabledScope(problem != null))
                {
                    bool enabled = EditorGUILayout.ToggleLeft(new GUIContent(prop.GetPropName(data), problem ?? "合体するプロップを選びます。"), selected.Contains(prop), GUILayout.Height(26));
                    if (enabled) selected.Add(prop); else selected.Remove(prop);
                }
                if (problem != null) EditorGUILayout.LabelField(problem, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            using (new EditorGUI.DisabledScope(selected.Count < 2))
                if (GUILayout.Button(selected.Count + "個を合体する", GUILayout.Height(32)))
                {
                    try { KaiPropBundle.Merge(data, group, selected, mergedName); foreach (var w in Resources.FindObjectsOfTypeAll<MainWindow>()) w.Repaint(); Close(); }
                    catch (Exception e) { error = e.Message; }
                }
        }
    }
}
#endif
