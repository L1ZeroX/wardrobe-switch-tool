#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    // Stage only heel fields. Closing the window never edits the avatar or its settings.
    public sealed class KaiHeelEditSession
    {
        public readonly Avatar Data;
        public bool Enabled;
        readonly bool initialEnabled;
        readonly Dictionary<Prop, Prop> drafts = new Dictionary<Prop, Prop>();
        readonly Dictionary<Prop, string> initial = new Dictionary<Prop, string>();
        static string Fields(Prop p) => JsonConvert.SerializeObject(new { p.KaiHeelMode, p.KaiHeelWeight, p.KaiHeelRenderer, p.KaiHeelShape });
        public KaiHeelEditSession(Avatar data) { Data = data; Enabled = initialEnabled = data.KaiExperimentalHeels; }
        public Prop Draft(Prop source)
        {
            if (!drafts.ContainsKey(source))
            {
                initial[source] = Fields(source);
                drafts[source] = JsonConvert.DeserializeObject<Prop>(JsonConvert.SerializeObject(source));
            }
            return drafts[source];
        }
        public bool Changed => Enabled != initialEnabled || drafts.Any(p => Fields(p.Value) != initial[p.Key]);
        public void Commit()
        {
            if (Data.GetAvatarRoot() == null) throw new InvalidOperationException("対象アバターが見つかりません。");
            var changed = drafts.Where(p => Fields(p.Value) != initial[p.Key]).ToArray();
            if (Enabled != initialEnabled && Data.KaiExperimentalHeels != initialEnabled)
                throw new InvalidOperationException("メイン画面で連動設定が変更されました。調整画面を開き直してください。");
            foreach (var pair in changed)
            {
                if (!Data.Groups.Any(g => g.Props.Contains(pair.Key))) throw new InvalidOperationException("編集中の衣装が削除されています。調整画面を開き直してください。");
                if (Fields(pair.Key) != initial[pair.Key]) throw new InvalidOperationException("別の画面でヒール設定が変更されました。調整画面を開き直してください。");
                var link = KaiHeelLink.Resolve(Data, pair.Value);
                if (Enabled && link?.Error != null) throw new InvalidOperationException(pair.Key.GetPropName(Data) + ": " + link.Error);
            }
            // Validate every edit before writing any of them.
            foreach (var pair in changed)
            {
                pair.Key.KaiHeelMode = pair.Value.KaiHeelMode;
                pair.Key.KaiHeelWeight = pair.Value.KaiHeelWeight;
                pair.Key.KaiHeelRenderer = pair.Value.KaiHeelRenderer;
                pair.Key.KaiHeelShape = pair.Value.KaiHeelShape;
            }
            if (Enabled != initialEnabled) Data.KaiExperimentalHeels = Enabled;
        }
    }

    public sealed class KaiHeelWindow : EditorWindow
    {
        KaiHeelEditSession session;
        Prop selected;
        Texture2D preview;
        Vector2 scroll;
        bool worn = true, feet = true, pending;
        float yaw = 35, pitch = -8, zoom = 1;
        double due;
        string error, notice;

        public static void Open(Avatar data, Prop prop = null)
        {
            // A separate window owns each draft; source edits are checked at commit time.
            var window = CreateInstance<KaiHeelWindow>();
            window.titleContent = new GUIContent("ヒール補正の調整");
            window.minSize = new Vector2(820, 620);
            window.session = new KaiHeelEditSession(data);
            window.selected = prop ?? data.Groups.SelectMany(g => g.Props).FirstOrDefault(p => KaiHeelLink.Resolve(data, p) != null)
                ?? data.Groups.SelectMany(g => g.Props).FirstOrDefault();
            window.RequestPreview(); window.Show();
        }
        void OnEnable() { EditorApplication.update += UpdatePreview; }
        void OnDisable() { EditorApplication.update -= UpdatePreview; ClearPreview(); }
        void ClearPreview() { if (preview != null) DestroyImmediate(preview); preview = null; }
        void RequestPreview() { pending = true; due = EditorApplication.timeSinceStartup + .18; Repaint(); }
        void UpdatePreview()
        {
            if (!pending || EditorApplication.timeSinceStartup < due || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            pending = false;
            if (session == null || selected == null) return;
            try
            {
                var image = KaiThumbnail.PreviewHeel(session.Data, selected, session.Draft(selected), worn, feet, yaw, pitch, zoom);
                ClearPreview(); preview = image; error = null;
            }
            catch (Exception ex) { ClearPreview(); error = ex.Message; }
            Repaint();
        }
        void OnGUI()
        {
            if (session == null || session.Data.GetAvatarRoot() == null)
            {
                EditorGUILayout.HelpBox("メイン画面でアバターを選び、ヒール補正の調整を開き直してください。", MessageType.Info); return;
            }
            var data = session.Data;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ヒール補正の調整（実験）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("足元を見ながら、靴に合う角度を調整します。", EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    EditorGUIUtility.labelWidth = 148;
                    session.Enabled = EditorGUILayout.ToggleLeft("靴の着脱とヒール補正を連動する", session.Enabled);
                    EditorGUI.BeginChangeCheck();
                    int group = data.Groups.FindIndex(g => g.Props.Contains(selected));
                    var groupNames = data.Groups.Select((g, i) => (i + 1) + ": " + g.Name).ToArray();
                    int newGroup = EditorGUILayout.Popup("衣装グループ", group, groupNames);
                    if (newGroup != group && newGroup >= 0 && newGroup < data.Groups.Count)
                    { group = newGroup; selected = data.Groups[group].Props.FirstOrDefault(); }
                    if (group >= 0)
                    {
                        var items = data.Groups[group].Props;
                        int item = EditorGUILayout.Popup("調整するプロップ", items.IndexOf(selected), items.Select(p => p.GetPropName(data)).ToArray());
                        if (item >= 0 && item < items.Count) selected = items[item];
                    }
                    if (EditorGUI.EndChangeCheck()) { notice = null; ClearPreview(); RequestPreview(); }
                    if (selected != null && group >= 0)
                    {
                        EditorGUILayout.Space(10);
                        EditorGUI.BeginChangeCheck();
                        KaiHeelLink.DrawSettings(data, session.Draft(selected));
                        if (EditorGUI.EndChangeCheck()) { notice = null; RequestPreview(); }
                    }
                    else EditorGUILayout.HelpBox("グループに衣装を追加してください。", MessageType.Info);
                    EditorGUILayout.Space(8);
                    EditorGUILayout.HelpBox("自動検出は body_all・body_base などに対応します。見つからない場合は「手動指定」でメッシュとシェイプキーを選べます。", MessageType.Info);
                    EditorGUILayout.HelpBox("プレビューは一時複製を使います。元アバターの表示・補正値は変更しません。", MessageType.None);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    feet = GUILayout.Toolbar(feet ? 0 : 1, new[] { "足元を拡大", "アバター全体" }, GUILayout.Height(28)) == 0;
                    worn = GUILayout.Toolbar(worn ? 0 : 1, new[] { "履いた状態（設定値）", "脱いだ状態（0）" }, GUILayout.Height(28)) == 0;
                    float messages = (string.IsNullOrEmpty(error) ? 0 : 60) + (string.IsNullOrEmpty(notice) ? 0 : 40);
                    var side = Mathf.Max(120, Mathf.Min(position.width - 386, position.height - 310 - messages));
                    var rect = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(rect, new Color(.15f, .15f, .15f));
                    if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
                    else GUI.Label(rect, pending ? "プレビューを作成中…" : "対象を選んでください", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
                    EditorGUIUtility.labelWidth = 70;
                    yaw = EditorGUILayout.Slider("向き", yaw, -180, 180);
                    pitch = EditorGUILayout.Slider("上下角度", pitch, -60, 30);
                    zoom = EditorGUILayout.Slider("ズーム", zoom, .5f, 3);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("正面")) { yaw = 0; pitch = 0; GUI.changed = true; }
                        if (GUILayout.Button("横")) { yaw = 90; pitch = 0; GUI.changed = true; }
                        if (GUILayout.Button("表示をリセット")) { yaw = 35; pitch = -8; zoom = 1; GUI.changed = true; }
                        if (GUILayout.Button("再描画")) RequestPreview();
                    }
                    if (EditorGUI.EndChangeCheck()) RequestPreview();
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (!string.IsNullOrEmpty(notice)) EditorGUILayout.HelpBox(notice, MessageType.Info);
            EditorGUILayout.LabelField("反映後はメイン画面で「適用する」と設定の保存を行ってください。VRChatには再アップロードが必要です。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!session.Changed))
                    if (GUILayout.Button("設定をメイン画面へ反映", GUILayout.Height(32)))
                    {
                        try
                        {
                            session.Commit(); session = new KaiHeelEditSession(data);
                            notice = "メイン画面へ反映しました。「適用する」と設定の保存へ進んでください。";
                            foreach (var window in Resources.FindObjectsOfTypeAll<MainWindow>()) window.Repaint();
                        }
                        catch (Exception ex) { error = ex.Message; }
                    }
                if (GUILayout.Button("閉じる（未反映の変更を破棄）", GUILayout.Height(32))) Close();
            }
        }
    }
}
#endif
