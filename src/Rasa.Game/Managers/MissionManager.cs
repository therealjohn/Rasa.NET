using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    using Repositories.Char.CharacterMissionProgress;
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

        internal MissionRewardGrant CreateGrant(
            int? selectionIndex,
            Action<Item> beforeItemPublication = null)
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
                if (selectionIndex.HasValue)
                    throw new GameplayRejectionException("Mission reward selection is invalid.");
                return new MissionRewardGrant(
                    Experience, Currencies, FixedItems, beforeItemPublication);
            }
            if (!selectionIndex.HasValue ||
                selectionIndex.Value < 0 ||
                selectionIndex.Value >= SelectableItems.Count)
                throw new GameplayRejectionException("Mission reward selection is invalid.");

            return new MissionRewardGrant(
                Experience,
                Currencies,
                FixedItems.Concat(new[] { SelectableItems[selectionIndex.Value] }).ToArray(),
                beforeItemPublication);
        }

        internal RewardInfo CreateInfo()
        {
            var info = new RewardInfo();
            foreach (var currency in Currencies)
            {
                if (currency.Value >= 0)
                    info.FixedReward.Credits[currency.Key] = (uint)currency.Value;
            }
            foreach (var item in FixedItems)
                info.FixedReward.FixedItems.Add(CreateItem(item));
            foreach (var item in SelectableItems)
                info.SelectableReward.Add(CreateItem(item));
            return info;

            static RewardItem CreateItem(MissionRewardItem item)
            {
                var reward = new RewardItem
                {
                    ItemTemplateId = item.ItemTemplateId,
                    Quantity = item.Quantity
                };
                if (ItemManager.Instance.ItemTemplateItemClass.TryGetValue(
                        item.ItemTemplateId, out var entityClass))
                    reward.Class = entityClass;
                return reward;
            }
        }
    }

    internal sealed class MissionRewardGrant : IDisposable
    {
        private readonly uint _experience;
        private readonly IReadOnlyDictionary<CurencyType, int> _currencies;
        private readonly InventoryManager.InventoryGrant _inventory;
        private readonly IReadOnlyList<InventoryManager.InventoryItemGrant> _items;
        private ManifestationManager.ProgressionGrant _progression;
        private int _previousCredits;
        private int _previousPrestige;
        private int _credits;
        private int _prestige;

        internal MissionRewardGrant(
            uint experience,
            IReadOnlyDictionary<CurencyType, int> currencies,
            IReadOnlyList<MissionRewardItem> items,
            Action<Item> beforeItemPublication = null)
        {
            _experience = experience;
            _currencies = currencies;
            _items = items.Select(item =>
                new InventoryManager.InventoryItemGrant(item.ItemTemplateId, item.Quantity)).ToArray();
            _inventory = new InventoryManager.InventoryGrant(beforeItemPublication);
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

            _previousCredits = durableCharacter.Credit;
            _previousPrestige = durableCharacter.Prestige;
            _credits = _previousCredits;
            _prestige = _previousPrestige;
            try
            {
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
            }
            catch (OverflowException error)
            {
                throw new GameplayRejectionException(
                    "Mission reward currency exceeds the supported range.",
                    error);
            }

            if (_items.Count > 0)
                _inventory.PlanAndSave(client, _items, unitOfWork);
            if (_experience > 0)
            {
                try
                {
                    _progression = manifestationManager.PlanExperience(
                        client, _experience, durableCharacter, unitOfWork);
                }
                catch (OverflowException error)
                {
                    throw new GameplayRejectionException(
                        "Mission reward experience exceeds the supported range.",
                        error);
                }
            }
            if (_credits != durableCharacter.Credit || _prestige != durableCharacter.Prestige)
                unitOfWork.Characters.UpdateCharacterCurrencies(player.Id, _credits, _prestige);
        }

        internal void ConvergeRuntime(Client client)
        {
            _inventory.ConvergeRuntime(client);
            if (_progression is { HasChanges: true })
            {
                client.Player.Experience = _progression.TotalExperience;
                client.Player.Level = _progression.FinalLevel;
                client.Player.CloneCredits = _progression.FinalCloneCredits;
            }
            client.Player.Credits[CurencyType.Credits] = _credits;
            client.Player.Credits[CurencyType.Prestige] = _prestige;
        }

        internal void Publish(Client client, ManifestationManager manifestationManager)
        {
            _inventory.Publish(client);
            MissionManager.TryPublish(
                () => manifestationManager.PublishExperience(client, _progression),
                "mission experience");
            PublishCurrency(
                CurencyType.Credits,
                _credits,
                _previousCredits);
            PublishCurrency(
                CurencyType.Prestige,
                _prestige,
                _previousPrestige);

            void PublishCurrency(CurencyType type, int total, int previous)
            {
                if (total == previous)
                    return;
                MissionManager.TryPublish(
                    () => client.CallMethod(
                        client.Player.EntityId,
                        new UpdateCreditsPacket(
                            type,
                            total,
                            (uint)(total - previous))),
                    $"mission {type} total");
            }
        }

        internal void RecordItemAcquisitions(
            Client client,
            MissionManager missionManager)
        {
            foreach (var item in _items)
            {
                if (!ItemManager.Instance.ItemTemplateItemClass.TryGetValue(
                        item.ItemTemplateId,
                        out var itemClass))
                    continue;
                missionManager.RecordProgress(
                    client,
                    MissionProgressEvent.ItemAcquired(
                        (uint)itemClass,
                        item.Quantity));
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
        private readonly IReadOnlyDictionary<uint, Mission> _loadedMissionsView;
        private readonly Dictionary<uint, MissionRewardDefinition> _rewardDefinitions;
        private readonly ManifestationManager _manifestationManager;
        private readonly Action<Item> _beforeRewardItemPublication;

        public IReadOnlyDictionary<uint, Mission> LoadedMissions => _loadedMissionsView;

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
            ManifestationManager manifestationManager,
            Action<Item> beforeRewardItemPublication = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _loadedMissions = definitions.ToDictionary(entry => entry.Key, entry => entry.Value);
            _loadedMissionsView = new ReadOnlyDictionary<uint, Mission>(_loadedMissions);
            _rewardDefinitions = new Dictionary<uint, MissionRewardDefinition>(
                rewardDefinitions ?? new Dictionary<uint, MissionRewardDefinition>());
            _manifestationManager = manifestationManager;
            _beforeRewardItemPublication = beforeRewardItemPublication;
        }

        public void LoadMissions()
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            foreach (var mission in unitOfWork.NpcMissions.Get())
            {
                var definition = new Mission(mission, isOperational: true);
                var rewardRows = unitOfWork.NpcMissionRewards.Get(mission.Id);
                if (rewardRows.Count == 0)
                    _rewardDefinitions[mission.Id] = new MissionRewardDefinition(
                        0,
                        new Dictionary<CurencyType, int>(),
                        Array.Empty<MissionRewardItem>(),
                        Array.Empty<MissionRewardItem>());
                else
                    definition = definition.DisableOperational(
                        "mission reward rows use an undocumented type contract");
                _loadedMissions[mission.Id] = definition;
            }
            foreach (var recovered in MissionDefinitionCatalog.CreateRecoveredInactiveDefinitions())
            {
                _loadedMissions.TryGetValue(recovered.Key, out var worldDefinition);
                _loadedMissions[recovered.Key] = recovered.Value.WithWorldMetadata(worldDefinition);
            }
            foreach (var definition in _loadedMissions.Values.Where(
                definition => !definition.IsOperational))
                Logger.WriteLog(
                    LogType.Error,
                    $"Mission {definition.MissionId} is inactive: {definition.OperationalDiagnostic}.");
        }

        internal void Hydrate(
            Manifestation player,
            IReadOnlyList<CharacterMissionEntry> rows,
            CharacterMissionProgressSnapshot progress)
        {
            var hydrated = new Dictionary<uint, MissionLog>();
            foreach (var row in rows)
            {
                if (!TryGetOperationalMission(row.MissionId, out var definition))
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

                if (!TryHydrateObjectives(definition, progress, out var objectives))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Skipped mission {row.MissionId} for character {player.Id}: objective state is incomplete or invalid.");
                    continue;
                }
                var derivedCompleteable =
                    state == MissionState.Active &&
                    definition.Objectives.Values
                        .Where(objective => objective.IsRequired.Value)
                        .All(objective =>
                            objectives[objective.ObjectiveId].State ==
                            MissionObjectiveState.Completed);
                if (state == MissionState.Active && row.Completeable != derivedCompleteable)
                {
                    Logger.WriteLog(LogType.Error,
                        $"Skipped mission {row.MissionId} for character {player.Id}: completable state does not match objectives.");
                    continue;
                }

                hydrated[row.MissionId] = new MissionLog(
                    row.MissionId,
                    state,
                    derivedCompleteable,
                    objectives);
            }

            player.Missions = hydrated;
        }

        internal void Hydrate(Manifestation player, IReadOnlyList<CharacterMissionEntry> rows) =>
            Hydrate(player, rows, CharacterMissionProgressSnapshot.Empty);

        public IReadOnlyDictionary<uint, MissionInfo> BuildStatusSnapshot(Manifestation player)
        {
            var snapshot = new Dictionary<uint, MissionInfo>();
            foreach (var entry in player.Missions)
            {
                if (!TryGetOperationalMission(entry.Key, out var definition) ||
                    !IsPublishedState(entry.Value.State))
                    continue;

                snapshot.Add(entry.Key, definition.CreateInfo(
                    entry.Value.State,
                    entry.Value.Completeable,
                    entry.Value.Objectives));
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

                var objectiveLogs = definition.CreateInitialObjectiveLogs();
                var initialCompleteable = definition.Objectives.Values
                    .Where(objective => objective.IsRequired.Value)
                    .All(objective =>
                        objective.InitialState.Value == MissionObjectiveState.Completed);
                var log = new MissionLog(
                    missionId,
                    MissionState.Active,
                    initialCompleteable,
                    objectiveLogs);
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
                        if (unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId) != null)
                            return;

                        unitOfWork.CharacterMissions.Add(new CharacterMissionEntry(
                            client.Player.Id,
                            missionId,
                            (uint)MissionState.Active)
                        {
                            Completeable = initialCompleteable
                        });
                        unitOfWork.CharacterMissionProgress.AddObjectives(
                            definition.Objectives.Values.Select(objective =>
                            {
                                var entry = new CharacterMissionObjectiveEntry(
                                    client.Player.Id,
                                    missionId,
                                    objective.ObjectiveId,
                                    (byte)objective.InitialState.Value);
                                foreach (var counter in objective.Counters)
                                    entry.Counters.Add(new CharacterMissionObjectiveCounterEntry(
                                        client.Player.Id,
                                        missionId,
                                        objective.ObjectiveId,
                                        counter.Key,
                                        counter.Value.InitialValue));
                                foreach (var counter in objective.ItemCounters)
                                    entry.ItemCounters.Add(new CharacterMissionObjectiveItemCounterEntry(
                                        client.Player.Id,
                                        missionId,
                                        objective.ObjectiveId,
                                        counter.Key,
                                        counter.Value.InitialValue));
                                return entry;
                            }));
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
                    new MissionGainedPacket(missionId, definition.CreateInfo(
                        log.State, log.Completeable, log.Objectives)));
                return true;
            }
        }

        internal bool TryCompleteNpcMission(
            Client client,
            ulong npcEntityId,
            uint missionId,
            int? selectionIndex,
            int? rating)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client))
                    return Reject($"Rejected mission {missionId} completion: character is not active in the world.");
                if (!TryGetOperationalMission(missionId, out var definition))
                    return Reject($"Rejected mission {missionId} completion: definition is not operational.");
                if (selectionIndex.HasValue || rating.HasValue)
                    return Reject($"Rejected mission {missionId} completion: reward selection belongs to the reward request.");
                if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var npc) ||
                    npc.Npc == null ||
                    npc.DbId != definition.MissionReciver)
                    return Reject($"Rejected mission {missionId} completion: NPC is not its authoritative receiver.");
                if (!client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State != MissionState.Active ||
                    !runtimeMission.Completeable)
                    return Reject($"Rejected mission {missionId} completion: runtime mission is not completable.");

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission =
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId);
                        if (durableMission?.MissionState != (uint)MissionState.Active ||
                            !durableMission.Completeable)
                            throw new GameplayRejectionException(
                                "Durable mission is not completable.");
                        if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var currentNpc) ||
                            !ReferenceEquals(currentNpc, npc) ||
                            currentNpc.DbId != definition.MissionReciver)
                            throw new GameplayRejectionException(
                                "Mission receiver changed before completion persistence.");

                        durableMission.MissionState = (uint)MissionState.Success;
                        durableMission.Completeable = false;
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to mark mission {missionId} successful for character {client.Player.Id}: {error}");
                    return false;
                }

                runtimeMission.State = MissionState.Success;
                runtimeMission.Completeable = false;
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionCompleteablePacket(missionId, false));
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionCompletedPacket(missionId));
                RecordProgress(client, MissionProgressEvent.Mission(missionId));
                return true;
            }
        }

        internal bool TryCompleteNpcMission(
            Client client,
            ulong npcEntityId,
            uint missionId,
            int selectionIndex)
        {
            if (!TryCompleteNpcMission(
                    client,
                    npcEntityId,
                    missionId,
                    null,
                    null))
                return false;
            return TryRewardNpcMission(
                client,
                npcEntityId,
                missionId,
                selectionIndex,
                null);
        }

        private bool TryGrantNpcMission(
            Client client,
            ulong npcEntityId,
            uint missionId,
            int? selectionIndex,
            int? rating,
            MissionState expectedState,
            bool requireCompletable,
            bool publishCompleted)
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
                if (rating.HasValue)
                    return Reject($"Rejected mission {missionId} turn-in: ratings are not supported.");
                if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var npc))
                    return Reject($"Rejected mission {missionId} turn-in: NPC entity {npcEntityId} is not in the current map instance.");
                if (npc.Npc == null || npc.DbId != definition.MissionReciver)
                    return Reject($"Rejected mission {missionId} turn-in: NPC {npc.DbId} is not its authoritative receiver.");
                if (!client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State != expectedState ||
                    requireCompletable && !runtimeMission.Completeable)
                    return Reject($"Rejected mission {missionId} turn-in: runtime mission is not in the required state.");
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
                            var mission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId);
                            if (client.AccountEntry == null ||
                                character.AccountId != client.AccountEntry.Id)
                                throw new GameplayRejectionException("Durable character owner changed.");
                            if (mission == null ||
                                mission.MissionState != (uint)expectedState ||
                                requireCompletable && !mission.Completeable ||
                                expectedState == MissionState.Success && mission.Completeable)
                                throw new GameplayRejectionException("Durable mission is not in the required state.");
                            if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var currentNpc) ||
                                !ReferenceEquals(currentNpc, npc) ||
                                currentNpc.EntityId != npcEntityId ||
                                currentNpc.DbId != definition.MissionReciver ||
                                currentNpc.Npc == null)
                                throw new GameplayRejectionException(
                                    "Mission receiver changed before reward persistence.");

                            grant = rewardDefinition.CreateGrant(
                                selectionIndex,
                                _beforeRewardItemPublication);
                            grant.PlanAndSave(client, character, unitOfWork, _manifestationManager);
                            mission.MissionState = (uint)MissionState.Completed;
                            mission.Completeable = false;
                        });
                    }
                    catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                    {
                        Logger.WriteLog(LogType.Error,
                            $"Unable to complete mission {missionId} for character {client.Player.Id}: {error}");
                        ReconcileMissionFromDurable(client, missionId);
                        return false;
                    }

                    client.Player.Missions[missionId] =
                        new MissionLog(
                            missionId,
                            MissionState.Completed,
                            false,
                            runtimeMission.Objectives);
                    grant.ConvergeRuntime(client);
                    TryPublish(
                        () => grant.RecordItemAcquisitions(client, this),
                        $"mission {missionId} reward item acquisition progress");
                    grant.Publish(client, _manifestationManager);
                    if (publishCompleted)
                        client.CallMethod(client.Player.EntityId, new MissionCompletedPacket(missionId));
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionRewardedPacket(missionId)),
                        $"mission {missionId} rewarded");
                    return true;
                }
                finally
                {
                    grant?.Dispose();
                }
            }
        }

        private void ReconcileMissionFromDurable(Client client, uint missionId)
        {
            client.Player.Missions.TryGetValue(missionId, out var previous);
            var previousObjectiveStates = previous?.Objectives.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.State) ??
                new Dictionary<uint, MissionObjectiveState>();

            try
            {
                using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                Hydrate(
                    client.Player,
                    unitOfWork.CharacterMissions.Get(client.Player.Id),
                    unitOfWork.CharacterMissionProgress.Get(client.Player.Id));
            }
            catch (Exception error) when (GameplayRejectionException.IsExpected(error))
            {
                Logger.WriteLog(
                    LogType.Error,
                    $"Unable to reconcile mission {missionId} for character {client.Player.Id}: {error}");
                return;
            }

            if (!client.Player.Missions.TryGetValue(missionId, out var current))
            {
                if (previous != null && previous.State is
                    MissionState.Success or MissionState.Failed or MissionState.Completed)
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionClearedPacket(missionId)),
                        $"mission {missionId} cleared reconciliation");
                return;
            }

            foreach (var objective in current.Objectives)
                if (objective.Value.State == MissionObjectiveState.Failed &&
                    (!previousObjectiveStates.TryGetValue(
                         objective.Key,
                         out var previousState) ||
                     previousState != MissionObjectiveState.Failed))
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new ObjectiveFailedPacket(missionId, objective.Key)),
                        $"mission {missionId} objective {objective.Key} failed reconciliation");

            if (previous?.State == current.State)
                return;

            switch (current.State)
            {
                case MissionState.Failed:
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionFailedPacket(missionId)),
                        $"mission {missionId} failed reconciliation");
                    break;
                case MissionState.Success:
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionCompleteablePacket(missionId, false)),
                        $"mission {missionId} completable reconciliation");
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionCompletedPacket(missionId)),
                        $"mission {missionId} success reconciliation");
                    break;
                case MissionState.Completed:
                    if (previous?.State != MissionState.Success)
                    {
                        TryPublish(
                            () => client.CallMethod(
                                client.Player.EntityId,
                                new MissionCompleteablePacket(missionId, false)),
                            $"mission {missionId} completable reconciliation");
                        TryPublish(
                            () => client.CallMethod(
                                client.Player.EntityId,
                                new MissionCompletedPacket(missionId)),
                            $"mission {missionId} completion reconciliation");
                    }
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionRewardedPacket(missionId)),
                        $"mission {missionId} reward reconciliation");
                    break;
            }
        }

        internal bool TryCompleteNpcObjective(
            Client client,
            ulong npcEntityId,
            uint missionId,
            uint objectiveId,
            uint playerFlagId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client))
                    return Reject($"Rejected mission {missionId} objective completion: character is not active in the world.");
                if (!TryGetOperationalMission(missionId, out var definition) ||
                    !definition.Objectives.TryGetValue(objectiveId, out var objectiveDefinition))
                    return Reject($"Rejected mission {missionId} objective {objectiveId}: definition is unavailable.");
                if (!client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State != MissionState.Active ||
                    !runtimeMission.Objectives.TryGetValue(objectiveId, out var runtimeObjective) ||
                    runtimeObjective.State != MissionObjectiveState.Incomplete)
                    return Reject($"Rejected mission {missionId} objective {objectiveId}: runtime state is not incomplete.");
                if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var npc))
                    return Reject($"Rejected mission {missionId} objective {objectiveId}: NPC is not in the current map instance.");
                if (npc.Npc == null ||
                    !objectiveDefinition.Conversations.Any(conversation =>
                        conversation.Type == MissionObjectiveConversationType.Completion &&
                        conversation.NpcPackageId == npc.Npc.NpcPackageId &&
                        conversation.PlayerFlagId == playerFlagId))
                    return Reject($"Rejected mission {missionId} objective {objectiveId}: completion binding does not match.");

                var npcPackageId = npc.Npc.NpcPackageId;
                var revealed = objectiveDefinition.RevealedObjectiveIds.ToArray();
                var activated = objectiveDefinition.ActivatedObjectiveIds.ToArray();
                var appliedRevealed = new List<uint>();
                var appliedActivated = new List<uint>();
                IReadOnlyDictionary<uint, CharacterMissionObjectiveEntry> durableObjectives = null;
                var completeable = false;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                            client.Player.Id, missionId);
                        durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
                            client.Player.Id, missionId);
                        durableObjectives.TryGetValue(objectiveId, out var durableObjective);
                        if (durableMission?.MissionState != (uint)MissionState.Active ||
                            durableObjective?.ObjectiveState != (byte)MissionObjectiveState.Incomplete)
                            throw new GameplayRejectionException(
                                "Durable mission objective is not incomplete.");
                        if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var currentNpc) ||
                            !ReferenceEquals(currentNpc, npc) ||
                            currentNpc.Npc?.NpcPackageId != npcPackageId)
                            throw new GameplayRejectionException(
                                "Mission objective NPC changed before persistence.");

                        durableObjective.ObjectiveState = (byte)MissionObjectiveState.Completed;
                        foreach (var successorId in revealed)
                        {
                            if (!durableObjectives.TryGetValue(successorId, out var successor))
                                throw new GameplayRejectionException(
                                    "Configured revealed mission objective is missing.");
                            if (successor.ObjectiveState == (byte)MissionObjectiveState.Inactive)
                            {
                                successor.ObjectiveState = (byte)MissionObjectiveState.NotAssigned;
                                appliedRevealed.Add(successorId);
                            }
                        }
                        foreach (var successorId in activated)
                        {
                            if (!durableObjectives.TryGetValue(successorId, out var successor))
                                throw new GameplayRejectionException(
                                    "Configured activated mission objective is missing.");
                            if (successor.ObjectiveState ==
                                (byte)MissionObjectiveState.Inactive ||
                                successor.ObjectiveState ==
                                (byte)MissionObjectiveState.NotAssigned)
                            {
                                successor.ObjectiveState = (byte)MissionObjectiveState.Incomplete;
                                appliedActivated.Add(successorId);
                            }
                            else if (successor.ObjectiveState is
                                ((byte)MissionObjectiveState.Incomplete) or
                                ((byte)MissionObjectiveState.Completed) or
                                ((byte)MissionObjectiveState.Failed))
                                continue;
                            else
                                throw new GameplayRejectionException(
                                    "Configured activated mission objective state is invalid.");
                        }

                        completeable = true;
                        foreach (var candidate in definition.Objectives.Values.Where(
                            candidate => candidate.IsRequired.Value))
                        {
                            if (candidate.ObjectiveId == objectiveId)
                                continue;
                            if (!durableObjectives.TryGetValue(candidate.ObjectiveId, out var required))
                                throw new GameplayRejectionException(
                                    "Required mission objective state is missing.");
                            if (required.ObjectiveState != (byte)MissionObjectiveState.Completed)
                                completeable = false;
                        }
                        durableMission.Completeable = completeable;
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to complete mission {missionId} objective {objectiveId} for character {client.Player.Id}: {error}");
                    return false;
                }

                foreach (var touchedObjectiveId in revealed
                    .Concat(activated)
                    .Append(objectiveId)
                    .Distinct())
                {
                    var durableObjective = durableObjectives[touchedObjectiveId];
                    var runtime = runtimeMission.Objectives[touchedObjectiveId];
                    runtime.State = (MissionObjectiveState)durableObjective.ObjectiveState;
                    foreach (var counter in durableObjective.Counters)
                        runtime.SetCounter(counter.CounterId, counter.CounterValue);
                    foreach (var counter in durableObjective.ItemCounters)
                        runtime.SetItemCounter(counter.ItemClassId, counter.CounterValue);
                }
                runtimeMission.Completeable = completeable;

                client.CallMethod(client.Player.EntityId,
                    new ObjectiveCompletedPacket(missionId, objectiveId));
                foreach (var successorId in revealed.Where(appliedRevealed.Contains))
                    client.CallMethod(client.Player.EntityId,
                        new ObjectiveRevealedPacket(
                            missionId,
                            successorId,
                            definition.CreateInfo(
                                runtimeMission.State,
                                runtimeMission.Completeable,
                                runtimeMission.Objectives)));
                foreach (var successorId in appliedActivated)
                    client.CallMethod(client.Player.EntityId,
                        new ObjectiveActivatedPacket(missionId, successorId));
                if (completeable)
                    client.CallMethod(client.Player.EntityId,
                        new MissionCompleteablePacket(missionId, true));
                return true;
            }
        }

        internal bool TryRewardNpcMission(
            Client client,
            ulong npcEntityId,
            uint missionId,
            int? selectionIndex,
            int? rating)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client))
                    return Reject($"Rejected mission {missionId} reward: character is not active in the world.");
                if (!TryGetOperationalMission(missionId, out _))
                    return Reject($"Rejected mission {missionId} reward: definition is not operational.");
            }

            return TryGrantNpcMission(
                client,
                npcEntityId,
                missionId,
                selectionIndex,
                rating,
                MissionState.Success,
                requireCompletable: false,
                publishCompleted: false);
        }

        internal bool TryFailObjective(
            Client client,
            uint missionId,
            uint objectiveId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    !TryGetOperationalMission(missionId, out var definition) ||
                    !definition.Objectives.TryGetValue(objectiveId, out var objectiveDefinition) ||
                    !client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State != MissionState.Active ||
                    !runtimeMission.Objectives.TryGetValue(objectiveId, out var runtimeObjective) ||
                    runtimeObjective.State != MissionObjectiveState.Incomplete)
                    return false;

                var failMission = objectiveDefinition.IsRequired.Value;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission =
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId);
                        var durableObjective = unitOfWork.CharacterMissionProgress.Get(
                            client.Player.Id, missionId, objectiveId);
                        if (durableMission?.MissionState != (uint)MissionState.Active ||
                            durableObjective?.ObjectiveState !=
                            (byte)MissionObjectiveState.Incomplete)
                            throw new GameplayRejectionException(
                                "Durable mission objective cannot fail from its current state.");

                        durableObjective.ObjectiveState =
                            (byte)MissionObjectiveState.Failed;
                        if (failMission)
                        {
                            durableMission.MissionState = (uint)MissionState.Failed;
                            durableMission.Completeable = false;
                        }
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to fail mission {missionId} objective {objectiveId}: {error}");
                    return false;
                }

                runtimeObjective.State = MissionObjectiveState.Failed;
                runtimeMission.Completeable = false;
                client.CallMethod(
                    client.Player.EntityId,
                    new ObjectiveFailedPacket(missionId, objectiveId));
                if (failMission)
                {
                    runtimeMission.State = MissionState.Failed;
                    client.CallMethod(
                        client.Player.EntityId,
                        new MissionFailedPacket(missionId));
                }
                return true;
            }
        }

        internal bool TryFailMission(Client client, uint missionId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    !TryGetOperationalMission(missionId, out _) ||
                    !client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State != MissionState.Active)
                    return false;

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission =
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId);
                        if (durableMission?.MissionState != (uint)MissionState.Active)
                            throw new GameplayRejectionException(
                                "Durable mission cannot fail from its current state.");
                        durableMission.MissionState = (uint)MissionState.Failed;
                        durableMission.Completeable = false;
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to fail mission {missionId}: {error}");
                    return false;
                }

                runtimeMission.State = MissionState.Failed;
                runtimeMission.Completeable = false;
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionFailedPacket(missionId));
                return true;
            }
        }

        internal bool TryClear(Client client, uint missionId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    !TryGetOperationalMission(missionId, out _) ||
                    !client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State is not
                        (MissionState.Success or MissionState.Failed or MissionState.Completed))
                    return false;

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    var removed = false;
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission =
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId);
                        if (durableMission == null ||
                            (MissionState)durableMission.MissionState is not
                                (MissionState.Success or MissionState.Failed or MissionState.Completed))
                            return;
                        unitOfWork.CharacterMissionProgress.Remove(
                            client.Player.Id, missionId);
                        unitOfWork.CharacterMissions.Remove(
                            client.Player.Id, missionId);
                        removed = true;
                    });
                    if (!removed)
                        return false;
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to clear mission {missionId}: {error}");
                    return false;
                }

                client.Player.Missions.Remove(missionId);
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionClearedPacket(missionId));
                return true;
            }
        }

        internal bool TryAbandon(Client client, uint missionId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    !TryGetOperationalMission(missionId, out _) ||
                    !client.Player.Missions.TryGetValue(missionId, out var log) ||
                    log.State != MissionState.Active)
                    return false;

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    var removed = false;
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                            client.Player.Id, missionId);
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

        internal bool RecordProgress(Client client, MissionProgressEvent progress)
        {
            if (client == null ||
                !Enum.IsDefined(typeof(MissionProgressEventKind), progress.Kind) ||
                progress.SubjectId == 0 ||
                progress.Quantity == 0)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) || client.AccountEntry == null)
                    return false;

                var candidates = new List<ProgressCandidate>();
                foreach (var mission in _loadedMissions.Values
                    .Where(definition => definition.IsOperational)
                    .OrderBy(definition => definition.MissionId))
                {
                    if (!client.Player.Missions.TryGetValue(
                            mission.MissionId, out var runtimeMission) ||
                        runtimeMission.State != MissionState.Active)
                        continue;
                    foreach (var objective in mission.Objectives.Values
                        .OrderBy(definition => definition.ObjectiveId))
                    {
                        if (objective.ProgressRule == null ||
                            !objective.ProgressRule.Matches(progress) ||
                            !runtimeMission.Objectives.TryGetValue(
                                objective.ObjectiveId, out var runtimeObjective) ||
                            runtimeObjective.State != MissionObjectiveState.Incomplete ||
                            !CanAdvanceFromRuntime(
                                client.Player,
                                objective.ProgressRule))
                            continue;
                        candidates.Add(new ProgressCandidate(
                            mission, runtimeMission, objective, runtimeObjective));
                    }
                }
                if (candidates.Count == 0)
                    return false;

                var publications = new List<ProgressPublication>();
                var completableMissions = new SortedSet<uint>();
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableCharacter = unitOfWork.Characters.Get(client.Player.Id);
                        if (durableCharacter.AccountId != client.AccountEntry.Id)
                            throw new GameplayRejectionException(
                                "Durable character owner changed.");

                        var waypointIds = new HashSet<uint>(
                            client.Player.GainedWaypoints.Select(entry => entry.WaypointId));
                        var logosIds = new HashSet<uint>(client.Player.Logos);
                        IReadOnlySet<uint> durableWaypointIds = null;
                        IReadOnlySet<uint> durableLogosIds = null;

                        foreach (var missionGroup in candidates.GroupBy(
                            candidate => candidate.Definition.MissionId))
                        {
                            var first = missionGroup.First();
                            var durableMission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, first.Definition.MissionId);
                            var durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
                                client.Player.Id, first.Definition.MissionId);
                            if (durableMission?.MissionState != (uint)MissionState.Active ||
                                durableMission.Completeable != first.RuntimeMission.Completeable)
                                throw new GameplayRejectionException(
                                    "Durable mission state is stale.");

                            foreach (var candidate in missionGroup)
                            {
                                if (!durableObjectives.TryGetValue(
                                        candidate.ObjectiveDefinition.ObjectiveId,
                                        out var durableObjective) ||
                                    durableObjective.ObjectiveState !=
                                        (byte)MissionObjectiveState.Incomplete ||
                                    candidate.RuntimeObjective.State !=
                                        MissionObjectiveState.Incomplete)
                                    throw new GameplayRejectionException(
                                        "Durable mission objective state is stale.");

                                var rule = candidate.ObjectiveDefinition.ProgressRule;
                                if (rule.RuleType == MissionProgressRuleType.CompleteDistinctSet)
                                {
                                    IReadOnlySet<uint> runtimeSubjects;
                                    IReadOnlySet<uint> durableSubjects;
                                    if (rule.Kind == MissionProgressEventKind.WaypointAcquired)
                                    {
                                        runtimeSubjects = waypointIds;
                                        durableWaypointIds ??= new HashSet<uint>(
                                            unitOfWork.CharacterTeleporters.Get(client.Player.Id)
                                                .Select(entry => entry.WaypointId));
                                        durableSubjects = durableWaypointIds;
                                    }
                                    else if (rule.Kind == MissionProgressEventKind.LogosAcquired)
                                    {
                                        runtimeSubjects = logosIds;
                                        durableLogosIds ??= new HashSet<uint>(
                                            unitOfWork.CharacterLogoses.GetLogos(client.Player.Id));
                                        durableSubjects = durableLogosIds;
                                    }
                                    else
                                        throw new GameplayRejectionException(
                                            "Distinct progress rule has an unsupported event kind.");

                                    if (!rule.AreAllSubjectsObserved(runtimeSubjects) ||
                                        !rule.AreAllSubjectsObserved(durableSubjects))
                                        throw new GameplayRejectionException(
                                            "Distinct progress subjects are incomplete.");
                                    durableObjective.ObjectiveState =
                                        (byte)MissionObjectiveState.Completed;
                                    publications.Add(ProgressPublication.ForCompleted(candidate));
                                    continue;
                                }

                                if (rule.RuleType == MissionProgressRuleType.IncrementExactCounter)
                                {
                                    var counterId = rule.CounterId.Value;
                                    if (!candidate.RuntimeObjective.Counters.TryGetValue(
                                        counterId, out var runtimeValue))
                                        throw new GameplayRejectionException(
                                            "Runtime mission counter is missing.");
                                    var durableCounter = durableObjective.Counters.SingleOrDefault(
                                        counter => counter.CounterId == counterId);
                                    if (durableCounter == null ||
                                        durableCounter.CounterValue != runtimeValue ||
                                        runtimeValue < rule.InitialValue.Value)
                                        throw new GameplayRejectionException(
                                            "Durable mission counter is stale.");
                                    if (runtimeValue >= rule.TargetValue.Value)
                                        throw new GameplayRejectionException(
                                            "Mission counter cannot advance monotonically.");
                                    var value = runtimeValue + Math.Min(
                                        progress.Quantity,
                                        rule.TargetValue.Value - runtimeValue);
                                    durableCounter.CounterValue = value;
                                    var completed = value == rule.TargetValue.Value;
                                    if (completed)
                                        durableObjective.ObjectiveState =
                                            (byte)MissionObjectiveState.Completed;
                                    publications.Add(ProgressPublication.Counter(
                                        candidate, counterId, value, completed));
                                    continue;
                                }

                                if (rule.RuleType ==
                                    MissionProgressRuleType.IncrementExactItemCounter)
                                {
                                    var itemClassId = rule.CounterId.Value;
                                    if (!candidate.RuntimeObjective.ItemCounters.TryGetValue(
                                        itemClassId, out var runtimeValue))
                                        throw new GameplayRejectionException(
                                            "Runtime mission item counter is missing.");
                                    var durableCounter =
                                        durableObjective.ItemCounters.SingleOrDefault(
                                            counter =>
                                                counter.ItemClassId == itemClassId);
                                    if (durableCounter == null ||
                                        durableCounter.CounterValue != runtimeValue ||
                                        runtimeValue < rule.InitialValue.Value)
                                        throw new GameplayRejectionException(
                                            "Durable mission item counter is stale.");
                                    if (runtimeValue >= rule.TargetValue.Value)
                                        throw new GameplayRejectionException(
                                            "Mission item counter cannot advance monotonically.");
                                    var value = runtimeValue + Math.Min(
                                        progress.Quantity,
                                        rule.TargetValue.Value - runtimeValue);
                                    durableCounter.CounterValue = value;
                                    var completed = value == rule.TargetValue.Value;
                                    if (completed)
                                        durableObjective.ObjectiveState =
                                            (byte)MissionObjectiveState.Completed;
                                    publications.Add(
                                        ProgressPublication.ItemCounter(
                                            candidate,
                                            itemClassId,
                                            value,
                                            completed));
                                    continue;
                                }

                                durableObjective.ObjectiveState =
                                    (byte)MissionObjectiveState.Completed;
                                publications.Add(ProgressPublication.ForCompleted(candidate));
                            }

                            var completeable = first.Definition.Objectives.Values
                                .Where(objective => objective.IsRequired.Value)
                                .All(objective =>
                                    durableObjectives.TryGetValue(
                                        objective.ObjectiveId, out var durableObjective) &&
                                    durableObjective.ObjectiveState ==
                                        (byte)MissionObjectiveState.Completed);
                            durableMission.Completeable = completeable;
                            if (completeable && !first.RuntimeMission.Completeable)
                                completableMissions.Add(first.Definition.MissionId);
                        }
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to record mission progress {progress.Kind}:{progress.SubjectId} " +
                        $"for character {client.Player.Id}: {error}");
                    return false;
                }

                foreach (var publication in publications.Where(
                    publication =>
                        publication.CounterId.HasValue &&
                        !publication.IsItemCounter))
                {
                    publication.Candidate.RuntimeObjective.SetCounter(
                        publication.CounterId.Value, publication.CounterValue.Value);
                    var rule = publication.Candidate.ObjectiveDefinition.ProgressRule;
                    client.CallMethod(
                        client.Player.EntityId,
                        new UpdateObjectiveCounterPacket(
                            publication.Candidate.Definition.MissionId,
                            publication.Candidate.ObjectiveDefinition.ObjectiveId,
                            publication.CounterId.Value,
                            publication.CounterValue.Value,
                            rule.InitialValue.Value,
                            rule.TargetValue.Value));
                }
                foreach (var publication in publications.Where(
                    publication => publication.IsItemCounter))
                {
                    publication.Candidate.RuntimeObjective.SetItemCounter(
                        publication.CounterId.Value,
                        publication.CounterValue.Value);
                    var rule =
                        publication.Candidate.ObjectiveDefinition.ProgressRule;
                    client.CallMethod(
                        client.Player.EntityId,
                        new UpdateObjectiveItemCounterPacket(
                            publication.Candidate.Definition.MissionId,
                            publication.Candidate.ObjectiveDefinition.ObjectiveId,
                            publication.CounterId.Value,
                            publication.CounterValue.Value,
                            rule.TargetValue.Value));
                }
                foreach (var publication in publications.Where(
                    publication => publication.Completed))
                {
                    publication.Candidate.RuntimeObjective.State =
                        MissionObjectiveState.Completed;
                    client.CallMethod(
                        client.Player.EntityId,
                        new ObjectiveCompletedPacket(
                            publication.Candidate.Definition.MissionId,
                            publication.Candidate.ObjectiveDefinition.ObjectiveId));
                }
                foreach (var missionId in completableMissions)
                {
                    client.Player.Missions[missionId].Completeable = true;
                    client.CallMethod(
                        client.Player.EntityId,
                        new MissionCompleteablePacket(missionId, true));
                }
                return publications.Count > 0;
            }
        }

        private static bool CanAdvanceFromRuntime(
            Manifestation player,
            MissionProgressRule rule)
        {
            if (rule.RuleType != MissionProgressRuleType.CompleteDistinctSet)
                return true;
            if (rule.Kind == MissionProgressEventKind.WaypointAcquired)
                return rule.AreAllSubjectsObserved(new HashSet<uint>(
                    player.GainedWaypoints.Select(entry => entry.WaypointId)));
            if (rule.Kind == MissionProgressEventKind.LogosAcquired)
                return rule.AreAllSubjectsObserved(new HashSet<uint>(player.Logos));
            return false;
        }

        private static bool IsPublishedState(MissionState state) =>
            state == MissionState.Active ||
            state == MissionState.Success ||
            state == MissionState.Failed ||
            state == MissionState.Completed;

        internal bool TryGetRewardInfo(uint missionId, out RewardInfo rewardInfo)
        {
            if (_rewardDefinitions.TryGetValue(missionId, out var definition))
            {
                rewardInfo = definition.CreateInfo();
                return true;
            }

            rewardInfo = null;
            return false;
        }

        internal MissionConversationState ClassifyNpcConversation(
            Manifestation player,
            Creature creature)
        {
            var dispensable = new Dictionary<uint, MissionInfo>();
            var objectives = new List<CompleteableObjectives>();
            var completeable = new Dictionary<uint, RewardInfo>();
            var rewardable = new List<RewardableMissions>();

            foreach (var mission in _loadedMissions.Values)
            {
                if (!mission.IsOperational)
                    continue;
                if (!player.Missions.TryGetValue(mission.MissionId, out var log))
                {
                    if (mission.MissionGiver == creature.DbId)
                        dispensable.Add(
                            mission.MissionId,
                            mission.CreateInfo(
                                MissionState.Active,
                                false,
                                mission.CreateInitialObjectiveLogs()));
                    continue;
                }

                if (log.State == MissionState.Active)
                {
                    if (log.Completeable &&
                        mission.MissionReciver == creature.DbId)
                    {
                        var completionReward = _rewardDefinitions.TryGetValue(
                            mission.MissionId, out var definition)
                            ? definition.CreateInfo()
                            : new RewardInfo();
                        completeable.Add(mission.MissionId, completionReward);
                        continue;
                    }

                    foreach (var objective in mission.Objectives.Values)
                    {
                        if (!log.Objectives.TryGetValue(objective.ObjectiveId, out var objectiveLog) ||
                            objectiveLog.State != MissionObjectiveState.Incomplete)
                            continue;
                        foreach (var conversation in objective.Conversations.Where(conversation =>
                            conversation.Type == MissionObjectiveConversationType.Completion &&
                            conversation.NpcPackageId == creature.Npc.NpcPackageId))
                            objectives.Add(new CompleteableObjectives(
                                (int)mission.MissionId,
                                (int)objective.ObjectiveId,
                                (int)conversation.PlayerFlagId));
                    }
                }
                else if (log.State == MissionState.Success &&
                    mission.MissionReciver == creature.DbId &&
                    TryGetRewardInfo(mission.MissionId, out var reward))
                {
                    rewardable.Add(new RewardableMissions((int)mission.MissionId, reward));
                }
            }

            return new MissionConversationState(
                dispensable,
                objectives,
                completeable,
                rewardable);
        }

        private static bool TryHydrateObjectives(
            Mission definition,
            CharacterMissionProgressSnapshot progress,
            out IReadOnlyDictionary<uint, MissionObjectiveLog> objectives)
        {
            objectives = null;
            if (definition.Objectives.Count == 0)
            {
                if (progress.Missions.TryGetValue(definition.MissionId, out var emptyProgress) &&
                    emptyProgress.Objectives.Count != 0)
                    return false;
                objectives = new Dictionary<uint, MissionObjectiveLog>();
                return true;
            }
            if (!progress.Missions.TryGetValue(definition.MissionId, out var missionProgress))
                return false;
            if (missionProgress.Objectives.Keys.Except(definition.Objectives.Keys).Any() ||
                definition.Objectives.Keys.Except(missionProgress.Objectives.Keys).Any())
                return false;

            var result = new Dictionary<uint, MissionObjectiveLog>();
            foreach (var objectiveDefinition in definition.Objectives.Values)
            {
                if (!missionProgress.Objectives.TryGetValue(
                        objectiveDefinition.ObjectiveId, out var objectiveProgress) ||
                    !Enum.IsDefined(typeof(MissionObjectiveState), (int)objectiveProgress.State) ||
                    objectiveDefinition.Counters.Keys.Except(objectiveProgress.Counters.Keys).Any() ||
                    objectiveProgress.Counters.Keys.Except(objectiveDefinition.Counters.Keys).Any() ||
                    objectiveDefinition.ItemCounters.Keys.Except(objectiveProgress.ItemCounters.Keys).Any() ||
                    objectiveProgress.ItemCounters.Keys.Except(objectiveDefinition.ItemCounters.Keys).Any())
                    return false;

                result.Add(
                    objectiveDefinition.ObjectiveId,
                    new MissionObjectiveLog(
                        objectiveDefinition.ObjectiveId,
                        (MissionObjectiveState)objectiveProgress.State,
                        objectiveProgress.Counters,
                        objectiveProgress.ItemCounters));
            }

            objectives = result;
            return true;
        }

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

        internal static void TryPublish(Action publication, string description)
        {
            try
            {
                publication();
            }
            catch (Exception error)
            {
                Logger.WriteLog(
                    LogType.Error,
                    $"Unable to publish {description}; durable state is already committed: {error}");
            }
        }

        private readonly struct ProgressCandidate
        {
            internal Mission Definition { get; }
            internal MissionLog RuntimeMission { get; }
            internal MissionObjectiveDefinition ObjectiveDefinition { get; }
            internal MissionObjectiveLog RuntimeObjective { get; }

            internal ProgressCandidate(
                Mission definition,
                MissionLog runtimeMission,
                MissionObjectiveDefinition objectiveDefinition,
                MissionObjectiveLog runtimeObjective)
            {
                Definition = definition;
                RuntimeMission = runtimeMission;
                ObjectiveDefinition = objectiveDefinition;
                RuntimeObjective = runtimeObjective;
            }
        }

        private readonly struct ProgressPublication
        {
            internal ProgressCandidate Candidate { get; }
            internal uint? CounterId { get; }
            internal uint? CounterValue { get; }
            internal bool Completed { get; }
            internal bool IsItemCounter { get; }

            private ProgressPublication(
                ProgressCandidate candidate,
                uint? counterId,
                uint? counterValue,
                bool completed,
                bool isItemCounter = false)
            {
                Candidate = candidate;
                CounterId = counterId;
                CounterValue = counterValue;
                Completed = completed;
                IsItemCounter = isItemCounter;
            }

            internal static ProgressPublication ForCompleted(ProgressCandidate candidate) =>
                new(candidate, null, null, true);

            internal static ProgressPublication Counter(
                ProgressCandidate candidate,
                uint counterId,
                uint counterValue,
                bool completed) =>
                new(candidate, counterId, counterValue, completed);

            internal static ProgressPublication ItemCounter(
                ProgressCandidate candidate,
                uint itemClassId,
                uint counterValue,
                bool completed) =>
                new(
                    candidate,
                    itemClassId,
                    counterValue,
                    completed,
                    isItemCounter: true);
        }
    }
}
