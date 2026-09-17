using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.MapChannel.Client;
    using Packets.MapChannel.Server;
    using Repositories.Char;
    using Repositories.UnitOfWork;
    using Structures;

    internal sealed class AbilityLoadoutManager
    {
        // InfiniteRasa/Game-Server manifestation.h [5*5] and SendAbilityDrawerFull [0..24].
        internal const int SlotCount = 25;
        private readonly ManifestationManager _manifestation;
        private readonly IGameUnitOfWorkFactory _factory;

        internal AbilityLoadoutManager(ManifestationManager manifestation, IGameUnitOfWorkFactory factory)
        {
            _manifestation = manifestation;
            _factory = factory;
        }

        internal bool IsLearned(Manifestation player, int abilityId, long rank)
        {
            if (player == null || abilityId <= 0 || rank < 1 || rank > 5)
                return false;
            var index = Array.IndexOf(_manifestation.SkillIdx2AbilityId, abilityId);
            if (index < 0)
                return false;
            var id = (SkillId)_manifestation.SkillIById[index];
            return player.Skills.TryGetValue(id, out var skill) && IsValidSkill(id, skill) &&
                rank <= skill.SkillLevel;
        }

        internal static bool IsSlot(int slot) => slot >= 0 && slot < SlotCount;

        internal bool Validate(Manifestation player)
        {
            if (player == null || player.Level < 1 || player.Level > ManifestationManager.MaxPlayerLevel ||
                player.Skills == null || player.Abilities == null || !IsSlot(player.CurrentAbilityDrawer) ||
                player.Skills.Any(entry => !IsValidSkill(entry.Key, entry.Value)) ||
                player.Skills.Values.Sum(skill => _manifestation.requiredSkillLevelPoints[skill.SkillLevel]) >
                    ManifestationManager.GetSkillPointsForLevel(player.Level) ||
                player.Abilities.Any(entry => !IsSlot(entry.Key) || entry.Value == null ||
                    entry.Value.AbilitySlotId != entry.Key ||
                    !IsLearned(player, entry.Value.AbilityId, entry.Value.AbilityLevel)))
                return Reject("Invalid restored learned state, budget, drawer or selection; character data needs repair.");
            return true;
        }

        internal bool Set(Client client, RequestSetAbilitySlotPacket packet)
        {
            if (client == null)
                return Reject("Drawer set has no client.");
            lock (client.SyncRoot)
            {
                if (!ManifestationManager.CanUseWeapons(client) || packet == null || !IsSlot(packet.SlotId) ||
                    !Validate(client.Player))
                    return Reject("Invalid drawer slot or character state.");
                var clear = (!packet.AbilityId.HasValue && !packet.AbilityLevel.HasValue) ||
                    (packet.AbilityId == 0 && packet.AbilityLevel == 0);
                if (!clear && (!packet.AbilityId.HasValue || !packet.AbilityLevel.HasValue ||
                    packet.AbilityId < 1 || packet.AbilityId > int.MaxValue ||
                    !IsLearned(client.Player, (int)packet.AbilityId.Value, packet.AbilityLevel.Value)))
                    return Reject("Drawer assignment is not a learned ability/rank or a complete clear.");
                var proposed = CopyDrawer(client.Player);
                if (clear)
                    proposed.Remove(packet.SlotId);
                else
                    proposed[packet.SlotId] = new AbilityDrawerData(packet.SlotId,
                        (int)packet.AbilityId.Value, (uint)packet.AbilityLevel.Value);
                return SaveDrawer(client, proposed, packet.SlotId);
            }
        }

        internal bool Swap(Client client, RequestSwapAbilitySlotsPacket packet)
        {
            if (client == null)
                return Reject("Drawer swap has no client.");
            lock (client.SyncRoot)
            {
                if (!ManifestationManager.CanUseWeapons(client) || packet == null ||
                    !IsSlot(packet.FromSlot) || !IsSlot(packet.ToSlot) || !Validate(client.Player))
                    return Reject("Invalid drawer swap or character state.");
                var proposed = CopyDrawer(client.Player);
                proposed.TryGetValue(packet.FromSlot, out var from);
                proposed.TryGetValue(packet.ToSlot, out var to);
                if (from != null && !IsLearned(client.Player, from.AbilityId, from.AbilityLevel) ||
                    to != null && !IsLearned(client.Player, to.AbilityId, to.AbilityLevel))
                    return Reject("Drawer swap contains an unlearned ability/rank.");
                proposed.Remove(packet.FromSlot);
                proposed.Remove(packet.ToSlot);
                if (from != null)
                    proposed[packet.ToSlot] = new AbilityDrawerData(packet.ToSlot, from.AbilityId, from.AbilityLevel);
                if (to != null)
                    proposed[packet.FromSlot] = new AbilityDrawerData(packet.FromSlot, to.AbilityId, to.AbilityLevel);
                return SaveDrawer(client, proposed, new[] { packet.FromSlot, packet.ToSlot }.Distinct().ToArray());
            }
        }

        internal bool Select(Client client, int slot)
        {
            if (client == null)
                return Reject("Drawer selection has no client.");
            lock (client.SyncRoot)
            {
                if (!ManifestationManager.CanUseWeapons(client) || !IsSlot(slot) || !Validate(client.Player) ||
                    (client.Player.Abilities.TryGetValue(slot, out var ability) &&
                        !IsLearned(client.Player, ability.AbilityId, ability.AbilityLevel)))
                    return Reject("Invalid drawer selection or unlearned ability.");
                if (!Save(client, unit =>
                {
                    RequireLearnedState(unit, client);
                    RequireDrawerState(unit, client);
                    unit.Characters.UpdateCharacterAbilitySlot(client.Player.Id, (byte)slot);
                }))
                    return false;
                client.Player.CurrentAbilityDrawer = slot;
                client.CallMethod(client.Player.EntityId, new AbilityDrawerSlotPacket(slot));
                return true;
            }
        }

        internal void Publish(Client client)
        {
            if (!Validate(client?.Player))
                return;
            client.CallMethod(client.Player.EntityId, new AbilityDrawerPacket(client.Player.Abilities));
            client.CallMethod(client.Player.EntityId, new AbilityDrawerSlotPacket(client.Player.CurrentAbilityDrawer));
        }

        private static Dictionary<int, AbilityDrawerData> CopyDrawer(Manifestation player) =>
            player.Abilities.ToDictionary(entry => entry.Key,
                entry => new AbilityDrawerData(entry.Key, entry.Value.AbilityId, entry.Value.AbilityLevel));

        private bool SaveDrawer(Client client, Dictionary<int, AbilityDrawerData> proposed, params int[] slots)
        {
            if (!Save(client, unit =>
            {
                RequireLearnedState(unit, client);
                RequireDrawerState(unit, client);
                foreach (var slot in slots)
                {
                    proposed.TryGetValue(slot, out var ability);
                    unit.CharacterAbilityDrawers.AddOrUpdate(client.Player.Id, slot,
                        ability?.AbilityId ?? 0, ability?.AbilityLevel ?? 0);
                }
            }))
                return false;
            client.Player.Abilities = proposed;
            client.CallMethod(client.Player.EntityId, new AbilityDrawerPacket(proposed));
            return true;
        }

        private static void RequireDrawerState(ICharUnitOfWork unit, Client client)
        {
            var player = client.Player;
            var saved = unit.CharacterAbilityDrawers.GetCharacterAbilities(player.Id)
                .Where(row => row.AbilityId != 0).ToList();
            if (unit.Characters.Get(player.Id).CurrentAbilitySlot != player.CurrentAbilityDrawer ||
                saved.Count != player.Abilities.Count || saved.Any(row =>
                    !player.Abilities.TryGetValue(row.AbilitySlot, out var ability) ||
                    row.AbilityId != ability.AbilityId || row.AbilityLevel != ability.AbilityLevel))
                throw new GameplayRejectionException("Persisted drawer state changed; reload the character.");
        }

        internal bool Train(Client client, LevelSkillsPacket packet)
        {
            if (client == null)
                return Reject("Training has no client.");
            lock (client.SyncRoot)
            {
                if (!ManifestationManager.CanUseWeapons(client) || !_manifestation.ValidateProgressionForClient(client))
                    return Reject("Training requires an active living character.");
                if (packet?.SkillIds == null || packet.SkillLevels == null || packet.ListLenght < 1 ||
                    packet.ListLenght > _manifestation.SkillIById.Length ||
                    packet.SkillIds.Length != packet.ListLenght || packet.SkillLevels.Length != packet.ListLenght)
                    return Reject("Malformed training batch.");
                var player = client.Player;
                if (!Validate(player))
                    return Reject("Invalid authoritative learned state.");
                var proposed = new Dictionary<SkillId, SkillsData>();
                var cost = 0;
                for (var i = 0; i < packet.ListLenght; i++)
                {
                    var index = _manifestation.GetSkillIndexById(packet.SkillIds[i]);
                    var id = (SkillId)packet.SkillIds[i];
                    var rank = packet.SkillLevels[i];
                    var previous = player.Skills.TryGetValue(id, out var skill) ? skill.SkillLevel : 0;
                    if (index < 0 || rank < 1 || rank > 5 || rank < previous || proposed.ContainsKey(id))
                        return Reject("Unknown/duplicate skill or invalid training rank.");
                    cost += _manifestation.requiredSkillLevelPoints[rank] - _manifestation.requiredSkillLevelPoints[previous];
                    proposed.Add(id, new SkillsData(id, _manifestation.SkillIdx2AbilityId[index], rank));
                }
                if (cost > _manifestation.GetSkillPointsAvailable(player))
                    return Reject("Training exceeds available skill points.");
                if (!Save(client, unit =>
                {
                    RequireLearnedState(unit, client);
                    foreach (var skill in proposed.Values)
                        unit.CharacterSkills.AddOrUpdate(player.Id, (uint)skill.SkillId, skill.AbilityId, skill.SkillLevel);
                }))
                    return false;
                foreach (var skill in proposed)
                    player.Skills[skill.Key] = skill.Value;
                client.CallMethod(player.EntityId, new SkillsPacket(player.Skills));
                client.CallMethod(player.EntityId, new AbilitiesPacket(player.Skills));
                _manifestation.SendAvailableAllocationPoints(client);
                return true;
            }
        }

        private bool IsValidSkill(SkillId id, SkillsData skill)
        {
            var index = _manifestation.GetSkillIndexById((int)id);
            return index >= 0 && skill != null && skill.SkillId == id && skill.SkillLevel >= 0 &&
                skill.SkillLevel <= 5 && skill.AbilityId == _manifestation.SkillIdx2AbilityId[index];
        }

        private void RequireLearnedState(ICharUnitOfWork unit, Client client)
        {
            var player = client.Player;
            var character = unit.Characters.Get(player.Id);
            if (character.AccountId != client.AccountEntry?.Id || character.Level != player.Level)
                throw new GameplayRejectionException("Character ownership or level changed.");
            var saved = unit.CharacterSkills.GetCharacterSkills(player.Id);
            if (saved.Any(skill => !player.Skills.TryGetValue((SkillId)skill.SkillId, out var runtime) ||
                    runtime.SkillLevel != skill.SkillLevel || runtime.AbilityId != skill.AbilityId) ||
                player.Skills.Values.Any(skill => skill.SkillLevel != 0 &&
                    !saved.Any(row => row.SkillId == (uint)skill.SkillId && row.SkillLevel == skill.SkillLevel &&
                        row.AbilityId == skill.AbilityId)))
                throw new GameplayRejectionException("Persisted learned state changed; reload the character.");
        }

        private bool Save(Client client, Action<ICharUnitOfWork> changes)
        {
            try
            {
                using var unit = _factory.CreateChar();
                unit.ExecuteTransaction(() => changes(unit));
                return true;
            }
            catch (Exception error) when (GameplayRejectionException.IsExpected(error))
            {
                Logger.WriteLog(LogType.Error, $"Ability loadout save failed for character {client.Player.Id}: {error}");
                return false;
            }
        }

        private static bool Reject(string reason)
        {
            Logger.WriteLog(LogType.Network, $"Rejected ability loadout request: {reason}");
            return false;
        }
    }
}
