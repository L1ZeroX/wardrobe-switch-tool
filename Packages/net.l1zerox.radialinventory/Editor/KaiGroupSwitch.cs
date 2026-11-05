#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiGroupSwitch
    {
        public static bool Enabled(Avatar data, int group) => data.MenuMode != RIS.MenuModeType.Simple &&
            (data.Groups[group].KaiToggleWholeGroup || (data.MenuMode == RIS.MenuModeType.Basic
                ? group >= 8 || data.GetExclusiveMode((RIS.ExclusiveGroupType)group) == RIS.ExclusiveModeType.None
                : data.Groups[group].Props.All(p => p.ExclusiveGroup == RIS.ExclusiveGroupType.None)));
        public const string Request = "RISV4-BULKREQUEST";
        public static float RequestValue(Avatar data, int group, bool on) => (2 * group + (on ? 1 : 2)) / (2f * data.Groups.Count + 1);
        public static string PropName(int group, int prop) => RIS.Prefix + "-G" + group + "P" + prop;
        public static string AutoOff(int group) => RIS.Prefix + "-G" + group + "AUTOOFF";
        public static string Keep(int group) => RIS.Prefix + "-G" + group + "KEEP";

        public static void Draw(Avatar data, int groupIndex, Group group)
        {
            var width = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 145;
            EditorGUILayout.HelpBox("Expressionsで「個別に切り替え」と「まとめて着る／脱ぐ」を使い分けられます。自動OFF・保持設定もメニューから変更できます。", MessageType.Info);
            group.KaiToggleWholeGroup = EditorGUILayout.Toggle(new GUIContent("グループ内の排他を解除", "排他設定のあるグループでも一括操作を使う場合にONにします。元の排他設定は保存されます。"), group.KaiToggleWholeGroup);
            group.KaiTurnOffOtherGroups = EditorGUILayout.Toggle("自動OFFの初期値", group.KaiTurnOffOtherGroups);
            group.KaiIncludeInOutfitSwitch = !EditorGUILayout.Toggle("グループ保持の初期値", !group.KaiIncludeInOutfitSwitch);
            if (groupIndex >= 0 && groupIndex < data.Groups.Count && !Enabled(data, groupIndex))
                EditorGUILayout.HelpBox("このグループは既存の排他動作を優先しています。一括操作も使う場合は「グループ内の排他を解除」をONにしてください。旧式排他のグループは自動OFFから保護されます。", MessageType.Info);
            EditorGUIUtility.labelWidth = width;
        }

        public static IEnumerable<VRCExpressionParameters.Parameter> Parameters(Avatar data)
        {
            if (data.Groups.Where((g, i) => Enabled(data, i)).Any())
                yield return new VRCExpressionParameters.Parameter { name = Request, valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0, saved = false, networkSynced = false };
            for (int g = 0; g < data.Groups.Count; g++)
            {
                if (data.MenuMode != RIS.MenuModeType.Simple)
                    yield return new VRCExpressionParameters.Parameter { name = Keep(g), valueType = VRCExpressionParameters.ValueType.Bool,
                        defaultValue = data.Groups[g].KaiIncludeInOutfitSwitch ? 0 : 1, saved = true, networkSynced = false };
                if (Enabled(data, g))
                {
                    yield return new VRCExpressionParameters.Parameter { name = AutoOff(g), valueType = VRCExpressionParameters.ValueType.Bool,
                        defaultValue = data.Groups[g].KaiTurnOffOtherGroups ? 1 : 0, saved = true, networkSynced = false };
                }
            }
        }

        public static VRCExpressionsMenu.Control Setting(string name, string parameter) => new VRCExpressionsMenu.Control {
            name = name, icon = TexAssets.GroupIcon, type = VRCExpressionsMenu.Control.ControlType.Toggle, value = 1,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter } };

        public static VRCExpressionsMenu.Control Control(Avatar data, int index, bool on)
        {
            return new VRCExpressionsMenu.Control { name = on ? "まとめて着る" : "まとめて脱ぐ",
                icon = data.Groups[index].Icon?.GetObject() ?? TexAssets.GroupIcon, type = VRCExpressionsMenu.Control.ControlType.Toggle,
                value = RequestValue(data, index, on), parameter = new VRCExpressionsMenu.Control.Parameter { name = Request } };
        }

        public static Dictionary<string, float> Values(Avatar data, int selected, bool on)
        {
            var values = new Dictionary<string, float>();
            for (int g = 0; g < data.Groups.Count; g++)
            {
                bool own = g == selected;
                if (!own && !(on && data.Groups[selected].KaiTurnOffOtherGroups && data.Groups[g].KaiIncludeInOutfitSwitch)) continue;
                for (int p = 0; p < data.Groups[g].Props.Count; p++) values[PropName(g, p)] = own && on ? 1 : 0;
            }
            // Commands are momentary and local; existing prop parameters carry the saved/networked state.
            values[Request] = 0;
            return values;
        }

        public static void Build(Avatar data, AnimatorController controller, AnimationClip noneClip, string animationsFolder)
        {
            if (!data.Groups.Where((g, i) => Enabled(data, i)).Any()) return;
            if (!controller.parameters.Any(p => p.name == "IsLocal")) controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
            foreach (var parameter in Parameters(data))
                if (!controller.parameters.Any(p => p.name == parameter.name)) controller.AddParameter(new AnimatorControllerParameter {
                    name = parameter.name, type = parameter.valueType == VRCExpressionParameters.ValueType.Float ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Bool,
                    defaultBool = parameter.defaultValue != 0, defaultFloat = parameter.defaultValue });
            // One interruptible dispatcher: a newer click replaces the pending request.
            // A Toggle latches the click until it is consumed; releasing the pointer cannot erase it.
            var layer = controller.AddAnimatorControllerLayer(RIS.Prefix + "-BULK-COMMANDS");
            var machine = layer.stateMachine;
            var wait = machine.AddState("Wait");
            wait.motion = noneClip;
            wait.writeDefaultValues = data.UseWriteDefaults;
            machine.defaultState = wait;
            for (int g = 0; g < data.Groups.Count; g++)
            {
                if (!Enabled(data, g)) continue;
                foreach (bool on in new[] { true, false })
                {
                    var state = machine.AddState("G" + g + (on ? "-AllOn" : "-AllOff"));
                    state.motion = noneClip;
                    state.writeDefaultValues = data.UseWriteDefaults;
                    var transition = machine.AddAnyStateTransition(state);
                    transition.hasExitTime = false;
                    transition.duration = 0;
                    transition.canTransitionToSelf = true;
                    float code = RequestValue(data, g, on), margin = 0.25f / (2 * data.Groups.Count + 1);
                    transition.AddCondition(AnimatorConditionMode.Greater, code - margin, Request);
                    transition.AddCondition(AnimatorConditionMode.Less, code + margin, Request);
                    transition.AddCondition(AnimatorConditionMode.If, 0, "IsLocal");
                    Drive(state, Enumerable.Range(0, data.Groups[g].Props.Count).Select(p => new KeyValuePair<string, float>(PropName(g, p), on ? 1 : 0))
                        .Concat(new[] { new KeyValuePair<string, float>(Request, 0) }));
                    var current = state;
                    if (on)
                        for (int other = 0; other < data.Groups.Count; other++)
                        {
                            if (other == g || IsLegacy(data, other)) continue;
                            var off = machine.AddState("G" + g + "-OffGroup" + other);
                            off.motion = noneClip; off.writeDefaultValues = data.UseWriteDefaults;
                            var next = machine.AddState("G" + g + "-CheckedGroup" + other);
                            next.motion = noneClip; next.writeDefaultValues = data.UseWriteDefaults;
                            var apply = Connect(current, off);
                            apply.AddCondition(AnimatorConditionMode.If, 0, AutoOff(g));
                            apply.AddCondition(AnimatorConditionMode.IfNot, 0, Keep(other));
                            Connect(current, next).AddCondition(AnimatorConditionMode.IfNot, 0, AutoOff(g));
                            Connect(current, next).AddCondition(AnimatorConditionMode.If, 0, Keep(other));
                            Drive(off, Enumerable.Range(0, data.Groups[other].Props.Count).Select(p => new KeyValuePair<string, float>(PropName(other, p), 0)));
                            Connect(off, next);
                            current = next;
                        }
                    // Never clear a newer request at the end of an older operation.
                    Connect(current, wait);
                }
                EditorUtility.SetDirty(machine);
            }
            BuildInactiveParents(data, controller, animationsFolder);
            EditorUtility.SetDirty(controller);
        }

        static AnimatorStateTransition Connect(AnimatorState from, AnimatorState to)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0;
            transition.AddCondition(AnimatorConditionMode.If, 0, "IsLocal");
            return transition;
        }

        static void BuildInactiveParents(Avatar data, AnimatorController controller, string folder)
        {
            var root = data.GetAvatarRoot().gameObject;
            var registered = new HashSet<GameObject>(data.Groups.SelectMany(g => g.Props).SelectMany(p => p.TargetObjects)
                .Where(t => t != null).Select(t => t.GetObject(root)).Where(t => t != null));
            var parents = new Dictionary<GameObject, HashSet<string>>();
            for (int g = 0; g < data.Groups.Count; g++)
                for (int p = 0; p < data.Groups[g].Props.Count; p++)
                    foreach (var pair in data.Groups[g].Props[p].TargetObjects)
                    {
                        var target = pair?.GetObject(root);
                        if (target == null) continue;
                        for (var parent = target.transform.parent; parent != null && parent != root.transform; parent = parent.parent)
                        {
                            if (parent.gameObject.activeSelf || registered.Contains(parent.gameObject)) continue;
                            if (!parents.ContainsKey(parent.gameObject)) parents.Add(parent.gameObject, new HashSet<string>());
                            parents[parent.gameObject].Add(PropName(g, p));
                        }
                    }
            int index = 0;
            foreach (var parent in parents)
            {
                string path = AnimationUtility.CalculateTransformPath(parent.Key.transform, root.transform);
                var layer = controller.AddAnimatorControllerLayer(RIS.Prefix + "-PARENT-" + index);
                var machine = layer.stateMachine;
                var off = machine.AddState("OriginalInactive");
                var on = machine.AddState("ChildrenEnabled");
                off.writeDefaultValues = on.writeDefaultValues = data.UseWriteDefaults;
                off.motion = UnityUtils.CreateAnimationClip(path, 0, typeof(GameObject), "m_IsActive", "Parent" + index + "Off", folder);
                on.motion = UnityUtils.CreateAnimationClip(path, 1, typeof(GameObject), "m_IsActive", "Parent" + index + "On", folder);
                machine.defaultState = off;
                foreach (var parameter in parent.Value)
                {
                    var enable = machine.MakeAnyStateTransition(on);
                    enable.AddCondition(AnimatorConditionMode.If, 0, parameter);
                }
                var disable = machine.MakeAnyStateTransition(off);
                foreach (var parameter in parent.Value) disable.AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
                EditorUtility.SetDirty(machine);
                index++;
            }
        }

        static void Drive(AnimatorState state, IEnumerable<KeyValuePair<string, float>> values)
        {
            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.localOnly = true;
            foreach (var value in values)
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter { name = value.Key, value = value.Value, type = VRC_AvatarParameterDriver.ChangeType.Set });
            EditorUtility.SetDirty(driver);
        }

        static bool IsLegacy(Avatar data, int group) => !Enabled(data, group) && (data.MenuMode == RIS.MenuModeType.Basic
            ? group < 8 && data.GetExclusiveMode((RIS.ExclusiveGroupType)group) == RIS.ExclusiveModeType.LegacyExclusive
            : data.Groups[group].Props.Any(p => data.GetExclusiveMode(p.ExclusiveGroup) == RIS.ExclusiveModeType.LegacyExclusive));

        // Preserve original exclusive settings when returning to individual mode, even after a build failure.
        public sealed class GenerationScope : IDisposable
        {
            readonly Avatar data;
            readonly RIS.ExclusiveModeType[] modes;
            readonly Dictionary<Prop, RIS.ExclusiveGroupType> props = new Dictionary<Prop, RIS.ExclusiveGroupType>();
            public GenerationScope(Avatar avatar)
            {
                data = avatar;
                modes = data.ExclusiveModes == null ? null : (RIS.ExclusiveModeType[])data.ExclusiveModes.Clone();
                for (int g = 0; g < data.Groups.Count; g++)
                {
                    if (!Enabled(data, g)) continue;
                    if (g < 8 && data.MenuMode == RIS.MenuModeType.Basic) data.SetExclusiveMode((RIS.ExclusiveGroupType)g, RIS.ExclusiveModeType.None);
                    foreach (var prop in data.Groups[g].Props)
                    {
                        props[prop] = prop.ExclusiveGroup;
                        prop.ExclusiveGroup = RIS.ExclusiveGroupType.None;
                    }
                }
            }
            public void Dispose()
            {
                data.ExclusiveModes = modes;
                foreach (var pair in props) pair.Key.ExclusiveGroup = pair.Value;
            }
        }

        public static void Validate(Avatar data)
        {
            for (int g = 0; g < data.Groups.Count; g++)
            {
                if (!Enabled(data, g)) continue;
                if (data.Groups[g].Props.Count == 0) throw new InvalidOperationException("一括切り替えのグループにプロップを登録してください: " + g);
            }
        }
    }
}
#endif
