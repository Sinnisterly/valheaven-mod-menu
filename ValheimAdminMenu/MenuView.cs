using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    internal sealed partial class MenuView : IDisposable
    {
        private readonly Plugin app;
        private const float Width=1020, Height=720;
        private GameObject root;
        private RectTransform canvasRect, window, body, popup, dialog, tooltip;
        private Canvas canvas;
        private TMP_FontAsset bodyFont, displayFont;
        private TextMeshProUGUI statusText, keyText, creditText, pageTitle, tooltipText, bindText;
        private static readonly string[] TabKeys =
        {
            "tabs.character","tabs.build","tabs.items","tabs.entities","tabs.vfx","tabs.equipment",
            "tabs.farming","tabs.world","tabs.teleport","tabs.camera","tabs.esp","tabs.events","tabs.hotkeys","tabs.settings"
        };
        private readonly int[] subTabs=new int[14];
        private string TabLabel(int index) => L.T(TabKeys[index]);
        private static string[] SubTabKeys(int main)
        {
            switch (main)
            {
                case 0: return new[] { "sub.character.combat", "sub.character.survival", "sub.character.food", "sub.character.movement", "sub.character.powers" };
                case 1: return new[] { "sub.build.placement", "sub.build.crafting", "sub.build.structure", "sub.build.stations", "sub.build.loot", "sub.build.tools" };
                case 10: return new[] { "Settings", "Filters" };
                case 7: return new[] { "sub.world.environment", "sub.world.creatures", "sub.world.terrain" };
                default: return null;
            }
        }
        private readonly List<UnityEngine.UI.Image> navImages=new List<UnityEngine.UI.Image>();
        private readonly List<UnityEngine.UI.Image> subNavImages=new List<UnityEngine.UI.Image>();
        private readonly List<Action> refreshers=new List<Action>();
        private int tab, bindAfterFrame;
        private RectTransform subNav;
        private const int SettingsTab=12, HotkeysTab=11, ItemsTab=2, EntitiesTab=3, VfxTab=4, FarmingTab=6;
        private string menuSearch="";
        private TMP_InputField menuSearchField;
        private bool SettingsDense => tab==SettingsTab;
        private float refreshAt, appliedScale;
        private Vector2 position=new Vector2(-1,-1);
        private MenuHover hovered;
        private float hoverAt;
        private string hoverText;
        private bool rebuild, needsLayout, restoreScroll, openLogged, sizeChecked;
        private int openedFrame;
        private float restoreScrollValue = 1f;
        private int builtScrollKey = -1;
        private readonly Dictionary<int, float> scrollMemory = new Dictionary<int, float>();
        private UnityEngine.UI.ScrollRect pageScroll;
        internal bool BindingKey;
        /// <summary>Null = rebinding menu open key; else HotkeyRegistry feature id.</summary>
        internal string BindingFeatureId;
        internal bool EditingText => inputSystem && inputSystem.currentSelectedGameObject
            && inputSystem.currentSelectedGameObject.GetComponent<TMP_InputField>()?.isFocused==true;
        internal BaseInputModule InputModule {get; private set;}
        private MenuRawInput rawInput;
        private EventSystem inputSystem;
        private BaseInputModule previousModule;
        private GameObject ownedSystem;
        internal Vector2 Position => position;
        internal MenuView(Plugin app)
        {
            this.app = app;
            MenuLanguage.Changed += OnLanguageChanged;
        }
        internal void SetPosition(float x,float y) { position=new Vector2(x,y); }
        internal void ResetScene() { ClearHover(); CancelConfirmation(); ClosePopup(); catalog.Clear(); rebuild=true; }
            internal void StopBinding() { BindingKey=false; BindingFeatureId=null; if(bindText)bindText.text=L.T("chrome.change_key"); }
        internal void PollKeybind()
        {
            if(!BindingKey || Time.frameCount<=bindAfterFrame)return;
            if(Input.GetKeyDown(KeyCode.Escape)){StopBinding();return;}
            foreach(KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if(!Rules.ValidHotkey(key) || !Input.GetKeyDown(key)) continue;
                if(string.IsNullOrEmpty(BindingFeatureId))
                {
                    var clash=HotkeyRegistry.All.FirstOrDefault(f=>f.Key==key);
                    if(clash!=null){app.Report(L.Tf("toast.hotkey_conflict", clash.Label));StopBinding();return;}
                    app.Hotkey=key; StopBinding(); app.Report(L.Tf("toast.menu_key", key)); if(app.Settings!=null && app.Settings.Data.autoSave)app.Settings.Save(false); return;
                }
                string error=HotkeyRegistry.Bind(BindingFeatureId,key,app.Hotkey);
                if(error!=null){app.Report(error);StopBinding();return;}
                var feature=HotkeyRegistry.Find(BindingFeatureId);
                StopBinding();
                app.Report(L.Tf("toast.feature_key", feature!=null?feature.Label:L.T("hotkeys.section.feature_binds"), key));
                if(app.Settings!=null && app.Settings.Data.autoSave)app.Settings.Save(false);
                return;
            }
        }
        internal bool DismissOverlay()
        {
            if(dialog){CancelConfirmation();return true;}
            if(popup){ClosePopup();return true;}
            return false;
        }
        internal void Show()
        {
            if(!root)BuildShell();
            EnsureCanvas();
            root.SetActive(true);
            AttachInput();
            BuildPage();
            // Drive the overlay rect to screen size first; a fresh root still reports its 100x100 default.
            Canvas.ForceUpdateCanvases();
            FitWindow();
            ClearHover();
            Canvas.ForceUpdateCanvases();
            openedFrame=Time.frameCount;
            sizeChecked=false;
            // DX11 "menu shows nothing" reports: log the visibility state once so logs can tell host vs canvas apart.
            if(!openLogged){openLogged=true;AppLog.Info("Menu opened. "+DescribeVisibility());}
        }
        internal string DescribeVisibility()
        {
            var size=canvasRect?canvasRect.rect.size:Vector2.zero;
            string display;
            try{display=Display.main.renderingWidth+"x"+Display.main.renderingHeight;}catch (Exception caught) { AppLog.Swallow(caught); display="?"; }
            return "GFX "+SystemInfo.graphicsDeviceType+" / "+Screen.fullScreenMode
                +", screen "+Screen.width+"x"+Screen.height+", display "+display
                +", canvas "+(canvas?(canvas.enabled+" "+canvas.renderMode+" scale "+canvas.scaleFactor.ToString("0.###")):"missing")
                +", rect "+size.x.ToString("0")+"x"+size.y.ToString("0")
                +", root "+(root?root.activeInHierarchy.ToString():"missing")
                +", window "+(window?(window.localScale.x.ToString("0.###")+" @ "+window.anchoredPosition):"missing");
        }
        private Vector2 CanvasSize()
        {
            var size=canvasRect.rect.size;
            if(size.x>=64f && size.y>=64f)return size;
            // Overlay rect can lag at 0 (seen around DX11 exclusive); Screen is the same space before scaling.
            float scale=canvas && canvas.scaleFactor>.01f?canvas.scaleFactor:1f;
            return new Vector2(Screen.width/scale,Screen.height/scale);
        }
        private void EnsureCanvas()
        {
            if(!root)return;
            if(!canvas)canvas=root.GetComponent<Canvas>();
            if(!canvas)canvas=root.AddComponent<Canvas>();
            // Overlay is independent of the game camera; force it on in case a prior failure disabled it.
            canvas.enabled=true;
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=32760;
            if(!root.GetComponent<UnityEngine.UI.GraphicRaycaster>())
                root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if(!canvasRect)canvasRect=(RectTransform)root.transform;
        }
        internal void Hide()
        {
            FlushSlider(); ClearHover(); ClosePopup(); CancelConfirmation(); StopBinding();
            DetachMenuInput();
            if(inputSystem)inputSystem.SetSelectedGameObject(null);
            if(ownedSystem)ownedSystem.SetActive(false);
            if(root)root.SetActive(false);
        }
        // Leaving MenuRawInput on the game EventSystem breaks StoreGui/trader (Unity falls back to our all-false BaseInput).
        private void DetachMenuInput()
        {
            if(InputModule)
            {
                InputModule.DeactivateModule();
                InputModule.enabled=false;
                InputModule.inputOverride=null;
                Object.Destroy(InputModule);
                InputModule=null;
            }
            if(rawInput){Object.Destroy(rawInput);rawInput=null;}
        }
        internal void RestoreInput()
        {
            if(!inputSystem)return;
            foreach(var module in inputSystem.GetComponents<BaseInputModule>())
                if(module && module!=InputModule) module.enabled=true;
            var target=previousModule && previousModule
                ? previousModule
                : inputSystem.GetComponents<BaseInputModule>().FirstOrDefault(m=>m && m.enabled);
            if(target)
                Reflection.Method(typeof(EventSystem),"ChangeEventModule",typeof(BaseInputModule)).Invoke(inputSystem,new object[]{target});
        }
        private void AttachInput()
        {
            var system=EventSystem.current;
            if(!system)
            {
                if(!ownedSystem){ownedSystem=new GameObject("Valheaven EventSystem",typeof(EventSystem));Object.DontDestroyOnLoad(ownedSystem);}
                ownedSystem.SetActive(true); system=ownedSystem.GetComponent<EventSystem>();
            }
            // Rebuild so a leftover MenuRawInput can't poison the game EventSystem.
            DetachMenuInput();
            foreach (var leftover in system.GetComponents<MenuRawInput>()) Object.Destroy(leftover);
            foreach (var leftover in system.GetComponents<MenuInputModule>()) Object.Destroy(leftover);
            inputSystem=system;
            previousModule=system.currentInputModule && system.currentInputModule.enabled
                ? system.currentInputModule
                : system.GetComponents<BaseInputModule>().FirstOrDefault(m=>m && m.enabled && !(m is MenuInputModule));
            rawInput=system.gameObject.AddComponent<MenuRawInput>();
            var module=system.gameObject.AddComponent<MenuInputModule>();
            module.inputOverride=rawInput;
            InputModule=module;
            InputModule.enabled=true; EventSystem.current=system;
            system.SetSelectedGameObject(null);
        }
        internal void Tick()
        {
            if(!app.Open || !root)return;
            // Scene / UI swaps can replace EventSystem; reattach instead of closing (DX11 exclusive focus blips did this too).
            if(inputSystem!=EventSystem.current)
            {
                try { AttachInput(); }
                catch (Exception ex) { AppLog.Warning("Menu input reattach failed: " + ex.Message); return; }
            }
            if(!root.activeSelf || (canvas && !canvas.enabled))
            {
                try { EnsureCanvas(); root.SetActive(true); }
                catch (Exception ex) { AppLog.Warning("Menu canvas restore failed: " + ex.Message); return; }
            }
            TickSliderLog();
            if(rebuild){BuildPage();rebuild=false;}
            FitWindow();
            if(!sizeChecked && Time.frameCount-openedFrame>30)
            {
                sizeChecked=true;
                var size=canvasRect.rect.size;
                if(size.x<64f || size.y<64f)AppLog.Warning("Menu canvas still has no size 30 frames after opening. "+DescribeVisibility());
            }
            if(!string.Equals(statusText.text, app.Status, StringComparison.Ordinal)) statusText.text=app.Status;
            if(Time.unscaledTime>=refreshAt)
            {
                refreshAt=Time.unscaledTime+.35f;
                keyText.text=app.Hotkey+"  /  Esc";
                for(int i=0;i<refreshers.Count;i++)
                {
                    var refresh=refreshers[i];
                    if(refresh!=null)refresh();
                }
            }
            if(needsLayout)
            {
                Canvas.ForceUpdateCanvases();
                needsLayout=false;
                if(restoreScroll)
                {
                    restoreScroll=false;
                    var scroll=pageScroll?pageScroll:catalogScroll;
                    if(scroll) scroll.verticalNormalizedPosition=restoreScrollValue;
                }
            }
            UpdateTooltip();
        }
        private void BuildShell()
        {
            var resolved = MenuFonts.Resolve(MenuLanguage.EffectiveCode);
            bodyFont = resolved.Body;
            displayFont = resolved.Display;
            root=new GameObject("Valheaven",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            Object.DontDestroyOnLoad(root);
            canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=32760;
            var scaler=root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            canvasRect=(RectTransform)root.transform;
            var shield=Box(canvasRect,"Input shield",Color.clear,true);Stretch(shield);
            window=Box(canvasRect,"Window",Panel,true);window.anchorMin=window.anchorMax=new Vector2(0,1);window.pivot=new Vector2(0,1);window.sizeDelta=new Vector2(Width,Height);
            var shadow=window.gameObject.AddComponent<UnityEngine.UI.Shadow>();shadow.effectColor=new Color(0,0,0,.6f);shadow.effectDistance=new Vector2(0,-6);
            Border(window,Line);
            var header=Box(window,"Header",new Color(.085f,.081f,.073f),true);At(header,1,1,Width-2,73);
            var drag=header.gameObject.AddComponent<MenuDrag>();drag.Menu=this;
            var logo=Label(header,L.T("chrome.brand"),32,Gold);At(logo.rectTransform,25,12,420,48);logo.font=displayFont;
            var search=InputBox(header,menuSearch,v=>{menuSearch=v;}, L.T("search.placeholder"),48);
            At((RectTransform)search.transform,Width-320,20,200,30);
            menuSearchField=search;
            Help((RectTransform)search.transform,L.T("search.help"));
            var goSearch=ActionButton(header,L.T("chrome.go"),JumpMenuSearch,L.T("search.go.help"));
            // ActionButton is NoWrap; 40px made "Go" stack as G/o.
            At((RectTransform)goSearch.transform,Width-118,20,48,30);
            var close=ActionButton(header,L.T("chrome.close_x"),()=>app.SetOpen(false),L.T("chrome.close_x.help"),false,"Close menu");At((RectTransform)close.transform,Width-62,18,34,34);
            var sidebar=Box(window,"Navigation",new Color(.069f,.067f,.061f),false);At(sidebar,1,75,181,Height-76);
            float navStep=Mathf.Min(42f,(Height-240f)/TabKeys.Length);
            for(int i=0;i<TabKeys.Length;i++)
            {
                int index=i;
                var b=ActionButton(sidebar,TabLabel(i),()=>{tab=index;BuildPage();},null,false,"Tab "+TabKeys[i].Substring(5));
                At((RectTransform)b.transform,12,10+i*navStep,157,Mathf.Min(36,navStep-3));
                var text=b.GetComponentInChildren<TextMeshProUGUI>();text.alignment=TextAlignmentOptions.MidlineLeft;text.margin=new Vector4(16,0,6,0);text.fontSize=15;
                ChromeCaption(text);
                navImages.Add(b.targetGraphic as UnityEngine.UI.Image);
            }
            keyText=Label(sidebar,"",13,Muted);At(keyText.rectTransform,24,Height-155,148,22);
            LabelAt(sidebar,L.T("chrome.close_menu"),24,Height-132,148,18,12,Muted);
            creditText=Label(sidebar,L.T("chrome.credits_short"),11,Muted);At(creditText.rectTransform,24,Height-112,148,18);
            creditText.overflowMode=TextOverflowModes.Overflow;
            var sep=Box(window,"Divider",Line,false);At(sep,182,75,1,Height-76);
            pageTitle=Label(window,TabLabel(0),27,Ink);pageTitle.font=displayFont;At(pageTitle.rectTransform,208,94,710,39);
            subNav=Node(window,"Sub navigation");At(subNav,208,138,Width-233,34);
            body=Node(window,"Page");At(body,208,147,Width-233,Height-225);
            var foot=Box(window,"Footer line",Line,false);At(foot,207,Height-58,Width-232,1);
            statusText=Label(window,"",13,Muted);At(statusText.rectTransform,208,Height-52,Width-232,44);
            statusText.textWrappingMode=TextWrappingModes.Normal;statusText.overflowMode=TextOverflowModes.Ellipsis;
            statusText.alignment=TextAlignmentOptions.TopLeft;
            pageTitle.textWrappingMode=TextWrappingModes.Normal;pageTitle.overflowMode=TextOverflowModes.Overflow;
            tooltip=Box(canvasRect,"Tooltip",new Color(.075f,.073f,.065f,1),false);Border(tooltip,GoldDim);
            tooltip.pivot=new Vector2(0,1);tooltip.anchorMin=tooltip.anchorMax=new Vector2(0,1);tooltip.sizeDelta=new Vector2(330,80);
            tooltipText=Label(tooltip,"",15,Ink);Stretch(tooltipText.rectTransform,14,11,14,11);tooltipText.textWrappingMode=TextWrappingModes.Normal;tooltipText.overflowMode=TextOverflowModes.Overflow;
            tooltip.gameObject.SetActive(false);
        }
        private void FitWindow()
        {
            if(!window || !canvasRect)return;
            var screen=CanvasSize();
            // No usable size at all: keep the window on-screen at 1:1 instead of leaving a stale scale/position.
            if(screen.x<64f || screen.y<64f){window.localScale=Vector3.one;window.anchoredPosition=new Vector2(8,-8);return;}
            // Don't rescale mid-drag (slider/drag keep their transform until release).
            if(!Input.GetMouseButton(0) || appliedScale==0)
                appliedScale=Mathf.Max(.35f,Mathf.Min(app.UiScale,(screen.x-32)/Width,(screen.y-32)/Height));
            window.localScale=Vector3.one*appliedScale;
            if(position.x<0 || position.y<0)position=new Vector2((screen.x-Width*appliedScale)/2,(screen.y-Height*appliedScale)/2);
            position.x=Mathf.Clamp(position.x,8,Mathf.Max(8,screen.x-Width*appliedScale-8));
            position.y=Mathf.Clamp(position.y,8,Mathf.Max(8,screen.y-Height*appliedScale-8));
            window.anchoredPosition=new Vector2(position.x,-position.y);
        }
        internal void Drag(Vector2 delta) { position+=new Vector2(delta.x,-delta.y)/canvas.scaleFactor;ClearHover();FitWindow(); }
        internal void Hover(MenuHover source,string text)
        {
            if(hovered==source)return;
            ClearHover();if(string.IsNullOrWhiteSpace(text))return;
            hovered=source;hoverText=text;hoverAt=Time.unscaledTime+.45f;
        }
        internal void ExitHover(MenuHover source) { if(hovered==source)ClearHover(); }
        private void ClearHover(){hovered=null;hoverText=null;if(tooltip)tooltip.gameObject.SetActive(false);}
        private void UpdateTooltip()
        {
            if(!hovered || !hovered.isActiveAndEnabled || !app.Settings.Data.tooltips || popup || dialog || Input.GetMouseButton(0) || !Application.isFocused)
            {ClearHover();return;}
            var rect=(RectTransform)hovered.transform;
            if(!RectTransformUtility.RectangleContainsScreenPoint(rect,Input.mousePosition,null)) {ClearHover();return;}
            // Scroll clip can hide a row without pointer-exit.
            foreach(var mask in hovered.GetComponentsInParent<UnityEngine.UI.RectMask2D>())
                if(!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)mask.transform,Input.mousePosition,null)){ClearHover();return;}
            if(Time.unscaledTime<hoverAt)return;
            tooltipText.text=hoverText;
            float h=Mathf.Clamp(tooltipText.GetPreferredValues(hoverText,302,0).y+24,46,190);
            tooltip.sizeDelta=new Vector2(330,h);
            Vector2 point;RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,Input.mousePosition,null,out point);
            float x=point.x-canvasRect.rect.xMin+18,y=canvasRect.rect.yMax-point.y+22;
            tooltip.anchoredPosition=new Vector2(Mathf.Clamp(x,8,canvasRect.rect.width-338),-Mathf.Clamp(y,8,canvasRect.rect.height-h-8));
            tooltip.SetAsLastSibling();tooltip.gameObject.SetActive(true);
        }
        private int ScrollKey()
        {
            int sub=tab>=0 && tab<subTabs.Length?subTabs[tab]:0;
            return tab*100+sub;
        }
        private void RememberScroll()
        {
            var scroll=pageScroll?pageScroll:catalogScroll;
            if(!scroll || builtScrollKey<0) return;
            scrollMemory[builtScrollKey]=scroll.verticalNormalizedPosition;
        }
        private void BuildPage()
        {
            RememberScroll();
            ClearHover();ClosePopup();refreshers.Clear();ResetFolds();Clear(body);subNavImages.Clear();
            if(subNav){Clear(subNav);subNav.gameObject.SetActive(false);}
            pageTitle.text=TabLabel(tab);
            for(int i=0;i<navImages.Count;i++)
            {
                var image=navImages[i];
                if(image) image.color=i==tab?Selected:Color.clear;
            }
            string[] subKeys=SubTabKeys(tab);
            string[] subs=subKeys==null?null:subKeys.Select(L.T).ToArray();
            bool showSub=subs!=null && subs.Length>1;
            LayoutBody(showSub);
            if(showSub)BuildSubNav(subs);
            if(tab==ItemsTab || tab==EntitiesTab){BuildCatalog(tab==ItemsTab);FinishScroll();return;}
            if(tab==VfxTab){BuildVfxCatalog();FinishScroll();return;}
            RectTransform content;pageScroll=Scroll(body,"Page scroll",out content);Stretch((RectTransform)pageScroll.transform);
            int sub=subs==null?0:Mathf.Clamp(subTabs[tab],0,Math.Max(0,subs.Length-1));
            switch(tab)
            {
                case 0:CharacterPage(content,sub);break;
                case 1:BuildPageContent(content,sub);break;
                case 5:EquipmentPage(content);break;
                case 6:FarmingPage(content);break;
                case 7:WorldPage(content,sub);break;
                case 8:TeleportPage(content);break;
                case 9:CameraPage(content);break;
                case 10:if(sub==0)OverlayPage(content);else EspFiltersPage(content);break;
                case 11:EventsPage(content);break;
                  case 12:HotkeysPage(content);break;
                  case 13:SettingsPage(content);break;
            }
            builtScrollKey=ScrollKey();
            restoreScroll=scrollMemory.TryGetValue(builtScrollKey, out restoreScrollValue);
            needsLayout=true;
        }
        private void FinishScroll()
        {
            builtScrollKey=ScrollKey();
            restoreScroll=scrollMemory.TryGetValue(builtScrollKey, out restoreScrollValue);
            needsLayout=true;
        }
        private void JumpMenuSearch()
        {
            string q=(menuSearch??"").Trim();
            if(string.IsNullOrEmpty(q)){app.Report(L.T("toast.search.empty"));return;}
            for(int i=0;i<TabKeys.Length;i++)
            {
                string label=TabLabel(i);
                if(label.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0
                    || TabKeys[i].IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)
                {tab=i;BuildPage();app.Report(L.Tf("toast.search.opened_tab", label));return;}
            }
            foreach(var hit in MenuSearchIndex.All)
            {
                string label=hit.ResolvedLabel;
                string keywords=hit.ResolvedKeywords;
                if(label.IndexOf(q,StringComparison.OrdinalIgnoreCase)<0
                    && (keywords==null || keywords.IndexOf(q,StringComparison.OrdinalIgnoreCase)<0)) continue;
                tab=hit.Tab;
                if(hit.Sub>=0 && hit.Tab<subTabs.Length) subTabs[hit.Tab]=hit.Sub;
                BuildPage();
                app.Report(L.Tf("toast.search.opened_feature", label));
                return;
            }
            app.Report(L.Tf("toast.search.no_match", q));
        }
        private void LayoutBody(bool withSub)
        {
            float top=withSub?182:147;
            At(body,208,top,Width-233,Height-top-63);
            if(subNav){subNav.gameObject.SetActive(withSub);if(withSub)At(subNav,208,138,Width-233,34);}
        }
        private void BuildSubNav(string[] labels)
        {
            subNav.gameObject.SetActive(true);
            Clear(subNav);subNavImages.Clear();
            float x=0;
            for(int i=0;i<labels.Length;i++)
            {
                int index=i;
                var b=ActionButton(subNav,labels[i],()=>{subTabs[tab]=index;BuildPage();},null,false,"Sub-tab "+labels[i]);
                var text=b.GetComponentInChildren<TextMeshProUGUI>();
                text.fontSize=15;text.alignment=TextAlignmentOptions.Center;text.margin=Vector4.zero;
                ChromeCaption(text);
                // NoWrap caption: chip width follows the word.
                float w=Mathf.Max(72f,Mathf.Ceil(text.GetPreferredValues(labels[i]).x)+28f);
                At((RectTransform)b.transform,x,0,w,32);
                subNavImages.Add(b.targetGraphic as UnityEngine.UI.Image);
                x+=w+8;
            }
            int active=Mathf.Clamp(subTabs[tab],0,labels.Length-1);
            for(int i=0;i<subNavImages.Count;i++)
            {
                var image=subNavImages[i];
                if(image) image.color=i==active?Selected:Color.clear;
            }
        }
        private void ClosePopup(){ClearHover();if(popup){popup.gameObject.SetActive(false);Object.Destroy(popup.gameObject);popup=null;}}
        internal void CancelConfirmation(){ClearHover();if(dialog){dialog.gameObject.SetActive(false);Object.Destroy(dialog.gameObject);dialog=null;}}
        private void Confirm(string title,string description,Action action)
        {
            CancelConfirmation();ClosePopup();ClearHover();
            dialog=Box(canvasRect,"Confirmation",new Color(0,0,0,.66f),true);Stretch(dialog);
            var panel=Box(dialog,"Dialog",Panel,true);panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(510,250);panel.localScale=Vector3.one*Mathf.Min(1,canvasRect.rect.width/550,canvasRect.rect.height/290);Border(panel,GoldDim);
            LabelAt(panel,title,24,22,462,40,25,Ink);
            var detail=LabelAt(panel,description,24,76,462,92,17,Muted);detail.textWrappingMode=TextWrappingModes.Normal;detail.alignment=TextAlignmentOptions.TopLeft;
            var cancel=ActionButton(panel,L.T("chrome.cancel"),CancelConfirmation,null,false,"Cancel \""+title+"\"");At((RectTransform)cancel.transform,242,190,112,36);
            var yes=ActionButton(panel,L.T("chrome.confirm"),()=>{CancelConfirmation();Run(action,"Confirm \""+title+"\"");},null,true,"Confirm \""+title+"\"");At((RectTransform)yes.transform,366,190,120,36);
        }
        private void SelectFrom(RectTransform anchor,IEnumerable<KeyValuePair<string,Action>> choices,string context=null)
        {
            ClosePopup();ClearHover();
            popup=Node(canvasRect,"Dropdown layer");Stretch(popup);
            var blocker=ActionButton(popup,"",ClosePopup);Stretch((RectTransform)blocker.transform);((UnityEngine.UI.Image)blocker.targetGraphic).color=Color.clear;
            var panel=Box(popup,"Dropdown",Panel,true);Border(panel,GoldDim);
            Vector3[] corners=new Vector3[4];anchor.GetWorldCorners(corners);
            Vector2 point;RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,corners[0],null,out point);
            float w=Mathf.Min(350,canvasRect.rect.width-24),h=Mathf.Min(350,canvasRect.rect.height-24);
            At(panel,Mathf.Clamp(point.x-canvasRect.rect.xMin,12,canvasRect.rect.width-w-12),Mathf.Clamp(canvasRect.rect.yMax-point.y+4,12,canvasRect.rect.height-h-12),w,h);
            var listHost=Node(panel,"Options");Stretch(listHost,8,50,8,8);
            RectTransform list;var scroll=Scroll(listHost,"Option scroll",out list);Stretch((RectTransform)scroll.transform);
            var all=choices.ToList();
            Action<string> filter=query=>
            {
                Clear(list);
                foreach(var choice in all.Where(c=>c.Key.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0))
                {var c=choice;var b=ActionButton(list,c.Key,()=>{ClosePopup();c.Value();},null,false,(context??"Select")+" = "+c.Key);Fixed((RectTransform)b.transform,34);}
                needsLayout=true;
            };
            var search=InputBox(panel,"",filter,L.T("chrome.dropdown.search"),80);At((RectTransform)search.transform,10,10,w-20,32);
            filter("");
        }
        private void Run(Action action,string label=null){app.Enqueue(action,label==null?null:PageName()+" | "+label);}
        private void RebuildAfter(Action action){Run(()=>{action();rebuild=true;});}
        private void OnLanguageChanged()
        {
            if (!root) return;
            int savedTab = tab;
            bool open = app.Open;
            Hide();
            Object.Destroy(root);
            root = null;
            // Root is gone; drop nav image refs before Show rebuilds.
            navImages.Clear();
            subNavImages.Clear();
            bodyFont = null;
            displayFont = null;
            tab = savedTab;
            if (open) Show();
        }
        public void Dispose()
        {
            MenuLanguage.Changed -= OnLanguageChanged;
            Hide();if(InputModule)Object.Destroy(InputModule);if(rawInput)Object.Destroy(rawInput);
            if(root)Object.Destroy(root);if(ownedSystem)Object.Destroy(ownedSystem);
        }
    }
    public sealed class MenuInputModule : StandaloneInputModule
    {
        public override bool ShouldActivateModule()=>enabled && gameObject.activeInHierarchy;
    }
    public sealed class MenuRawInput : BaseInput
    {
        public override float GetAxisRaw(string axisName)=>0;
        public override bool GetButtonDown(string buttonName)=>false;
    }
    public sealed class MenuHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        internal MenuView Menu; internal string Text;
        public void OnPointerEnter(PointerEventData e){Menu?.Hover(this,Text);}
        public void OnPointerExit(PointerEventData e){Menu?.ExitHover(this);}
        public void OnPointerDown(PointerEventData e){Menu?.ExitHover(this);}
        private void OnDisable(){Menu?.ExitHover(this);}
    }
    public sealed class MenuDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        internal MenuView Menu;
        public void OnBeginDrag(PointerEventData e){}
        public void OnDrag(PointerEventData e){Menu?.Drag(e.delta);}
    }
}
