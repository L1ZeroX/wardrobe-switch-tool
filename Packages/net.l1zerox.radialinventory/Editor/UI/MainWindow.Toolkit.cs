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
        [SerializeField] bool toolkitSettings;
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
                UI<Label>("title").text = "衣装簡単切替ツール";
                UI<Label>("version").text = "v" + RIS.CurrentVersion.Replace("ris_kai", "");
                DrawTabIcon("brand-icon",0);DrawTabIcon("wardrobe-tab-icon",0);DrawTabIcon("group-tab-icon",1);DrawTabIcon("details-tab-icon",2);
                Wire("group-menu",OpenToolkitGroupMenu);
                Wire("wardrobe-tab",()=>ShowToolkitSection(false));Wire("details-tab",()=>ShowToolkitSection(true));
                var avatar = UI<ObjectField>("avatar");avatar.objectType = typeof(VRCAvatarDescriptor);avatar.allowSceneObjects = true;
                avatar.style.flexShrink=1;
                avatar.RegisterValueChangedCallback(e => RunToolkit(() => SelectToolkitAvatar(e.newValue as VRCAvatarDescriptor)));
                Wire("use-selection", () => SelectToolkitAvatar(Selection.activeGameObject?.GetComponentInParent<VRCAvatarDescriptor>()));
                Wire("export", ExportToolkit);Wire("import", ImportToolkit);
                Wire("settings", KaiModeSettingsWindow.Open);Wire("helpers", () => KaiWardrobeWindow.Open(risAvatar));
                Wire("chest", () => KaiChestFitWindow.OpenRegistered(risAvatar));
                Wire("legacy", () => { toolkitLegacy = !toolkitLegacy; RefreshToolkit(); });
                Wire("apply", ApplyToolkit);Wire("detach", () => tabItems[risAvatar.MenuMode].RemoveFromAvatar(ref risAvatar));
                Wire("add-group", AddToolkitGroup);
                Wire("remove-group", () => { if (ToolkitGroup == null) return;KaiReferences.RemoveGroup(risAvatar,toolkitGroup);toolkitGroup=Mathf.Min(toolkitGroup,risAvatar.Groups.Count-1);toolkitProp=0;RefreshToolkit(); });
                Wire("add-prop", () => { var g=ToolkitGroup;if(g==null||g.Props.Count>=255)return;var p=new Prop { IsDefaultEnabled=KaiDefaultOutfit.Enabled(risAvatar)&&g.KaiDefaultGroup };p.TargetObjects.Add(null);g.Props.Add(p);toolkitProp=g.Props.Count-1;RefreshToolkit();UI<Foldout>("prop-foldout").value=true; });
                Wire("remove-prop", () => { if(ToolkitProp==null)return;KaiReferences.RemoveProp(risAvatar,ToolkitGroup,toolkitProp);toolkitProp=Mathf.Min(toolkitProp,ToolkitGroup.Props.Count-1);RefreshToolkit(); });
                Wire("group-up", () => MoveToolkitGroup(-1));Wire("group-down", () => MoveToolkitGroup(1));
                Wire("prop-up", () => MoveToolkitProp(-1));Wire("prop-down", () => MoveToolkitProp(1));
                groupView=UI<ListView>("groups");propView=UI<ListView>("props");
                SetupList(groupView,true);SetupList(propView,false);
                propView.RegisterCallback<GeometryChangedEvent>(e=>{
                    if(Mathf.Abs(e.newRect.height-e.oldRect.height)<.5f||!UI<Foldout>("prop-foldout").value)return;
                    int index=visibleProps.IndexOf(ToolkitProp);var scroll=propView.Q<ScrollView>();
                    if(index>=0&&scroll!=null)scroll.scrollOffset=new Vector2(0,index*propView.fixedItemHeight);
                });
                groupView.selectionChanged += items => { var selected=items.OfType<Group>().FirstOrDefault();if(selected==null)return;SelectToolkitGroup(selected); };
                propView.selectionChanged += items => { var selected=items.OfType<Prop>().FirstOrDefault();if(selected==null||ToolkitGroup==null)return;toolkitProp=ToolkitGroup.Props.IndexOf(selected);BuildPropDetails();propView.RefreshItems();UI<Foldout>("prop-foldout").value=true; };
                groupView.itemIndexChanged += (from,to) => RunToolkit(() => { KaiReferences.GroupMoved(risAvatar,from,to);toolkitGroup=to;RefreshToolkit(); });
                propView.itemIndexChanged += (from,to) => RunToolkit(() => {
                    var group=ToolkitGroup;if(group==null||propSearch.Length>0)return;
                    var moved=group.Props[from];group.Props.RemoveAt(from);group.Props.Insert(to,moved);
                    KaiReferences.PropMoved(risAvatar,group,from,to);toolkitProp=to;RefreshToolkit();
                });
                UI<ToolbarSearchField>("prop-search").RegisterValueChangedCallback(e => { propSearch=e.newValue??"";RefreshProps(); });
                UI<ScrollView>("group-picture").RegisterCallback<GeometryChangedEvent>(e=>{var image=UI<Image>("group-image");if(image!=null)image.style.height=Mathf.Max(100,e.newRect.height-(image.image==null?80:44));});
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
            groupView.style.height=Mathf.Clamp(risAvatar.Groups.Count*68,68,204);groupView.itemsSource=risAvatar.Groups;groupView.Rebuild();
            toolkitGroup=risAvatar.Groups.Count==0?-1:Mathf.Clamp(toolkitGroup,0,risAvatar.Groups.Count-1);
            groupView.SetSelectionWithoutNotify(toolkitGroup<0?new int[0]:new[]{toolkitGroup});
            RefreshSelection();ShowToolkitSection(toolkitSettings||avatarRoot==null);structure=ToolkitStructure();UpdateToolkitStatus();
        }
        void ShowToolkitSection(bool settings)
        {
            toolkitSettings=settings;
            if(toolkitLegacy){toolkitLegacy=false;RefreshToolkit();}
            UI<VisualElement>("clothes-page").style.display=settings?DisplayStyle.None:DisplayStyle.Flex;
            UI<ScrollView>("settings-page").style.display=settings?DisplayStyle.Flex:DisplayStyle.None;
            UI<Button>("wardrobe-tab").EnableInClassList("tab-active",!settings);
            UI<Button>("details-tab").EnableInClassList("tab-active",settings);
        }
        void SelectToolkitGroup(Group selected)
        {
            int index=risAvatar.Groups.IndexOf(selected);if(index<0)return;
            toolkitGroup=index;toolkitProp=0;
            groupView.SetSelectionWithoutNotify(new[]{index});RefreshSelection();
        }
        void AddToolkitGroup()
        {
            if(avatarRoot==null||EditorApplication.isPlaying||risAvatar.Groups.Count>=8)return;
            risAvatar.Groups.Add(new Group{Name="グループ"+risAvatar.Groups.Count});
            toolkitGroup=risAvatar.Groups.Count-1;toolkitProp=0;toolkitSettings=true;RefreshToolkit();
        }
        GenericDropdownMenu BuildToolkitGroupMenu()
        {
            var menu=new GenericDropdownMenu();
            foreach(var group in risAvatar.Groups)
            {
                var selected=group;
                menu.AddItem((risAvatar.Groups.IndexOf(group)+1)+". "+(group.Name??"グループ")+"  · 衣装 "+group.Props.Count+"点",group==ToolkitGroup,
                    ()=>RunToolkit(()=>{SelectToolkitGroup(selected);ShowToolkitSection(false);}));
            }
            if(risAvatar.Groups.Count==0)menu.AddDisabledItem("グループはまだありません",false);
            menu.AddSeparator("");
            if(avatarRoot!=null&&!EditorApplication.isPlaying&&risAvatar.Groups.Count<8)menu.AddItem("＋ グループを追加",false,()=>RunToolkit(AddToolkitGroup));
            else menu.AddDisabledItem("＋ グループを追加",false);
            menu.AddItem("グループを編集…",false,()=>RunToolkit(()=>ShowToolkitSection(true)));
            return menu;
        }
        void OpenToolkitGroupMenu()
        {
            var button=UI<Button>("group-menu");BuildToolkitGroupMenu().DropDown(button.worldBound,button,true);
        }
        void DrawTabIcon(string name,int kind)
        {
            UI<VisualElement>(name).generateVisualContent+=context=>
            {
                var pen=context.painter2D;pen.strokeColor=new Color(.93f,.93f,.93f);pen.lineWidth=1.7f;
                if(kind==0)
                {
                    var points=new[]{new Vector2(8,3),new Vector2(4,5),new Vector2(1,10),new Vector2(6,13),new Vector2(8,10),new Vector2(8,24),new Vector2(20,24),new Vector2(20,10),new Vector2(22,13),new Vector2(26,10),new Vector2(23,5),new Vector2(19,3),new Vector2(17,6),new Vector2(11,6)};
                    pen.BeginPath();pen.MoveTo(points[0]);foreach(var point in points.Skip(1))pen.LineTo(point);pen.ClosePath();pen.Stroke();
                }
                else if(kind==1)
                {
                    pen.BeginPath();pen.MoveTo(new Vector2(4,10));pen.LineTo(new Vector2(9,5));pen.LineTo(new Vector2(19,5));pen.LineTo(new Vector2(24,10));pen.ClosePath();pen.Stroke();
                    foreach(int y in new[]{15,20}){pen.BeginPath();pen.MoveTo(new Vector2(4,y));pen.LineTo(new Vector2(24,y));pen.Stroke();}
                }
                else
                {
                    for(int i=0;i<3;i++){float y=6+i*8,x=i==1?18:9;pen.BeginPath();pen.MoveTo(new Vector2(3,y));pen.LineTo(new Vector2(24,y));pen.Stroke();pen.BeginPath();pen.MoveTo(new Vector2(x,y-3));pen.LineTo(new Vector2(x,y+3));pen.Stroke();}
                }
            };
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
            var auto=new Toggle("衣装追加時に自動撮影"){value=risAvatar.KaiAutoThumbnail};auto.RegisterValueChangedCallback(e=>risAvatar.KaiAutoThumbnail=e.newValue);row.Add(auto);
            var wd=new Toggle("Write Defaultsを使用"){value=risAvatar.UseWriteDefaults};wd.RegisterValueChangedCallback(e=>risAvatar.UseWriteDefaults=e.newValue);row.Add(wd);
            if(risAvatar.MenuMode!=RIS.MenuModeType.Simple)
            {
                var names=new List<string>{"個別の初期状態を使う","なし（すべてOFF）"};names.AddRange(risAvatar.Groups.Select((g,i)=>(i+1)+": "+g.Name));
                int selected=!risAvatar.KaiUseDefaultGroup?0:risAvatar.Groups.FindIndex(g=>g.KaiDefaultGroup)+2;
                var defaults=new PopupField<string>("初期衣装",names,selected);
                defaults.style.flexGrow=1;
                defaults.RegisterValueChangedCallback(e=>RunToolkit(()=>{if(defaults.index==0)risAvatar.KaiUseDefaultGroup=false;else KaiDefaultOutfit.Select(risAvatar,defaults.index==1?null:risAvatar.Groups[defaults.index-2]);BuildPropDetails();}));row.Add(defaults);
            }
            if(risAvatar.MenuMode!=RIS.MenuModeType.Simple)
            {
                var heels=new VisualElement();heels.AddToClassList("toolbar");heels.style.flexDirection=FlexDirection.Column;heels.style.alignItems=Align.Stretch;heels.style.minHeight=32;host.Add(heels);
                var enabled=Toggle(heels,"実験：靴とヒール補正を連動",risAvatar.KaiExperimentalHeels,v=>{risAvatar.KaiExperimentalHeels=v;BuildGlobalSettings();BuildPropDetails();});enabled.style.flexGrow=1;
                heels.Add(new Button(()=>KaiHeelWindow.Open(risAvatar)){text="ヒール補正を調整…"});
                if(risAvatar.KaiExperimentalHeels){var links=KaiHeelLink.Collect(risAvatar);host.Add(new HelpBox("ヒール連動対象："+links.Count+"件／要設定："+links.Count(l=>l.Error!=null)+"件。調整画面で確認できます。",HelpBoxMessageType.Info));}
            }
            if(RISV3toV4Updater.HasV3SettingsOnScene(RIS.SettingsNameV3))host.Add(new Button(()=>RunToolkit(()=>RISV3toV4Updater.SalvageDatas(RIS.SettingsNameV3))){text="旧V3設定を移行"});
        }
        void SetupList(ListView list,bool groups)
        {
            list.fixedItemHeight=68;list.selectionType=SelectionType.Single;list.reorderable=true;list.reorderMode=ListViewReorderMode.Animated;
            list.makeItem=()=> {
                var row=new VisualElement();row.AddToClassList("list-row");row.Add(new Image {scaleMode=ScaleMode.ScaleToFit});var copy=new VisualElement();copy.AddToClassList("row-copy");row.Add(copy);
                var name=new Label{name="row-name"};name.AddToClassList("row-name");copy.Add(name);
                var count=new Label{name="row-count"};count.AddToClassList("row-count");copy.Add(count);
                var check=new Label{name="row-check"};check.AddToClassList("row-check");row.Add(check);
                if(groups)
                {
                    row.RegisterCallback<DragUpdatedEvent>(e=>{if(DragAndDrop.objectReferences.OfType<GameObject>().Any()){DragAndDrop.visualMode=DragAndDropVisualMode.Copy;e.StopPropagation();}});
                    row.RegisterCallback<DragPerformEvent>(e=>{var group=row.userData as Group;var objects=DragAndDrop.objectReferences.OfType<GameObject>().ToArray();if(group==null||objects.Length==0)return;DragAndDrop.AcceptDrag();RunToolkit(()=>KaiOutfitImport.Begin(risAvatar,group,objects));e.StopPropagation();});
                }
                return row;
            };
            list.bindItem=(row,i)=> {
                if(groups){var g=risAvatar.Groups[i];row.userData=g;row.Q<Label>("row-name").text=string.IsNullOrEmpty(g.Name)?"グループ"+i:g.Name;row.Q<Label>("row-count").text="衣装 "+g.Props.Count+"点";row.Q<Label>("row-check").text=g==ToolkitGroup?"✓":"";row.Q<Image>().image=g.Icon?.GetObject()??KaiPackageResources.Load<Texture2D>("Runtime/Textures/group_icon_v4.png");}
                else {var p=visibleProps[i];row.userData=p;row.Q<Label>("row-name").text=p.GetPropName(risAvatar)??"衣装";row.Q<Label>("row-count").style.display=DisplayStyle.None;row.Q<Label>("row-check").text=p==ToolkitProp?"✓":"";row.Q<Image>().image=p.Icon?.GetObject()??KaiPackageResources.Load<Texture2D>("Runtime/Textures/dresser_icon.png");}
            };
        }
        void RefreshSelection(){BuildGroupSettings();RefreshProps();BuildPropDetails();groupView.RefreshItems();UI<Label>("empty-groups").style.display=risAvatar.Groups.Count==0?DisplayStyle.Flex:DisplayStyle.None;}
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
            var image=new Image{name="group-image",image=g.Icon?.GetObject(),scaleMode=ScaleMode.ScaleToFit};image.style.height=Mathf.Max(100,picture.layout.height-(g.Icon?.GetObject()==null?80:44));picture.Add(image);
            if(image.image==null)picture.Add(new Label("全身サムネイルを撮影できます"));
            picture.Add(new Button(()=>RunToolkit(()=>KaiGroupThumbnail.Request(risAvatar,g,true))){text="全身を撮影・撮り直す"});
        }
        void BuildPropDetails()
        {
            var host=UI<ScrollView>("prop-details");host.Clear();var p=ToolkitProp;UI<Foldout>("prop-foldout").text=p==null?"選択した衣装の設定":p.GetPropName(risAvatar)+" の設定";if(p==null){host.Add(new Label("衣装を選択してください。"));return;}
            var exclusive=risAvatar.GetExclusiveMode((RIS.ExclusiveGroupType)toolkitGroup);
            p.ExclusiveGroup=exclusive==RIS.ExclusiveModeType.None?RIS.ExclusiveGroupType.None:(RIS.ExclusiveGroupType)toolkitGroup;
            Text(host,"プロップ名",p.Name,v=>{p.Name=v;propView.RefreshItems();});
            Object<Texture2D>(host,"メニュー画像",p.Icon.GetObject(),false,v=>p.Icon.SetObject(v));
            var image=new Image{name="prop-image",image=p.Icon.GetObject(),scaleMode=ScaleMode.ScaleToFit};image.style.height=180;image.style.display=image.image==null?DisplayStyle.None:DisplayStyle.Flex;host.Add(image);
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
                var propImage=UI<Image>("prop-image");if(propImage!=null){propImage.image=ToolkitProp?.Icon?.GetObject();propImage.style.display=propImage.image==null?DisplayStyle.None:DisplayStyle.Flex;}
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
            groupView?.SetEnabled(ready);UI<ScrollView>("group-settings").SetEnabled(ready);
            foreach(var name in new[]{"export","import","helpers","chest","add-group","remove-group","group-up","group-down"})UI<Button>(name).SetEnabled(ready);
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
            mark.generateVisualContent+=context=>{var pen=context.painter2D;pen.strokeColor=Color.white;pen.lineWidth=1.4f;pen.BeginPath();pen.MoveTo(new Vector2(3,3));pen.LineTo(new Vector2(7,3));pen.LineTo(new Vector2(17,17));pen.LineTo(new Vector2(13,17));pen.ClosePath();pen.Stroke();pen.BeginPath();pen.MoveTo(new Vector2(17,3));pen.LineTo(new Vector2(3,17));pen.Stroke();};
            link.Add(mark);link.Add(new Label("@L1ZE_ragram1"));host.Add(link);
        }
    }
}
#endif
