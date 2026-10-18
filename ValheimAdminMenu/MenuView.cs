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
        private TextMeshProUGUI statusText, keyText, pageTitle, tooltipText, bindText;
        private readonly string[] tabs={"Character","Items","Entities","Equipment","Terrain","World","ESP","Settings"};
        private readonly List<UnityEngine.UI.Image> navImages=new List<UnityEngine.UI.Image>();
        private readonly List<Action> refreshers=new List<Action>();
        private int tab, bindAfterFrame;
        private float refreshAt, appliedScale;
        private Vector2 position=new Vector2(-1,-1);
        private MenuHover hovered;
        private float hoverAt;
        private string hoverText;
        private bool rebuild, needsLayout;
        private UnityEngine.UI.ScrollRect pageScroll;
        internal bool BindingKey;
        internal bool EditingText => inputSystem && inputSystem.currentSelectedGameObject
            && inputSystem.currentSelectedGameObject.GetComponent<TMP_InputField>()?.isFocused==true;
        internal BaseInputModule InputModule {get; private set;}
        private MenuRawInput rawInput;
        private EventSystem inputSystem;
        private BaseInputModule previousModule;
        private GameObject ownedSystem;
        internal Vector2 Position => position;
        internal MenuView(Plugin app) { this.app=app; }
        internal void SetPosition(float x,float y) { position=new Vector2(x,y); }
        internal void ResetScene() { ClearHover(); CancelConfirmation(); ClosePopup(); catalog.Clear(); rebuild=true; }
        internal void StopBinding() { BindingKey=false; if(bindText)bindText.text="Change key"; }
        internal void PollKeybind()
        {
            if(!BindingKey || Time.frameCount<=bindAfterFrame)return;
            if(Input.GetKeyDown(KeyCode.Escape)){StopBinding();return;}
            foreach(KeyCode key in Enum.GetValues(typeof(KeyCode)))
                if(Rules.ValidHotkey(key) && Input.GetKeyDown(key))
                { app.Hotkey.Value=key; StopBinding(); app.Report("Menu key: "+key); break; }
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
            root.SetActive(true);
            AttachInput();
            BuildPage();
            FitWindow();
            ClearHover();
        }
        internal void Hide()
        {
            ClearHover(); ClosePopup(); CancelConfirmation(); StopBinding();
            if(InputModule){InputModule.DeactivateModule();InputModule.enabled=false;}
            if(inputSystem)inputSystem.SetSelectedGameObject(null);
            if(ownedSystem)ownedSystem.SetActive(false);
            if(root)root.SetActive(false);
        }
        internal void RestoreInput()
        {
            if(inputSystem && previousModule && previousModule.enabled)
                Reflection.Method(typeof(EventSystem),"ChangeEventModule",typeof(BaseInputModule)).Invoke(inputSystem,new object[]{previousModule});
        }
        private void AttachInput()
        {
            var system=EventSystem.current;
            if(!system)
            {
                if(!ownedSystem){ownedSystem=new GameObject("Valheaven EventSystem",typeof(EventSystem));Object.DontDestroyOnLoad(ownedSystem);}
                ownedSystem.SetActive(true); system=ownedSystem.GetComponent<EventSystem>();
            }
            if(inputSystem!=system || !InputModule)
            {
                if(InputModule)Object.Destroy(InputModule);
                if(rawInput)Object.Destroy(rawInput);
                inputSystem=system;
                rawInput=system.gameObject.AddComponent<MenuRawInput>();
                var module=system.gameObject.AddComponent<MenuInputModule>();
                module.inputOverride=rawInput;
                InputModule=module;
            }
            previousModule=system.currentInputModule!=InputModule && system.currentInputModule && system.currentInputModule.enabled
                ? system.currentInputModule : system.GetComponents<BaseInputModule>().FirstOrDefault(m=>m!=InputModule && m.enabled);
            InputModule.enabled=true; EventSystem.current=system;
            system.SetSelectedGameObject(null);
        }
        internal void Tick()
        {
            if(!app.Open || !root)return;
            if(inputSystem!=EventSystem.current){app.SetOpen(false);return;}
            if(rebuild){BuildPage();rebuild=false;}
            FitWindow();
            statusText.text=app.Status;
            keyText.text=app.Hotkey.Value+"  /  Esc";
            if(Time.unscaledTime>=refreshAt)
            {
                refreshAt=Time.unscaledTime+.2f;
                foreach(var refresh in refreshers.ToArray())refresh();
            }
            if(needsLayout){Canvas.ForceUpdateCanvases();needsLayout=false;}
            UpdateTooltip();
        }
        private void BuildShell()
        {
            var fonts=Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            bodyFont=fonts.FirstOrDefault(f=>f && f.name=="Valheim-AveriaSansLibre")
                ?? fonts.Where(f=>f && f.name.IndexOf("AveriaSans",StringComparison.OrdinalIgnoreCase)>=0).OrderBy(f=>f.name).FirstOrDefault()
                ?? TMP_Settings.defaultFontAsset ?? fonts.Where(f=>f).OrderBy(f=>f.name).FirstOrDefault();
            if(!bodyFont)throw new InvalidOperationException("Menu fonts are not ready. Try opening the menu again.");
            displayFont=fonts.FirstOrDefault(f=>f && f.name.IndexOf("Norse",StringComparison.OrdinalIgnoreCase)>=0) ?? bodyFont;
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
            var logo=Label(header,"VALHEAVEN",32,Gold);At(logo.rectTransform,25,12,420,48);logo.font=displayFont;
            var close=ActionButton(header,"×",()=>app.SetOpen(false),"Close the menu.");At((RectTransform)close.transform,Width-62,18,34,34);
            var sidebar=Box(window,"Navigation",new Color(.069f,.067f,.061f),false);At(sidebar,1,75,181,Height-76);
            for(int i=0;i<tabs.Length;i++)
            {
                int index=i;
                var b=ActionButton(sidebar,tabs[i],()=>{tab=index;BuildPage();},null);
                At((RectTransform)b.transform,12,17+i*47,157,39);
                var text=b.GetComponentInChildren<TextMeshProUGUI>();text.alignment=TextAlignmentOptions.MidlineLeft;text.margin=new Vector4(16,0,6,0);text.fontSize=17;
                navImages.Add(b.targetGraphic as UnityEngine.UI.Image);
            }
            keyText=Label(sidebar,"",13,Muted);At(keyText.rectTransform,24,Height-147,148,24);
            LabelAt(sidebar,"Close menu",24,Height-122,148,20,13,Muted);
            var sep=Box(window,"Divider",Line,false);At(sep,182,75,1,Height-76);
            pageTitle=Label(window,tabs[0],27,Ink);pageTitle.font=displayFont;At(pageTitle.rectTransform,208,94,710,39);
            body=Node(window,"Page");At(body,208,147,Width-233,Height-210);
            var foot=Box(window,"Footer line",Line,false);At(foot,207,Height-49,Width-232,1);
            statusText=Label(window,"",13,Muted);At(statusText.rectTransform,208,Height-39,Width-232,27);
            tooltip=Box(canvasRect,"Tooltip",new Color(.075f,.073f,.065f,1),false);Border(tooltip,GoldDim);
            tooltip.pivot=new Vector2(0,1);tooltip.anchorMin=tooltip.anchorMax=new Vector2(0,1);tooltip.sizeDelta=new Vector2(330,80);
            tooltipText=Label(tooltip,"",15,Ink);Stretch(tooltipText.rectTransform,14,11,14,11);tooltipText.textWrappingMode=TextWrappingModes.Normal;tooltipText.overflowMode=TextOverflowModes.Overflow;
            tooltip.gameObject.SetActive(false);
        }
        private void FitWindow()
        {
            if(!window)return;
            // Don't rescale mid-drag; coords stay on the old transform until mouse up.
            if(!Input.GetMouseButton(0) || appliedScale==0)
                appliedScale=Mathf.Max(.35f,Mathf.Min(app.UiScale.Value,(canvasRect.rect.width-32)/Width,(canvasRect.rect.height-32)/Height));
            window.localScale=Vector3.one*appliedScale;
            var screen=canvasRect.rect.size;
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
            // Scroll can hide a row without PointerExit; clear tooltip if outside the mask.
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
        private void BuildPage()
        {
            ClearHover();ClosePopup();refreshers.Clear();Clear(body);
            pageTitle.text=tabs[tab];
            for(int i=0;i<navImages.Count;i++)navImages[i].color=i==tab?Selected:Color.clear;
            if(tab==1 || tab==2){BuildCatalog(tab==1);return;}
            RectTransform content;pageScroll=Scroll(body,"Page scroll",out content);Stretch((RectTransform)pageScroll.transform);
            switch(tab)
            {
                case 0:CharacterPage(content);break;
                case 3:EquipmentPage(content);break;case 4:TerrainPage(content);break;case 5:WorldPage(content);break;
                case 6:OverlayPage(content);break;case 7:SettingsPage(content);break;
            }
            needsLayout=true;
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
            var cancel=ActionButton(panel,"Cancel",CancelConfirmation);At((RectTransform)cancel.transform,242,190,112,36);
            var yes=ActionButton(panel,"Confirm",()=>{CancelConfirmation();Run(action);},null,true);At((RectTransform)yes.transform,366,190,120,36);
        }
        private void SelectFrom(RectTransform anchor,IEnumerable<KeyValuePair<string,Action>> choices)
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
                {var c=choice;var b=ActionButton(list,c.Key,()=>{ClosePopup();c.Value();});Fixed((RectTransform)b.transform,34);}
                needsLayout=true;
            };
            var search=InputBox(panel,"",filter,"Search",80);At((RectTransform)search.transform,10,10,w-20,32);
            filter("");
        }
        private void Run(Action action){app.Enqueue(action);}
        private void RebuildAfter(Action action){Run(()=>{action();rebuild=true;});}
        public void Dispose()
        {
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
