using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.MapChannel.Server;
    using Packets.Mission.Server;
    using Repositories.Char;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;

    internal readonly struct MissionRewardItem
    {
        internal uint ItemTemplateId { get; }
        internal uint Quantity { get; }

        internal MissionRewardItem(uint itemTemplateId, uint quantity)
        {
            ItemTemplateId = itemTemplateId;
            Quantity = quantity;
        }
    }

    internal sealed class MissionRewardDefinition
    {
        internal uint Experience { get; }
        internal IReadOnlyDictionary<CurencyType, int> Currencies { get; }
        internal IReadOnlyList<MissionRewardItem> FixedItems { get; }
        internal IReadOnlyList<MissionRewardItem> SelectableItems { get; }

        internal MissionRewardDefinition(
            uint experience,
            IReadOnlyDictionary<CurencyType, int> currencies,
            IReadOnlyList<MissionRewardItem> fixedItems,
            IReadOnlyList<MissionRewardItem> selectableItems)
        {
            Experience = experience;
            Currencies = new Dictionary<CurencyType, int>(
                currencies ?? new Dictionary<CurencyType, int>());
            FixedItems = (fixedItems ?? Array.Empty<MissionRewardItem>()).ToArray();
            SelectableItems = (selectableItems ?? Array.Empty<MissionRewardItem>()).ToArray();
        }

        internal MissionRewardGrant CreateGrant(int selectionIndex)
        {
            if (Currencies.Any(entry =>
                    entry.Value < 0 ||
                    entry.Key is not CurencyType.Credits and not CurencyType.Prestige))
                throw new GameplayRejectionException("Mission reward contains an unsupported currency.");
            if (FixedItems.Any(item => item.ItemTemplateId == 0 || item.Quantity == 0) ||
                SelectableItems.Any(item => item.ItemTemplateId == 0 || item.Quantity == 0))
                throw new GameplayRejectionException("Mission reward contains an invalid item.");
            if (SelectableItems.Count == 0)
            {
                if (selectionIndex != 0)
                    throw new GameplayRejectionException("Mission reward selection is invalid.");
                return new MissionRewardGrant(Experience, Currencies, FixedItems);
            }
            if (selectionIndex < 0 || selectionIndex >= SelectableItems.Count)
                throw new GameplayRejectionException("Mission reward selection is invalid.");

            return new MissionRewardGrant(
                Experience,
                Currencies,
                FixedItems.Concat(new[] { SelectableItems[selectionIndex] }).ToArray());
        }
    }

    internal sealed class MissionRewardGrant : IDisposable
    {
        private readonly uint _experience;
        private readonly IReadOnlyDictionary<CurencyType, int> _currencies;
        private readonly InventoryManager.InventoryGrant _inventory = new();
        private readonly IReadOnlyList<InventoryManager.InventoryItemGrant> _items;
        private ManifestationManager.ProgressionGrant _progression;
        private int _credits;
        private int _prestige;

        internal MissionRewardGrant(
            uint experience,
            IReadOnlyDictionary<CurencyType, int> currencies,
            IReadOnlyList<MissionRewardItem> items)
        {
            _experience = experience;
            _currencies = currencies;
            _items = items.Select(item =>
                new InventoryManager.InventoryItemGrant(item.ItemTemplateId, item.Quantity)).ToArray();
        }

        internal void PlanAndSave(
            Client client,
            CharacterEntry durableCharacter,
            ICharUnitOfWork unitOfWork,
            ManifestationManager manifestationManager)
        {
            var player = client.Player;
            if (!player.Credits.TryGetValue(CurencyType.Credits, out var runtimeCredits) ||
                !player.Credits.TryGetValue(CurencyType.Prestige, out var runtimePrestige) ||
                durableCharacter.Credit != runtimeCredits ||
                durableCharacter.Prestige != runtimePrestige)
                throw new GameplayRejectionException("Runtime currencies no longer match durable character state.");

            _credits = durableCharacter.Credit;
            _prestige = durableCharacter.Prestige;
            foreach (var currency in _currencies)
            {
                switch (currency.Key)
                {
                    case CurencyType.Credits:
                        _credits = checked(_credits + currency.Value);
                        break;
                    case CurencyType.Prestige:
                        _prestige = checked(_prestige + currency.Value);
                        break;
                    default:
                        throw new GameplayRejectionException("Mission reward contains an unsupported currency.");
                }
            }

            if (_items.Count > 0)
                _inventory.PlanAndSave(client, _items, unitOfWork);
            if (_experience > 0)
                _progression = manifestationManager.PlanExperience(
                    client, _experience, durableCharacter, unitOfWork);
            if (_credits != durableCharacter.Credit || _prestige != durableCharacter.Prestige)
                unitOfWork.Characters.UpdateCharacterCurrencies(player.Id, _credits, _prestige);
        }

        internal void Publish(Client client, ManifestationManager manifestationManager)
        {
            _inventory.Publish(client);
            manifestationManager.PublishExperience(client, _progression);
            PublishCurrency(CurencyType.Credits, _credits);
            PublishCurrency(CurencyType.Prestige, _prestige);

            void PublishCurrency(CurencyType type, int total)
            {
                var previous = client.Player.Credits[type];
                if (total == previous)
                    return;
                client.Player.Credits[type] = total;
                client.CallMethod(client.Player.EntityId,
                    new UpdateCreditsPacket(type, total, checked((uint)(total - previous))));
            }
        }

        public void Dispose() => _inventory.Dispose();
    }

    public class MissionManager
    {
        private const int MissionLogCapacity = 30;
        private static MissionManager _instance;
        private static readonly object InstanceLock = new();
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly Dictionary<uint, Mission> _loadedMissions;
        private readonly IReadOnlyDictionary<uint, MissionRewardDefinition> _rewardDefinitions;
        private readonly ManifestationManager _manifestationManager;

        public IReadOnlyDictionary<uint, Mission> LoadedMissions => _loadedMissions;

        public static MissionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new MissionManager(Server.GameUnitOfWorkFactory);
                    }
                }

                return _instance;
            }
        }

        private MissionManager(IGameUnitOfWorkFactory gameUnitOfWorkFactory)
            : this(gameUnitOfWorkFactory, new Dictionary<uint, Mission>())
        {
        }

        public MissionManager(
            IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            IReadOnlyDictionary<uint, Mission> definitions)
            : this(
                gameUnitOfWorkFactory,
                definitions,
                new Dictionary<uint, MissionRewardDefinition>(),
                new ManifestationManager(gameUnitOfWorkFactory))
        {
        }

        internal MissionManager(
            IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            IReadOnlyDictionary<uint, Mission> definitions,
            IReadOnlyDictionary<uint, MissionRewardDefinition> rewardDefinitions,
            ManifestationManager manifestationManager)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _loadedMissions = definitions.ToDictionary(entry => entry.Key, entry => entry.Value);
            _rewardDefinitions = new Dictionary<uint, MissionRewardDefinition>(
                rewardDefinitions ?? new Dictionary<uint, MissionRewardDefinition>());
            _manifestationManager = manifestationManager;
        }

        public void LoadMissions()
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            foreach (var mission in unitOfWork.NpcMissions.Get())
                _loadedMissions[mission.Id] = new Mission(mission);
        }

        internal void Hydrate(Manifestation player, IReadOnlyList<CharacterMissionEntry> rows)
        {
            var hydrated = new Dictionary<uint, MissionLog>();
            foreach (var row in rows)
            {
                if (!TryGetOperationalMission(row.MissionId, out _))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Skipped mission {row.MissionId} for character {player.Id}: definition is not operational.");
                    continue;
                }

                var state = (MissionState)row.MissionState;
                if (!IsPublishedState(state))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Skipped mission {row.MissionId} for character {player.Id}: unsupported state {row.MissionState}.");
                    continue;
                }

                hydrated[row.MissionId] = new MissionLog(
                    row.MissionId,
                    state,
                    state == MissionState.Active && row.Completeable);
            }

            player.Missions = hydrated;
        }

        public IReadOnlyDictionary<uint, MissionInfo> BuildStatusSnapshot(Manifestation player)
        {
            var snapshot = new Dictionary<uint, MissionInfo>();
            foreach (var entry in player.Missions)
            {
                if (!TryGetOperationalMission(entry.Key, out var definition) ||
                    !IsPublishedState(entry.Value.State))
                    continue;

                snapshot.Add(entry.Key, definition.CreateInfo(entry.Value.State, entry.Value.Completeable));
            }

            return snapshot;
        }

        public void PublishInitialState(Client client)
        {
            client.CallMethod(
                client.Player.EntityId,
                new MissionStatusInfoPacket(BuildStatusSnapshot(client.Player)));
        }

        public bool TryAcceptNpcMission(Client client, ulong npcEntityId, uint missionId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client))
                    return Reject($"Rejected mission {missionId}: character is not active in the world.");
                if (!TryGetOperationalMission(missionId, out var definition))
                    return Reject($"Rejected mission {missionId}: definition is not operational.");
                if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var npc))
                    return Reject($"Rejected mission {missionId}: NPC entity {npcEntityId} is not in the current map instance.");
                if (npc.Npc == null || npc.DbId != definition.MissionGiver)
                    return Reject($"Rejected mission {missionId}: NPC {npc.DbId} is not its authoritative giver.");
                if (client.Player.Missions.ContainsKey(missionId))
                    return Reject($"Rejected mission {missionId}: character {client.Player.Id} already has it.");

                var log = new MissionLog(missionId, MissionState.Active, false);
                var accepted = false;
                var durableLogFull = false;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        if (unitOfWork.CharacterMissions.Count(client.Player.Id) >= MissionLogCapacity)
                        {
                            durableLogFull = true;
                            return;
                        }
                        if (unitOfWork.CharacterMissions.Get(client.Player.Id, missionId) != null)
                            return;

                        unitOfWork.CharacterMissions.Add(new CharacterMissionEntry(
                            client.Player.Id,
                            missionId,
                            (uint)MissionState.Active));
                        accepted = true;
                    });
                }
                catch (Exception error) when (error is DbUpdateException || error is DbException)
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to accept mission {missionId} for character {client.Player.Id}: {error.Message}");
                    return false;
                }

                if (durableLogFull)
                {
                    CommunicatorManager.Instance.SystemMessage(client, "Mission log is full.");
                    return false;
                }
                if (!accepted)
                    return Reject($"Rejected mission {missionId}: character {client.Player.Id} already has it.");

                client.Player.Missions.Add(missionId, log);
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionGainedPacket(missionId, definition.CreateInfo(log.State, log.Completeable)));
                return true;
            }
        }

        internal bool TryCompleteNpcMission(Client client, ulong npcEntityId, uint missionId, int selectionIndex)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client))
                    return Reject($"Rejected mission {missionId} turn-in: character is not active in the world.");
                if (!TryGetOperationalMission(missionId, out var definition))
                    return Reject($"Rejected mission {missionId} turn-in: definition is not operational.");
                if (!_rewardDefinitions.TryGetValue(missionId, out var rewardDefinition))
                    return Reject($"Rejected mission {missionId} turn-in: no approved reward definition is loaded.");
                if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var npc))
                    return Reject($"Rejected mission {missionId} turn-in: NPC entity {npcEntityId} is not in the current map instance.");
                if (npc.Npc == null || npc.DbId != definition.MissionReciver)
                    return Reject($"Rejected mission {missionId} turn-in: NPC {npc.DbId} is not its authoritative receiver.");
                if (!client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State != MissionState.Active ||
                    !runtimeMission.Completeable)
                    return Reject($"Rejected mission {missionId} turn-in: runtime mission is not completable.");
                if (!_manifestationManager.ValidateProgressionForClient(client))
                    return false;

                MissionRewardGrant grant = null;
                try
                {
                    try
                    {
                        using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                        unitOfWork.ExecuteTransaction(() =>
                        {
                            var character = unitOfWork.Characters.Get(client.Player.Id);
                            var mission = unitOfWork.CharacterMissions.Get(client.Player.Id, missionId);
                            if (client.AccountEntry == null ||
                                character.AccountId != client.AccountEntry.Id)
                                throw new GameplayRejectionException("Durable character owner changed.");
                            if (mission == null ||
                                mission.MissionState != (uint)MissionState.Active ||
                                !mission.Completeable)
                                throw new GameplayRejectionException("Durable mission is not completable.");
                            if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var currentNpc) ||
                                !ReferenceEquals(currentNpc, npc) ||
                                currentNpc.EntityId != npcEntityId ||
                                currentNpc.DbId != definition.MissionReciver ||
                                currentNpc.Npc == null)
                                throw new GameplayRejectionException(
                                    "Mission receiver changed before reward persistence.");

                            grant = rewardDefinition.CreateGrant(selectionIndex);
                            grant.PlanAndSave(client, character, unitOfWork, _manifestationManager);
                            mission.MissionState = (uint)MissionState.Completed;
                            mission.Completeable = false;
                        });
                    }
                    catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                    {
                        Logger.WriteLog(LogType.Error,
                            $"Unable to complete mission {missionId} for character {client.Player.Id}: {error}");
                        return false;
                    }

                    grant.Publish(client, _manifestationManager);
                    client.Player.Missions[missionId] =
                        new MissionLog(missionId, MissionState.Completed, false);
                    client.CallMethod(client.Player.EntityId,
                        new MissionCompleteablePacket(missionId, false));
                    client.CallMethod(client.Player.EntityId, new MissionCompletedPacket(missionId));
                    client.CallMethod(client.Player.EntityId, new MissionRewardedPacket(missionId));
                    return true;
                }
                finally
                {
                    grant?.Dispose();
                }
            }
        }

        internal bool TryAbandon(Client client, uint missionId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    !client.Player.Missions.TryGetValue(missionId, out var log) ||
                    log.State != MissionState.Active)
                    return false;

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    var removed = false;
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission = unitOfWork.CharacterMissions.Get(client.Player.Id, missionId);
                        if (durableMission?.MissionState != (uint)MissionState.Active)
                            return;

                        unitOfWork.CharacterMissions.Remove(client.Player.Id, missionId);
                        removed = true;
                    });
                    if (!removed)
                        return false;
                }
                catch (Exception error) when (error is DbUpdateException || error is DbException)
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to abandon mission {missionId} for character {client.Player.Id}: {error.Message}");
                    return false;
                }

                client.Player.Missions.Remove(missionId);
                client.CallMethod(client.Player.EntityId, new MissionDiscardedPacket(missionId));
                return true;
            }
        }

        private static bool IsPublishedState(MissionState state) =>
            state == MissionState.Active ||
            state == MissionState.Failded ||
            state == MissionState.Completed;

        internal bool TryGetOperationalMission(uint missionId, out Mission mission)
        {
            if (_loadedMissions.TryGetValue(missionId, out mission) && mission.IsOperational)
                return true;

            mission = null;
            return false;
        }

        private static bool IsActivePlayer(Client client)
        {
            var player = client.Player;
            return client.State == ClientState.Ingame &&
                client.PendingTransfer == null &&
                player != null &&
                player.Id != 0 &&
                player.MapChannel != null &&
                !player.Disconected &&
                !player.RemoveFromMap &&
                CellManager.Instance.IsInWorld(client);
        }

        private static bool TryGetNpcOnPlayerMap(
            Manifestation player,
            ulong npcEntityId,
            out Creature npc)
        {
            npc = null;
            if (!EntityManager.Instance.RegisteredEntities.TryGetValue(npcEntityId, out var entityType) ||
                entityType != EntityType.Creature ||
                !EntityManager.Instance.Creatures.TryGetValue(npcEntityId, out var candidate) ||
                candidate.MapContextId != player.MapChannel.MapInfo.MapContextId ||
                !player.MapChannel.MapCellInfo.Cells.Values.Any(
                    cell => cell.CreatureList.Any(creature => ReferenceEquals(creature, candidate))))
                return false;

            npc = candidate;
            return true;
        }

        private static bool Reject(string message)
        {
            Logger.WriteLog(LogType.Network, message);
            return false;
        }
    }
}
