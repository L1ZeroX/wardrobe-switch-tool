#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiPreflight
    {
        public static int Cost(Avatar data)
        {
            if (data == null) return 0;
            var parameters = KaiParameters.Collect(data);
            var temp = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            try { temp.parameters = parameters.ToArray(); return temp.CalcTotalCost(); }
            finally { UnityEngine.Object.DestroyImmediate(temp); }
        }
        public static void Require(Avatar data)
        {
            if (data == null) throw new InvalidOperationException("設定を読み込んでください。");
            KaiHeelLink.Validate(data);
            if (KaiDefaultOutfit.Enabled(data))
                for (int g = 0; g < data.Groups.Count; g++)
                    if (data.Groups[g].KaiDefaultGroup && !KaiGroupSwitch.Enabled(data, g))
                        throw new InvalidOperationException("初期衣装の「グループ内の排他を解除」をONにしてください: " + data.Groups[g].Name);
            KaiGroupSwitch.Validate(data);
            if (data.MenuMode == RIS.MenuModeType.Simple && data.Groups.Count > 8)
                throw new InvalidOperationException("シンプルモードは8グループの固定構成です。多数のグループにはベーシックまたはアドバンスを使用してください。");
            int cost = Cost(data);
            if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
                throw new InvalidOperationException("パラメーター容量超過: " + cost + " / " + VRCExpressionParameters.MAX_PARAMETER_COST + " bits。衣装数または他の同期パラメーターを減らしてください。生成結果は更新していません。");
            if (data.MenuMode == RIS.MenuModeType.Simple && data.Groups.Count > 7 && data.Groups[7].Props.Count > 255)
                throw new InvalidOperationException("シンプルモードの衣装セットは255個までです。");
        }
    }
}
#endif
