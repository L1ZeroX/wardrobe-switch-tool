#if RISV4_JSON
using System.Collections.Generic;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiDiagnostics
    {
        static void Missing<T>(List<string> errors, string label, GUIDPathPair<T> reference, GameObject root = null) where T : Object
        {
            if (reference != null && !string.IsNullOrEmpty(reference.GetFormattedPath()) && reference.GetObject(root) == null)
                errors.Add(label + ": " + reference.GetFormattedPath());
        }
        public static List<string> Inspect(Avatar data)
        {
            var errors = new List<string>();
            if (data.GetAvatarRoot() == null) errors.Add("対象アバターが見つかりません。対象アバター欄で指定してください。");
            var root = data.GetAvatarRoot()?.gameObject;
            for (int g = 0; g < data.Groups.Count; g++)
            {
                var group = data.Groups[g];
                Missing(errors, "グループ" + g + " 画像", group.Icon);
                Missing(errors, "グループ" + g + " 既存メニュー", group.BaseMenu);
                for (int p = 0; p < group.Props.Count; p++)
                {
                    var prop = group.Props[p];
                    string label = "G" + g + " / " + prop.GetPropName(data);
                    Missing(errors, label + " 画像", prop.Icon);
                    Missing(errors, label + " ONアニメーション", prop.EnableAnimation);
                    Missing(errors, label + " OFFアニメーション", prop.DisableAnimation);
                    foreach (var target in prop.TargetObjects) Missing(errors, label + " 対象", target, root);
                    foreach (var material in prop.MaterialOverrides) Missing(errors, label + " マテリアル", material);
                    foreach (var pair in prop.PropSets)
                        if (pair == null || pair.GroupIndex < 0 || pair.GroupIndex >= data.Groups.Count || pair.PropIndex < 0 || pair.PropIndex >= data.Groups[pair.GroupIndex].Props.Count)
                            errors.Add(label + " 衣装セットの参照が不正です。元のEditorで修正してください。");
                }
            }
            return errors;
        }
    }
}
#endif
