#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YagihataItems.RadialInventorySystemV4
{
    public class TabBasic : EditorTab
    {
        private ReorderableList groupsReorderableList;
        private ReorderableList propsReorderableList;
        private Group selectedGroup;
        private Prop selectedProp;

        public override string[] CheckErrors(ref Avatar risAvatar)
        {
            if (risAvatar == null)
                return new string[] { };

            var errors = new List<string>();
            if (risAvatar.Groups.Count == 0)
                errors.Add(RISStrings.GetString("err_addgroup"));

            foreach (var groupIndex in Enumerable.Range(0, risAvatar.Groups.Count))
            {
                var group = risAvatar.Groups[groupIndex];
                var groupName = group.Name;
                if (string.IsNullOrEmpty(groupName))
                    groupName = string.Format(RISStrings.GetString("defaultname_group"), risAvatar.Groups.IndexOf(group));

                if (!group.Props.Any())
                    errors.Add(string.Format(RISStrings.GetString("error_emptygroup"), groupName));

                var maxPropsCount = 255;
                if (group.UseResetButton)
                    maxPropsCount = 255;
                if (group.Props.Count > maxPropsCount)
                    errors.Add(string.Format(RISStrings.GetString("err_overprop"), groupName, maxPropsCount));

                foreach (var prop in group.Props)
                {
                    var propName = prop.Name;
                    if (string.IsNullOrEmpty(propName))
                        propName = string.Format(RISStrings.GetString("defaultname_prop"), group.Props.IndexOf(prop));
                    if (risAvatar.GetAvatarRoot() != null)
                    {
                        var parentGameObject = risAvatar.GetAvatarRoot()?.gameObject;
                        if (prop.TargetObjects.Count <= 0 || prop.TargetObjects[0]?.GetObject(parentGameObject) == null)
                            errors.Add(string.Format(RISStrings.GetString("err_nullobject"), groupName, propName));
                    }
                }
            }


            return errors.ToArray();
        }

        public override void DrawTab(ref Avatar risAvatar, Rect position, bool showingVerticalScroll)
        {
            if (groupsReorderableList == null)
                InitializeGroupList(risAvatar);
            if (propsReorderableList == null)
                InitializePropList(null, risAvatar);
            var cellWidth = position.width / 3f - 15f;
            var selectedGroupIsChanged = false;
            var selectedPropIsChanged = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    using (var scope = new EditorGUILayout.HorizontalScope())
                    {
                        var scopeWidth = cellWidth - 40;
                        var GroupsHeight = groupsReorderableList.GetHeight();
                        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(GroupsHeight), GUILayout.Width(scopeWidth)))
                        {
                            var oldSelectedIndex = groupsReorderableList.index;
                            groupsReorderableList.DoList(new Rect(scope.rect.x, scope.rect.y, scopeWidth, GroupsHeight));
                            selectedGroupIsChanged = oldSelectedIndex != groupsReorderableList.index;
                            GUILayout.Space(0);
                        }

                    }
                    GUILayout.FlexibleSpace();
                    KaiGroupThumbnail.Draw(risAvatar, groupsReorderableList.index, cellWidth - 40);
                }
                GUILayout.Box("", GUILayout.ExpandHeight(true), GUILayout.Width(1));
                var groupIndex = groupsReorderableList.index;
                selectedGroupIsChanged = BindGroup(risAvatar, groupIndex);
                var groupIsSelected = risAvatar != null && groupIndex >= 0 && groupIndex < risAvatar.Groups.Count;
                if (selectedGroupIsChanged) selectedPropIsChanged = true;
                RIS.ExclusiveModeType exclusiveMode = RIS.ExclusiveModeType.None;
                var propIndex = -1;
                using (new EditorGUI.DisabledGroupScope(!groupIsSelected))
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(cellWidth + 20)))
                    {
                        EditorGUIUtility.labelWidth = RISStrings.GetWidth(2);
                        EditorGUILayout.LabelField("② グループ設定・衣装一覧", KaiEditorUI.SectionHeading, GUILayout.ExpandWidth(true));
                        GUILayout.Space(3);
                        selectedGroup.Name = EditorGUILayout.TextField(RISStrings.GetString("group_name"), selectedGroup.Name);

                        EditorGUI.BeginChangeCheck();

                        var icon = selectedGroup.Icon.GetObject();
                        icon = (Texture2D)EditorGUILayout.ObjectField(RISStrings.GetString("icon"), icon, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                        exclusiveMode = risAvatar.GetExclusiveMode((RIS.ExclusiveGroupType)groupIndex);
                        exclusiveMode = (RIS.ExclusiveModeType)EditorGUILayout.EnumPopup(RISStrings.GetString("exclusive_mode"), exclusiveMode);

                        if (EditorGUI.EndChangeCheck())
                        {
                            selectedGroup.Icon.SetObject(icon);
                            risAvatar.SetExclusiveMode((RIS.ExclusiveGroupType)groupIndex, exclusiveMode);
                        }

                        selectedGroup.UseResetButton = EditorGUILayout.Toggle(RISStrings.GetString("reset_button"), selectedGroup.UseResetButton);
                        KaiGroupSwitch.Draw(risAvatar, groupIndex, selectedGroup);
                        KaiOutfitImport.Draw(risAvatar, selectedGroup);

                        using (var scope = new EditorGUILayout.HorizontalScope())
                        {
                            var scopeWidth = cellWidth + 20;
                            var propListHeight = propsReorderableList.GetHeight();
                            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(propListHeight), GUILayout.Width(scopeWidth)))
                            {
                                propIndex = propsReorderableList.index;
                                propsReorderableList.DoList(new Rect(scope.rect.x, scope.rect.y, scopeWidth, propListHeight));

                                selectedPropIsChanged = propIndex != propsReorderableList.index;
                                GUILayout.Space(0);
                            }
                        }
                    }
                }
                GUILayout.Box("", GUILayout.ExpandHeight(true), GUILayout.Width(1));
                var propIsSelected = groupIsSelected && groupIndex != -1 && propsReorderableList.index != -1 && groupIndex < risAvatar.Groups.Count &&
                        propsReorderableList.index < risAvatar.Groups[groupIndex].Props.Count;
                
                var nextProp = propIsSelected ? risAvatar.Groups[groupIndex].Props[propsReorderableList.index] : null;
                selectedPropIsChanged |= !ReferenceEquals(nextProp, selectedProp);
                selectedProp = nextProp ?? new Prop();
                using (new EditorGUI.DisabledGroupScope(!propIsSelected))
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(cellWidth + 30 - (showingVerticalScroll ? 14 : 0))))
                    {
                        GUIStyle headerStyle = new GUIStyle("HeaderLabel");
                        headerStyle.margin = new RectOffset(5, 5, 20, 20);
                        EditorGUILayout.LabelField("③ 選択した衣装の詳細", KaiEditorUI.SectionHeading, GUILayout.ExpandWidth(true));
                        GUILayout.Space(3);
                        EditorGUIUtility.labelWidth = RISStrings.GetWidth(3);
                        selectedProp.Name = EditorGUILayout.TextField(RISStrings.GetString("prop_name"), selectedProp.Name);

                        EditorGUI.BeginChangeCheck();

                        var icon = selectedProp.Icon.GetObject();
                        icon = (Texture2D)EditorGUILayout.ObjectField(RISStrings.GetString("icon"), icon, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        if (EditorGUI.EndChangeCheck())
                            selectedProp.Icon.SetObject(icon);
                        KaiInlineImage.Draw(risAvatar, selectedProp);

                        if (exclusiveMode == RIS.ExclusiveModeType.None)
                            selectedProp.ExclusiveGroup = RIS.ExclusiveGroupType.None;
                        else
                            selectedProp.ExclusiveGroup = (RIS.ExclusiveGroupType)groupIndex;

                        EditorGUI.BeginChangeCheck();
                        selectedProp.IsDefaultEnabled = EditorGUILayout.Toggle(RISStrings.GetString("default_status"), selectedProp.IsDefaultEnabled);
                        if (EditorGUI.EndChangeCheck())
                        {
                            var exclusiveGroup = selectedProp.ExclusiveGroup;
                            if (EditorSettings.DefaultStatusLimitter && selectedProp.IsDefaultEnabled && risAvatar.GetExclusiveMode(exclusiveGroup) == RIS.ExclusiveModeType.Exclusive)
                            {
                                foreach (var subGroupIndex in Enumerable.Range(0, risAvatar.Groups.Count))
                                {
                                    var subGroup = risAvatar.Groups[subGroupIndex];
                                    foreach (var subPropIndex in Enumerable.Range(0, subGroup.Props.Count))
                                    {
                                        var subProp = subGroup.Props[subPropIndex];
                                        if ((subGroupIndex != groupIndex || subPropIndex != propIndex) && subProp.ExclusiveGroup == selectedProp.ExclusiveGroup)
                                            subProp.IsDefaultEnabled = false;
                                    }
                                }
                            }
                        }

                        selectedProp.IsLocalOnly = EditorGUILayout.Toggle(RISStrings.GetString("local_mode"), selectedProp.IsLocalOnly);
                        selectedProp.UseSaveParameter = EditorGUILayout.Toggle(RISStrings.GetString("save_status"), selectedProp.UseSaveParameter);

                        if(selectedProp.TargetObjects.Count <= 0)
                            selectedProp.TargetObjects.Add(null);
                        if (risAvatar.GetAvatarRoot() == null)
                            return;
                        var parent = risAvatar.GetAvatarRoot()?.gameObject;
                        var targetObject = selectedProp.TargetObjects[0]?.GetObject(parent);
                        EditorGUI.BeginChangeCheck();
                        targetObject = (GameObject)EditorGUILayout.ObjectField(targetObject, typeof(GameObject), true);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (selectedProp.TargetObjects[0] == null)
                                selectedProp.TargetObjects[0] = new GUIDPathPair<GameObject>(ObjectPathStateType.RelativeFromObject, targetObject, parent);
                            else
                                selectedProp.TargetObjects[0].SetObject(targetObject, parent);
                            if (selectedProp.TargetObjects[0].GetObject(parent) == null && targetObject != null)
                            {
                                EditorUtility.DisplayDialog(RISStrings.GetString("ris"), RISStrings.GetString("dlg_childprop"), RISStrings.GetString("ok"));
                            }
                        }

                    }
                }
                GUILayout.FlexibleSpace();
            }
            EditorGUIUtility.labelWidth = 0;
        }

        public override void InitializeTab(ref Avatar risAvatar)
        {
            InitializeGroupList(risAvatar);
            InitializePropList(null, risAvatar);
        }
        // Rebind by data identity on every GUI event, rather than relying on a one-frame index change.
        internal Group BoundGroup => selectedGroup;
        internal Prop BoundProp => selectedProp;
        internal bool BindGroup(Avatar data, int index)
        {
            if (groupsReorderableList == null) InitializeGroupList(data);
            groupsReorderableList.index = index;
            var next = data != null && index >= 0 && index < data.Groups.Count ? data.Groups[index] : null;
            if (next != null && ReferenceEquals(next, selectedGroup) && propsReorderableList != null && ReferenceEquals(propsReorderableList.list, next.Props)) return false;
            selectedGroup = next ?? new Group();
            InitializePropList(next, data);
            propsReorderableList.index = next != null && next.Props.Count > 0 ? 0 : -1;
            selectedProp = next != null && next.Props.Count > 0 ? next.Props[0] : null;
            return true;
        }
        private void InitializeGroupList(Avatar risAvatar)
        {
            List<Group> groups = null;
            if (risAvatar != null && risAvatar.Groups != null)
                groups = risAvatar.Groups;
            else
                groups = new List<Group>();
            groupsReorderableList = new ReorderableList(groups, typeof(Group))
            {
                drawElementBackgroundCallback = (rect, index, isActive, isFocused) =>
                    KaiEditorUI.DrawGroupSelection(rect, index == groupsReorderableList.index),
                onReorderCallbackWithDetails = (l, from, to) => KaiReferences.GroupMoved(risAvatar, from, to),
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "① グループを選択（" + groups.Count + "）");
                    var position =
                        new Rect(
                            rect.x + rect.width - 20f,
                            rect.y,
                            20f,
                            13f
                        );
                    if (groups.Count < 8 && GUI.Button(position, ReorderableListStyle.AddContent, ReorderableListStyle.AddStyle))
                    {
                        var newGroup = new Group();
                        newGroup.Name = string.Format(RISStrings.GetString("defaultname_group"), groups.Count);
                        risAvatar.Groups.Add(newGroup);
                    }
                },
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (groups.Count <= index)
                        return;

                    var style = GUI.skin.label;
                    style.fontSize = (int)(rect.height / 1.75f);
                    var name = groups[index].Name;
                    if (string.IsNullOrEmpty(name))
                        name = string.Format(RISStrings.GetString("defaultname_group"), index);
                    KaiOutfitImport.HandleDrop(new Rect(rect.x, rect.y, rect.width - 22, rect.height), risAvatar, groups[index]);
                    var thumbnail = groups[index].Icon?.GetObject();
                    if (thumbnail != null) GUI.DrawTexture(new Rect(rect.x, rect.y + 2, 32, 32), thumbnail, ScaleMode.ScaleToFit, true);
                    var labelRect = new Rect(rect.x + 38, rect.y, rect.width - 60, rect.height);
                    GUI.Label(labelRect, name, style);
                    style.fontSize = 0;
                    rect.x = rect.x + rect.width - 20f;
                    rect.width = 20f;
                    if (GUI.Button(rect, ReorderableListStyle.SubContent, ReorderableListStyle.SubStyle))
                    {
                        KaiReferences.RemoveGroup(risAvatar, index);
                        if (index >= groups.Count)
                            index = groupsReorderableList.index = -1;
                    }
                },

                drawFooterCallback = rect => { },
                footerHeight = 0f,
                elementHeightCallback = index =>
                {
                    if (groups.Count <= index)
                        return 0;

                    return Mathf.Max(36, EditorGUIUtility.singleLineHeight * 1.6f);
                }

            };
        }
        private void InitializePropList(Group group, Avatar risAvatar)
        {
            List<Prop> props = null;
            if (group != null && group.Props != null)
                props = group.Props;
            else
                props = new List<Prop>();
            propsReorderableList = new ReorderableList(props, typeof(Prop))
            {
                onReorderCallbackWithDetails = (l, from, to) => KaiReferences.PropMoved(risAvatar, group, from, to),
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, string.Format(RISStrings.GetString("list_prop"), props.Count));
                    var position =
                        new Rect(
                            rect.x + rect.width - 20f,
                            rect.y,
                            20f,
                            13f
                        );
                    var maxPropCount = 255;
                    if (risAvatar != null && group != null)
                    {
                        if (group.UseResetButton)
                            maxPropCount = 255;
                    }
                    if (group != null && props.Count < maxPropCount && GUI.Button(position, ReorderableListStyle.AddContent, ReorderableListStyle.AddStyle))
                    {
                        var newProp = new Prop();
                        newProp.TargetObjects.Add(null);
                        if (group.Props == null)
                            group.Props = new List<Prop>();
                        group.Props.Add(newProp);
                    }
                },
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (props.Count <= index)
                        return;
                    var prop = props[index];
                    var rawPropName = prop.GetPropName(risAvatar);
                    var propName = !string.IsNullOrEmpty(rawPropName) ? rawPropName : string.Format(RISStrings.GetString("defaultname_prop"), index);
                    var previewRect = new Rect(rect.x, rect.y + 2, 32, 32);
                    var thumbnail = prop.Icon?.GetObject();
                    if (thumbnail != null) GUI.DrawTexture(previewRect, thumbnail, ScaleMode.ScaleToFit, true);
                    else GUI.Label(previewRect, EditorGUIUtility.IconContent("Prefab Icon"));
                    GUI.Label(new Rect(rect.x + 38, rect.y, rect.width - 62, rect.height), propName);
                    rect.x = rect.x + rect.width - 20f;
                    rect.width = 20f;
                    if (GUI.Button(rect, ReorderableListStyle.SubContent, ReorderableListStyle.SubStyle))
                    {
                        KaiReferences.RemoveProp(risAvatar, group, index);
                        if (index >= props.Count)
                            index = propsReorderableList.index = -1;
                    }
                },
                drawElementBackgroundCallback = (rect, index, isActive, isFocused) =>
                {
                    if (!isFocused)
                    {
                        var maxPropCount = 255;
                        if (risAvatar != null && group != null)
                        {
                            if (group.UseResetButton)
                                maxPropCount = 255;
                        }
                        if (index >= maxPropCount)
                        {
                            GUI.DrawTexture(rect, TexAssets.RedTexture);
                        }

                    }
                    else
                    {
                        GUI.DrawTexture(rect, TexAssets.BlueTexture);
                    }
                },
                drawFooterCallback = rect => { },
                footerHeight = 0f,
                elementHeightCallback = index =>
                {
                    if (props.Count <= index)
                        return 0;

                    return 36f;
                }

            };
        }

        public override void BuildExpressionParameters(ref Avatar risAvatar, string autoGeneratedFolder)
        {
            KaiParameters.Build(risAvatar, autoGeneratedFolder);
        }

        public override void BuildExpressionsMenu(ref Avatar risAvatar, string autoGeneratedFolder)
        {
            KaiMenuBuilder.Build(risAvatar, autoGeneratedFolder);
        }
        protected override void BuildFXLayer(ref Avatar risAvatar, string autoGeneratedFolder)
        {
            using (new KaiGroupSwitch.GenerationScope(risAvatar)) BuildFXLayerCore(ref risAvatar, autoGeneratedFolder);
        }
        private void BuildFXLayerCore(ref Avatar risAvatar, string autoGeneratedFolder)
        {
            Dictionary<GameObject, List<MaterialOverrideData>> materialOverrideDatas = new Dictionary<GameObject, List<MaterialOverrideData>>();
            List<IndexPair>[] exclusiveGroupIndexes = Enumerable.Range(0, 8).Select(n => new List<IndexPair>()).ToArray();

            var avatar = risAvatar.GetAvatarRoot();
            if (avatar == null)
                return;
            avatar.customizeAnimationLayers = true;
            var fxLayer = avatar.GetFXLayer(autoGeneratedFolder);
            // Isolated controllers must not replace clips still used by shared original FX assets.
            var animationsFolder = AssetDatabase.GetAssetPath(fxLayer).StartsWith(autoGeneratedFolder + "KaiIsolatedFX/", StringComparison.Ordinal)
                ? autoGeneratedFolder + "KaiIsolatedFX/Animations/" : autoGeneratedFolder + "Animations/";
            UnityUtils.ReCreateFolder(animationsFolder);
            foreach (var name in fxLayer.layers.Where(n => n.name.StartsWith("RIS")).Select(n => n.name))
                fxLayer.TryRemoveLayer(name);
            var parameters = fxLayer.parameters;
            foreach (var name in fxLayer.parameters.Where(n => n != null && n.name != null && n.name.StartsWith(RIS.Prefix + "-")).Select(n => n.name))
                fxLayer.TryRemoveParameter(name);
            fxLayer.parameters = parameters;
            var fallbackClip = new AnimationClip();
            var fallbackParamName = $"{RIS.Prefix}-Initialize";

            var noneClip = new AnimationClip();
            var noneClipName = "RIS_None";
            AssetDatabase.CreateAsset(noneClip, animationsFolder + $"{noneClipName}.anim");
            EditorUtility.SetDirty(noneClip);

            foreach (var groupIndex in Enumerable.Range(0, risAvatar.Groups.Count))
            {
                var group = risAvatar.Groups[groupIndex];
                foreach (var propIndex in Enumerable.Range(0, group.Props.Count))
                {
                    var prop = group.Props[propIndex];
                    var targetObject = prop?.GetFirstTargetObject(risAvatar);
                    if(targetObject != null)
                    {
                        if (EditorSettings.ApplyEnableDefault)
                            targetObject.SetActive(prop.IsDefaultEnabled);
                        var relativePath = targetObject.GetRelativePath(avatar.gameObject, false);
                        var curve = new AnimationCurve();
                        var frameValue = prop.IsDefaultEnabled ? 1 : 0;
                        curve.AddKey(0f, frameValue);
                        curve.AddKey(1f / fallbackClip.frameRate, frameValue);
                        fallbackClip.SetCurve(relativePath, typeof(GameObject), "m_IsActive", curve);
                    }
                    var layerName = $"{RIS.Prefix}-MAIN-G{groupIndex}P{propIndex}";

                    var paramName = $"{RIS.Prefix}-G{groupIndex}P{propIndex}";
                    CheckParam(avatar, fxLayer, paramName, prop.IsDefaultEnabled);
                    if (prop.IsLocalOnly)
                        CheckParam(avatar, fxLayer, "IsLocal", false);

                    var layer = fxLayer.FindAnimatorControllerLayer(layerName);
                    if (layer == null)
                        layer = fxLayer.AddAnimatorControllerLayer(layerName);
                    var stateMachine = layer.stateMachine;
                    stateMachine.Clear();
                    var exclusiveMode = groupIndex < 8 ? risAvatar.GetExclusiveMode((RIS.ExclusiveGroupType)groupIndex) : RIS.ExclusiveModeType.None;
                    var onState = stateMachine.AddState("PropON", new Vector3(300, 100, 0));
                    onState.writeDefaultValues = risAvatar.UseWriteDefaults;
                    var offState = stateMachine.AddState("PropOFF", new Vector3(300, 200, 0));
                    offState.writeDefaultValues = risAvatar.UseWriteDefaults;

                    var transition = stateMachine.MakeAnyStateTransition(onState);
                    transition.CreateSingleCondition(AnimatorConditionMode.If, paramName, 1f, prop.IsLocalOnly && !prop.IsDefaultEnabled, true);
                    transition = stateMachine.MakeAnyStateTransition(offState);
                    transition.CreateSingleCondition(AnimatorConditionMode.IfNot, paramName, 1f, prop.IsLocalOnly && prop.IsDefaultEnabled, true);
                    if (exclusiveMode == RIS.ExclusiveModeType.LegacyExclusive)
                    {
                        exclusiveGroupIndexes[groupIndex].Add(new IndexPair() { GroupIndex = groupIndex, PropIndex = propIndex });
                        stateMachine.defaultState = offState;
                    }
                    else
                    {
                        var clipName = "G" + groupIndex.ToString() + "P" + propIndex.ToString();
                        var relativePath = targetObject.GetRelativePath(avatar.gameObject, false);

                        stateMachine.defaultState = prop.IsDefaultEnabled ? onState : offState;

                        onState.motion = UnityUtils.CreateAnimationClip(relativePath, 1, typeof(GameObject), "m_IsActive", $"{clipName}ON", animationsFolder);
                        offState.motion = UnityUtils.CreateAnimationClip(relativePath, 0, typeof(GameObject), "m_IsActive", $"{clipName}OFF", animationsFolder);
                    }
                    if (exclusiveMode != RIS.ExclusiveModeType.None)
                    {
                        var driver = onState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                        foreach (var subPropIndex in Enumerable.Range(0, group.Props.Count))
                        {
                            var driverParameters = driver.parameters;
                            if (propIndex != subPropIndex)
                                driverParameters.Add(new VRC_AvatarParameterDriver.Parameter()
                                {
                                    name = $"{RIS.Prefix}-G{groupIndex}P{subPropIndex}",
                                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                                    value = 0
                                });
                            driver.parameters = driverParameters;
                        }
                    }
                    if (stateMachine.states.Length <= 0)
                    {
                        fxLayer.TryRemoveLayer(layerName);
                        EditorUtility.SetDirty(fxLayer);
                    }
                    else
                    {
                        layer.stateMachine = stateMachine;
                        EditorUtility.SetDirty(layer.stateMachine);
                    }
                }
                if(group.UseResetButton)
                {
                    var layerName = $"{RIS.Prefix}-RESET-G{groupIndex}";

                    var paramName = $"{RIS.Prefix}-G{groupIndex}RESET";
                    CheckParam(avatar, fxLayer, paramName, false);

                    var layer = fxLayer.FindAnimatorControllerLayer(layerName);
                    if (layer == null)
                        layer = fxLayer.AddAnimatorControllerLayer(layerName);
                    var stateMachine = layer.stateMachine;
                    stateMachine.Clear();

                    var onState = stateMachine.AddState("Reset", new Vector3(300, 100, 0));
                    onState.writeDefaultValues = risAvatar.UseWriteDefaults;
                    onState.motion = noneClip;
                    var offState = stateMachine.AddState("Wait", new Vector3(300, 200, 0));
                    offState.writeDefaultValues = risAvatar.UseWriteDefaults;
                    offState.motion = noneClip;

                    var transition = stateMachine.MakeAnyStateTransition(onState);
                    transition.CreateSingleCondition(AnimatorConditionMode.If, paramName, 1f, false, false);
                    transition = stateMachine.MakeAnyStateTransition(offState);
                    transition.CreateSingleCondition(AnimatorConditionMode.IfNot, paramName, 1f, false, false);

                    stateMachine.defaultState = offState;

                    var driver = onState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    var driverParameters = driver.parameters;
                    driverParameters =
                        Enumerable.Range(0, group.Props.Count).Select(num => new VRC_AvatarParameterDriver.Parameter()
                        {
                            name = $"{RIS.Prefix}-G{groupIndex}P{num}",
                            type = VRC_AvatarParameterDriver.ChangeType.Set,
                            value = group.Props[num].IsDefaultEnabled ? 1 : 0
                        }).ToList();
                    driverParameters.Add(new VRC_AvatarParameterDriver.Parameter()
                    {
                        name = $"{RIS.Prefix}-G{groupIndex}RESET",
                        type = VRC_AvatarParameterDriver.ChangeType.Set,
                        value = 0
                    });
                    driver.parameters = driverParameters;
                }
            }
            //V2排他モードが有効な場合
            foreach (var pairsIndex in Enumerable.Range(0, exclusiveGroupIndexes.Length))
            {
                var pairs = exclusiveGroupIndexes[pairsIndex];
                if (!pairs.Any())
                    continue;


                var layerName = $"{RIS.Prefix}-PAIR{pairsIndex}";

                var layer = fxLayer.FindAnimatorControllerLayer(layerName);
                if (layer == null)
                    layer = fxLayer.AddAnimatorControllerLayer(layerName);
                var stateMachine = layer.stateMachine;
                stateMachine.Clear();

                foreach (var v in Enumerable.Range(0, pairs.Count + 1))
                {
                    var activeIndex = v - 1;

                    var stateName = "DEFAULT";
                    if (activeIndex >= 0)
                    {
                        var targetPair = pairs[activeIndex];
                        stateName = $"G{targetPair.GroupIndex}P{targetPair.PropIndex}ON";
                    }
                    var state = stateMachine.AddState(stateName, new Vector3(300, 200 + (activeIndex * 50), 0));
                    state.writeDefaultValues = risAvatar.UseWriteDefaults;

                    var clip = new AnimationClip();
                    AnimatorStateTransition transition = null;
                    if (activeIndex == -1)
                    {
                        transition = stateMachine.MakeAnyStateTransition(state);
                        stateMachine.defaultState = state;
                    }
                    foreach (var pairIndex in Enumerable.Range(0, pairs.Count))
                    {
                        var pair = pairs[pairIndex];
                        var groupIndex = pair.GroupIndex;
                        var propIndex = pair.PropIndex;
                        var group = risAvatar.Groups[groupIndex];
                        var prop = group.Props[propIndex];
                        var curve = new AnimationCurve();
                        var frameValue = 0;
                        if (activeIndex == -1)
                            frameValue = prop.IsDefaultEnabled ? 1 : 0;
                        else if (activeIndex == pairIndex)
                            frameValue = 1;
                        curve.AddKey(0f, frameValue);
                        curve.AddKey(1f / clip.frameRate, frameValue);
                        clip.SetCurve(prop.GetFirstTargetObject(risAvatar)?.GetRelativePath(avatar.gameObject, false), typeof(GameObject), "m_IsActive", curve);

                        if (activeIndex == -1)
                        {
                            var paramName = $"{RIS.Prefix}-G{groupIndex}P{propIndex}";
                            CheckParam(avatar, fxLayer, paramName, false);
                            transition.AddCondition(AnimatorConditionMode.IfNot, paramName, 1f, false, true);
                        }
                    }
                    if (activeIndex != -1)
                    {
                        var targetPair = pairs[activeIndex];
                        var paramName = $"{RIS.Prefix}-G{targetPair.GroupIndex}P{targetPair.PropIndex}";
                        CheckParam(avatar, fxLayer, paramName, false);
                        transition = stateMachine.MakeAnyStateTransition(state);
                        var prop = risAvatar.Groups[targetPair.GroupIndex].Props[targetPair.PropIndex];
                        transition.CreateSingleCondition(AnimatorConditionMode.If, paramName, 1f, prop.IsLocalOnly && !prop.IsDefaultEnabled, true);
                    }
                    var clipName = $"PAIR{pairsIndex}-{stateName}";
                    AssetDatabase.CreateAsset(clip, animationsFolder + clipName + ".anim");
                    EditorUtility.SetDirty(clip);
                    state.motion = clip;
                }
            }

            AssetDatabase.CreateAsset(fallbackClip, animationsFolder + $"FallbackClip" + ".anim");
            AddFallbackDriver(ref fxLayer, ref risAvatar, fallbackClip);
            KaiGroupSwitch.Build(risAvatar, fxLayer, noneClip, animationsFolder);

            var genericFlag = avatar.IsGenericAvatar();
            var layerCount = genericFlag ? 3 : 5;
            if (avatar.baseAnimationLayers == null)
                avatar.baseAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[layerCount];
            if (avatar.baseAnimationLayers.Count() != layerCount)
                Array.Resize(ref avatar.baseAnimationLayers, layerCount);
            avatar.baseAnimationLayers[4].animatorController = fxLayer;



            EditorUtility.SetDirty(avatar.baseAnimationLayers[layerCount - 1].animatorController);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
