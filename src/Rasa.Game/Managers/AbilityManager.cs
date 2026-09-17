using System;
using System.Linq;
using System.Numerics;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.MapChannel.Client;
    using Packets.MapChannel.Server;
    using Structures;

    internal sealed class AbilityManager
    {
        private readonly AbilityLoadoutManager _loadout;
        private readonly Func<long> _clock;
        private readonly Func<int, int, int> _roll;

        internal AbilityManager(AbilityLoadoutManager loadout, Func<long> clock, Func<int, int, int> roll)
        {
            _loadout = loadout;
            _clock = clock;
            _roll = roll ?? Random.Shared.Next;
        }

        internal bool Request(Client client, RequestPerformAbilityPacket packet)
        {
            if (client == null || packet == null)
                return Fail(client, packet?.ActionId ?? 0, packet?.ActionArgId ?? 0, "Missing ability request or client.");
            lock (client.SyncRoot)
            {
                if (!CanAct(client) || packet.ItemId != 0)
                    return Fail(client, packet.ActionId, packet.ActionArgId, "Ability requires an active living character and no item.");
                if (!_loadout.IsLearned(client.Player, (int)packet.ActionId, packet.ActionArgId))
                    return Fail(client, packet.ActionId, packet.ActionArgId, "Ability/rank does not match an authoritative learned skill.");
                if (packet.ActionId == ActionId.AaRecruitSprint)
                {
                    if (packet.Target != 0 && packet.Target != client.Player.EntityId)
                        return Fail(client, packet.ActionId, packet.ActionArgId, "Sprint only targets its owner.");
                    if (client.Player.Sprint != null)
                    {
                        GameEffectManager.Instance.CancelSprint(client);
                        return true;
                    }
                    if (client.Player.PendingAbility != null || client.Player.PendingWeaponAction != null ||
                        client.Player.CurrentAction != 0 || !client.Player.Attributes.TryGetValue(Attributes.Chi, out var chi) ||
                        chi.Current <= 0 || chi.CurrentMax <= 0 || chi.Current > chi.CurrentMax)
                        return Fail(client, packet.ActionId, packet.ActionArgId, "Sprint requires an idle actor and positive adrenaline.");
                    var rank = packet.ActionArgId;
                    GameEffectManager.Instance.StartSprint(client, rank, _clock,
                        () => _loadout.IsLearned(client.Player, (int)ActionId.AaRecruitSprint, rank));
                    return true;
                }
                if (packet.ActionId != ActionId.AaRecruitLightning)
                    return Fail(client, packet.ActionId, packet.ActionArgId, "Ability execution is not supported.");
                if (packet.ActionArgId > 2)
                    return Fail(client, packet.ActionId, packet.ActionArgId,
                        "Lightning ranks 3-5 are blocked: the supplied arc layout does not establish Sonic/stun/storm contracts.");
                var player = client.Player;
                if (player.PendingAbility != null || player.PendingWeaponAction != null || player.CurrentAction != 0 ||
                    _clock() < player.NextLightningTime)
                    return Fail(client, packet.ActionId, packet.ActionArgId, "Actor is busy or Lightning is cooling down.");
                var data = HistoricalAbilities.Lightning(packet.ActionArgId);
                if (!HasPower(player, data.Power) || !TryTarget(player, packet.Target, out var target))
                    return Fail(client, packet.ActionId, packet.ActionArgId, "Insufficient Power or invalid hostile target/range.");

                var now = _clock();
                var action = new ActionData(player, packet.ActionId, (uint)packet.ActionArgId,
                    packet.Target, HistoricalAbilities.LightningActivationMilliseconds);
                var pending = new PendingAbility(client, player, player.MapChannel, target, action,
                    packet.ActionId, packet.ActionArgId, now + HistoricalAbilities.LightningActivationMilliseconds);
                action.Ability = pending;
                player.PendingAbility = pending;
                player.NextLightningTime = now + HistoricalAbilities.LightningCooldownMilliseconds;
                player.MapChannel.PerformRecovery.Add(action);
                CellManager.Instance.CellCallMethod(player.MapChannel, player,
                    new PerformWindupPacket(PerformType.ThreeArgs, packet.ActionId, (uint)packet.ActionArgId, packet.Target));
                client.CallMethod(player.EntityId, new ActionReuseTimerRestartedPacket(packet.ActionId, packet.ActionArgId));
                return true;
            }
        }

        internal void Recover(MapChannel map, ActionData action)
        {
            var pending = action?.Ability;
            if (pending == null || !ReferenceEquals(pending.Map, map) || pending.Completed)
                return;
            lock (pending.Client.SyncRoot)
            {
                if (pending.Completed)
                    return;
                if (!ReferenceEquals(pending.Client.Player, pending.Player) ||
                    pending.Player.ActionLifetime != pending.PlayerLifetime ||
                    pending.Target.ActionLifetime != pending.TargetLifetime ||
                    !ReferenceEquals(pending.Player.PendingAbility, pending) ||
                    !ReferenceEquals(pending.Player.MapChannel, map) || !map.PerformRecovery.Contains(action) ||
                    !ReferenceEquals(action.Actor, pending.Player) || action.ActionId != pending.Id ||
                    action.ActionArgId != pending.Rank || action.TargetId != pending.Target.EntityId ||
                    action.IsInrerrupted || pending.Player.CurrentAction != 0 ||
                    pending.Player.PendingWeaponAction != null || !CanAct(pending.Client) ||
                    !_loadout.IsLearned(pending.Player, (int)pending.Id, pending.Rank) ||
                    !TryTarget(pending.Player, action.TargetId, out var target) || !ReferenceEquals(target, pending.Target))
                {
                    Finish(pending);
                    Fail(pending.Client, pending.Id, pending.Rank,
                        "Ability recovery was interrupted or its actor/target lifetime changed.", pending.Player);
                    return;
                }
                if (_clock() < pending.ReadyAt)
                    return;
                Finish(pending);
                var data = HistoricalAbilities.Lightning(pending.Rank);
                if (!HasPower(pending.Player, data.Power))
                {
                    Fail(pending.Client, pending.Id, pending.Rank, "Insufficient current Power at recovery.");
                    return;
                }
                var damage = _roll(data.MinimumDamage, data.MaximumDamage + 1);
                if (damage < data.MinimumDamage || damage > data.MaximumDamage)
                    throw new InvalidOperationException("Lightning random source returned an out-of-range roll.");
                var bonus = ManifestationManager.GetLogosBonusPercent(pending.Player.Level,
                    pending.Player.Attributes[Attributes.Mind].CurrentMax);
                damage = ApplyMindBonus(damage, bonus);
                LightningArc arc = null;
                if (pending.Rank == 2)
                {
                    var secondary = map.MapCellInfo.Cells.Values.SelectMany(cell => cell.CreatureList).Distinct()
                        .Where(candidate => IsLightningArcTarget(pending.Player, pending.Target, candidate, data.ArcRadius))
                        .OrderBy(candidate => Vector3.DistanceSquared(pending.Target.Position, candidate.Position))
                        .ThenBy(candidate => candidate.EntityId).FirstOrDefault();
                    if (secondary != null)
                        arc = new LightningArc(secondary, ApplyMindBonus(data.ArcDamage, bonus));
                }
                pending.Player.Attributes[Attributes.Power].Current -= data.Power;
                pending.Client.CallMethod(pending.Player.EntityId,
                    new UpdatePowerPacket(pending.Player.Attributes[Attributes.Power], 0));
                MissileManager.Instance.MissileLaunch(map, action, damage, arc);
            }
        }

        private static int ApplyMindBonus(int damage, double bonus) =>
            checked((int)decimal.Floor(damage * (1m + (decimal)bonus / 100m)));

        internal static bool CanAct(Client client) => ManifestationManager.CanUseWeapons(client) &&
            client.Player.Level >= 1 && client.Player.Level <= ManifestationManager.MaxPlayerLevel &&
            client.Player.State != CharacterState.Stunned && client.Player.State != CharacterState.Uncontrolled &&
            client.Player.Attributes.TryGetValue(Attributes.Health, out var health) && health.Current > 0;

        private static bool HasPower(Manifestation player, int cost) =>
            player.Attributes.TryGetValue(Attributes.Power, out var power) && power.Current >= cost &&
            power.CurrentMax >= cost && power.Current <= power.CurrentMax &&
            player.Attributes.ContainsKey(Attributes.Mind);

        private static bool TryTarget(Manifestation player, ulong id, out Actor target)
        {
            target = null;
            return id != 0 && !player.GmFlagAlwaysFriendly &&
                TryHostileTarget(player.MapChannel, id, out target) &&
                IsWithinRange(player.Position, target.Position, HistoricalAbilities.LightningRange);
        }

        private static bool TryHostileTarget(MapChannel map, ulong id, out Actor target) =>
            MissileManager.TryGetTarget(map, id, out target) &&
            target is Creature creature && creature.Faction == Factions.Bane &&
            creature.Attributes.TryGetValue(Attributes.Health, out var health) && health.Current > 0 &&
            creature.Attributes.TryGetValue(Attributes.Armor, out var armor) && armor.Current >= 0;

        internal static bool IsLightningArcTarget(Manifestation player, Actor primary, Creature secondary, int radius) =>
            secondary != null && secondary.EntityId != primary.EntityId && !player.GmFlagAlwaysFriendly &&
            TryHostileTarget(player.MapChannel, secondary.EntityId, out var registered) &&
            ReferenceEquals(secondary, registered) && IsWithinRange(primary.Position, secondary.Position, radius);

        private static bool IsWithinRange(Vector3 origin, Vector3 destination, int range)
        {
            var distance = Vector3.DistanceSquared(origin, destination);
            return float.IsFinite(distance) && distance <= range * range;
        }

        private static void Finish(PendingAbility pending)
        {
            pending.Completed = true;
            pending.Map.PerformRecovery.Remove(pending.Action);
            if (ReferenceEquals(pending.Player.PendingAbility, pending))
                pending.Player.PendingAbility = null;
        }

        internal static void CancelPending(Client client)
        {
            if (client?.Player == null)
                return;
            lock (client.SyncRoot)
                if (client.Player.PendingAbility is { } pending)
                    Finish(pending);
        }

        private static bool Fail(Client client, ActionId id, int rank, string reason, Manifestation expectedPlayer = null)
        {
            Logger.WriteLog(LogType.Network, $"Rejected ability {id}/{rank}: {reason}");
            if (client?.Player != null && client.State != ClientState.Disconnected && (int)id >= 0 && rank >= 0 &&
                (expectedPlayer == null || ReferenceEquals(client.Player, expectedPlayer)))
                client.CallMethod(client.Player.EntityId, new ActionFailedPacket(id, rank));
            return false;
        }
    }
}
