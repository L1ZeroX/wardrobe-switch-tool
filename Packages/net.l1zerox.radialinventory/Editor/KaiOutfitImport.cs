#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YagihataItems.RadialInventorySystemV4
{
    // Import scene clothing, never the avatar root, skeleton, or prefab asset itself.
    public static class KaiOutfitImport
    {
        public sealed class Part
        {
            public GameObject Target;
            public string Path;
            public bool Selected = true;
        }
        public sealed class Choice
        {
            public string Name;
            public Part[] Parts;
            public int Selection = -1; // No guessed size. Parts.Length explicitly means "none".
        }
        public sealed class Plan
        {
            public Avatar Data;
            public Group Group;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<Choice> Choices = new List<Choice>();
            public int AlreadyRegistered;
        }
        static readonly Regex BreastSize = new Regex(@"(?i)(?:big|small|large|medium)[ _-]*breasts?|breasts?[ _-]*(?:big|small|large|medium)|[大小]胸|胸[大小]", RegexOptions.CultureInvariant);
        static string VariantKey(string path) => BreastSize.Replace(path.Normalize(), "{breast-size}").ToLowerInvariant();
        static bool Related(Transform a, Transform b) => a == b || a.IsChildOf(b) || b.IsChildOf(a);
        static GameObject Root(Avatar data, Group group)
        {
            if (data == null || group == null || !data.Groups.Contains(group)) throw new InvalidOperationException("登録先のグループを選択してください。");
            if (data.MenuMode == RIS.MenuModeType.Simple) throw new InvalidOperationException("衣装の自動登録は「基本」または「詳細」で使用してください。");
            var avatar = data.GetAvatarRoot();
            if (avatar == null) throw new InvalidOperationException("対象アバターを指定してください。");
            return avatar.gameObject;
        }
        static bool Valid(GameObject obj, GameObject root) => obj != null && obj != root && !EditorUtility.IsPersistent(obj)
            && obj.transform.IsChildOf(root.transform) && obj.GetComponent<VRCAvatarDescriptor>() == null;
        public static Plan Analyze(Avatar data, Group group, IEnumerable<GameObject> outfits)
        {
            var root = Root(data, group);
            var sources = outfits.Where(o => o != null).Distinct().ToArray();
            if (sources.Length == 0 || sources.Any(o => !Valid(o, root)))
                throw new InvalidOperationException("Hierarchyにある、対象アバター配下の衣装を入れてください。アバター全体やProject内のPrefabは登録できません。");
            var candidates = new List<GameObject>();
            foreach (var source in sources)
                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    bool editorOnly = false;
                    for (var node = renderer.transform; node != root.transform; node = node.parent)
                        if (node.CompareTag("EditorOnly")) { editorOnly = true; break; }
                    if (editorOnly) continue;
                    var skin = renderer as SkinnedMeshRenderer;
                    var mesh = renderer as MeshRenderer;
                    if (skin == null && mesh == null) continue;
                    if (skin != null && skin.sharedMesh == null) continue;
                    if (mesh != null && (mesh.GetComponent<MeshFilter>() == null || mesh.GetComponent<MeshFilter>().sharedMesh == null)) continue;
                    var target = renderer.gameObject;
                    // Keep LOD renderers together so their visibility is controlled by Unity.
                    for (var node = target.transform; node != null && node.IsChildOf(source.transform); node = node.parent)
                        if (node.GetComponent<LODGroup>() != null) target = node.gameObject;
                    if (Valid(target, root) && !candidates.Contains(target)) candidates.Add(target);
                }
            // Parent/child toggles in the same import would fight each other.
            candidates = candidates.Where(o => !candidates.Any(parent => parent != o && o.transform.IsChildOf(parent.transform))).ToList();
            var existing = group.Props.SelectMany(p => p.TargetObjects).Select(r => r?.GetObject(root)).Where(o => o != null).ToArray();
            var registeredFamilies = new HashSet<string>(existing.Select(o => VariantKey(AnimationUtility.CalculateTransformPath(o.transform, root.transform))));
            var plan = new Plan { Data = data, Group = group };
            foreach (var candidate in candidates)
            {
                var path = AnimationUtility.CalculateTransformPath(candidate.transform, root.transform);
                if (existing.Any(o => Related(o.transform, candidate.transform)) || registeredFamilies.Contains(VariantKey(path))) { plan.AlreadyRegistered++; continue; }
                plan.Parts.Add(new Part { Target = candidate, Path = path });
            }
            foreach (var set in plan.Parts.GroupBy(p => VariantKey(p.Path)).Where(s => s.Count() > 1))
            {
                var choice = new Choice { Name = set.First().Target.name, Parts = set.ToArray() };
                foreach (var part in choice.Parts) part.Selected = false;
                plan.Choices.Add(choice);
            }
            return plan;
        }
        public static int Commit(Plan plan)
        {
            var root = Root(plan.Data, plan.Group);
            if (plan.Choices.Any(c => c.Selection < 0 || c.Selection > c.Parts.Length))
                throw new InvalidOperationException("サイズ違いのパーツを選ぶか「どれも登録しない」を選んでください。");
            var variants = new HashSet<Part>(plan.Choices.SelectMany(c => c.Parts));
            var selected = plan.Parts.Where(p => !variants.Contains(p) && p.Selected)
                .Concat(plan.Choices.Where(c => c.Selection < c.Parts.Length).Select(c => c.Parts[c.Selection])).ToArray();
            // Revalidate after the selection window has been open; never partially append invalid targets.
            if (selected.Any(p => !Valid(p.Target, root))) throw new InvalidOperationException("衣装が移動・削除されています。もう一度衣装を入れてください。");
            var existing = plan.Group.Props.SelectMany(p => p.TargetObjects).Select(r => r?.GetObject(root)).Where(o => o != null).ToArray();
            var registeredFamilies = new HashSet<string>(existing.Select(o => VariantKey(AnimationUtility.CalculateTransformPath(o.transform, root.transform))));
            selected = selected.Where(p => !existing.Any(o => Related(o.transform, p.Target.transform)) &&
                !registeredFamilies.Contains(VariantKey(AnimationUtility.CalculateTransformPath(p.Target.transform, root.transform)))).ToArray();
            if (plan.Group.Props.Count + selected.Length > 255) throw new InvalidOperationException("1グループのプロップは255個までです。別のグループへ分けてください。");
            var additions = selected.Select(p => {
                var prop = new Prop { Name = p.Target.name, IsDefaultEnabled = p.Target.activeSelf };
                prop.TargetObjects.Add(new GUIDPathPair<GameObject>(ObjectPathStateType.RelativeFromObject, p.Target, root));
                return prop;
            }).ToArray();
            KaiAutoThumbnail.Observe(plan.Data);
            plan.Group.Props.AddRange(additions);
            KaiAutoThumbnail.Observe(plan.Data);
            foreach (var window in Resources.FindObjectsOfTypeAll<MainWindow>())
            {
                window.ShowNotification(new GUIContent(additions.Length + "個のパーツを登録しました。「適用する」で反映します。"));
                window.Repaint();
            }
            return additions.Length;
        }
        public static void Begin(Avatar data, Group group, GameObject[] objects, bool review = false)
        {
            try
            {
                var plan = Analyze(data, group, objects);
                if (plan.Parts.Count == 0)
                {
                    EditorUtility.DisplayDialog("衣装の登録", plan.AlreadyRegistered > 0 ? "この衣装はすでに登録されています。" : "登録できるメッシュがありません。衣装の親オブジェクトを指定してください。", "閉じる");
                    return;
                }
                if (review || plan.Choices.Count > 0) KaiOutfitImportWindow.Open(plan);
                else Commit(plan);
            }
            catch (Exception error) { EditorUtility.DisplayDialog("衣装の登録", error.Message, "閉じる"); }
        }
        public static void Draw(Avatar data, Group group)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var outfit = (GameObject)EditorGUILayout.ObjectField(new GUIContent("衣装を追加", "衣装の親を入れるとパーツを自動登録。サイズ違い候補のみ選択します。"), null, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck() && outfit != null) Queue(data, group, new[] { outfit }, false);
                if (GUILayout.Button(new GUIContent("選んで追加", "Hierarchyで選択中の衣装を、すべてのパーツを確認して追加します。"), GUILayout.Width(86)))
                    Queue(data, group, Selection.gameObjects, true);
            }
        }
        static void Queue(Avatar data, Group group, GameObject[] objects, bool review) => EditorApplication.delayCall += () => Begin(data, group, objects, review);
        public static void HandleDrop(Rect rect, Avatar data, Group group)
        {
            if (!GUI.enabled || !rect.Contains(Event.current.mousePosition) ||
                (Event.current.type != EventType.DragUpdated && Event.current.type != EventType.DragPerform)) return;
            var objects = DragAndDrop.objectReferences.OfType<GameObject>().ToArray();
            if (objects.Length == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (Event.current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag(); Queue(data, group, objects, false);
            }
            Event.current.Use();
        }
    }
    public sealed class KaiOutfitImportWindow : EditorWindow
    {
        KaiOutfitImport.Plan plan;
        Vector2 scroll;
        bool showOther;
        string error;
        public static void Open(KaiOutfitImport.Plan plan)
        {
            var window = CreateInstance<KaiOutfitImportWindow>();
            window.plan = plan;
            window.showOther = plan.Choices.Count == 0;
            window.titleContent = new GUIContent("登録するパーツを選択");
            window.minSize = new Vector2(520, 360);
            window.ShowUtility();
        }
        void OnGUI()
        {
            if (plan == null) { EditorGUILayout.HelpBox("衣装をもう一度指定してください。", MessageType.Info); return; }
            EditorGUILayout.LabelField("登録先: " + plan.Data.GetAvatarRoot()?.name + " / " + plan.Group.Name, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("通常パーツは自動で追加します。サイズ違いなど、選択肢のあるパーツだけ選んでください。名前からの候補判定なので、必要に応じて通常パーツも確認できます。", MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var choice in plan.Choices)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("どれを登録しますか？", EditorStyles.boldLabel);
                for (int i = 0; i < choice.Parts.Length; i++)
                {
                    var part = choice.Parts[i];
                    var label = part.Target == null ? "（削除済み）" : choice.Parts.Count(p => p.Target != null && p.Target.name == part.Target.name) > 1 ? part.Path : part.Target.name;
                    if (GUILayout.Toggle(choice.Selection == i, new GUIContent(label, part.Path), EditorStyles.radioButton)) choice.Selection = i;
                }
                if (GUILayout.Toggle(choice.Selection == choice.Parts.Length, "どれも登録しない", EditorStyles.radioButton)) choice.Selection = choice.Parts.Length;
                EditorGUILayout.EndVertical();
            }
            var variants = new HashSet<KaiOutfitImport.Part>(plan.Choices.SelectMany(c => c.Parts));
            var regular = plan.Parts.Where(p => !variants.Contains(p)).ToArray();
            showOther = EditorGUILayout.Foldout(showOther, "通常パーツ " + regular.Count(p => p.Selected) + "個を自動登録（手動で変更できます）", true);
            if (showOther)
                foreach (var part in regular)
                    part.Selected = EditorGUILayout.ToggleLeft(new GUIContent(part.Target != null ? part.Target.name : "（削除済み）", part.Path), part.Selected);
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.HelpBox("選ばなかったパーツは削除・非表示にしません。アバターの表示状態はそのままです。", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("キャンセル", GUILayout.Height(30))) Close();
                using (new EditorGUI.DisabledScope(plan.Choices.Any(c => c.Selection < 0)))
                    if (GUILayout.Button("選択を確定して登録", GUILayout.Height(30)))
                    {
                        try { KaiOutfitImport.Commit(plan); Close(); }
                        catch (Exception ex) { error = ex.Message; }
                    }
            }
        }
    }
}
#endif
