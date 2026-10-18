using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace ValheimAdminMenu
{
    internal sealed class ClientPlayers : IDisposable
    {
        internal sealed class Member { internal long Uid; internal string Name; internal bool Visible; internal Vector3 Position; internal Player Player; }
        private readonly Plugin app;
        internal List<Member> Members = new List<Member>();
        private readonly HashSet<long> stamina = new HashSet<long>();
        private float nextScan, nextRefill;
        private ZNet session;
        internal ClientPlayers(Plugin app) { this.app = app; }
        internal void Tick()
        {
            if (session != ZNet.instance) { Dispose(); session = ZNet.instance; }
            if (!session || !Player.m_localPlayer) return;
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + 1;
                var info = session.GetPlayerList();
                Members = Character.GetAllCharacters().OfType<Player>().Where(p => p && p.GetComponent<ZNetView>()?.IsValid() == true)
                    .Select(p => new Member { Player = p, Uid = p.GetPlayerID(), Name = p.GetPlayerName(), Position = p.transform.position,
                        Visible = p == Player.m_localPlayer || info.Any(i => i.m_characterID == p.GetZDOID() && i.m_publicPosition) }).ToList();
                stamina.RemoveWhere(id => !Members.Any(m => m.Uid == id));
            }
            if (Time.realtimeSinceStartup < nextRefill) return;
            nextRefill = Time.realtimeSinceStartup + .3f;
            foreach (var member in Members.Where(m => stamina.Contains(m.Uid) && m.Player && !m.Player.IsDead()))
            {
                var view = member.Player.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                if (member.Player == Player.m_localPlayer) member.Player.AddStamina(member.Player.GetMaxStamina());
                else
                {
                    float current = view.GetZDO().GetFloat(ZDOVars.s_stamina, 100);
                    float deficit = Mathf.Max(0, member.Player.GetMaxStamina() - current);
                    if (deficit > 1) view.InvokeRPC("UseStamina", -deficit);
                }
            }
        }
        internal void Send(string action, long target, float amount = 0, Vector3 position = default, long destination = 0, string text = "", string alias = "")
        {
            var local = app.RequirePlayer();
            if (!Rules.Finite(amount) || !Rules.ValidPosition(position)) throw new ArgumentException("Invalid action values.");
            if (action == "event")
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "ShowMessage", (int)MessageHud.MessageType.Center, Rules.Text(text));
                app.Report("Center-screen message sent."); return;
            }
            if (action == "chat")
            {
                string message = Rules.Text(text), name = Rules.Text(alias, 48);
                var router = ZRoutedRpc.instance; var sender = local;
                Chat.CheckPermissionsAndSendChatMessageRPCsAsync((user, filter) =>
                {
                    if (!Player.m_localPlayer || Player.m_localPlayer != sender || ZRoutedRpc.instance != router) return;
                    Chat.GetChatMessageData(message, filter, out var userInfo, out var outgoing);
                    userInfo.Name = name;
                    ZRoutedRpc.instance.InvokeRoutedRPC(user, "ChatMessage", Player.m_localPlayer.GetHeadPoint(), (int)Talker.Type.Shout, userInfo, outgoing);
                });
                app.Report("Chat submitted with display name " + name + "."); return;
            }
            if (action == "teleport" && destination != 0)
            {
                var dest = Members.FirstOrDefault(m => m.Uid == destination && m.Visible && m.Player);
                if (dest == null) throw new InvalidOperationException("Destination must be loaded and sharing its minimap position.");
                position = dest.Player.transform.position + Vector3.up * 2;
            }
            int count = 0;
            foreach (var member in Members.Where(m => target == -1 || m.Uid == target).ToArray())
            {
                var p = member.Player;
                if (!p || p.IsDead()) continue;
                switch (action)
                {
                    case "heal": p.Heal(p.GetMaxHealth()); break;
                    case "power":
                        string power = local.GetGuardianPowerName();
                        if (string.IsNullOrEmpty(power)) throw new InvalidOperationException("Equip a Guardian Power on your character first.");
                        p.GetSEMan().AddStatusEffect(power.GetStableHashCode(), true); break;
                    case "teleport": if (p == local) app.Features.Teleport(position); else p.TeleportTo(position, p.transform.rotation, true); break;
                    case "stamina": if (amount > 0) stamina.Add(member.Uid); else stamina.Remove(member.Uid); break;
                    case "kill": if (p == local) app.Features.Kill(); else Hit(p, 1000000); break;
                    case "damage": Hit(p, Mathf.Clamp(amount, 0, 100000)); break;
                    default: throw new ArgumentException("Unknown player action.");
                }
                count++;
            }
            app.Report(action + ": requested for " + count + " loaded player(s). Remote results depend on game ownership and protections.");
        }
        private static void Hit(Player p, float amount)
        {
            var hit = new HitData { m_ignorePVP = true, m_dodgeable = false, m_blockable = false, m_point = p.transform.position };
            hit.m_damage.m_damage = amount; p.Damage(hit);
        }
        internal bool IsRefilling(long id) => stamina.Contains(id);
        public void Dispose() { Members.Clear(); stamina.Clear(); nextScan = nextRefill = 0; }
    }
}

