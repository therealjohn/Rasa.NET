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

        internal MissionRewardGrant CreateGrant(int? selectionIndex)
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
                return new MissionRewardGrant(Experience, Currencies, FixedItems);
            }
            if (!selectionIndex.HasValue ||
                selectionIndex.Value < 0 ||
                selectionIndex.Value >= SelectableItems.Count)
                throw new GameplayRejectionException("Mission reward selection is invalid.");

            return new MissionRewardGrant(
                Experience,
                Currencies,
                FixedItems.Concat(new[] { SelectableItems[selectionIndex.Value] }).ToArray());
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
        private readonly IReadOnlyDictionary<uint, Mission> _loadedMissionsView;
        private readonly IReadOnlyDictionary<uint, MissionRewardDefinition> _rewardDefinitions;
        private readonly ManifestationManager _manifestationManager;

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
            ManifestationManager manifestationManager)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _loadedMissions = definitions.ToDictionary(entry => entry.Key, entry => entry.Value);
            _loadedMissionsView = new ReadOnlyDictionary<uint, Mission>(_loadedMissions);
            _rewardDefinitions = new Dictionary<uint, MissionRewardDefinition>(
                rewardDefinitions ?? new Dictionary<uint, MissionRewardDefinition>());
            _manifestationManager = manifestationManager;
        }

        public void LoadMissions()
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            foreach (var mission in unitOfWork.NpcMissions.Get())
                _loadedMissions[mission.Id] = new Mission(mission);
            foreach (var recovered in MissionDefinitionCatalog.CreateRecoveredInactiveDefinitions())
            {
                _loadedMissions.TryGetValue(recovered.Key, out var worldDefinition);
                _loadedMissions[recovered.Key] = recovered.Value.WithWorldMetadata(worldDefinition);
            }
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
                        if (unitOfWork.CharacterMissions.Get(client.Player.Id, missionId) != null)
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
            int? rating) =>
            TryGrantNpcMission(
                client,
                npcEntityId,
                missionId,
                selectionIndex,
                rating,
                MissionState.Active,
                requireCompletable: true,
                publishCompleted: true);

        internal bool TryCompleteNpcMission(
            Client client,
            ulong npcEntityId,
            uint missionId,
            int selectionIndex) =>
            TryCompleteNpcMission(client, npcEntityId, missionId, selectionIndex, null);

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
                            var mission = unitOfWork.CharacterMissions.Get(client.Player.Id, missionId);
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
                        new MissionLog(
                            missionId,
                            MissionState.Completed,
                            false,
                            runtimeMission.Objectives);
                    client.CallMethod(client.Player.EntityId,
                        new MissionCompleteablePacket(missionId, false));
                    if (publishCompleted)
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
                var completeable = false;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission = unitOfWork.CharacterMissions.Get(client.Player.Id, missionId);
                        var durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
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
                        foreach (var successorId in revealed.Except(activated))
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
                                (byte)MissionObjectiveState.Incomplete)
                                continue;
                            if (successor.ObjectiveState is not
                                ((byte)MissionObjectiveState.Inactive) and not
                                ((byte)MissionObjectiveState.NotAssigned))
                                throw new GameplayRejectionException(
                                    "Configured activated mission objective is not available.");
                            successor.ObjectiveState = (byte)MissionObjectiveState.Incomplete;
                            appliedActivated.Add(successorId);
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

                runtimeObjective.State = MissionObjectiveState.Completed;
                foreach (var successorId in appliedRevealed)
                    runtimeMission.Objectives[successorId].State = MissionObjectiveState.NotAssigned;
                foreach (var successorId in appliedActivated)
                    runtimeMission.Objectives[successorId].State = MissionObjectiveState.Incomplete;
                runtimeMission.Completeable = completeable;

                client.CallMethod(client.Player.EntityId,
                    new ObjectiveCompletedPacket(missionId, objectiveId));
                foreach (var successorId in revealed.Where(successorId =>
                    appliedRevealed.Contains(successorId) ||
                    appliedActivated.Contains(successorId)))
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
                        mission.MissionReciver == creature.DbId &&
                        TryGetRewardInfo(mission.MissionId, out var completionReward))
                    {
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
    }
}
