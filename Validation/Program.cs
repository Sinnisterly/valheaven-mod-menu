using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using ValheimAdminMenu;

internal static class Program
{
    private static int count;
    private static int Main(string[] args)
    {
        string game = args.Length == 0 ? @"D:\SteamLibrary\steamapps\common\Valheim" : args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (var folder in new[] { Path.Combine(game, "valheim_Data", "Managed"), Path.Combine(game, "BepInEx", "core") })
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        try { Run(); System.Console.WriteLine("PASS: " + count + " checks. No game scene or world was opened."); return 0; }
        catch (Exception ex) { System.Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception("FAIL: " + message);
        count++; System.Console.WriteLine("PASS: " + message);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        ValheimAdminMenu.Reflection.Check();
        Check(true, "All private runtime signatures used by compatibility check resolve");
        Check(Rules.ValidPosition(new Vector3(0, 100, 0)), "Ordinary teleport destination accepted");
        Check(Rules.ValidPosition(new Vector3(-20000, 10000, 20000)), "Coordinate boundary accepted");
        foreach (var p in new[] { new Vector3(float.NaN,0,0), new Vector3(0,float.PositiveInfinity,0), new Vector3(0,0,float.NegativeInfinity), new Vector3(20001,0,0), new Vector3(0,10001,0), new Vector3(0,0,-20001) })
            Check(!Rules.ValidPosition(p), "Invalid or out-of-range coordinate rejected: " + p);
        Check(Rules.Text("<b>test</b>\nnext") == "[b]test[/b] next", "User text cannot inject rich-text markup or new lines");
        Check(Rules.Text(new string('x', 500)).Length == 240, "Broadcast text length bounded");
        Check(Rules.Text(null) == "", "Null message handled");
        Check(Rules.ValidHotkey(KeyCode.F8), "F8 is a valid menu binding");
        Check(Rules.ValidHotkey(KeyCode.Insert), "Insert is a valid menu binding");
        foreach(var key in new[]{ KeyCode.Escape,KeyCode.None,KeyCode.Mouse0,KeyCode.Mouse1,(KeyCode)99999 })
            Check(!Rules.ValidHotkey(key),"Invalid or reserved binding rejected: "+key);
        var settings=new SettingsData { hotkey=(int)KeyCode.Escape, scale=float.NaN, espFont=400, espMax=-1, espOpacity=float.PositiveInfinity,
            espRadius=900, espRefresh=0, categories=null,colors=null, worldRadius=-1,brushRadius=500,interactionDistance=999 };
        settings.Validate();
        Check(settings.hotkey==(int)KeyCode.F8,"Invalid saved hotkey recovers to F8");
        Check(settings.scale==1,"Invalid saved scale recovers to default");
        Check(settings.espFont==24 && settings.espMax==10,"Saved label limits are bounded");
        Check(settings.espOpacity==.3f && settings.espRadius==500 && settings.espRefresh==.25f,"Saved ESP values are finite and bounded");
        Check(settings.categories.Length==5 && settings.colors.Length==5,"Missing category settings recover to complete defaults");
        Check(settings.worldRadius==1 && settings.brushRadius==20 && settings.interactionDistance==50,"Saved gameplay ranges are bounded");
        Check(CatalogRules.IconIndex(0,0)==-1,"Missing icons do not cause an array access");
        Check(CatalogRules.IconIndex(2,9)==1,"Oversized item variant uses a valid icon index");
        Check(CatalogRules.IconIndex(2,-3)==0,"Negative item variant uses a valid icon index");
        Check(CatalogRules.Matches("SwordIron","Iron sword"," SWORD "),"Catalog search ignores case and surrounding spaces");
        Check(CatalogRules.Matches("SwordIron","Iron sword","SwordIron"),"Catalog matches prefab ID");
        Check(!CatalogRules.Matches("SwordIron","Iron sword","wood"),"Catalog excludes unrelated search results");
        var skillEntry=new Skills.Skill(new Skills.SkillDef { m_skill=Skills.SkillType.Swords });
        skillEntry.m_level=87.75f; skillEntry.m_accumulator=9;
        PlayerFeatures.SetBaseSkill(skillEntry,25.5f);
        Check(skillEntry.m_level==25.5f && skillEntry.m_accumulator==0,"Exact skill edit lowers fractional base level and clears progress");
        PlayerFeatures.SetBaseSkill(skillEntry,72);
        Check(skillEntry.m_level==72,"Exact skill edit raises to requested level without maximizing");
        foreach(float invalid in new[]{float.NaN,-1f,101f})
        {
            bool rejected=false;
            try { PlayerFeatures.SetBaseSkill(skillEntry,invalid); } catch(ArgumentException) { rejected=true; }
            Check(rejected && skillEntry.m_level==72,"Invalid skill edit rejected without changing level: "+invalid);
        }
        Check(ExpansionFeatures.TrainingFactor(100,0,1,1,1)==1,"Fast training respects the level cap");
        Check(ExpansionFeatures.TrainingFactor(20,0,1,0,1)==1,"Fast training preserves disabled XP gain");
        Check(new ItemEdits().Valid(),"Default item overrides are valid");
        Check(!new ItemEdits{weight=float.NaN}.Valid(),"Item editor rejects non-finite values");
        Check(!new ItemEdits{damage=new HitData.DamageTypes{m_slash=-1}}.Valid(),"Item editor rejects negative damage");
        var rect=Overlay.LabelBounds(100,100,60,12,3);
        Check(rect.x==67 && rect.y==91 && rect.width==66 && rect.height==18,"Compact label bounds fit text plus spacing");
        Check(!new SettingsData().espBackground && new SettingsData().espFont==11,"Default ESP uses small text without fixed background bars");
        var pluginAssembly=typeof(Plugin).Assembly;
        Check(!pluginAssembly.GetTypes().Any(t=>t.Name=="PeerRegistrationPatch"),"Custom server handshake patch removed");
        Check(typeof(Plugin).GetMethod("RequireAdmin",BindingFlags.Instance|BindingFlags.NonPublic)==null,"World administrator gate removed");
        Check(typeof(Plugin).GetCustomAttributesData().All(a=>a.AttributeType.Name!="BepInDependency"),"No companion plugin dependency added");
        // Resolve TargetMethods only; do not Apply patches or touch Unity.
        var assembly = typeof(Plugin).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            var targetMethods = type.GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethods != null)
            {
                var methods = (System.Collections.Generic.IEnumerable<MethodBase>)targetMethods.Invoke(null, null);
                foreach (var method in methods) Check(method != null, "Dynamic Harmony target: " + method.DeclaringType.Name + "." + method.Name);
            }
            foreach (var patch in type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Cast<HarmonyLib.HarmonyPatch>())
            {
                if (patch.info.declaringType != null && patch.info.methodName != null)
                    Check(patch.info.declaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Any(m => m.Name == patch.info.methodName), "Harmony target: " + patch.info.declaringType.Name + "." + patch.info.methodName);
            }
        }
    }
}

