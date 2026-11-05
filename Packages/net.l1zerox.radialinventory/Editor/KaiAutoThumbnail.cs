#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    // Observe changes in all three original tabs as well as the batch-registration UI.
    // Capture after OnGUI, once per changed item; failures never create a repaint retry loop.
    internal static class KaiAutoThumbnail
    {
        sealed class Pending
        {
            public string Signature;
            public GUIDPathPair<Texture2D> Icon;
            public string IconGuid;
        }
        sealed class State
        {
            public bool Initialized;
            public readonly Dictionary<Prop, string> Seen = new Dictionary<Prop, string>();
            public readonly Dictionary<Prop, Pending> Pending = new Dictionary<Prop, Pending>();
            public string Status = "";
        }
        static readonly ConditionalWeakTable<Avatar, State> states = new ConditionalWeakTable<Avatar, State>();
        static readonly Queue<KeyValuePair<Avatar, Prop>> queue = new Queue<KeyValuePair<Avatar, Prop>>();
        static bool scheduled;

        static IEnumerable<Prop> Props(Avatar data) => data.Groups.Where(g => g != null).SelectMany(g => g.Props).Where(p => p != null);
        static string Signature(Avatar data, Prop prop)
        {
            var root = data.GetAvatarRoot();
            if (root == null) return "";
            var items = new List<Prop> { prop };
            foreach (var pair in prop.PropSets.Where(p => p != null))
                if (pair.GroupIndex >= 0 && pair.GroupIndex < data.Groups.Count && pair.PropIndex >= 0 && pair.PropIndex < data.Groups[pair.GroupIndex].Props.Count)
                    items.Add(data.Groups[pair.GroupIndex].Props[pair.PropIndex]);
            var targets = items.SelectMany(p => p.TargetObjects).Select(r => r?.GetObject(root.gameObject)).Where(o => o != null).Select(o => o.GetInstanceID()).Distinct().OrderBy(id => id).ToArray();
            return targets.Length == 0 ? "" : root.GetInstanceID() + ":" + string.Join(",", targets);
        }
        public static void Observe(Avatar data)
        {
            if (data == null) return;
            var state = states.GetValue(data, _ => new State());
            var props = Props(data).ToArray();
            foreach (var removed in state.Seen.Keys.Where(p => !props.Contains(p)).ToArray())
            {
                state.Seen.Remove(removed);
                state.Pending.Remove(removed);
            }
            foreach (var prop in props)
            {
                var signature = Signature(data, prop);
                string old;
                bool changed = !state.Seen.TryGetValue(prop, out old) || signature != old;
                state.Seen[prop] = signature;
                if (state.Initialized && changed && signature.Length > 0 && data.KaiAutoThumbnail)
                    Enqueue(data, prop, state, signature);
            }
            state.Initialized = true;
        }
        static void Enqueue(Avatar data, Prop prop, State state, string signature)
        {
            if (!state.Pending.ContainsKey(prop)) queue.Enqueue(new KeyValuePair<Avatar, Prop>(data, prop));
            state.Pending[prop] = new Pending { Signature = signature, Icon = prop.Icon, IconGuid = prop.Icon?.ObjectGUID };
            state.Status = "衣装のサムネイルを撮影しています…";
            Schedule();
        }
        static void Schedule()
        {
            if (scheduled || queue.Count == 0) return;
            scheduled = true;
            EditorApplication.delayCall += ProcessNext;
        }
        static void ProcessNext()
        {
            scheduled = false;
            if (queue.Count == 0) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Schedule(); return; }
            var request = queue.Dequeue();
            CapturePending(request.Key, request.Value);
            foreach (var window in Resources.FindObjectsOfTypeAll<MainWindow>()) window.Repaint();
            foreach (var window in Resources.FindObjectsOfTypeAll<KaiWardrobeWindow>()) window.Repaint();
            Schedule();
        }
        static void CapturePending(Avatar data, Prop prop)
        {
            var state = states.GetValue(data, _ => new State());
            Pending pending;
            if (state.Pending.TryGetValue(prop, out pending))
            {
                state.Pending.Remove(prop);
                if (data.KaiAutoThumbnail && Props(data).Contains(prop) && Signature(data, prop) == pending.Signature && ReferenceEquals(prop.Icon, pending.Icon) && prop.Icon?.ObjectGUID == pending.IconGuid)
                {
                    try
                    {
                        KaiThumbnail.Capture(data, prop, 0, 1);
                        state.Status = prop.GetPropName(data) + " を撮影しました。メニューへの反映は「適用する」。";
                    }
                    catch (Exception ex)
                    {
                        state.Status = "自動撮影できませんでした: " + ex.Message + " 現在の画像は保持しています。「撮影する」で再試行できます。";
                        Debug.LogWarning("[衣装簡単切替ツール] " + state.Status);
                    }
                }
            }
        }
        // Saving/applying immediately after dropping clothes must also save their icons.
        public static void CompletePending(Avatar data)
        {
            Observe(data);
            foreach (var prop in states.GetValue(data, _ => new State()).Pending.Keys.ToArray())
                CapturePending(data, prop);
        }
        public static void DrawSettings(Avatar data)
        {
            if (data == null) return;
            Observe(data);
            var state = states.GetValue(data, _ => new State());
            var automatic = EditorGUILayout.Popup(new GUIContent("サムネイル作成", "自動: 衣装の登録・対象変更時に、衣装だけを透明背景で撮影。手動: ボタンで撮影、または画像を指定。"), data.KaiAutoThumbnail ? 0 : 1, new[] { "自動（衣装追加時に撮影）", "手動（撮影・画像指定）" }) == 0;
            if (automatic != data.KaiAutoThumbnail) SetAutomatic(data, automatic);
            if (!string.IsNullOrEmpty(state.Status)) EditorGUILayout.HelpBox(state.Status, MessageType.None);
        }
        internal static void SetAutomatic(Avatar data, bool automatic)
        {
            Observe(data);
            var state = states.GetValue(data, _ => new State());
            {
                data.KaiAutoThumbnail = automatic;
                if (!automatic) { state.Pending.Clear(); state.Status = "手動モードです。撮影ボタンまたは画像指定を使用します。"; }
                else
                {
                    state.Status = "自動モードです。画像未設定の衣装を撮影します。";
                    foreach (var prop in Props(data).Where(p => p.Icon?.GetObject() == null))
                    {
                        var signature = Signature(data, prop);
                        if (signature.Length > 0) Enqueue(data, prop, state, signature);
                    }
                }
            }
        }
    }
}
#endif
