#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YagihataItems.RadialInventorySystemV4
{
    public partial class MainWindow
    {
        [SerializeField] string toolkitDraft;
        [SerializeField] VRCAvatarDescriptor toolkitAvatar;
        [SerializeField] int toolkitGroup, toolkitProp;
        [SerializeField] bool toolkitLegacy;
        ListView groupView, propView;
        List<Prop> visibleProps = new List<Prop>();
        string propSearch = "", structure = "", diagnosticText = "";
        IVisualElementScheduledItem toolkitPoll;
        Sprite creditSprite;
        bool toolkitBusy;
        Group ToolkitGroup => risAvatar != null && toolkitGroup >= 0 && toolkitGroup < risAvatar.Groups.Count ? risAvatar.Groups[toolkitGroup] : null;
        Prop ToolkitProp => ToolkitGroup != null && toolkitProp >= 0 && toolkitProp < ToolkitGroup.Props.Count ? ToolkitGroup.Props[toolkitProp] : null;
        T UI<T>(string name) where T : VisualElement => rootVisualElement.Q<T>(name);

        public void CreateGUI()
        {
            toolkitPoll?.Pause();rootVisualElement.Clear();
            try
            {
                RISStrings.EditorInitialize();
                KaiPackageResources.Load<VisualTreeAsset>("Editor/UI/WardrobeWindow.uxml").CloneTree(rootVisualElement);
                var sheet=KaiPackageResources.Load<StyleSheet>("Editor/UI/WardrobeWindow.uss");
                if(!rootVisualElement.styleSheets.Contains(sheet))rootVisualElement.styleSheets.Add(sheet);
                rootVisualElement.EnableInClassList("light", !EditorGUIUtility.isProSkin);
                if (risAvatar == null && !string.IsNullOrEmpty(toolkitDraft))
                {
                    risAvatar = JsonConvert.DeserializeObject<Avatar>(toolkitDraft);
                    if (risAvatar != null && toolkitAvatar != null) risAvatar.AvatarRoot.SetObject(toolkitAvatar);
                    avatarRoot = toolkitAvatar;
                }
                EnsureToolkitModel();
                UI<Label>("title").text = "衣装簡単切替ツール " + RIS.CurrentVersion.Replace("ris_kai", "");
                var avatar = UI<ObjectField>("avatar");avatar.objectType = typeof(VRCAvatarDescriptor);avatar.allowSceneObjects = true;
                avatar.style.flexBasis=360;avatar.style.flexShrink=1;
                avatar.RegisterValueChangedCallback(e => RunToolkit(() => SelectToolkitAvatar(e.newValue as VRCAvatarDescriptor)));
                Wire("use-selection", () => SelectToolkitAvatar(Selection.activeGameObject?.GetComponentInParent<VRCAvatarDescriptor>()));
                Wire("export", ExportToolkit);Wire("import", ImportToolkit);
                Wire("settings", KaiModeSettingsWindow.Open);Wire("helpers", () => KaiWardrobeWindow.Open(risAvatar));
                Wire("chest", () => KaiChestFitWindow.OpenRegistered(risAvatar));
                Wire("legacy", () => { toolkitLegacy = !toolkitLegacy; RefreshToolkit(); });
                Wire("apply", ApplyToolkit);Wire("detach", () => tabItems[risAvatar.MenuMode].RemoveFromAvatar(ref risAvatar));
                Wire("add-group", () => { if (risAvatar.Groups.Count >= 8) return;risAvatar.Groups.Add(new Group { Name = "グループ" + risAvatar.Groups.Count });toolkitGroup = risAvatar.Groups.Count-1;toolkitProp = 0;RefreshToolkit(); });
                Wire("remove-group", () => { if (ToolkitGroup == null) return;KaiReferences.RemoveGroup(risAvatar,toolkitGroup);toolkitGroup=Mathf.Min(toolkitGroup,risAvatar.Groups.Count-1);toolkitProp=0;RefreshToolkit(); });
                Wire("add-prop", () => { var g=ToolkitGroup;if(g==null||g.Props.Count>=255)return;var p=new Prop { IsDefaultEnabled=KaiDefaultOutfit.Enabled(risAvatar)&&g.KaiDefaultGroup };p.TargetObjects.Add(null);g.Props.Add(p);toolkitProp=g.Props.Count-1;RefreshToolkit(); });
                Wire("remove-prop", () => { if(ToolkitProp==null)return;KaiReferences.RemoveProp(risAvatar,ToolkitGroup,toolkitProp);toolkitProp=Mathf.Min(toolkitProp,ToolkitGroup.Props.Count-1);RefreshToolkit(); });
                Wire("group-up", () => MoveToolkitGroup(-1));Wire("group-down", () => MoveToolkitGroup(1));
                Wire("prop-up", () => MoveToolkitProp(-1));Wire("prop-down", () => MoveToolkitProp(1));
                groupView=UI<ListView>("groups");propView=UI<ListView>("props");
                SetupList(groupView,true);SetupList(propView,false);
                groupView.selectionChanged += items => { var selected=items.OfType<Group>().FirstOrDefault();if(selected==null)return;toolkitGroup=risAvatar.Groups.IndexOf(selected);toolkitProp=0;RefreshSelection(); };
                propView.selectionChanged += items => { var selected=items.OfType<Prop>().FirstOrDefault();if(selected==null||ToolkitGroup==null)return;toolkitProp=ToolkitGroup.Props.IndexOf(selected);BuildPropDetails(); };
                groupView.itemIndexChanged += (from,to) => RunToolkit(() => { KaiReferences.GroupMoved(risAvatar,from,to);toolkitGroup=to;RefreshToolkit(); });
                propView.itemIndexChanged += (from,to) => RunToolkit(() => {
                    var group=ToolkitGroup;if(group==null||propSearch.Length>0)return;
                    var moved=group.Props[from];group.Props.RemoveAt(from);group.Props.Insert(to,moved);
                    KaiReferences.PropMoved(risAvatar,group,from,to);toolkitProp=to;RefreshToolkit();
                });
                UI<ToolbarSearchField>("prop-search").RegisterValueChangedCallback(e => { propSearch=e.newValue??"";RefreshProps(); });
                BuildCredit();RefreshToolkit();
                toolkitPoll=rootVisualElement.schedule.Execute(PollToolkit).Every(500);
            }
            catch(Exception error)
            {
                rootVisualElement.Clear();rootVisualElement.Add(new HelpBox(error.Message,HelpBoxMessageType.Error));
                rootVisualElement.Add(new Button(() => { rootVisualElement.Clear();rootVisualElement.Add(new IMGUIContainer(DrawLegacyEditor)); }) { text="互換画面を開く" });
                Debug.LogException(error);
            }
        }
        void OnDisable()
        {
            toolkitPoll?.Pause();
            // Preserve the unsaved model over an assembly reload. No generated assets are written.
            if(risAvatar!=null){toolkitDraft=JsonConvert.SerializeObject(risAvatar);toolkitAvatar=avatarRoot;}
            if(creditSprite!=null)DestroyImmediate(creditSprite);
        }
        void EnsureToolkitModel()
        {
            if(risAvatar==null)risAvatar=KaiModeSettings.CreateAvatar();
            if(tabItems==null){tabItems=new Dictionary<RIS.MenuModeType,EditorTab>{{RIS.MenuModeType.Simple,new TabSimple()},{RIS.MenuModeType.Basic,new TabBasic()},{RIS.MenuModeType.Advanced,new TabAdvanced()}};foreach(var t in tabItems.Values)t.InitializeTab(ref risAvatar);}
            KaiAutoThumbnail.Observe(risAvatar);
        }
        void Wire(string name,Action action) => UI<Button>(name).clicked += () => RunToolkit(action);
        void RunToolkit(Action action)
        {
            try { action();UpdateToolkitStatus(); }
            catch(Exception error){UI<Label>("status").text=error.Message;Debug.LogException(error);}
        }
        void SelectToolkitAvatar(VRCAvatarDescriptor selected)
        {
            if(selected!=null&&(EditorUtility.IsPersistent(selected)||!selected.gameObject.scene.IsValid()))throw new InvalidOperationException("シーン上のアバターを選択してください。");
            if(selected==avatarRoot)return;
            avatarRoot=selected;toolkitAvatar=selected;toolkitGroup=0;toolkitProp=0;
            LoadAvatars();var choices=avatars.Where(a=>a.GetAvatarRoot()==selected&&selected!=null).OrderByDescending(a=>a.LastWriteDate).ToArray();
            risAvatar=choices.FirstOrDefault()??KaiModeSettings.CreateAvatar();
            if(selected!=null)risAvatar.AvatarRoot.SetObject(selected);
            foreach(var t in tabItems.Values)t.InitializeTab(ref risAvatar);
            RefreshToolkit();
            if(choices.Length>1)
            {
                var popup=new PopupField<string>("保存済み設定",choices.Select(a=>a.LastWriteDate.ToLocalTime().ToString("g")+" / "+a.UniqueID).ToList(),0);
                popup.RegisterValueChangedCallback(e=> {risAvatar=choices[popup.index];foreach(var t in tabItems.Values)t.InitializeTab(ref risAvatar);RefreshToolkit();});
                UI<VisualElement>("global-settings").Add(popup);
            }
        }
        internal void RefreshToolkit()
        {
            if(UI<VisualElement>("native-editor")==null||groupView==null)return;
            EnsureToolkitModel();avatarRoot=risAvatar.GetAvatarRoot();
            UI<ObjectField>("avatar").SetValueWithoutNotify(avatarRoot);
            UI<Button>("legacy").text=toolkitLegacy?"新しい画面へ":"互換画面";
            UI<VisualElement>("native-editor").style.display=toolkitLegacy?DisplayStyle.None:DisplayStyle.Flex;
            var legacy=UI<VisualElement>("legacy-host");legacy.Clear();legacy.style.display=toolkitLegacy?DisplayStyle.Flex:DisplayStyle.None;
            if(toolkitLegacy){legacy.Add(new IMGUIContainer(DrawLegacyEditor));return;}
            BuildGlobalSettings();BuildModes();
            bool basic=risAvatar.MenuMode==RIS.MenuModeType.Basic;
            UI<VisualElement>("basic-editor").style.display=basic?DisplayStyle.Flex:DisplayStyle.None;
            UI<VisualElement>("basic-editor").SetEnabled(avatarRoot!=null&&!EditorApplication.isPlaying);
            var compatible=UI<ScrollView>("compatible-tab");compatible.Clear();compatible.style.display=basic?DisplayStyle.None:DisplayStyle.Flex;
            if(!basic)
            {
                compatible.Add(new HelpBox("このモードは機能互換のため従来の編集部を使用します。設定形式と生成処理は共通です。",HelpBoxMessageType.Info));
                compatible.Add(new IMGUIContainer(()=> { if(avatarRoot!=null)tabItems[risAvatar.MenuMode].DrawTab(ref risAvatar,new Rect(0,0,Mathf.Max(960,position.width-30),position.height),true); }));
            }
            groupView.itemsSource=risAvatar.Groups;groupView.Rebuild();
            toolkitGroup=risAvatar.Groups.Count==0?-1:Mathf.Clamp(toolkitGroup,0,risAvatar.Groups.Count-1);
            groupView.SetSelectionWithoutNotify(toolkitGroup<0?new int[0]:new[]{toolkitGroup});
            RefreshSelection();structure=ToolkitStructure();UpdateToolkitStatus();
        }
        void BuildModes()
        {
            var host=UI<VisualElement>("modes");host.Clear();
            foreach(var mode in KaiModeSettings.VisibleModes(risAvatar.MenuMode,KaiModeSettings.ShowSimple,KaiModeSettings.ShowAdvanced))
            {
                var chosen=mode;var button=new Button(()=>RunToolkit(()=>{risAvatar.MenuMode=chosen;foreach(var t in tabItems.Values)t.InitializeTab(ref risAvatar);RefreshToolkit();})){text=KaiModeSettings.Label(mode)};
                button.EnableInClassList("selected-mode",mode==risAvatar.MenuMode);host.Add(button);
            }
        }
        void BuildGlobalSettings()
        {
            var host=UI<VisualElement>("global-settings");host.Clear();host.SetEnabled(avatarRoot!=null&&!EditorApplication.isPlaying);
            host.style.flexShrink=0;
            var row=new VisualElement();row.AddToClassList("toolbar");host.Add(row);
            var auto=new Toggle("衣装追加時に自動撮影"){value=risAvatar.KaiAutoThumbnail};auto.style.width=240;auto.RegisterValueChangedCallback(e=>risAvatar.KaiAutoThumbnail=e.newValue);row.Add(auto);
            var wd=new Toggle("Write Defaultsを使用"){value=risAvatar.UseWriteDefaults};wd.style.width=220;wd.RegisterValueChangedCallback(e=>risAvatar.UseWriteDefaults=e.newValue);row.Add(wd);
            if(risAvatar.MenuMode!=RIS.MenuModeType.Simple)
            {
                var names=new List<string>{"個別の初期状態を使う","なし（すべてOFF）"};names.AddRange(risAvatar.Groups.Select((g,i)=>(i+1)+": "+g.Name));
                int selected=!risAvatar.KaiUseDefaultGroup?0:risAvatar.Groups.FindIndex(g=>g.KaiDefaultGroup)+2;
                var defaults=new PopupField<string>("初期衣装",names,selected);
                defaults.style.flexGrow=1;defaults.style.flexBasis=300;
                defaults.RegisterValueChangedCallback(e=>RunToolkit(()=>{if(defaults.index==0)risAvatar.KaiUseDefaultGroup=false;else KaiDefaultOutfit.Select(risAvatar,defaults.index==1?null:risAvatar.Groups[defaults.index-2]);BuildPropDetails();}));row.Add(defaults);
            }
            if(risAvatar.MenuMode!=RIS.MenuModeType.Simple)
            {
                var heels=new VisualElement();heels.AddToClassList("toolbar");heels.style.flexWrap=Wrap.NoWrap;heels.style.minHeight=32;host.Add(heels);
                var enabled=Toggle(heels,"実験：靴とヒール補正を連動",risAvatar.KaiExperimentalHeels,v=>{risAvatar.KaiExperimentalHeels=v;BuildGlobalSettings();BuildPropDetails();});enabled.style.flexGrow=1;enabled.style.flexBasis=300;enabled.style.flexShrink=1;
                heels.Add(new Button(()=>KaiHeelWindow.Open(risAvatar)){text="ヒール補正を調整…"});
                if(risAvatar.KaiExperimentalHeels){var links=KaiHeelLink.Collect(risAvatar);host.Add(new HelpBox("ヒール連動対象："+links.Count+"件／要設定："+links.Count(l=>l.Error!=null)+"件。調整画面で確認できます。",HelpBoxMessageType.Info));}
            }
            if(RISV3toV4Updater.HasV3SettingsOnScene(RIS.SettingsNameV3))host.Add(new Button(()=>RunToolkit(()=>RISV3toV4Updater.SalvageDatas(RIS.SettingsNameV3))){text="旧V3設定を移行"});
        }
        void SetupList(ListView list,bool groups)
        {
            list.fixedItemHeight=42;list.selectionType=SelectionType.Single;list.reorderable=true;list.reorderMode=ListViewReorderMode.Animated;
            list.makeItem=()=> {
                var row=new VisualElement();row.AddToClassList("list-row");row.Add(new Image {scaleMode=ScaleMode.ScaleToFit});row.Add(new Label());
                if(groups)
                {
                    row.RegisterCallback<DragUpdatedEvent>(e=>{if(DragAndDrop.objectReferences.OfType<GameObject>().Any()){DragAndDrop.visualMode=DragAndDropVisualMode.Copy;e.StopPropagation();}});
                    row.RegisterCallback<DragPerformEvent>(e=>{var group=row.userData as Group;var objects=DragAndDrop.objectReferences.OfType<GameObject>().ToArray();if(group==null||objects.Length==0)return;DragAndDrop.AcceptDrag();RunToolkit(()=>KaiOutfitImport.Begin(risAvatar,group,objects));e.StopPropagation();});
                }
                return row;
            };
            list.bindItem=(row,i)=> {
                if(groups){var g=risAvatar.Groups[i];row.userData=g;row.Q<Label>().text=string.IsNullOrEmpty(g.Name)?"グループ"+i:g.Name;row.Q<Image>().image=g.Icon?.GetObject()??KaiPackageResources.Load<Texture2D>("Runtime/Textures/group_icon_v4.png");}
                else {var p=visibleProps[i];row.userData=p;row.Q<Label>().text=p.GetPropName(risAvatar)??"衣装";row.Q<Image>().image=p.Icon?.GetObject()??KaiPackageResources.Load<Texture2D>("Runtime/Textures/dresser_icon.png");}
            };
        }
        void RefreshSelection(){BuildGroupSettings();RefreshProps();BuildPropDetails();}
        void RefreshProps()
        {
            visibleProps=ToolkitGroup?.Props.Where(p=>string.IsNullOrEmpty(propSearch)||(p.GetPropName(risAvatar)??"").IndexOf(propSearch,StringComparison.OrdinalIgnoreCase)>=0).ToList()??new List<Prop>();
            propView.itemsSource=visibleProps;propView.reorderable=propSearch.Length==0;propView.Rebuild();
            int index=visibleProps.IndexOf(ToolkitProp);propView.SetSelectionWithoutNotify(index<0?new int[0]:new[]{index});
        }
        void BuildGroupSettings()
        {
            var host=UI<ScrollView>("group-settings");host.Clear();var picture=UI<ScrollView>("group-picture");picture.Clear();var g=ToolkitGroup;if(g==null)return;
            Text(host,"グループ名",g.Name,v=>{g.Name=v;groupView.RefreshItems();BuildGlobalSettings();});
            Object<Texture2D>(host,"メニュー画像",g.Icon.GetObject(),false,v=>g.Icon.SetObject(v));
            var mode=new EnumField("排他モード",risAvatar.GetExclusiveMode((RIS.ExclusiveGroupType)toolkitGroup));
            mode.RegisterValueChangedCallback(e=>{risAvatar.SetExclusiveMode((RIS.ExclusiveGroupType)toolkitGroup,(RIS.ExclusiveModeType)e.newValue);foreach(var p in g.Props)p.ExclusiveGroup=(RIS.ExclusiveModeType)e.newValue==RIS.ExclusiveModeType.None?RIS.ExclusiveGroupType.None:(RIS.ExclusiveGroupType)toolkitGroup;BuildPropDetails();});host.Add(mode);
            Toggle(host,"リセットボタン",g.UseResetButton,v=>g.UseResetButton=v);
            var fold=new Foldout{text="一括着替えの設定",value=false};host.Add(fold);
            Toggle(fold,"グループ内の排他を解除",g.KaiToggleWholeGroup,v=>g.KaiToggleWholeGroup=v);
            Toggle(fold,"ほかの衣装を自動OFF",g.KaiTurnOffOtherGroups,v=>g.KaiTurnOffOtherGroups=v);
            Toggle(fold,"ほかの衣装へ切替えても保持",!g.KaiIncludeInOutfitSwitch,v=>g.KaiIncludeInOutfitSwitch=!v);
            Toggle(fold,"個別の着脱状態を記憶",g.KaiRememberPropStates,v=>g.KaiRememberPropStates=v);
            host.Add(new IMGUIContainer(()=>{KaiDefaultOutfit.DrawName(risAvatar,g);KaiOutfitImport.Draw(risAvatar,g);KaiPropBundle.Draw(risAvatar,g,ToolkitProp);}));
            var image=new Image{name="group-image",image=g.Icon?.GetObject(),scaleMode=ScaleMode.ScaleToFit};image.style.height=220;picture.Add(image);
            picture.Add(new Button(()=>RunToolkit(()=>KaiGroupThumbnail.Request(risAvatar,g,true))){text="全身を撮影・撮り直す"});
        }
        void BuildPropDetails()
        {
            var host=UI<ScrollView>("prop-details");host.Clear();var p=ToolkitProp;if(p==null){host.Add(new Label("衣装を選択してください。"));return;}
            var exclusive=risAvatar.GetExclusiveMode((RIS.ExclusiveGroupType)toolkitGroup);
            p.ExclusiveGroup=exclusive==RIS.ExclusiveModeType.None?RIS.ExclusiveGroupType.None:(RIS.ExclusiveGroupType)toolkitGroup;
            Text(host,"プロップ名",p.Name,v=>{p.Name=v;propView.RefreshItems();});
            Object<Texture2D>(host,"メニュー画像",p.Icon.GetObject(),false,v=>p.Icon.SetObject(v));
            var image=new Image{name="prop-image",image=p.Icon.GetObject(),scaleMode=ScaleMode.ScaleToFit};image.style.height=180;host.Add(image);
            host.Add(new Button(()=>RunToolkit(()=>KaiThumbnail.Capture(risAvatar,p,0,1))){text="衣装だけを撮影・撮り直す"});
            host.Add(new Button(()=>KaiWardrobeWindow.Open(risAvatar)){text="画像の構図を調整…"});
            var initial=Toggle(host,"初期状態",p.IsDefaultEnabled,v=>SetToolkitDefault(p,v));initial.SetEnabled(!KaiDefaultOutfit.Enabled(risAvatar));
            Toggle(host,"ローカル動作",p.IsLocalOnly,v=>p.IsLocalOnly=v);
            var saved=Toggle(host,"有効状態を保存",!KaiDefaultOutfit.Enabled(risAvatar)&&p.UseSaveParameter,v=>p.UseSaveParameter=v);saved.SetEnabled(!KaiDefaultOutfit.Enabled(risAvatar));
            var root=avatarRoot?.gameObject;
            var obj=Object<GameObject>(host,"対象オブジェクト",p.TargetObjects.FirstOrDefault()?.GetObject(root),true,v=>{
                if(v!=null&&(root==null||v==root||!v.transform.IsChildOf(root.transform)))throw new InvalidOperationException("対象アバター配下の衣装を選んでください。");
                if(p.TargetObjects.Count==0)p.TargetObjects.Add(null);
                p.TargetObjects[0]=new GUIDPathPair<GameObject>(ObjectPathStateType.RelativeFromObject,v,root);
            });obj.SetEnabled(!KaiPropBundle.IsMerged(p));
            host.Add(new IMGUIContainer(()=>{KaiPropBundle.DrawDetails(risAvatar,p);KaiHeelLink.DrawProp(risAvatar,p);}));
        }
        void Text(VisualElement parent,string label,string value,Action<string> changed)
        {var field=new TextField(label){value=value??"",isDelayed=true};field.RegisterValueChangedCallback(e=>RunToolkit(()=>changed(e.newValue)));parent.Add(field);}
        Toggle Toggle(VisualElement parent,string label,bool value,Action<bool> changed)
        {var field=new Toggle(label){value=value};field.RegisterValueChangedCallback(e=>RunToolkit(()=>changed(e.newValue)));parent.Add(field);return field;}
        ObjectField Object<T>(VisualElement parent,string label,T value,bool scene,Action<T> changed) where T:UnityEngine.Object
        {
            var field=new ObjectField(label){objectType=typeof(T),allowSceneObjects=scene,value=value};
            field.RegisterValueChangedCallback(e=>{try{changed(e.newValue as T);}catch(Exception error){field.SetValueWithoutNotify(e.previousValue);UI<Label>("status").text=error.Message;}});parent.Add(field);return field;
        }
        void SetToolkitDefault(Prop p,bool value)
        {
            p.IsDefaultEnabled=value;var mode=risAvatar.GetExclusiveMode((RIS.ExclusiveGroupType)toolkitGroup);
            p.ExclusiveGroup=mode==RIS.ExclusiveModeType.None?RIS.ExclusiveGroupType.None:(RIS.ExclusiveGroupType)toolkitGroup;
            if(value&&EditorSettings.DefaultStatusLimitter&&mode==RIS.ExclusiveModeType.Exclusive)
                foreach(var other in risAvatar.Groups.SelectMany(g=>g.Props).Where(q=>q!=p&&q.ExclusiveGroup==p.ExclusiveGroup))other.IsDefaultEnabled=false;
        }
        internal void MoveToolkitGroup(int delta)
        {
            int from=toolkitGroup,to=from+delta;if(from<0||to<0||to>=risAvatar.Groups.Count)return;
            var g=risAvatar.Groups[from];risAvatar.Groups.RemoveAt(from);risAvatar.Groups.Insert(to,g);KaiReferences.GroupMoved(risAvatar,from,to);toolkitGroup=to;RefreshToolkit();
        }
        internal void MoveToolkitProp(int delta)
        {
            var g=ToolkitGroup;int from=toolkitProp,to=from+delta;if(g==null||from<0||to<0||to>=g.Props.Count)return;
            var p=g.Props[from];g.Props.RemoveAt(from);g.Props.Insert(to,p);KaiReferences.PropMoved(risAvatar,g,from,to);toolkitProp=to;RefreshToolkit();
        }
        string ToolkitStructure()=>risAvatar.UniqueID+"|"+risAvatar.MenuMode+"|"+KaiModeSettings.ShowSimple+KaiModeSettings.ShowAdvanced+"|"+string.Join(";",risAvatar.Groups.Select(g=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g)+":"+g.Name+":"+string.Join(",",g.Props.Select(p=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p)))));
        void PollToolkit()
        {
            if(toolkitBusy||risAvatar==null||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(toolkitLegacy)return;
            try
            {
                if(structure!=ToolkitStructure())RefreshToolkit();
                KaiDefaultOutfit.Enforce(risAvatar);KaiAutoThumbnail.Observe(risAvatar);
                if(ToolkitGroup!=null)KaiGroupThumbnail.Observe(risAvatar,ToolkitGroup);
                var groupImage=UI<Image>("group-image");if(groupImage!=null)groupImage.image=ToolkitGroup?.Icon?.GetObject();
                var propImage=UI<Image>("prop-image");if(propImage!=null)propImage.image=ToolkitProp?.Icon?.GetObject();
                groupView?.RefreshItems();propView?.RefreshItems();UpdateToolkitStatus();
            }
            catch(Exception error){UI<Label>("status").text=error.Message;}
        }
        void UpdateToolkitStatus()
        {
            if(UI<ProgressBar>("capacity")==null||risAvatar==null)return;
            bool ready=avatarRoot!=null&&!EditorApplication.isPlaying&&!toolkitBusy;
            var errors=ready?tabItems[risAvatar.MenuMode].CheckErrors(ref risAvatar):new[]{"シーン上の対象アバターを選択してください。"};
            bool hasAnimator=avatarRoot!=null&&avatarRoot.GetComponent<Animator>()!=null;
            if(ready&&!hasAnimator)errors=errors.Concat(new[]{"対象アバターにAnimatorがありません。"}).ToArray();
            int cost=ready?KaiPreflight.Cost(risAvatar):0;var bar=UI<ProgressBar>("capacity");bar.value=Mathf.Min(256,cost);bar.title="パラメーター容量："+cost+" / 256";
            var messages=new List<string>(errors);if(cost>256)messages.Add("パラメーター容量が256を超えています。既存の生成結果は変更しません。");
            var fx=ready&&hasAnimator?avatarRoot.GetFXLayer(RIS.AutoGeneratedFolderPath+risAvatar.UniqueID+"/",false):null;
            if(fx!=null&&!fx.ValidateWriteDefaults(risAvatar.UseWriteDefaults))messages.Add(RISStrings.GetString("warn_writedefaults"));
            if(!EditorSettings.OptimizeParameters)messages.Add(RISStrings.GetString("warn_paramspace"));
            string joined=string.Join("\n",messages);if(joined!=diagnosticText){diagnosticText=joined;var host=UI<ScrollView>("diagnostics");host.Clear();foreach(var message in messages)host.Add(new HelpBox(message,HelpBoxMessageType.Warning));}
            UI<Button>("apply").SetEnabled(ready&&errors.Length==0&&cost<=256);UI<Button>("detach").SetEnabled(ready);
            foreach(var name in new[]{"export","import","helpers","chest"})UI<Button>(name).SetEnabled(ready);
        }
        void ApplyToolkit()
        {
            if(avatarRoot==null||EditorApplication.isPlaying)return;
            if(avatarRoot.GetComponent<Animator>()==null)throw new InvalidOperationException("対象アバターにAnimatorがありません。");
            var errors=tabItems[risAvatar.MenuMode].CheckErrors(ref risAvatar);if(errors.Length>0)throw new InvalidOperationException(string.Join("\n",errors));
            KaiPreflight.Require(risAvatar);toolkitBusy=true;
            try{risAvatar.SaveToJson();tabItems[risAvatar.MenuMode].ApplyToAvatar(ref risAvatar);UI<Label>("status").text="適用しました。シーンを保存してください。";}
            finally{toolkitBusy=false;}
        }
        void ExportToolkit()
        {
            if(avatarRoot==null)return;string path=EditorUtility.SaveFilePanel("設定を保存","","RISSettings-"+SafeParser.ParseFileName(avatarRoot.name)+".json","json");
            if(!string.IsNullOrEmpty(path)){risAvatar.SaveToJson(path);UI<Label>("status").text="設定を保存しました。";}
        }
        void ImportToolkit()
        {
            if(avatarRoot==null)return;string path=EditorUtility.OpenFilePanel("設定を読込","","json");if(string.IsNullOrEmpty(path))return;
            var loaded=Avatar.LoadFromJson(path);if(loaded==null)return;
            // Replace the model, preserving the current avatar identity and generated-asset directory.
            var json=Newtonsoft.Json.Linq.JObject.FromObject(loaded);json["UniqueID"]=risAvatar.UniqueID;
            loaded=json.ToObject<Avatar>();loaded.AvatarRoot.SetObject(avatarRoot);risAvatar=loaded;risAvatar.ForceReload();
            foreach(var t in tabItems.Values)t.InitializeTab(ref risAvatar);toolkitGroup=0;toolkitProp=0;RefreshToolkit();
        }
        void BuildCredit()
        {
            var host=UI<VisualElement>("credit");host.Clear();
            if(creditSprite!=null)DestroyImmediate(creditSprite);
            var sheet=KaiPackageResources.Load<Texture2D>("Editor/Artwork/Rize.png");
            creditSprite=Sprite.Create(sheet,new Rect(0,sheet.height*10f/11f,sheet.width/8f,sheet.height/11f),Vector2.zero);
            host.Add(new Image{sprite=creditSprite,scaleMode=ScaleMode.ScaleToFit});host.Add(new Label("リゼ"));
            var link=new Button(()=>Application.OpenURL(KaiEditorUI.CreditProfileUrl)){tooltip=KaiEditorUI.CreditProfileUrl};link.style.flexDirection=FlexDirection.Row;link.style.alignItems=Align.Center;
            var mark=new VisualElement();mark.style.width=20;mark.style.height=20;mark.style.marginRight=8;
            mark.generateVisualContent+=context=>{var pen=context.painter2D;pen.strokeColor=EditorGUIUtility.isProSkin?Color.white:Color.black;pen.lineWidth=1.4f;pen.BeginPath();pen.MoveTo(new Vector2(3,3));pen.LineTo(new Vector2(7,3));pen.LineTo(new Vector2(17,17));pen.LineTo(new Vector2(13,17));pen.ClosePath();pen.Stroke();pen.BeginPath();pen.MoveTo(new Vector2(17,3));pen.LineTo(new Vector2(3,17));pen.Stroke();};
            link.Add(mark);link.Add(new Label("@L1ZE_ragram1"));host.Add(link);
        }
    }
}
#endif
