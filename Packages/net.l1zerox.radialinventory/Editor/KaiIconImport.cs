#if RISV4_JSON
using System;
using System.Collections.Generic;
using UnityEditor;

namespace YagihataItems.RadialInventorySystemV4
{
    // Match the SDK's Expressions icon requirements without changing PNGs or their GUIDs.
    public sealed class KaiIconImport : AssetPostprocessor
    {
        public const int MaxSize = 256;
        public const string GeneratedFolder = "Assets/YagihataItems/RadialInventorySystemV4/KaiIcons/";
        static readonly string[] Platforms = { "Standalone", "Android", "iPhone" };
        static readonly HashSet<string> Pending = new HashSet<string>();
        static bool pendingScheduled;

        public static bool IsGeneratedIcon(string path)
        {
            return !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith(GeneratedFolder, StringComparison.Ordinal)
                && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Do not register OnPreprocessTexture: adding it invalidates every texture in large projects.
            foreach (string path in imported) if (IsGeneratedIcon(path)) Pending.Add(path);
            foreach (string path in moved) if (IsGeneratedIcon(path)) Pending.Add(path);
            if (Pending.Count == 0 || pendingScheduled) return;
            pendingScheduled = true;
            EditorApplication.delayCall += RepairPending;
        }

        static void RepairPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += RepairPending; return; }
            var paths = new List<string>(Pending);
            Pending.Clear();
            pendingScheduled = false;
            foreach (string path in paths) Repair(path);
        }

        public static bool Configure(TextureImporter importer)
        {
            if (importer == null || !IsGeneratedIcon(importer.assetPath)) return false;
            bool changed = false;
            if (importer.maxTextureSize > MaxSize) { importer.maxTextureSize = MaxSize; changed = true; }
            if (importer.textureCompression == TextureImporterCompression.Uncompressed)
            { importer.textureCompression = TextureImporterCompression.Compressed; changed = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
            if (importer.alphaSource != TextureImporterAlphaSource.FromInput)
            { importer.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            foreach (string name in Platforms)
            {
                var settings = importer.GetPlatformTextureSettings(name);
                if (!settings.overridden) continue;
                bool platformChanged = false;
                if (settings.maxTextureSize > MaxSize) { settings.maxTextureSize = MaxSize; platformChanged = true; }
                if (settings.textureCompression == TextureImporterCompression.Uncompressed)
                { settings.textureCompression = TextureImporterCompression.Compressed; platformChanged = true; }
                // Explicit RGBA formats bypass compression even when the default is compressed.
                if (settings.format != TextureImporterFormat.Automatic)
                { settings.format = TextureImporterFormat.Automatic; platformChanged = true; }
                if (platformChanged) { importer.SetPlatformTextureSettings(settings); changed = true; }
            }
            return changed;
        }

        [InitializeOnLoadMethod]
        static void ScheduleRepair() { EditorApplication.delayCall += RepairWhenReady; }

        static void RepairWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += RepairWhenReady; return; }
            RepairExisting();
        }

        public static int RepairExisting()
        {
            string folder = GeneratedFolder.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            int repaired = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsGeneratedIcon(path)) continue;
                if (Repair(path)) repaired++;
            }
            return repaired;
        }

        static bool Repair(string path)
        {
            try
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!Configure(importer)) return false;
                importer.SaveAndReimport();
                return true;
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("衣装サムネイルのサイズ設定を更新できませんでした: " + path + "\n" + error.Message);
                return false;
            }
        }
    }
}
#endif
