#if RISV4_JSON
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YagihataItems.RadialInventorySystemV4
{
    public sealed class KaiChestFitWindow : EditorWindow
    {
        GameObject root;
        SkinnedMeshRenderer body,clothing;
        string shape="",message="";
        KaiChestFitSession session;
        Vector2 scroll,lastMouse;
        int tab;
        KaiFitBrush brush;
        bool before,closeup=true,painting;
        float radius=.06f,strength=.5f,gap=.002f,yaw=15,pitch=0,zoom=1;

        [MenuItem("衣装簡単切替ツール/胸・衣装補正（実験）",priority=12)]
        static void Menu()
        {
            var selected=Selection.activeGameObject?.GetComponentInParent<VRCAvatarDescriptor>();
            var avatars=FindObjectsOfType<VRCAvatarDescriptor>();Open(selected!=null?selected.gameObject:avatars.Length==1?avatars[0].gameObject:null);
        }
        public static void Open(GameObject avatar)
        {
            var window=GetWindow<KaiChestFitWindow>();window.titleContent=new GUIContent("胸・衣装補正（実験）");window.minSize=new Vector2(960,710);
            if(window.root!=avatar){window.Clear();window.root=avatar;window.SelectBody();}window.Show();
        }
        void OnEnable(){wantsMouseMove=true;}
        void OnDisable(){Clear();}
        void Clear(){session?.Dispose();session=null;painting=false;}
        static string Normalize(string s)=>KaiHeelLink.Normalize(s);
        static bool ChestName(string s)=>new[]{"breast","bust","chestsize","mune","胸"}.Any(n=>Normalize(s).Contains(n));
        void SelectBody()
        {
            body=root==null?null:root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh!=null)
                .OrderByDescending(r=>Normalize(r.name)=="bodybase"||Normalize(r.name)=="bodyall"?4:Normalize(r.name)=="body"?3:Normalize(r.name).Contains("body")?2:0)
                .ThenByDescending(r=>Enumerable.Range(0,r.sharedMesh.blendShapeCount).Any(i=>ChestName(r.sharedMesh.GetBlendShapeName(i)))).FirstOrDefault();
            clothing=null;shape="";SuggestShape();
        }
        void SuggestShape()
        {
            shape=body?.sharedMesh==null?"":Enumerable.Range(0,body.sharedMesh.blendShapeCount).Select(body.sharedMesh.GetBlendShapeName).FirstOrDefault(ChestName)??"";
        }
        void Run(Action action)
        {
            try{action();}catch(Exception ex){message=ex.Message;}Repaint();
        }
        void StartEditing()
        {
            // Construct first so a failed selection never destroys the previous draft.
            var next=new KaiChestFitSession(root,body,clothing,shape);Clear();session=next;before=false;message="左ドラッグで選択した衣装を補正します。右ドラッグで回転、ホイールで拡大できます。";
        }
        SkinnedMeshRenderer RendererPopup(string label,SkinnedMeshRenderer current,SkinnedMeshRenderer exclude=null)
        {
            var all=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh!=null && r!=exclude).ToArray();
            var names=new[]{"選択してください"}.Concat(all.Select(r=>AnimationUtility.CalculateTransformPath(r.transform,root.transform))).ToArray();
            int selected=EditorGUILayout.Popup(label,Array.IndexOf(all,current)+1,names);
            return selected<=0?null:all[selected-1];
        }
        void OnGUI()
        {
            EditorGUILayout.Space(6);EditorGUILayout.LabelField("胸・衣装補正（実験）",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("① 素体と衣装を選ぶ → ② 胸サイズを調整してブラシで直す → ③ 適用してシーンを保存",EditorStyles.wordWrappedLabel);
            EditorGUIUtility.labelWidth=110;
            var nextRoot=(GameObject)EditorGUILayout.ObjectField("対象アバター",root,typeof(GameObject),true);
            if(nextRoot!=root){Clear();root=nextRoot;SelectBody();}
            tab=GUILayout.Toolbar(tab,new[]{"調整・プレビュー","適用済み・復元"},GUILayout.Height(30));
            if(root==null){EditorGUILayout.HelpBox("シーン上のアバターを選んでください。",MessageType.Info);return;}
            if(EditorUtility.IsPersistent(root)||EditorApplication.isPlayingOrWillChangePlaymode)
            {EditorGUILayout.HelpBox("シーン上のアバターを再生停止中に編集してください。",MessageType.Warning);return;}
            if(tab==1){DrawRestore();return;}
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(325)))
                {
                    scroll=EditorGUILayout.BeginScrollView(scroll);
                    var nextBody=RendererPopup("素体メッシュ",body);
                    if(nextBody!=body){body=nextBody;SuggestShape();}
                    clothing=RendererPopup("直す衣装",clothing,body);
                    if(body?.sharedMesh!=null)
                    {
                        var shapes=new[]{"現在の体型を使う"}.Concat(Enumerable.Range(0,body.sharedMesh.blendShapeCount).Select(body.sharedMesh.GetBlendShapeName)).ToArray();
                        int i=Array.IndexOf(shapes,shape);i=EditorGUILayout.Popup("胸シェイプキー",Mathf.Max(0,i),shapes);shape=i==0?"":shapes[i];
                    }
                    bool matches=session!=null&&session.Body==body&&session.Clothing==clothing&&session.Shape==shape;
                    using(new EditorGUI.DisabledScope(body==null||clothing==null))
                        if(GUILayout.Button(session==null?"プレビューを開始":"選んだ対象で編集を開始し直す",GUILayout.Height(30)))Run(StartEditing);
                    if(session!=null&&!matches)EditorGUILayout.HelpBox("対象が変わっています。「編集を開始し直す」で切り替えます。未適用のブラシ編集は破棄されます。",MessageType.Info);
                    if(session!=null)
                    {
                        EditorGUILayout.Space(8);
                        EditorGUILayout.LabelField("編集中："+session.Clothing.name,EditorStyles.boldLabel);
                        using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(session.Shape)))
                        {
                            EditorGUI.BeginChangeCheck();float value=EditorGUILayout.Slider("胸サイズ",session.Value,0,100);
                            if(EditorGUI.EndChangeCheck())Run(()=>session.SetValue(value));
                        }
                        EditorGUILayout.LabelField("サイズ変更は素体のプレビューだけに反映。はみ出した部分を下のブラシで直します。",EditorStyles.wordWrappedMiniLabel);
                        EditorGUILayout.Space(8);
                        brush=(KaiFitBrush)GUILayout.Toolbar((int)brush,new[]{"貫通を直す","押し出す","元へ戻す"},GUILayout.Height(30));
                        radius=EditorGUILayout.Slider("大きさ（cm）",radius*100,1,25)/100;
                        strength=EditorGUILayout.Slider("強さ",strength,.05f,1);
                        gap=EditorGUILayout.Slider("肌との隙間（mm）",gap*1000,0,10)/1000;
                        EditorGUILayout.HelpBox(brush==KaiFitBrush.Correct?"肌が出ている部分を左ドラッグします。選択した衣装の表側を素体の外へ動かします。":brush==KaiFitBrush.Push?"選択した衣装を少しずつ外側へ広げます。貫通補正だけで足りない部分に使います。":"なぞった範囲を、このツールで補正する前の形へ戻します。",MessageType.None);
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            using(new EditorGUI.DisabledScope(session.UndoCount==0))if(GUILayout.Button("ひとつ戻す"))Run(session.UndoStroke);
                            if(GUILayout.Button("やり直す"))Run(session.RedoStroke);
                        }
                        if(GUILayout.Button("ブラシ補正をすべて戻す"))Run(session.Reset);
                        EditorGUILayout.LabelField("補正した頂点："+session.ChangedVertices,EditorStyles.miniLabel);
                    }
                    EditorGUILayout.Space(8);
                    EditorGUILayout.HelpBox("Unity上で体型を決めるための実験機能です。VRChat内のサイズ変更には自動追従しません。元のメッシュは保持します。",MessageType.Info);
                    EditorGUILayout.EndScrollView();
                }
                using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    before=GUILayout.Toolbar(before?0:1,new[]{"補正前の衣装","補正後の衣装"},GUILayout.Height(28))==0;
                    closeup=GUILayout.Toolbar(closeup?0:1,new[]{"胸まわり","全身"},GUILayout.Height(28))==0;
                    var rect=GUILayoutUtility.GetRect(100,Mathf.Max(150,position.height-380),GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(rect,new Color(.14f,.15f,.17f));
                    if(session!=null)
                    {
                        session.Draw(rect,before,closeup,yaw,pitch,zoom);HandlePreview(rect);
                    }
                    else GUI.Label(rect,"対象を選び「プレビューを開始」",new GUIStyle(EditorStyles.centeredGreyMiniLabel){alignment=TextAnchor.MiddleCenter});
                    EditorGUIUtility.labelWidth=70;
                    yaw=EditorGUILayout.Slider("向き",yaw,-180,180);pitch=EditorGUILayout.Slider("上下角度",pitch,-60,60);zoom=EditorGUILayout.Slider("ズーム",zoom,.5f,3);
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        if(GUILayout.Button("正面")){yaw=0;pitch=0;}
                        if(GUILayout.Button("横")){yaw=90;pitch=0;}
                        if(GUILayout.Button("表示リセット")){yaw=15;pitch=0;zoom=1;}
                    }
                    EditorGUILayout.LabelField("左ドラッグ：ブラシ　右ドラッグ：回転　ホイール：拡大",EditorStyles.wordWrappedMiniLabel);
                }
            }
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.None);
            using(new EditorGUI.DisabledScope(session==null||!session.Changed))
                if(GUILayout.Button("補正をアバターへ適用（元メッシュを保持）",GUILayout.Height(34)))Run(()=>
                {
                    string path=KaiChestFitStore.Apply(session);Clear();
                    message="適用しました。シーンを保存してください。「適用済み・復元」でOFFにできます。保存先："+path;
                    tab=1;
                });
        }
        void HandlePreview(Rect rect)
        {
            var e=Event.current;
            if(e.type==EventType.MouseUp){painting=false;}
            if(e.type==EventType.KeyDown&&(e.control||e.command))
            {
                if(e.keyCode==KeyCode.Z){if(e.shift)session.RedoStroke();else session.UndoStroke();e.Use();Repaint();}
                else if(e.keyCode==KeyCode.Y){session.RedoStroke();e.Use();Repaint();}
            }
            if(!rect.Contains(e.mousePosition))return;
            if(e.type==EventType.MouseDrag&&e.button==1){yaw+=e.delta.x*.5f;pitch=Mathf.Clamp(pitch+e.delta.y*.5f,-60,60);e.Use();Repaint();return;}
            if(e.type==EventType.ScrollWheel){zoom=Mathf.Clamp(zoom-e.delta.y*.05f,.5f,3);e.Use();Repaint();return;}
            KaiFitSurface.Hit hit;
            if(!session.Pick(e.mousePosition,rect,out hit))return;
            if(e.type==EventType.Repaint&&!before)
            {
                Handles.BeginGUI();Handles.color=brush==KaiFitBrush.Restore?Color.cyan:new Color(1,.75f,.2f);
                Handles.DrawWireDisc(new Vector3(e.mousePosition.x,e.mousePosition.y,0),Vector3.forward,session.ScreenRadius(radius,hit.Point,rect));Handles.EndGUI();
            }
            if(before)return;
            if(e.type==EventType.MouseDown&&e.button==0){session.BeginStroke();painting=true;lastMouse=e.mousePosition;Run(()=>session.Brush(hit.Point,session.CameraPosition,radius,strength,gap,brush));e.Use();}
            else if(e.type==EventType.MouseDrag&&e.button==0&&painting&&(e.mousePosition-lastMouse).sqrMagnitude>=4)
            {lastMouse=e.mousePosition;Run(()=>session.Brush(hit.Point,session.CameraPosition,radius,strength,gap,brush));e.Use();}
            if(e.type==EventType.MouseMove)Repaint();
        }
        void DrawRestore()
        {
            var records=root.GetComponentsInChildren<KaiChestFitRecord>(true);
            if(records.Length==0)EditorGUILayout.HelpBox("適用済みの補正はありません。",MessageType.Info);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var record in records)
                using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField((record.Clothing!=null?record.Clothing.name:"参照不明")+(record.Applied?"：補正ON":"：補正OFF"),EditorStyles.boldLabel);
                    using(new EditorGUI.DisabledScope(true)){EditorGUILayout.ObjectField("元メッシュ",record.OriginalMesh,typeof(Mesh),false);EditorGUILayout.ObjectField("補正メッシュ",record.CorrectedMesh,typeof(Mesh),false);}
                    EditorGUILayout.LabelField("胸サイズ："+(string.IsNullOrEmpty(record.BodyShape)?"変更なし":record.BodyShape+" = "+record.AppliedBodyValue));
                    if(GUILayout.Button(record.Applied?"補正をOFFにして元へ戻す":"補正をONにする",GUILayout.Height(28)))Run(()=>{Clear();message=KaiChestFitStore.SetEnabled(record,!record.Applied);});
                    if(!record.Applied&&GUILayout.Button("この補正記録を解除（生成メッシュは保持）"))Run(()=>{Clear();KaiChestFitStore.ForgetDisabled(record);message="補正記録を解除しました。別の素体・シェイプキーで再編集できます。";});
                }
            EditorGUILayout.EndScrollView();
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
            EditorGUILayout.HelpBox("ON/OFF後はシーンを保存してください。メッシュが他のツールで差し替えられている場合は、上書きせず停止します。",MessageType.None);
        }
    }
}
#endif
