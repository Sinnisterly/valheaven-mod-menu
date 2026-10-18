#if VALHEAVEN_UI_CHECK
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ValheimAdminMenu
{
    internal sealed partial class MenuView
    {
        private void CaptureCheck(string path)
        {
            var cameraObject=new GameObject("UI capture camera",typeof(Camera));
            var camera=cameraObject.GetComponent<Camera>();camera.transform.position=new Vector3(1000000,1000000,1000000);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.037f,.038f);camera.orthographic=true;camera.orthographicSize=Screen.height/2f;camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.cullingMask=1<<31;
            var target=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))rect.gameObject.layer=31;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            Canvas.ForceUpdateCanvases();camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            RenderTexture.active=previous;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;
            foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))rect.gameObject.layer=0;
            camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(cameraObject);
        }
        internal IEnumerator RuntimeCheck()
        {
            if(!Environment.GetCommandLineArgs().Contains("-valheaven-ui-check"))yield break;
            string output=Environment.GetEnvironmentVariable("VALHEAVEN_UI_CHECK_OUTPUT");
            if(string.IsNullOrEmpty(output))yield break;
            Directory.CreateDirectory(output);
            var lines=new List<string>();
            Action<bool,string> check=(ok,name)=>{lines.Add((ok?"PASS: ":"FAIL: ")+name);File.WriteAllLines(Path.Combine(output,"runtime-checks.txt"),lines);};
            float deadline=Time.realtimeSinceStartup+90;
            while((!FejdStartup.instance || !Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Any()) && Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSecondsRealtime(3);
            bool autoSave=app.Settings.Data.autoSave;float scale=app.UiScale;Vector2 pos=position;
            app.Settings.Data.autoSave=false;
            app.SetOpen(true);tab=12;BuildPage();position=new Vector2(-1,-1);app.UiScale=1;
            yield return null;yield return null;yield return new WaitForEndOfFrame();
            check(root && root.activeInHierarchy,"Canvas opens at the title screen");
            check(EventSystem.current && EventSystem.current.currentInputModule==InputModule,"Menu input module is the active EventSystem module");
            check(EventSystem.current.GetComponents<BaseInputModule>().Count(m=>m.enabled)==1,"Exactly one input module is enabled");
            check(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>()!=null,"Canvas has a graphic raycaster");
            var outside=new PointerEventData(EventSystem.current){position=new Vector2(4,4)};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(outside,hits);
            check(hits.Count>0 && hits[0].gameObject.name=="Input shield","Clicks outside the menu hit the input shield");
            check(pageScroll.content.rect.height<=pageScroll.viewport.rect.height+1,"Settings fit within the page without overflow");
            check(body.GetComponentsInChildren<UnityEngine.UI.Slider>().All(s=>s.handleRect.rect.height>0 && s.handleRect.rect.height<=40),"Slider handles fit their tracks");
            var slider=body.GetComponentInChildren<UnityEngine.UI.Slider>();
            var handleArea=(RectTransform)slider.handleRect.parent;
            Vector3[] corners=new Vector3[4];handleArea.GetWorldCorners(corners);
            var start=(Vector2)((corners[0]+corners[1])*.5f);
            var end=(Vector2)((corners[2]+corners[3])*.5f);
            var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=start,pressPosition=start};
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            pointer.position=Vector2.Lerp(start,end,.8f);
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.dragHandler);
            check(Math.Abs(slider.value-Mathf.Lerp(slider.minValue,slider.maxValue,.8f))<.02,"Slider responds to a pointer drag across its track");
            pointer.position=end+Vector2.right*100;
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.dragHandler);
            check(Math.Abs(slider.value-slider.maxValue)<.01,"Slider clamps a drag beyond its right edge");
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerUpHandler);
            app.UiScale=1;slider.SetValueWithoutNotify(1);
            var hover=body.GetComponentInChildren<MenuHover>();Hover(hover,"Hover check");tooltip.gameObject.SetActive(true);
            hover.OnPointerExit(pointer);check(!tooltip.gameObject.activeSelf,"Pointer exit hides the tooltip immediately");
            Hover(hover,"Hover check");tooltip.gameObject.SetActive(true);BuildPage();check(!tooltip.gameObject.activeSelf,"Changing page clears hover state");
            yield return null;yield return new WaitForEndOfFrame();
            CaptureCheck(Path.Combine(output,"settings.png"));
            var anchor=(RectTransform)body.GetComponentInChildren<UnityEngine.UI.Button>().transform;
            bool selected=false;SelectFrom(anchor,new[]{Choice("Test selection",()=>selected=true)});
            var option=popup.GetComponentsInChildren<UnityEngine.UI.Button>().Last();option.onClick.Invoke();
            check(selected && !popup,"Dropdown selection invokes its action and closes the list");
            tab=1;BuildPage();yield return null;yield return null;
            check(catalog.Count>0,"Item catalog builds from actual title-screen prefabs: "+catalog.Count+" entries");
            check(catalog.Count==catalog.Select(e=>e.Id).Distinct().Count(),"Item catalog has no duplicate prefab IDs");
            check(catalogList && catalogList.rect.height>catalogScroll.viewport.rect.height,"Item content is taller than its viewport and can scroll");
            if(catalog.Count>0)
            {
                var rowRect=(RectTransform)catalogRows[0].Value.transform;
                var itemPointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rowRect.TransformPoint(rowRect.rect.center))};
                var itemHits=new List<RaycastResult>();EventSystem.current.RaycastAll(itemPointer,itemHits);
                check(itemHits.Count>0 && itemHits[0].gameObject==rowRect.gameObject,"Item rows receive pointer raycasts");
                var first=catalog.FirstOrDefault(e=>e.Id=="Wood")??catalog[0];
                catalogQuery=first.Id;FilterCatalog();ShowEntry(first);
                check(catalogRows.Count>0 && catalogRows.All(r=>CatalogRules.Matches(r.Key.Id,r.Key.Name,catalogQuery)),"Search filters rendered item rows");
                check(details.GetComponentsInChildren<TMP_InputField>().Length==2,"Selected item exposes quantity and quality fields");
                var input=details.GetComponentInChildren<TMP_InputField>();EventSystem.current.SetSelectedGameObject(input.gameObject);input.ActivateInputField();
                yield return null;yield return null;
                check(input.isFocused,"Item quantity field receives keyboard focus");check(EditingText,"Text entry suppresses the menu hotkey");input.SetTextWithoutNotify("12");input.DeactivateInputField();
                catalogQuery="";FilterCatalog();ShowEntry(first);
            }
            var prefab=ObjectDB.instance.m_items.First(p=>p && p.name=="Wood").GetComponent<ItemDrop>().m_itemData;
            var original=prefab.Clone();var edited=prefab.Clone();edited.m_stack=3;
            var overrideData=new ItemEdits{weight=2,maxDurability=777,block=123,damageOverride=true,damage=new HitData.DamageTypes{m_slash=88}};
            edited.m_customData[ItemEditor.Key]=JsonUtility.ToJson(overrideData);
            check(edited.GetWeight()==6 && edited.GetNonStackedWeight()==2,"Item weight override honors stack count");
            check(edited.GetMaxDurability()==777 && edited.GetBaseBlockPower(1)==123,"Item durability and block overrides apply");
            check(edited.GetDamage().m_slash==88,"Item damage override applies");
            check(!original.m_customData.ContainsKey(ItemEditor.Key) && original.GetMaxDurability()!=777,"Editing a clone leaves other copies unchanged");
            check(!edited.IsSameType(original),"Edited and ordinary item stacks remain separate");
            var inventory=new Inventory("Validation",null,4,4);inventory.GetAllItems().Add(original);original.m_stack=1;
            ItemStackIdentity.Incoming=edited;
            var candidate=Reflection.Method(typeof(Inventory),"FindFreeStackItem",typeof(string),typeof(int),typeof(float)).Invoke(inventory,new object[]{edited.m_shared.m_name,edited.m_quality,(float)edited.m_worldLevel});
            check(candidate==null && inventory.FindFreeStackSpace(edited.m_shared.m_name,edited.m_worldLevel)==0,"Stack search and capacity exclude incompatible item edits");
            ItemStackIdentity.Incoming=null;
            var savedClone=edited.Clone();check(savedClone.GetDamage().m_slash==88,"Item cloning preserves saved stat metadata");
            edited.m_customData.Remove(ItemEditor.Key);check(edited.GetMaxDurability()!=777,"Removing metadata restores original item stats");
            var training=new Skills.Skill(new Skills.SkillDef{m_skill=Skills.SkillType.Swords,m_increseStep=1});training.m_level=42;training.m_accumulator=5;
            float oldRate=Game.m_skillGainRate;Game.m_skillGainRate=1;
            bool leveled=training.Raise(ExpansionFeatures.TrainingFactor(training.m_level,training.m_accumulator,1,1,.1f));Game.m_skillGainRate=oldRate;
            check(leveled && training.m_level==43,"Fast training triggers exactly one native skill level-up");
            check(!tabs.Contains("Players") && tabs.Contains("Equipment") && tabs.Contains("Hotkeys") && tabs.Contains("VFX") && tabs.Contains("Farming"),"Navigation includes Equipment, Hotkeys, VFX, Farming; removes Players");
            yield return null;yield return new WaitForEndOfFrame();CaptureCheck(Path.Combine(output,"items.png"));
            tab=3;BuildPage();
            foreach(Transform child in pageScroll.content)UnityEngine.Object.Destroy(child.gameObject);
            yield return null;
            editedItem=savedClone;EquipmentContent(pageScroll.content,new List<ItemDrop.ItemData>{savedClone});
            yield return null;yield return new WaitForEndOfFrame();
            check(pageScroll.content.GetComponentsInChildren<TMP_InputField>().Length>=16,"Equipment editor renders condition and damage input fields");
            CaptureCheck(Path.Combine(output,"equipment.png"));editedItem=null;
            yield return null;yield return null;
            app.SetOpen(false);check(!root.activeSelf,"Closing deactivates the Canvas");
            check(InputModule==null || !InputModule.enabled,"Closing disables menu input");
            check(!EventSystem.current || EventSystem.current.currentInputModule!=InputModule,"Closing immediately hands input back to the game");
            check(!EventSystem.current || EventSystem.current.GetComponents<BaseInputModule>().Any(m=>m!=InputModule && m.enabled),"Closing restores the game's input module");
            check(EventSystem.current==null || EventSystem.current.GetComponent<MenuRawInput>()==null,"Closing removes MenuRawInput stub from EventSystem");
            app.UiScale=scale;position=pos;app.Settings.Data.autoSave=autoSave;
            File.AppendAllText(Path.Combine(output,"runtime-checks.txt"),"\nCOMPLETE\n");
            yield return new WaitForSecondsRealtime(1);
            Application.Quit();
        }
    }
}
#endif
