#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiHeelLink
    {
        public const string Prefix = "RISV4-HEEL-";
        public sealed class Link
        {
            public Prop Prop;
            public SkinnedMeshRenderer Renderer;
            public string Shape, Error;
            public float Weight = 100;
            public int Group, Index;
            public string Active => Prefix + "G" + Group + "P" + Index;
        }
        public static string Normalize(string value) => new string((value ?? "").Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        public static bool IsHeelName(string value)
        {
            string n = Normalize(value);
            return n.Contains("highheel") || new[] { "heel", "heels", "heellift", "heelheight", "ヒール", "ハイヒール", "つま先立ち" }.Contains(n);
        }
        public static bool IsShoes(Avatar data, Prop prop)
        {
            var root = data.GetAvatarRoot();
            return (prop.Name ?? "").IndexOf("shoes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prop.TargetObjects.Any(t => (t?.GetObject(root?.gameObject)?.name ?? "").IndexOf("shoes", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        static int BodyScore(string name)
        {
            string n = Normalize(name);
            if (n == "bodyall" || n == "bodybase") return 4;
            if (n.StartsWith("bodyall", StringComparison.Ordinal) || n.StartsWith("bodybase", StringComparison.Ordinal)) return 3;
            if (n == "body") return 2;
            return n.Contains("body") || n == "素体" ? 1 : 0;
        }
        public static List<Link> Candidates(Avatar data)
        {
            var root = data.GetAvatarRoot(); var list = new List<Link>();
            if (root == null) return list;
            var clothing = new HashSet<SkinnedMeshRenderer>(data.Groups.SelectMany(g => g.Props).SelectMany(p => p.TargetObjects)
                .Select(t => t?.GetObject(root.gameObject)).Where(t => t != null)
                .SelectMany(t => t.GetComponentsInChildren<SkinnedMeshRenderer>(true)));
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || clothing.Contains(renderer)) continue;
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    string name = renderer.sharedMesh.GetBlendShapeName(i);
                    if (IsHeelName(name)) list.Add(new Link { Renderer = renderer, Shape = name, Weight = 100 });
                }
            }
            if (list.Count == 0) return list;
            int score = list.Max(l => BodyScore(l.Renderer.name));
            return list.Where(l => BodyScore(l.Renderer.name) == score).ToList();
        }
        public static Link Resolve(Avatar data, Prop prop)
        {
            if (prop == null || prop.KaiHeelMode == 1) return null;
            Link result;
            if (prop.KaiHeelMode == 2)
            {
                var root = data.GetAvatarRoot();
                result = new Link { Renderer = prop.KaiHeelRenderer?.GetObject(root?.gameObject)?.GetComponent<SkinnedMeshRenderer>(), Shape = prop.KaiHeelShape, Weight = 100 };
            }
            else if (KaiPropBundle.IsMerged(prop))
            {
                var children = prop.KaiMergedParts.Select(p => Resolve(data, p)).Where(l => l != null).ToArray();
                if (children.Length == 0) return null;
                result = children.FirstOrDefault(l => l.Error != null);
                if (result != null) result = new Link { Error = result.Error };
                else if (children.Select(l => l.Renderer.GetInstanceID() + "/" + l.Shape).Distinct().Count() > 1)
                    result = new Link { Error = "合体した靴の連動先が異なります。手動指定で連動先を選んでください。" };
                else result = new Link { Renderer = children[0].Renderer, Shape = children[0].Shape, Weight = children.Max(l => l.Weight) };
            }
            else
            {
                if (!IsShoes(data, prop)) return null;
                var candidates = Candidates(data);
                result = candidates.Count == 1 ? candidates[0] : new Link { Error = candidates.Count == 0
                    ? "ヒール用の候補が見つかりません。手動指定で対象を選ぶか、この靴の連動をOFFにしてください。"
                    : "ヒール用の候補が複数あります。手動指定で対象を選んでください。" };
            }
            result.Prop = prop;
            result.Weight = prop.KaiHeelWeight ?? result.Weight;
            if (result.Error == null)
            {
                var root = data.GetAvatarRoot();
                if (root == null || result.Renderer == null || !result.Renderer.transform.IsChildOf(root.transform) || result.Renderer.sharedMesh == null || string.IsNullOrEmpty(result.Shape) || result.Renderer.sharedMesh.GetBlendShapeIndex(result.Shape) < 0)
                    result.Error = "連動先のメッシュ／ブレンドシェイプを指定し直してください。";
                else if (float.IsNaN(result.Weight) || float.IsInfinity(result.Weight) || result.Weight < 0 || result.Weight > 100)
                    result.Error = "履いたときの値を0〜100にしてください。";
                else if (prop.TargetObjects.Count == 0 || prop.TargetObjects.Any(t => t?.GetObject(root.gameObject) == null))
                    result.Error = "靴の対象オブジェクトが見つかりません。";
                else if (prop.EnableAnimation?.GetObject() != null || prop.DisableAnimation?.GetObject() != null || prop.MaterialOverrides.Any(m => m?.GetObject() != null))
                    result.Error = "実験版のヒール連動は、オブジェクトをON/OFFする衣装が対象です。独自アニメーション／マテリアル差替えの連動はOFFにしてください。";
            }
            return result;
        }
        public static List<Link> Collect(Avatar data)
        {
            var links = new List<Link>();
            if (!data.KaiExperimentalHeels || data.MenuMode == RIS.MenuModeType.Simple) return links;
            for (int g = 0; g < data.Groups.Count; g++) for (int p = 0; p < data.Groups[g].Props.Count; p++)
            {
                var link = Resolve(data, data.Groups[g].Props[p]);
                if (link == null) continue;
                link.Group = g; link.Index = p; links.Add(link);
            }
            return links;
        }
        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            foreach (var state in machine.states) yield return state.state;
            foreach (var sub in machine.stateMachines) foreach (var state in States(sub.stateMachine)) yield return state;
        }
        static IEnumerable<AnimationClip> Clips(Motion motion)
        {
            if (motion is AnimationClip clip) yield return clip;
            if (motion is BlendTree tree) foreach (var child in tree.children) foreach (var c in Clips(child.motion)) yield return c;
        }
        static bool Writes(AnimationClip clip, Link link, Transform root) => AnimationUtility.GetCurveBindings(clip).Any(b => b.type == typeof(SkinnedMeshRenderer) && b.path == AnimationUtility.CalculateTransformPath(link.Renderer.transform, root) && b.propertyName == "blendShape." + link.Shape);
        public static void Validate(Avatar data)
        {
            var links = Collect(data); var root = data.GetAvatarRoot();
            foreach (var link in links)
            {
                string error = link.Error;
                var prop = link.Prop;
                var exclusive = data.MenuMode == RIS.MenuModeType.Basic ? (RIS.ExclusiveGroupType)link.Group : prop.ExclusiveGroup;
                if (error == null && !KaiGroupSwitch.Enabled(data, link.Group) && exclusive != RIS.ExclusiveGroupType.None && data.GetExclusiveDisableClip(exclusive) != null)
                    error = "独自の排他OFFアニメーションがあるため、ヒール連動をOFFにするかグループ内の排他を解除してください。";
                if (error != null) throw new InvalidOperationException("ヒール連動: " + prop.GetPropName(data) + " — " + error);
            }
            if (links.Count == 0 || root == null) return;
            var layers = root.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0];
            var fx = layers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            var clips = fx == null ? new List<AnimationClip>() : fx.layers.Where(l => !l.name.StartsWith("RIS", StringComparison.Ordinal)).SelectMany(l => States(l.stateMachine)).SelectMany(s => Clips(s.motion)).Distinct().ToList();
            clips.AddRange(data.Groups.SelectMany(g => g.Props).SelectMany(p => new[] { p.EnableAnimation?.GetObject(), p.DisableAnimation?.GetObject() }).Where(c => c != null));
            foreach (var link in links)
                if (clips.Any(c => Writes(c, link, root.transform)))
                    throw new InvalidOperationException("ヒール連動: " + link.Shape + " は既存のFX／衣装アニメーションでも操作されています。連動をOFFにするか別の対象を選んでください。");
        }
        public static void Build(Avatar data, AnimatorController controller, string folder)
        {
            // Old managed layers are removed by the tab generator. Remove stale internal parameters even when disabled.
            controller.parameters = controller.parameters.Where(p => !p.name.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
            var links = Collect(data); var root = data.GetAvatarRoot();
            if (links.Count == 0) return;
            foreach (var link in links)
            {
                if (link.Error != null) throw new InvalidOperationException(link.Error);
                controller.AddParameter(new AnimatorControllerParameter { name = link.Active, type = AnimatorControllerParameterType.Bool, defaultBool = link.Prop.IsDefaultEnabled });
                // Observe the actual generated clothing states. This also follows local-only guards,
                // legacy exclusive defaults, reset timers, and whole-group commands without extra sync bits.
                string main = RIS.Prefix + "-MAIN-G" + link.Group + "P" + link.Index;
                var sourceLayers = controller.layers.Where(l => l.name == main || l.name.StartsWith(RIS.Prefix + "-PAIR", StringComparison.Ordinal) || l.name.StartsWith(RIS.Prefix + "-EXCLUSIVE-", StringComparison.Ordinal));
                string target = AnimationUtility.CalculateTransformPath(link.Prop.GetFirstTargetObject(data).transform, root.transform);
                int tracked = 0;
                foreach (var state in sourceLayers.SelectMany(l => States(l.stateMachine)))
                {
                    var clip = state.motion as AnimationClip;
                    if (clip == null) continue;
                    var bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(GameObject) && b.path == target && b.propertyName == "m_IsActive").ToArray();
                    if (bindings.Length == 0) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
                    var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    driver.localOnly = false;
                    driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter { name = link.Active, value = curve.Evaluate(0) > 0.5f ? 1 : 0, type = VRC_AvatarParameterDriver.ChangeType.Set });
                    EditorUtility.SetDirty(driver); tracked++;
                }
                if (tracked == 0) throw new InvalidOperationException("ヒール連動の衣装ステートが見つかりません: " + link.Prop.GetPropName(data));
            }
            int index = 0;
            foreach (var binding in links.GroupBy(l => l.Renderer.GetInstanceID() + "/" + l.Shape))
            {
                var ordered = binding.OrderByDescending(l => l.Weight).ThenBy(l => l.Group).ThenBy(l => l.Index).ToArray();
                var layer = controller.AddAnimatorControllerLayer(Prefix + "OUTPUT" + index);
                var machine = layer.stateMachine;
                string path = AnimationUtility.CalculateTransformPath(ordered[0].Renderer.transform, root.transform);
                Func<string, float, AnimatorState> state = (name, weight) => {
                    var clip = new AnimationClip();
                    clip.SetCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + ordered[0].Shape, AnimationCurve.Constant(0, 1f / 60, weight));
                    AssetDatabase.CreateAsset(clip, folder + "Heel" + index + "-" + name + ".anim");
                    var s = machine.AddState(name); s.motion = clip; s.writeDefaultValues = data.UseWriteDefaults; return s;
                };
                var off = state("Barefoot", 0); machine.defaultState = off;
                var offTransition = machine.AddAnyStateTransition(off); Configure(offTransition);
                foreach (var l in ordered) offTransition.AddCondition(AnimatorConditionMode.IfNot, 0, l.Active);
                for (int i = 0; i < ordered.Length; i++)
                {
                    var on = state("Wear" + i, ordered[i].Weight);
                    var transition = machine.AddAnyStateTransition(on); Configure(transition);
                    transition.AddCondition(AnimatorConditionMode.If, 0, ordered[i].Active);
                    for (int j = 0; j < i; j++) transition.AddCondition(AnimatorConditionMode.IfNot, 0, ordered[j].Active);
                    if (machine.defaultState == off && ordered[i].Prop.IsDefaultEnabled) machine.defaultState = on;
                }
                index++;
            }
            EditorUtility.SetDirty(controller);
        }
        static void Configure(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false; transition.duration = 0; transition.hasFixedDuration = true; transition.canTransitionToSelf = false;
        }
        public static void DrawGlobal(Avatar data)
        {
            if (data.MenuMode == RIS.MenuModeType.Simple) return;
            using (new EditorGUILayout.HorizontalScope())
            {
            data.KaiExperimentalHeels = EditorGUILayout.ToggleLeft("実験：靴とヒール補正を連動", data.KaiExperimentalHeels);
            if (GUILayout.Button("ヒール補正を調整…", GUILayout.Width(180), GUILayout.Height(26))) KaiHeelWindow.Open(data);
            }
            if (!data.KaiExperimentalHeels) return;
            var links = Collect(data); int missing = links.Count(l => l.Error != null);
            EditorGUILayout.HelpBox("Shoesを含む衣装を自動検出。連動対象 " + links.Count + "件" + (missing > 0 ? "／要設定 " + missing + "件（調整画面で確認）" : "") + "。脱ぐと0、履くと設定値になります。", missing > 0 ? MessageType.Warning : MessageType.None);
        }
        public static void DrawProp(Avatar data, Prop prop)
        {
            if (!data.KaiExperimentalHeels) return;
            if (GUILayout.Button("ヒール補正をプレビューで調整…", GUILayout.Height(28))) KaiHeelWindow.Open(data, prop);
        }
        public static void DrawSettings(Avatar data, Prop prop)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("実験：ヒール補正", EditorStyles.boldLabel);
                var resolved = Resolve(data, prop);
                int next = EditorGUILayout.Popup("連動", prop.KaiHeelMode, new[] { "自動（Shoesを検出）", "OFF", "手動指定" });
                if (next != prop.KaiHeelMode)
                {
                    if (next == 2 && resolved?.Renderer != null && resolved.Error == null)
                    {
                        prop.KaiHeelRenderer = new GUIDPathPair<GameObject>(ObjectPathStateType.RelativeFromObject, resolved.Renderer.gameObject, data.GetAvatarRoot().gameObject);
                        prop.KaiHeelShape = resolved.Shape;
                    }
                    prop.KaiHeelMode = next;
                }
                if (prop.KaiHeelMode == 1) return;
                if (prop.KaiHeelMode == 2)
                {
                    var root = data.GetAvatarRoot();
                    var old = prop.KaiHeelRenderer?.GetObject(root?.gameObject)?.GetComponent<SkinnedMeshRenderer>();
                    var meshes = root == null ? new SkinnedMeshRenderer[0] : root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToArray();
                    int before = Array.IndexOf(meshes, old) + 1;
                    int after = EditorGUILayout.Popup("メッシュを一覧から選ぶ", before, new[] { "選択してください" }.Concat(meshes.Select(r => AnimationUtility.CalculateTransformPath(r.transform, root.transform))).ToArray());
                    if (after != before)
                    {
                        old = after == 0 ? null : meshes[after - 1];
                        prop.KaiHeelRenderer = old == null ? null : new GUIDPathPair<GameObject>(ObjectPathStateType.RelativeFromObject, old.gameObject, root.gameObject);
                        prop.KaiHeelShape = null;
                    }
                    var renderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("対象メッシュ", old, typeof(SkinnedMeshRenderer), true);
                    if (renderer != old)
                    {
                        if (renderer != null && (root == null || !renderer.transform.IsChildOf(root.transform)))
                        {
                            EditorGUILayout.HelpBox("対象アバター内のメッシュを指定してください。", MessageType.Warning);
                            renderer = old;
                        }
                        else
                        {
                            prop.KaiHeelRenderer = renderer == null ? null : new GUIDPathPair<GameObject>(ObjectPathStateType.RelativeFromObject, renderer.gameObject, root.gameObject);
                            prop.KaiHeelShape = null;
                        }
                    }
                    if (renderer?.sharedMesh != null)
                    {
                        var names = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Select(renderer.sharedMesh.GetBlendShapeName).ToArray();
                        int current = Array.IndexOf(names, prop.KaiHeelShape) + 1;
                        int selected = EditorGUILayout.Popup("ブレンドシェイプ", current, new[] { "選択してください" }.Concat(names).ToArray());
                        if (selected != current) prop.KaiHeelShape = selected == 0 ? null : names[selected - 1];
                    }
                }
                resolved = Resolve(data, prop);
                if (resolved == null) { EditorGUILayout.LabelField("Shoesを含まないため自動連動しません。", EditorStyles.wordWrappedMiniLabel); return; }
                if (resolved.Error != null) EditorGUILayout.HelpBox(resolved.Error, MessageType.Warning);
                else EditorGUILayout.LabelField(resolved.Renderer.name + " → " + resolved.Shape, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider("履いたとき", prop.KaiHeelWeight ?? resolved.Weight, 0, 100);
                if (EditorGUI.EndChangeCheck()) prop.KaiHeelWeight = value;
                EditorGUILayout.LabelField("脱いだとき：0。同時に履く靴は最大値を使用。", EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
#endif
