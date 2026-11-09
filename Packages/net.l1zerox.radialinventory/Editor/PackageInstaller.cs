
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class PackageInstaller
    {
        [MenuItem("衣装簡単切替ツール/メンテナンス/依存パッケージを確認", priority = 32)]
        public static void ReloadPackages()
        {
            CheckPackages();
        }
        [InitializeOnLoadMethod]
        static void CheckPackages()
        {
            // Both assemblies enable RISV4_JSON from the declared Newtonsoft package.
            // Do not depend on the original RIS package or mutate project-wide symbols.
#if !RISV4_JSON
            Debug.LogError("衣装簡単切替ツール: Newtonsoft JSONの読み込みを確認してください。Unity Package Managerで依存パッケージの解決が完了すると有効になります。");
#endif
        }
        public static Type GetTypeByClassName(string className)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(className, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
