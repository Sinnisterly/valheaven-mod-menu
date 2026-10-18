using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ValheimAdminMenu
{
    internal static class NaturalCatalogRules
    {
        internal static string OreItemLabel(string id)
        {
            switch((id??"").Replace("(Clone)","").Replace("_","").Replace(" ","").Trim().ToLowerInvariant())
            {
                case "copperore": return "$item_copperore";
                case "tinore": return "$item_tinore";
                case "silverore": return "$item_silverore";
                case "ironscrap": case "ironore": return "$item_ironscrap";
                case "flametalore": return "$item_flametalore";
                case "flametalorenew": return "$item_flametalore_new";
                case "obsidian": return "$item_obsidian";
                default: return null;
            }
        }
        internal static string OreNodeLabel(string id)
        {
            // Token match only - "ore" in "forest" must not count.
            string text=Regex.Replace(id??"","([a-z])([A-Z])","$1_$2").ToLowerInvariant();
            var tokens=text.Split(new[]{'_','-',' ','(',')'},StringSplitOptions.RemoveEmptyEntries);
            foreach(var mineral in new[]{"copper","tin","silver","iron","flametal","obsidian"})
                if(tokens.Any(t=>t==mineral || t==mineral+"ore" || t==mineral+"vein" || t==mineral+"rock"))
                    return OreItemLabel(mineral=="iron"?"IronScrap":mineral=="obsidian"?"Obsidian":mineral+"Ore");
            return null;
        }
        internal static bool HarvestablePlant(string itemId)
        {
            string id=(itemId??"").ToLowerInvariant();
            return new[]{"raspberry","blueberr","cloudberr","blackberr","vineberr","pukeberr","mushroom","carrot","turnip","onion","barley","flax","dandelion","thistle","jotunpuff","magecap","fiddlehead"}.Any(p=>id.StartsWith(p,StringComparison.Ordinal));
        }
        internal static bool DecorativePlant(string prefabId)
        {
            string id=Regex.Replace(prefabId??"","([a-z])([A-Z])","$1_$2").ToLowerInvariant();
            if(id.StartsWith("vfx_",StringComparison.Ordinal) || id.StartsWith("sfx_",StringComparison.Ordinal) || id.StartsWith("fx_",StringComparison.Ordinal)
                || id.Contains("_stub") || id.Contains("_log") || id.Contains("_fallen") || id.Contains("_destroy") || id.Contains("_debris"))return false;
            var tokens=id.Split('_');
            return new[]{"bush","shrub","fern","heath","vine","grass","reed","sapling"}.Any(p=>tokens.Any(t=>t.StartsWith(p,StringComparison.Ordinal)))
                || id.StartsWith("beech_small",StringComparison.Ordinal) || id.StartsWith("fir_tree_small",StringComparison.Ordinal) || id.StartsWith("firtree_small",StringComparison.Ordinal)
                || id.StartsWith("yggdrasil_shoot",StringComparison.Ordinal) || id.StartsWith("yggdrasilshoot",StringComparison.Ordinal);
        }
        internal static string PlantGroup(bool plant,bool growsTree,bool tree,bool harvestable,bool decorative)
        {
            if(tree || (plant && growsTree))return "Trees";
            if(plant)return "Crops";
            if(harvestable)return "Harvestable plants";
            return decorative?"Decorative plants":null;
        }
        internal static string FriendlyPrefabName(string id)
        {
            string text=Regex.Replace(id??"","([a-z])([A-Z])","$1 $2").Replace('_',' ');
            text=Regex.Replace(text,"([A-Za-z])([0-9])","$1 $2");
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.Trim());
        }
    }
}
