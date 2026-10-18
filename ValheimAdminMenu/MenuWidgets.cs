using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    internal sealed partial class MenuView
    {
        private static readonly Color Panel=new Color(.095f,.092f,.081f,1);
        private static readonly Color Ink=new Color(.93f,.91f,.85f,1);
        private static readonly Color Muted=new Color(.69f,.68f,.61f,1);
        private static readonly Color Gold=new Color(.88f,.72f,.42f,1);
        private static readonly Color GoldDim=new Color(.44f,.36f,.22f,1);
        private static readonly Color Line=new Color(.23f,.22f,.18f,1);
        private static readonly Color FieldColor=new Color(.055f,.055f,.05f,1);
        private static readonly Color Selected=new Color(.24f,.21f,.14f,1);
        private static RectTransform Node(Transform parent,string name)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;
        }
        private static RectTransform Box(Transform parent,string name,Color color,bool raycast=false)
        {
            var rect=Node(parent,name);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=raycast;return rect;
        }
        private static void Stretch(RectTransform rect,float left=0,float top=0,float right=0,float bottom=0)
        {
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,.5f);
            rect.offsetMin=new Vector2(left,bottom);rect.offsetMax=new Vector2(-right,-top);
        }
        private static void At(RectTransform rect,float x,float y,float width,float height)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
        }
        private static void Fixed(RectTransform rect,float height,float width=-1)
        {
            var element=rect.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ?? rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minHeight=element.preferredHeight=height;element.flexibleHeight=0;
            if(width>=0){element.minWidth=element.preferredWidth=width;element.flexibleWidth=0;}
        }
        private static void Border(RectTransform parent,Color color)
        {
            var top=Box(parent,"Top edge",color);Stretch(top,0,0,0,0);top.anchorMin=new Vector2(0,1);top.sizeDelta=new Vector2(0,1);top.pivot=new Vector2(.5f,1);
            var bottom=Box(parent,"Bottom edge",color);Stretch(bottom);bottom.anchorMax=new Vector2(1,0);bottom.sizeDelta=new Vector2(0,1);bottom.pivot=new Vector2(.5f,0);
            var left=Box(parent,"Left edge",color);Stretch(left);left.anchorMax=new Vector2(0,1);left.sizeDelta=new Vector2(1,0);left.pivot=new Vector2(0,.5f);
            var right=Box(parent,"Right edge",color);Stretch(right);right.anchorMin=new Vector2(1,0);right.sizeDelta=new Vector2(1,0);right.pivot=new Vector2(1,.5f);
        }
        private TextMeshProUGUI Label(Transform parent,string text,float size=17,Color? color=null,bool localize=true)
        {
            var rect=Node(parent,"Text");rect.gameObject.SetActive(false);var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font=bodyFont;label.text=localize?L.TValue(text):text;label.fontSize=size;label.color=color??Ink;
            label.raycastTarget=false;label.richText=false;label.alignment=TextAlignmentOptions.MidlineLeft;
            // Wrap inside row bounds; no ellipsis on UI scale/resize. Chrome overrides to NoWrap.
            label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Overflow;
            rect.gameObject.SetActive(true);return label;
        }
        /// <summary>Short chrome captions (Go, Bind, Clear, x). NoWrap so they never stack vertically.</summary>
        private static void ChromeCaption(TextMeshProUGUI label)
        {
            if(!label)return;
            label.textWrappingMode=TextWrappingModes.NoWrap;
            label.overflowMode=TextOverflowModes.Ellipsis;
        }
        /// <summary>Grow row height so a wrapping label stays fully visible.</summary>
        private static void FitWrappedRow(RectTransform row, TextMeshProUGUI label, float minHeight, float padY=10f, float reservedRight=0f)
        {
            if(!row || !label)return;
            var fit=row.gameObject.GetComponent<MenuWrapFit>() ?? row.gameObject.AddComponent<MenuWrapFit>();
            fit.Row=row.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ?? row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            fit.Label=label;fit.MinHeight=minHeight;fit.PadY=padY;fit.ReservedRight=reservedRight;
            fit.Apply();
        }
        /// <summary>Left label column with a real wrap width (not glyph-width collapse).</summary>
        private static void LabelColumn(TextMeshProUGUI label, float rightFrac=.46f)
        {
            if(!label)return;
            var rt=label.rectTransform;
            rt.anchorMin=new Vector2(0,0);rt.anchorMax=new Vector2(Mathf.Clamp(rightFrac,.28f,.7f),1);
            rt.pivot=new Vector2(0,.5f);rt.offsetMin=new Vector2(0,2);rt.offsetMax=new Vector2(-6,-2);
            label.alignment=TextAlignmentOptions.Left;
            label.textWrappingMode=TextWrappingModes.Normal;
            label.overflowMode=TextOverflowModes.Overflow;
        }
        private TextMeshProUGUI LabelAt(Transform parent,string text,float x,float y,float w,float h,float size=17,Color? color=null)
        {var t=Label(parent,text,size,color);At(t.rectTransform,x,y,w,h);return t;}
        private void Help(RectTransform rect,string text)
        {
            if(string.IsNullOrWhiteSpace(text))return;
            var hover=rect.gameObject.AddComponent<MenuHover>();hover.Menu=this;hover.Text=L.TValue(text);
        }
        private static void Clear(RectTransform parent)
        {
            for(int i=parent.childCount-1;i>=0;i--){var child=parent.GetChild(i).gameObject;child.SetActive(false);Object.Destroy(child);}
        }
        private UnityEngine.UI.Button ActionButton(Transform parent,string text,Action action,string help=null,bool danger=false)
        {
            var rect=Box(parent,text, danger?new Color(.24f,.11f,.095f):new Color(.17f,.16f,.13f),true);
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=rect.GetComponent<UnityEngine.UI.Image>();
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.35f,1.28f,1.12f);colors.pressedColor=new Color(.8f,.75f,.65f);colors.selectedColor=Color.white;colors.disabledColor=new Color(.45f,.45f,.45f,.6f);colors.fadeDuration=.08f;button.colors=colors;
            button.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            // Tight side pad for short chrome labels so one line still fits.
            float pad = string.IsNullOrEmpty(text) || text.Length > 8 ? 12f : (text.Length <= 2 ? 4f : 8f);
            var label=Label(rect,text,16,danger?new Color(1,.7f,.59f):Ink);
            Stretch(label.rectTransform,pad,0,pad,0);label.alignment=TextAlignmentOptions.Center;
            ChromeCaption(label);
            button.onClick.AddListener(()=>{ClearHover();try{action();}catch(Exception e){app.Report(e.GetBaseException().Message);}});
            Help(rect,help);return button;
        }
        private RectTransform Row(Transform parent,float height)
        {var row=Node(parent,"Row");if(SettingsDense && height==46)height=38;Fixed(row,height);return row;}
        private void Section(Transform parent,string text)
        {
            var row=Row(parent,SettingsDense?32:42);
            string shown=L.TValue(text).ToUpperInvariant();
            var label=Label(row,shown,13,Gold,localize:false);Stretch(label.rectTransform,0,SettingsDense?6:12,0,SettingsDense?6:8);label.characterSpacing=1.4f;
            var line=Box(row,"Section rule",Line);line.anchorMin=new Vector2(0,0);line.anchorMax=new Vector2(1,0);line.pivot=new Vector2(.5f,0);line.sizeDelta=new Vector2(0,1);
        }
        private void Note(Transform parent,string text)
        {
            var t=Label(parent,text,15,Muted);
            t.alignment=TextAlignmentOptions.TopLeft;
            Fixed(t.rectTransform,44);
            FitWrappedRow(t.rectTransform,t,36,8f);
        }
        private bool RequireCharacter(Transform parent)
        {if(Player.m_localPlayer)return true;Note(parent,"Enter a world to use these controls.");return false;}
        private void ToggleRow(Transform parent,string name,Func<bool> read,Action<bool> write,string help=null)
        {
            float minH=SettingsDense?36:43;
            var row=Box(parent,name,Color.clear,true);Fixed(row,minH);
            var text=Label(row,name,17);LabelColumn(text,.78f);
            FitWrappedRow(row,text,minH,12f,56f);
            var toggle=row.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            var outer=Box(row,"Checkbox",FieldColor,true);outer.anchorMin=outer.anchorMax=new Vector2(1,.5f);outer.sizeDelta=new Vector2(22,22);outer.anchoredPosition=new Vector2(-16,0);Border(outer,GoldDim);
            var fill=Box(outer,"Check",Gold);Stretch(fill,5,5,5,5);
            toggle.targetGraphic=outer.GetComponent<UnityEngine.UI.Image>();toggle.graphic=fill.GetComponent<UnityEngine.UI.Image>();
            toggle.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};toggle.SetIsOnWithoutNotify(read());
            toggle.onValueChanged.AddListener(v=>{ClearHover();write(v);});
            refreshers.Add(()=>{if(toggle)toggle.SetIsOnWithoutNotify(read());});Help(row,help);
        }
        private UnityEngine.UI.Slider SliderRow(Transform parent,string name,Func<float> read,Action<float> write,float min,float max,string help,bool whole=false)
        {
            var row=Row(parent,69);var nameLabel=Label(row,name,17);LabelColumn(nameLabel,.72f);nameLabel.alignment=TextAlignmentOptions.TopLeft;
            FitWrappedRow(row,nameLabel,69,38f,90f);
            var value=Label(row,"",15,Gold);value.alignment=TextAlignmentOptions.MidlineRight;value.rectTransform.anchorMin=new Vector2(1,1);value.rectTransform.anchorMax=new Vector2(1,1);value.rectTransform.pivot=new Vector2(1,1);value.rectTransform.sizeDelta=new Vector2(80,31);
            var sliderRoot=Box(row,"Slider",Color.clear,true);Stretch(sliderRoot,0,32,0,9);
            var slider=sliderRoot.gameObject.AddComponent<UnityEngine.UI.Slider>();slider.minValue=min;slider.maxValue=max;slider.wholeNumbers=whole;
            slider.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            var track=Box(sliderRoot,"Track",FieldColor);Stretch(track,6,11,6,11);
            var fillArea=Node(sliderRoot,"Fill area");Stretch(fillArea,6,11,6,11);
            var fill=Box(fillArea,"Fill",GoldDim);Stretch(fill);
            var handleArea=Node(sliderRoot,"Handle area");Stretch(handleArea,6,0,6,0);
            var handle=Box(handleArea,"Handle",Gold,true);handle.sizeDelta=new Vector2(12,-8);
            slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<UnityEngine.UI.Image>();slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(v=>{write(v);value.text=v.ToString(whole?"0":"0.##",CultureInfo.InvariantCulture);ClearHover();});
            Action refresh=()=>{if(!slider)return;var v=read();slider.SetValueWithoutNotify(v);value.text=v.ToString(whole?"0":"0.##",CultureInfo.InvariantCulture);};refresh();refreshers.Add(refresh);Help(sliderRoot,help);return slider;
        }
        private TMP_InputField InputBox(Transform parent,string value,Action<string> changed,string placeholder="",int limit=80)
        {
            var rect=Box(parent,"Input",FieldColor,true);Border(rect,Line);
            var input=rect.gameObject.AddComponent<TMP_InputField>();input.targetGraphic=rect.GetComponent<UnityEngine.UI.Image>();
            var viewport=Node(rect,"Text viewport");Stretch(viewport,10,4,10,4);viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var text=Label(viewport,"",17,Ink);Stretch(text.rectTransform);text.overflowMode=TextOverflowModes.Overflow;
            var hint=Label(viewport,placeholder,17,Muted);Stretch(hint.rectTransform);
            input.textViewport=viewport;input.textComponent=text;input.placeholder=hint;input.characterLimit=limit;
            input.lineType=TMP_InputField.LineType.SingleLine;input.richText=false;input.customCaretColor=true;input.caretColor=Gold;input.selectionColor=new Color(.55f,.43f,.24f,.55f);
            input.SetTextWithoutNotify(value);input.onValueChanged.AddListener(v=>changed(v));
            input.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};return input;
        }
        private TMP_InputField Field(Transform parent,string name,string value,Action<string> changed,string help=null,int limit=80)
        {
            var row=Row(parent,44);var text=Label(row,name,17);LabelColumn(text,.46f);
            FitWrappedRow(row,text,44,10f,0f);
            var input=InputBox(row,value,changed,"",limit);var rect=(RectTransform)input.transform;
            rect.anchorMin=new Vector2(.48f,0);rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(0,5);rect.offsetMax=new Vector2(0,-5);
            Help(rect,help);return input;
        }
        private void ActionRow(Transform parent,string name,string caption,Action action,string help=null,bool danger=false)
        {
            var row=Row(parent,46);var text=Label(row,name,17);LabelColumn(text,.55f);
            FitWrappedRow(row,text,46,12f,160f);
            var button=ActionButton(row,caption,()=>Run(action),help,danger);var rect=(RectTransform)button.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);rect.sizeDelta=new Vector2(150,33);
            Help(text.rectTransform,help);
        }
        private void DangerRow(Transform parent,string name,string caption,string warning,Action action)
        {
            var row=Row(parent,46);var label=Label(row,name,17);LabelColumn(label,.55f);
            FitWrappedRow(row,label,46,12f,160f);
            var button=ActionButton(row,caption,()=>Confirm(name,warning,action),warning,true);var rect=(RectTransform)button.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);rect.sizeDelta=new Vector2(150,33);
        }
        private UnityEngine.UI.Button SelectRow(Transform parent,string label,Func<string> selected,Func<System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,Action>>> options)
        {
            var row=Row(parent,44);var name=Label(row,label,17);LabelColumn(name,.46f);
            FitWrappedRow(row,name,44,10f,0f);
            UnityEngine.UI.Button button=null;button=ActionButton(row,"",()=>SelectFrom((RectTransform)button.transform,options()));
            var rect=(RectTransform)button.transform;rect.anchorMin=new Vector2(.48f,0);rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(0,5);rect.offsetMax=new Vector2(0,-5);
            var title=button.GetComponentInChildren<TextMeshProUGUI>();title.alignment=TextAlignmentOptions.Left;
            title.textWrappingMode=TextWrappingModes.Normal;title.overflowMode=TextOverflowModes.Ellipsis;
            Action refresh=()=>{if(title)title.text=selected()+"   v";};refresh();refreshers.Add(refresh);return button;
        }
        private UnityEngine.UI.ScrollRect Scroll(Transform parent,string name,out RectTransform content)
        {
            var rect=Box(parent,name,Color.clear,true);var scroll=rect.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var viewport=Box(rect,"Viewport",Color.clear,true);Stretch(viewport,0,0,13,0);viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            content=Node(viewport,"Content");content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            var layout=content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();layout.spacing=3;layout.padding=new RectOffset(0,2,0,8);layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var fitter=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();fitter.horizontalFit=UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.content=content;scroll.viewport=viewport;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;scroll.inertia=false;scroll.scrollSensitivity=30;
            var bar=Box(rect,"Scrollbar",FieldColor,true);bar.anchorMin=new Vector2(1,0);bar.anchorMax=Vector2.one;bar.pivot=new Vector2(1,.5f);bar.sizeDelta=new Vector2(5,0);
            var scrollbar=bar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();var handle=Box(bar,"Handle",GoldDim,true);Stretch(handle);
            scrollbar.handleRect=handle;scrollbar.targetGraphic=handle.GetComponent<UnityEngine.UI.Image>();scrollbar.direction=UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            scrollbar.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};scroll.verticalScrollbar=scrollbar;
            scroll.verticalScrollbarVisibility=UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.onValueChanged.AddListener(_=>ClearHover());return scroll;
        }
        private static float Number(string value,string name,float min,float max)
        {
            if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var result) || !Rules.Finite(result) || result<min || result>max)
                throw new ArgumentException(name+" must be between "+min+" and "+max+".");
            return result;
        }
        private static int Integer(string value,string name,int min,int max)
        {
            if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var result) || result<min || result>max)
                throw new ArgumentException(name+" must be a whole number between "+min+" and "+max+".");
            return result;
        }
    }

    /// <summary>Recompute row height from wrapping TMP. Never measure at glyph width (that stacked letters vertically).</summary>
    internal sealed class MenuWrapFit : MonoBehaviour
    {
        internal UnityEngine.UI.LayoutElement Row;
        internal TextMeshProUGUI Label;
        internal float MinHeight = 36f;
        internal float PadY = 10f;
        internal float ReservedRight = 0f;
        internal const float MinLabelWidth = 140f;
        private float lastWidth = -1f;
        private string lastText;

        private void OnEnable() => Apply();
        private void OnRectTransformDimensionsChange() => Apply();
        private void LateUpdate()
        {
            if (!Label) return;
            if (Label.text != lastText) Apply();
        }

        internal void Apply()
        {
            if (!Row || !Label) return;
            var rect = (RectTransform)transform;
            float rowW = rect.rect.width;
            if (rowW < 8f) return;
            float width = Label.rectTransform.rect.width;
            float avail = Mathf.Max(MinLabelWidth, rowW - Mathf.Max(0f, ReservedRight) - 8f);
            // First layout frames can report ~0-1px width; use a real horizontal column.
            if (width < MinLabelWidth * .85f) width = avail;
            width = Mathf.Clamp(width, MinLabelWidth, Mathf.Max(MinLabelWidth, rowW - 4f));
            if (Mathf.Abs(width - lastWidth) < .5f && Label.text == lastText) return;
            lastWidth = width;
            lastText = Label.text;
            float h = Label.GetPreferredValues(Label.text ?? "", width, 0f).y + PadY;
            float target = Mathf.Max(MinHeight, h);
            Row.minHeight = Row.preferredHeight = target;
            Row.flexibleHeight = 0f;
        }
    }
}
