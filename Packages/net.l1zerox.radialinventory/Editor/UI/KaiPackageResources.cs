#if RISV4_JSON
using System;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    internal static class KaiPackageResources
    {
        // assetPath is Unity's virtual package path, for embedded, local and cached packages.
        internal static string Root
        {
            get
            {
                var info = PackageInfo.FindForAssembly(typeof(MainWindow).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.assetPath)) return info.assetPath;
                // Also supports a source checkout imported under Assets, without scanning by filename.
                var anchor = AssetDatabase.GUIDToAssetPath("8f0f10642b2148c08d5f6abffb93b891");
                const string suffix = "/Editor/UI/WardrobeWindow.uxml";
                if (anchor.EndsWith(suffix, StringComparison.Ordinal)) return anchor.Substring(0, anchor.Length-suffix.Length);
                throw new InvalidOperationException("UIリソースが見つかりません。パッケージを再インストールしてください。");
            }
        }
        internal static T Load<T>(string relative) where T : UnityEngine.Object
        {
            var path = Root + "/" + relative;
            var result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (result == null) throw new InvalidOperationException("UIリソースを読み込めません：" + path);
            return result;
        }
    }
}
#endif
