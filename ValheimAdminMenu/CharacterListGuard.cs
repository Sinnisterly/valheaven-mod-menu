using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>
    /// Drops destroyed entries from Character.s_characters. A Character whose Awake fails halfway never removes itself,
    /// and vanilla loops (placement ghost checks, AnimalAI, MonsterAI) then throw every frame: no placing, and
    /// MonoUpdaters skips every character's physics (stuck "falling", AI frozen).
    /// </summary>
    internal static class CharacterListGuard
    {
        private const float Interval = 1f;
        private static float nextAt;
        private static bool reported;

        internal static void Tick()
        {
            if (Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + Interval;
            List<Character> list;
            try { list = Reflection.GetStatic<List<Character>>(typeof(Character), "s_characters"); }
            catch (Exception caught) { AppLog.Swallow(caught); return; }
            if (list == null) return;
            int removed = list.RemoveAll(c => !c);
            if (removed <= 0) return;
            if (!reported)
            {
                reported = true;
                AppLog.Warning("Guard", "Removed " + removed + " destroyed character(s) from the game's character list. Further purges log at Debug.");
            }
            else AppLog.Debug("Guard", "Removed " + removed + " destroyed character(s) from Character.s_characters");
        }
    }
}
