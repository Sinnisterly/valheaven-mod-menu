using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal sealed partial class Overlay
    {
        internal enum Kind { Player, Monster, Pickable, Deposit, Drop, POI, Tombstone, Container }
        internal sealed class Entry { internal Component Object; internal string Label; internal string Detail; internal Kind Category; internal float Distance; }
        private readonly Plugin app;
        internal bool Enabled, AutoPin;
        internal bool[] Categories = new bool[8] { true, true, false, true, false, false, true, false };
        internal float Radius = 150;
        internal int MaxLabels = 100;
        internal int CachedCount => entries.Count;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Rect> occupied = new List<Rect>();
        private float nextScan;
        private GUIStyle label;
        private static readonly Vector2[] OutlineOffsets = { new Vector2(-1,0),new Vector2(1,0),new Vector2(0,-1),new Vector2(0,1) };
        private Minimap pinMap;
        
        internal Overlay(Plugin app) { this.app = app; }
        private static string Name(string raw) => Rules.Text(Localization.instance != null ? Localization.instance.Localize(raw) : raw, 55);
        internal void Tick()
        {
            if ((!Enabled && !AutoPin) || !Player.m_localPlayer || Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + app.Settings.Data.espRefresh;
            entries.Clear();
            var seen=new HashSet<int>();
            var center = Player.m_localPlayer.transform.position;
            void Add(Component obj, string name, Kind kind, string detail = null)
            {
                if (!obj || string.IsNullOrEmpty(name) || Hidden(kind,PrefabId(obj.gameObject)) || !WorldTools.Nearby(obj, center, Radius) || entries.Count >= 4000) return;
                if(!seen.Add(obj.gameObject.GetInstanceID()))return;
                entries.Add(new Entry { Object = obj, Label = Name(name), Detail = detail, Category = kind, Distance = Vector3.Distance(center, obj.transform.position) });
            }
            if (Enabled)
            {
                foreach (var c in Character.GetAllCharacters())
                {
                    if (!c || c == Player.m_localPlayer || c.IsDead()) continue;
                    var p = c as Player; Add(c, p ? p.GetPlayerName() : c.m_name, p ? Kind.Player : Kind.Monster);
                }
                if (Categories[(int)Kind.Pickable]) foreach (var p in WorldTools.Loaded<Pickable>()) Add(p, p.GetHoverName(), Kind.Pickable);
                if (Categories[(int)Kind.Drop]) foreach (var d in WorldTools.Loaded<ItemDrop>()) Add(d, d.m_itemData.m_shared.m_name + " x" + d.m_itemData.m_stack, Kind.Drop);
                float radius2 = Radius * Radius;
                if (Categories[(int)Kind.POI])
                    foreach (var l in WorldTools.Loaded<Location>())
                    {
                        if (!l || (l.transform.position - center).sqrMagnitude > radius2) continue;
                        Add(l, l.gameObject.name.Replace("(Clone)", "").Trim(), Kind.POI);
                    }
                if (Categories[(int)Kind.Tombstone])
                    foreach (var t in WorldTools.Loaded<TombStone>())
                    {
                        if (!t || (t.transform.position - center).sqrMagnitude > radius2) continue;
                        Add(t, t.GetHoverName(), Kind.Tombstone);
                    }
                if (Categories[(int)Kind.Container])
                    foreach (var chest in WorldTools.Loaded<Container>())
                    {
                        if (!chest || (chest.transform.position - center).sqrMagnitude > radius2) continue;
                        string label = ContainerLabel(chest);
                        if (label == null) continue;
                        Add(chest, label, Kind.Container, ContainerItems(chest));
                    }
            }
            if (AutoPin || Categories[(int)Kind.Deposit])
            {
                float radius2 = Radius * Radius;
                foreach (var rock in WorldTools.Loaded<MineRock>())
                {
                    if (!rock || (rock.transform.position - center).sqrMagnitude > radius2) continue;
                    if (IsDeposit(rock.name, rock.m_dropItems)) Add(rock, DepositName(rock.gameObject), Kind.Deposit);
                }
                foreach (var rock in WorldTools.Loaded<MineRock5>())
                {
                    if (!rock || (rock.transform.position - center).sqrMagnitude > radius2) continue;
                    if (IsDeposit(rock.name, rock.m_dropItems)) Add(rock, DepositName(rock.gameObject), Kind.Deposit);
                }
                // Tin is Destructible; scanning all DropOnDestroyed walked every tree and hitched.
                foreach (var d in WorldTools.Loaded<Destructible>())
                {
                    if (!d || (d.transform.position - center).sqrMagnitude > radius2 || !MightBeOre(d.name)) continue;
                    if (DepositObject(d.gameObject)) Add(d, DepositName(d.gameObject), Kind.Deposit);
                }
            }
            entries.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (AutoPin && Minimap.instance)
            {
                pinMap = Minimap.instance;
                var existing = app.World.Pins();
                int own = 0;
                for (int i = 0; i < existing.Count; i++)
                {
                    var pin = existing[i];
                    if (pin != null && pin.m_name != null && pin.m_name.StartsWith("[VAM] ", StringComparison.Ordinal)) own++;
                }
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e.Category != Kind.Deposit || !e.Object) continue;
                    if (own >= 500) break;
                    var position = e.Object.transform.position;
                    bool near = false;
                    for (int p = 0; p < existing.Count; p++)
                    {
                        if ((existing[p].m_pos - position).sqrMagnitude < 16) { near = true; break; }
                    }
                    if (near) continue;
                    var pin = pinMap.AddPin(position, Minimap.PinType.Icon3, "[VAM] " + e.Label, true, false);
                    existing.Add(pin); own++;
                }
            }
        }
        private string ContainerLabel(Container chest)
        {
            if (!chest || chest.GetComponent<TombStone>()) return null;
            string name = "";
            try { name = chest.GetHoverName(); } catch (Exception caught) { AppLog.Swallow(caught); name = ""; }
            if (string.IsNullOrWhiteSpace(name)) name = PrefabId(chest.gameObject);
            if (!app.Settings.Data.espContainerCount || app.Settings.Data.espContainerItems) return name;
            int stacks = 0;
            try
            {
                var inv = chest.GetInventory();
                if (inv != null) stacks = inv.NrOfItems();
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
            return name + "  " + stacks;
        }
        private string ContainerItems(Container chest)
        {
            if (!chest || !app.Settings.Data.espContainerItems) return null;
            Inventory inv = null;
            try { inv = chest.GetInventory(); } catch (Exception caught) { AppLog.Swallow(caught); return null; }
            if (inv == null) return null;
            System.Collections.Generic.List<ItemDrop.ItemData> items = null;
            try { items = inv.GetAllItems(); } catch (Exception caught) { AppLog.Swallow(caught); return null; }
            if (items == null || items.Count == 0) return "(empty)";
            var lines = new System.Text.StringBuilder();
            int shown = 0;
            int slots = ChestSlots(inv);
            int limit = Mathf.Clamp(app.Settings.Data.espContainerLines, 1, slots);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.m_shared == null) continue;
                if (shown >= limit)
                {
                    lines.Append('\n').Append("...");
                    break;
                }
                if (shown > 0) lines.Append('\n');
                lines.Append(Name(item.m_shared.m_name)).Append(" x").Append(item.m_stack);
                shown++;
            }
            return shown == 0 ? "(empty)" : lines.ToString();
        }
        private static int ChestSlots(Inventory inv)
        {
            int slots = 64;
            try
            {
                int w = inv.GetWidth();
                int h = inv.GetHeight();
                if (w > 0 && h > 0) slots = w * h;
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
            return Mathf.Clamp(slots, 1, 64);
        }
        private static bool MightBeOre(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("MineRock", StringComparison.Ordinal) >= 0) return true;
            if (name.IndexOf("mudpile", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("tin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("copper", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("silver", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("iron", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("flametal", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return name.IndexOf("obsidian", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static bool IsDeposit(string name, DropTable table)=>DepositResource(name,table)!=null;
        private static string DepositResource(string name,DropTable table)
        {
            if(table?.m_drops!=null)
            {
                foreach(var drop in table.m_drops)
                {
                    if(!drop.m_item || NaturalCatalogRules.OreItemLabel(PrefabId(drop.m_item))==null)continue;
                    var label=drop.m_item.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name;
                    return string.IsNullOrWhiteSpace(label)?NaturalCatalogRules.OreItemLabel(PrefabId(drop.m_item)):label;
                }
            }
            return NaturalCatalogRules.OreNodeLabel(name);
        }
        internal void RemovePins()
        {
            if (!Minimap.instance) return;
            var pins = app.World.Pins();
            int removed = 0;
            for (int i = pins.Count - 1; i >= 0; i--)
            {
                var pin = pins[i];
                if (pin == null || pin.m_name == null || !pin.m_name.StartsWith("[VAM] ", StringComparison.Ordinal)) continue;
                Minimap.instance.RemovePin(pin);
                removed++;
            }
            app.Report("Removed " + removed + " VAM deposit pins. Disable automatic pinning to keep them removed.");
        }
        internal static Rect LabelBounds(float x,float y,float width,float height,float spacing) => new Rect(x-width/2-spacing,y-height/2-spacing,width+spacing*2,height+spacing*2);
        internal void Draw()
        {
            var d=app.Settings.Data;
            if (!Enabled || entries.Count == 0 || !Player.m_localPlayer || Event.current.type != EventType.Repaint || (d.espHideInMenu && app.Open)) return;
            var camera=Camera.main; if(!camera)return;
            if(label==null)label=new GUIStyle { alignment=TextAnchor.MiddleCenter,richText=false,padding=new RectOffset() };
            label.font=GUI.skin.font; label.fontSize=d.espFont; label.normal.textColor=Color.white;
            occupied.Clear(); int drawn=0;
            Color prior=GUI.color, content=GUI.contentColor, background=GUI.backgroundColor;
            GUI.contentColor=GUI.backgroundColor=Color.white;
            float radius2 = Radius * Radius;
            Vector3 playerPos = Player.m_localPlayer.transform.position;
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if(!entry.Object || !Categories[(int)entry.Category] || Hidden(entry.Category,PrefabId(entry.Object.gameObject)))continue;
                    Vector3 world = entry.Object.transform.position;
                    float dist2 = (playerPos - world).sqrMagnitude;
                    if(dist2>radius2)continue;
                    float distance = Mathf.Sqrt(dist2);
                    Vector3 point=camera.WorldToScreenPoint(world+Vector3.up);
                    if(point.z<=0 || point.x<0 || point.x>Screen.width || point.y<0 || point.y>Screen.height)continue;
                    string text=entry.Label+(d.espDistance?"  "+distance.ToString("0")+"m":"");
                    if(!string.IsNullOrEmpty(entry.Detail)) text+="\n"+entry.Detail;
                    bool list=!string.IsNullOrEmpty(entry.Detail);
                    label.wordWrap=false;
                    label.alignment=list?TextAnchor.UpperCenter:TextAnchor.MiddleCenter;
                    float width=0f, height=0f;
                    var lines=text.Split('\n');
                    for(int line=0; line<lines.Length; line++)
                    {
                        var lineSize=label.CalcSize(new GUIContent(lines[line].Length==0?" ":lines[line]));
                        if(lineSize.x>width) width=lineSize.x;
                        height+=lineSize.y;
                    }
                    var size=new Vector2(width, height);
                    float anchorY=Screen.height-point.y+d.espOffset;
                    var rect=list
                        ? new Rect(point.x-(size.x+8f)/2f, anchorY, size.x+8f, size.y+2f)
                        : LabelBounds(point.x, anchorY, size.x+8, size.y+2, 0);
                    var bounds=LabelBounds(rect.center.x,rect.center.y,rect.width,rect.height,d.espSpacing);
                    if(d.espAvoidOverlap && OverlapsOccupied(bounds))continue;
                    occupied.Add(bounds);
                    if(d.espBackground) { GUI.color=new Color(.025f,.04f,.045f,d.espOpacity); GUI.DrawTexture(rect,Texture2D.whiteTexture); }
                    if(d.espOutline)
                    {
                        GUI.color=new Color(0,0,0,.85f);
                        for (int o = 0; o < OutlineOffsets.Length; o++)
                            GUI.Label(new Rect(rect.position+OutlineOffsets[o],rect.size),text,label);
                    }
                    GUI.color=d.colors[(int)entry.Category]; GUI.Label(rect,text,label);
                    if(++drawn>=MaxLabels)break;
                }
            }
            finally { GUI.color=prior; GUI.contentColor=content; GUI.backgroundColor=background; }
        }
        private bool OverlapsOccupied(Rect bounds)
        {
            for (int i = 0; i < occupied.Count; i++)
                if (occupied[i].Overlaps(bounds)) return true;
            return false;
        }
        internal void Clear() { entries.Clear(); occupied.Clear(); pinMap = null; nextScan = 0; }
    }
}
