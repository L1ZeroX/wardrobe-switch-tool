#if RISV4_JSON
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    internal static class KaiModeSettings
    {
        const string Prefix = "net.l1zerox.radialinventory.UI.";
        internal static bool ShowSimple
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowSimple", false); }
            set { EditorPrefs.SetBool(Prefix + "ShowSimple", value); }
        }
        internal static bool ShowAdvanced
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowAdvanced", false); }
            set { EditorPrefs.SetBool(Prefix + "ShowAdvanced", value); }
        }

        // Only newly created editor settings use Basic. Legacy JSON defaults remain compatible.
        internal static Avatar CreateAvatar() => new Avatar { MenuMode = RIS.MenuModeType.Basic };
        internal static string Label(RIS.MenuModeType mode)
        {
            switch (mode)
            {
                case RIS.MenuModeType.Simple: return "シンプル";
                case RIS.MenuModeType.Advanced: return "詳細";
                default: return "基本";
            }
        }

        internal static List<RIS.MenuModeType> VisibleModes(RIS.MenuModeType current, bool simple, bool advanced)
        {
            var modes = new List<RIS.MenuModeType>();
            // Keep a loaded mode reachable without silently converting its saved configuration.
            if (simple || current == RIS.MenuModeType.Simple) modes.Add(RIS.MenuModeType.Simple);
            modes.Add(RIS.MenuModeType.Basic);
            if (advanced || current == RIS.MenuModeType.Advanced) modes.Add(RIS.MenuModeType.Advanced);
            return modes;
        }

        internal static void DrawToolbar(Avatar data)
        {
            var modes = VisibleModes(data.MenuMode, ShowSimple, ShowAdvanced);
            var labels = modes.Select(mode => new GUIContent(
                (mode == data.MenuMode ? "● " : "") + Label(mode),
                mode == RIS.MenuModeType.Simple ? "部位と衣装セットを編集します。" :
                mode == RIS.MenuModeType.Basic ? "グループごとの衣装を編集します。" :
                "既存メニューやアニメーションなどを細かく設定します。")).ToArray();
            int selected = modes.IndexOf(data.MenuMode);
            int next = GUILayout.Toolbar(selected, labels, KaiEditorUI.Tabs,
                GUILayout.Width(120 * modes.Count), GUILayout.Height(KaiEditorUI.ControlHeight));
            if (next != selected && next >= 0 && next < modes.Count) data.MenuMode = modes[next];
        }

        internal static void DrawRetainedModeNotice(Avatar data)
        {
            if ((data.MenuMode == RIS.MenuModeType.Simple && !ShowSimple) ||
                (data.MenuMode == RIS.MenuModeType.Advanced && !ShowAdvanced))
                EditorGUILayout.HelpBox("この衣装設定は「" + Label(data.MenuMode) +
                    "」で作成されています。設定を保つため、使用中のモードを一時表示しています。「基本」を選ぶと非表示になります。", MessageType.Info);
        }
    }

    public class KaiModeSettingsWindow : EditorWindow
    {
        [MenuItem("衣装簡単切替ツール/表示設定", priority = 10)]
        public static void Open()
        {
            var window = GetWindow<KaiModeSettingsWindow>("表示設定");
            window.minSize = new Vector2(480, 280);
            window.Show();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                GUILayout.Label("表示する編集モード", KaiEditorUI.Title);
                GUILayout.Label("普段は「基本」だけで衣装を編集できます。必要なモードを追加してください。", KaiEditorUI.Hint);
                using (new EditorGUI.DisabledScope(true))
                    GUILayout.Toggle(true, "基本（常に表示）", KaiEditorUI.Toggle);
                EditorGUI.BeginChangeCheck();
                bool simple = GUILayout.Toggle(KaiModeSettings.ShowSimple, "シンプルを表示（部位・衣装セット）", KaiEditorUI.Toggle);
                bool advanced = GUILayout.Toggle(KaiModeSettings.ShowAdvanced, "詳細を表示（メニュー・アニメーション）", KaiEditorUI.Toggle);
                if (EditorGUI.EndChangeCheck())
                {
                    KaiModeSettings.ShowSimple = simple;
                    KaiModeSettings.ShowAdvanced = advanced;
                    foreach (var window in Resources.FindObjectsOfTypeAll<MainWindow>()) window.Repaint();
                }
                GUILayout.Space(8);
                GUILayout.Label("変更は自動で保存されます。非表示にしても衣装設定は削除されません。使用中のモードは、別のモードを選ぶまで表示します。", KaiEditorUI.Hint);
            }
        }
    }
}
#endif
