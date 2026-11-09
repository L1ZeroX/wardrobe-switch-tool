#if RISV4_JSON
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiParameters
    {
        public static List<VRCExpressionParameters.Parameter> Collect(Avatar data)
        {
            var original = data.GetAvatarRoot()?.expressionParameters;
            var result = (original?.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Where(p => p != null && !string.IsNullOrEmpty(p.name) && !p.name.StartsWith(RIS.Prefix + "-", StringComparison.Ordinal)).ToList();
            Action<string, float, bool, VRCExpressionParameters.ValueType> add = (name, value, saved, type) =>
                result.Add(new VRCExpressionParameters.Parameter { name = name, defaultValue = value, saved = saved, valueType = type });
            add(RIS.Prefix + "-Initialize", 1, true, VRCExpressionParameters.ValueType.Bool);
            for (int g = 0; g < data.Groups.Count; g++)
            {
                var group = data.Groups[g];
                if (data.MenuMode == RIS.MenuModeType.Simple && g == 7)
                {
                    if (group.Props.Any(p => p.PropSets.Any(s => s != null)))
                    {
                        int selected = group.Props.FindIndex(p => p.IsDefaultEnabled);
                        add(RIS.Prefix + "-SET", selected + 1, true, VRCExpressionParameters.ValueType.Int);
                    }
                    continue;
                }
                if (group.UseResetButton && data.MenuMode != RIS.MenuModeType.Simple)
                    add(RIS.Prefix + "-G" + g + "RESET", 0, false, VRCExpressionParameters.ValueType.Bool);
                for (int p = 0; p < group.Props.Count; p++)
                {
                    var prop = group.Props[p];
                    var exclusive = data.MenuMode != RIS.MenuModeType.Advanced ? (g < 8 ? (RIS.ExclusiveGroupType)g : RIS.ExclusiveGroupType.None) : prop.ExclusiveGroup;
                    bool legacy = !KaiGroupSwitch.Enabled(data, g) && data.GetExclusiveMode(exclusive) == RIS.ExclusiveModeType.LegacyExclusive;
                    add(RIS.Prefix + "-G" + g + "P" + p, KaiDefaultOutfit.Default(data, group, prop) && !legacy ? 1 : 0,
                        prop.UseSaveParameter && !KaiDefaultOutfit.Enabled(data), VRCExpressionParameters.ValueType.Bool);
                }
            }
            result.AddRange(KaiGroupSwitch.Parameters(data));
            return result;
        }
        public static void Build(Avatar data, string folder)
        {
            KaiPreflight.Require(data);
            var avatar = data.GetAvatarRoot();
            if (avatar == null) return;
            var parameters = Collect(data);
            var asset = avatar.expressionParameters;
            if (asset == null)
            {
                UnityUtils.CreateFolderRecursively(folder);
                string path = folder + "GeneratedExpressionParameters.asset";
                asset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                    AssetDatabase.CreateAsset(asset, path);
                }
            }
            asset.parameters = parameters.ToArray();
            avatar.expressionParameters = asset;
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
