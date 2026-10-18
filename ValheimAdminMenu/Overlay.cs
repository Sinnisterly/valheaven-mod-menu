using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal sealed class Overlay
    {
        internal enum Kind { Player, Monster, Pickable, Deposit, Drop }
        internal sealed class Entry { internal Component Object; internal string Label; internal Kind Category; internal float Distance; }
        private readonly Plugin app;
        internal bool Enabled, AutoPin;
        internal bool[] Categories = { true, true, false, true, false };
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
            var center = Player.m_localPlayer.transform.position;
            void Add(Component obj, string name, Kind kind)
            {
                if (!WorldTools.Nearby(obj, center, Radius) || entries.Count >= 4000) return;
                entries.Add(new Entry { Object = obj, Label = Name(name), Category = kind, Distance = Vector3.Distance(center, obj.transform.position) });
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
            }
            if (AutoPin || Categories[(int)Kind.Deposit])
            {
                foreach (var rock in WorldTools.Loaded<MineRock>()) if (IsDeposit(rock.name, rock.m_dropItems)) Add(rock, rock.m_name, Kind.Deposit);
                foreach (var rock in WorldTools.Loaded<MineRock5>()) if (IsDeposit(rock.name, rock.m_dropItems)) Add(rock, rock.m_name, Kind.Deposit);
            }
            entries.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (AutoPin && Minimap.instance)
            {
                pinMap = Minimap.instance;
                var existing = app.World.Pins();
                int own = existing.Count(p => p.m_name.StartsWith("[VAM] ", StringComparison.Ordinal));
                foreach (var e in entries.Where(e => e.Category == Kind.Deposit && e.Object))
                {
                    if (own >= 500) break;
                    var position = e.Object.transform.position;
                    if (existing.Any(p => (p.m_pos - position).sqrMagnitude < 16)) continue;
                    var pin = pinMap.AddPin(position, Minimap.PinType.Icon3, "[VAM] " + e.Label, true, false);
                    existing.Add(pin); own++;
                }
            }
        }
        private static bool IsDeposit(string name, DropTable table)
        {
            bool Match(string text)
            {
                text = text.ToLowerInvariant();
                return new[] { "copper", "tin", "silver", "iron", "flametal", "obsidian", "ore", "deposit" }.Any(text.Contains);
            }
            return Match(name) || (table?.m_drops != null && table.m_drops.Any(d => d.m_item && Match(d.m_item.name)));
        }
        internal void RemovePins()
        {
            if (!Minimap.instance) return;
            var pins = app.World.Pins().Where(p => p.m_name.StartsWith("[VAM] ", StringComparison.Ordinal)).ToArray();
            foreach (var pin in pins) Minimap.instance.RemovePin(pin);
            app.Report("Removed " + pins.Length + " VAM deposit pins. Disable automatic pinning to keep them removed.");
        }
        internal static Rect LabelBounds(float x,float y,float width,float height,float spacing) => new Rect(x-width/2-spacing,y-height/2-spacing,width+spacing*2,height+spacing*2);
        internal void Draw()
        {
            var d=app.Settings.Data;
            if (!Enabled || !Player.m_localPlayer || Event.current.type != EventType.Repaint || (d.espHideInMenu && app.Open)) return;
            var camera=Camera.main; if(!camera)return;
            if(label==null)label=new GUIStyle { alignment=TextAnchor.MiddleCenter,richText=false,padding=new RectOffset() };
            label.font=GUI.skin.font; label.fontSize=d.espFont; label.normal.textColor=Color.white;
            occupied.Clear(); int drawn=0;
            Color prior=GUI.color, content=GUI.contentColor, background=GUI.backgroundColor;
            GUI.contentColor=GUI.backgroundColor=Color.white;
            try
            {
                foreach(var entry in entries)
                {
                    if(!entry.Object || !Categories[(int)entry.Category])continue;
                    float distance=Vector3.Distance(Player.m_localPlayer.transform.position,entry.Object.transform.position);
                    if(distance>Radius)continue;
                    Vector3 point=camera.WorldToScreenPoint(entry.Object.transform.position+Vector3.up);
                    if(point.z<=0 || point.x<0 || point.x>Screen.width || point.y<0 || point.y>Screen.height)continue;
                    string text=entry.Label+(d.espDistance?"  "+distance.ToString("0")+"m":"");
                    var size=label.CalcSize(new GUIContent(text));
                    var rect=LabelBounds(point.x,Screen.height-point.y+d.espOffset,size.x+8,size.y+2,0);
                    var bounds=LabelBounds(rect.center.x,rect.center.y,rect.width,rect.height,d.espSpacing);
                    if(d.espAvoidOverlap && occupied.Any(r=>r.Overlaps(bounds)))continue;
                    occupied.Add(bounds);
                    if(d.espBackground) { GUI.color=new Color(.025f,.04f,.045f,d.espOpacity); GUI.DrawTexture(rect,Texture2D.whiteTexture); }
                    if(d.espOutline)
                    {
                        GUI.color=new Color(0,0,0,.85f);
                        foreach(var offset in OutlineOffsets)
                            GUI.Label(new Rect(rect.position+offset,rect.size),text,label);
                    }
                    GUI.color=d.colors[(int)entry.Category]; GUI.Label(rect,text,label);
                    if(++drawn>=MaxLabels)break;
                }
            }
            finally { GUI.color=prior; GUI.contentColor=content; GUI.backgroundColor=background; }
        }
        internal void Clear() { entries.Clear(); occupied.Clear(); pinMap = null; nextScan = 0; }
    }
}
