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
    using Packets;
    using Packets.MapChannel.Server;
    using Packets.Mission.Server;
    using Repositories.Char;
    using Repositories.Char.CharacterMissionProgress;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;
    using Structures.Missions;
    using Structures.World;

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

        internal MissionRewardGrant CreateScenarioGrant(
            Action<Item> beforeItemPublication = null)
        {
            if (Currencies.Any(entry =>
                    entry.Value < 0 ||
                    entry.Key is not CurencyType.Credits and not CurencyType.Prestige))
                throw new GameplayRejectionException("Mission reward contains an unsupported currency.");
            if (FixedItems.Any(item => item.ItemTemplateId == 0 || item.Quantity == 0) ||
                SelectableItems.Any(item => item.ItemTemplateId == 0 || item.Quantity == 0))
                throw new GameplayRejectionException("Mission reward contains an invalid item.");
            if (SelectableItems.Count > 0)
                throw new GameplayRejectionException(
                    "Scenario reward packages must not contain selectable alternatives.");

            return new MissionRewardGrant(
                Experience,
                Currencies,
                FixedItems.ToArray(),
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

        internal IReadOnlyList<MissionProgressEvent> CreateItemAcquisitionEvents() =>
            _items
                .Select(item =>
                    ItemManager.Instance.ItemTemplateItemClass.TryGetValue(
                        item.ItemTemplateId,
                        out var itemClass)
                        ? new
                        {
                            ItemClassId = (uint)itemClass,
                            item.Quantity
                        }
                        : null)
                .Where(item => item != null)
                .GroupBy(item => item.ItemClassId)
                .Select(group => MissionProgressEvent.ItemAcquired(
                    group.Key,
                    group.Aggregate(
                        0U,
                        (total, item) => checked(total + item.Quantity))))
                .ToArray();

        public void Dispose() => _inventory.Dispose();
    }

    public class MissionManager
    {
        private const int MissionLogCapacity = 30;
        private const uint InitiationMissionId = 1990;
        private static MissionManager _instance;
        private static readonly object InstanceLock = new();
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly Dictionary<uint, Mission> _loadedMissions;
        private readonly IReadOnlyDictionary<uint, Mission> _loadedMissionsView;
        private readonly Dictionary<uint, MissionRewardDefinition> _rewardDefinitions;
        private readonly Dictionary<uint, IReadOnlyDictionary<uint, MissionRewardDefinition>> _rewardPackagesByMission;
        private readonly Dictionary<uint, IReadOnlyList<MissionPrerequisiteDefinition>> _prerequisitesByMission;
        private readonly Dictionary<uint, IReadOnlyDictionary<uint, MissionAreaDefinition>> _areasByMission;
        private readonly Dictionary<uint, IReadOnlyDictionary<uint, MissionSpawnGroupDefinition>> _spawnGroupsByMission;
        private readonly Dictionary<uint, IReadOnlyDictionary<uint, MissionScenarioDefinition>> _scenariosByMission;
        private readonly ManifestationManager _manifestationManager;
        private readonly Action<Item> _beforeRewardItemPublication;
        private readonly Action<PythonPacket> _beforeMissionPacketPublication;
        private readonly MissionDeadlineService _deadlineService;
        private readonly IMissionScenarioService _scenarioService;
        private readonly Func<DateTime> _utcNow;

        public IReadOnlyDictionary<uint, Mission> LoadedMissions => _loadedMissionsView;
        internal IMissionScenarioService ScenarioService => _scenarioService;
        internal Action<Item> BeforeRewardItemPublication => _beforeRewardItemPublication;
        internal MissionValidationReport LatestValidationReport { get; private set; } =
            new MissionValidationReport(
                Array.Empty<MissionValidationDiagnostic>(),
                Array.Empty<uint>());

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
            IReadOnlyDictionary<uint, Mission> definitions,
            MissionDeadlineService deadlineService = null)
            : this(
                gameUnitOfWorkFactory,
                definitions,
                new Dictionary<uint, MissionRewardDefinition>(),
                new ManifestationManager(gameUnitOfWorkFactory),
                deadlineService: deadlineService)
        {
        }

        internal MissionManager(
            IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            IReadOnlyDictionary<uint, Mission> definitions,
            IReadOnlyDictionary<uint, MissionRewardDefinition> rewardDefinitions,
            ManifestationManager manifestationManager,
            Action<Item> beforeRewardItemPublication = null,
            Action<PythonPacket> beforeMissionPacketPublication = null,
            MissionDeadlineService deadlineService = null,
            IMissionScenarioService scenarioService = null,
            Func<DateTime> utcNow = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _loadedMissions = definitions.ToDictionary(entry => entry.Key, entry => entry.Value);
            _loadedMissionsView = new ReadOnlyDictionary<uint, Mission>(_loadedMissions);
            _rewardDefinitions = new Dictionary<uint, MissionRewardDefinition>(
                rewardDefinitions ?? new Dictionary<uint, MissionRewardDefinition>());
            _rewardPackagesByMission = new Dictionary<uint, IReadOnlyDictionary<uint, MissionRewardDefinition>>();
            _prerequisitesByMission = new Dictionary<uint, IReadOnlyList<MissionPrerequisiteDefinition>>();
            _areasByMission = new Dictionary<uint, IReadOnlyDictionary<uint, MissionAreaDefinition>>();
            _spawnGroupsByMission = new Dictionary<uint, IReadOnlyDictionary<uint, MissionSpawnGroupDefinition>>();
            _scenariosByMission = new Dictionary<uint, IReadOnlyDictionary<uint, MissionScenarioDefinition>>();
            _manifestationManager = manifestationManager;
            _beforeRewardItemPublication = beforeRewardItemPublication;
            _beforeMissionPacketPublication = beforeMissionPacketPublication;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _deadlineService = deadlineService ?? new MissionDeadlineService(
                () => _gameUnitOfWorkFactory,
                () => this,
                _utcNow);
            _scenarioService = scenarioService ?? new MissionScenarioService(
                () => _gameUnitOfWorkFactory,
                () => this,
                _manifestationManager);
        }

        internal MissionValidationReport LoadMissions()
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            _loadedMissions.Clear();
            _rewardDefinitions.Clear();
            _rewardPackagesByMission.Clear();
            _prerequisitesByMission.Clear();
            _areasByMission.Clear();
            _spawnGroupsByMission.Clear();
            _scenariosByMission.Clear();

            if (unitOfWork.MissionContent != null)
            {
                var snapshot = new MissionContentLoader().Load(unitOfWork.MissionContent);
                var report = new MissionContentValidator().Validate(snapshot, unitOfWork);
                foreach (var definition in MissionDefinitionCatalog.CreateDefinitions(snapshot, report))
                    _loadedMissions[definition.Key] = definition.Value;
                foreach (var reward in MissionDefinitionCatalog.CreateRewardDefinitions(snapshot, report))
                    _rewardDefinitions[reward.Key] = reward.Value;
                foreach (var rewardPackages in MissionDefinitionCatalog.CreateRewardPackages(snapshot, report))
                    _rewardPackagesByMission[rewardPackages.Key] = rewardPackages.Value;
                foreach (var definition in snapshot.Definitions.Values)
                {
                    _prerequisitesByMission[definition.MissionId] = definition.Prerequisites;
                    _areasByMission[definition.MissionId] = definition.Areas;
                    _spawnGroupsByMission[definition.MissionId] = definition.SpawnGroups;
                    _scenariosByMission[definition.MissionId] = definition.Scenarios;
                }
                LatestValidationReport = report;
                return report;
            }

            foreach (var mission in unitOfWork.NpcMissions.Get())
            {
                var rewardRows = unitOfWork.NpcMissionRewards.Get(mission.Id);
                // Temporary compatibility gate until Task 3 replaces legacy npc_mission loading
                // with mission_content_definition hydration.
                var diagnostic = rewardRows.Count == 0
                    ? "legacy npc_mission rows stay inactive until mission_content_definition loading replaces this temporary loader"
                    : "legacy npc_mission rows stay inactive until mission_content_definition loading replaces this temporary loader; mission reward rows use an undocumented type contract";
                var definition = new Mission(mission).DisableOperational(diagnostic);
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
            LatestValidationReport = new MissionValidationReport(
                Array.Empty<MissionValidationDiagnostic>(),
                Array.Empty<uint>());
            return LatestValidationReport;
        }

        internal void Hydrate(
            Manifestation player,
            IReadOnlyList<CharacterMissionEntry> rows,
            CharacterMissionProgressSnapshot progress)
        {
            player.Missions = BuildHydration(player.Id, rows, progress).Missions;
        }

        internal void HydrateAndClearInvalid(
            Manifestation player,
            ICharUnitOfWork unitOfWork)
        {
            HydrationResult result = null;
            unitOfWork.ExecuteTransaction(() =>
            {
                result = BuildHydration(
                    player.Id,
                    unitOfWork.CharacterMissions.Get(player.Id),
                    unitOfWork.CharacterMissionProgress.Get(player.Id));
                foreach (var missionId in result.InvalidMissionIds)
                    unitOfWork.CharacterMissions.Remove(player.Id, missionId);
            });
            player.Missions = result.Missions;
        }

        private HydrationResult BuildHydration(
            uint characterId,
            IReadOnlyList<CharacterMissionEntry> rows,
            CharacterMissionProgressSnapshot progress)
        {
            var hydrated = new Dictionary<uint, MissionLog>();
            var invalid = new List<uint>();
            foreach (var row in rows)
            {
                if (!TryGetOperationalMission(row.MissionId, out var definition))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Cleared mission {row.MissionId} for character {characterId}: definition is not operational.");
                    invalid.Add(row.MissionId);
                    continue;
                }

                var state = (MissionState)row.MissionState;
                if (!IsPublishedState(state))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Cleared mission {row.MissionId} for character {characterId}: unsupported state {row.MissionState}.");
                    invalid.Add(row.MissionId);
                    continue;
                }

                if (!TryHydrateObjectives(definition, progress, out var objectives))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Cleared mission {row.MissionId} for character {characterId}: objective state is incomplete or invalid.");
                    invalid.Add(row.MissionId);
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
                        $"Cleared mission {row.MissionId} for character {characterId}: completable state does not match objectives.");
                    invalid.Add(row.MissionId);
                    continue;
                }

                hydrated[row.MissionId] = new MissionLog(
                    row.MissionId,
                    state,
                    derivedCompleteable,
                    objectives);
            }

            return new HydrationResult(hydrated, invalid);
        }

        internal void Hydrate(Manifestation player, IReadOnlyList<CharacterMissionEntry> rows) =>
            Hydrate(player, rows, CharacterMissionProgressSnapshot.Empty);

        private sealed class HydrationResult
        {
            internal Dictionary<uint, MissionLog> Missions { get; }
            internal IReadOnlyList<uint> InvalidMissionIds { get; }

            internal HydrationResult(
                Dictionary<uint, MissionLog> missions,
                IReadOnlyList<uint> invalidMissionIds)
            {
                Missions = missions;
                InvalidMissionIds = invalidMissionIds;
            }
        }

        public IReadOnlyDictionary<uint, MissionInfo> BuildStatusSnapshot(Manifestation player)
        {
            var snapshot = new Dictionary<uint, MissionInfo>();
            foreach (var entry in player.Missions)
            {
                if (!TryGetOperationalMission(entry.Key, out var definition) ||
                    !IsPublishedState(entry.Value.State))
                    continue;

                snapshot.Add(entry.Key, BuildPublishedMissionInfo(
                    player,
                    definition,
                    entry.Value));
            }

            return snapshot;
        }

        internal void PublishMissionStatus(Client client, uint missionId, string description)
        {
            if (client?.Player == null ||
                !client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                !TryGetOperationalMission(missionId, out var definition) ||
                !IsPublishedState(runtimeMission.State))
                return;

            PublishMissionPacket(
                client,
                new MissionStatusInfoPacket(
                    new Dictionary<uint, MissionInfo>
                    {
                        [missionId] = BuildPublishedMissionInfo(
                            client.Player,
                            definition,
                            runtimeMission)
                    }),
                description);
        }

        private MissionInfo BuildPublishedMissionInfo(
            Manifestation player,
            Mission definition,
            MissionLog runtimeMission)
        {
            uint activeDeadlineObjectiveId = 0;
            uint? timeRemaining = null;
            if (player != null &&
                definition != null &&
                runtimeMission != null &&
                runtimeMission.State == MissionState.Active &&
                definition.Objectives.Values.Any(objective =>
                    objective.GetExecutableTransitionsOrLegacyDefault().Any(transition =>
                        transition.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed)))
            {
                using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                var deadline = unitOfWork.CharacterMissionDeadlines.Get(
                    player.Id,
                    definition.MissionId);
                if (deadline?.State == CharacterMissionDeadlineState.Active)
                {
                    var timedObjectives = definition.Objectives.Values
                        .Where(objective =>
                            objective.GetExecutableTransitionsOrLegacyDefault().Any(transition =>
                                transition.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed) &&
                            runtimeMission.Objectives.TryGetValue(objective.ObjectiveId, out var runtimeObjective) &&
                            runtimeObjective.State == MissionObjectiveState.Incomplete)
                        .Select(objective => objective.ObjectiveId)
                        .ToArray();
                    if (timedObjectives.Length == 1)
                    {
                        activeDeadlineObjectiveId = timedObjectives[0];
                        var remaining = deadline.DueAtUtc - _utcNow();
                        timeRemaining = remaining <= TimeSpan.Zero
                            ? 0U
                            : checked((uint)Math.Ceiling(remaining.TotalSeconds));
                    }
                }
            }

            return definition.CreateInfo(
                runtimeMission.State,
                runtimeMission.Completeable,
                runtimeMission.Objectives,
                objectiveId =>
                    objectiveId == activeDeadlineObjectiveId
                        ? timeRemaining
                        : null);
        }

        public void PublishInitialState(Client client)
        {
            PublishMissionPacket(
                client,
                new MissionStatusInfoPacket(BuildStatusSnapshot(client.Player)),
                "mission status snapshot");
        }

        internal bool TryGetAreaDefinition(
            uint missionId,
            uint areaId,
            out MissionAreaDefinition area)
        {
            if (_areasByMission.TryGetValue(missionId, out var areas) &&
                areas.TryGetValue(areaId, out area))
                return true;

            area = null;
            return false;
        }

        internal bool TryGetSpawnGroupDefinition(
            uint missionId,
            uint spawnGroupId,
            out MissionSpawnGroupDefinition spawnGroup)
        {
            if (_spawnGroupsByMission.TryGetValue(missionId, out var spawnGroups) &&
                spawnGroups.TryGetValue(spawnGroupId, out spawnGroup))
                return true;

            spawnGroup = null;
            return false;
        }

        internal bool TryGetScenarioDefinitions(
            uint missionId,
            out IReadOnlyDictionary<uint, MissionScenarioDefinition> scenarios) =>
            _scenariosByMission.TryGetValue(missionId, out scenarios);

        internal bool TryGetScenarioDefinition(
            uint missionId,
            uint scenarioId,
            out MissionScenarioDefinition scenario)
        {
            if (_scenariosByMission.TryGetValue(missionId, out var scenarios) &&
                scenarios.TryGetValue(scenarioId, out scenario))
                return true;

            scenario = null;
            return false;
        }

        internal IReadOnlyDictionary<uint, MissionRewardDefinition> GetRewardPackages(uint missionId) =>
            _rewardPackagesByMission.TryGetValue(missionId, out var rewards)
                ? rewards
                : new Dictionary<uint, MissionRewardDefinition>();

        private bool TryGetScenarioStepDefinition(
            uint missionId,
            uint scenarioId,
            uint stepId,
            out MissionScenarioStepDefinition step)
        {
            step = null;
            if (!_scenariosByMission.TryGetValue(missionId, out var scenarios) ||
                !scenarios.TryGetValue(scenarioId, out var scenario))
                return false;

            step = scenario.Steps.SingleOrDefault(candidate => candidate.StepId == stepId);
            return step != null;
        }

        internal bool TryRecordScenarioEvent(
            Client client,
            uint missionId,
            uint scenarioId,
            uint stepId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    !TryGetOperationalMission(missionId, out _) ||
                    !client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                    runtimeMission.State != MissionState.Active ||
                    !TryGetScenarioStepDefinition(
                        missionId,
                        scenarioId,
                        stepId,
                        out var stepDefinition))
                    return false;

                var progressPlan = MissionProgressPublicationPlan.Empty;
                var committed = false;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission =
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id,
                                missionId);
                        if (durableMission?.MissionState != (uint)MissionState.Active)
                            throw new GameplayRejectionException(
                                "Durable mission is not active.");

                        var stepKey = CreateScenarioStepKey(scenarioId, stepId);
                        if (unitOfWork.CharacterMissionScenario.HasStep(
                                client.Player.Id,
                                missionId,
                                stepKey))
                            return;

                        unitOfWork.CharacterMissionScenario.Add(
                            new CharacterMissionScenarioStepEntry(
                                client.Player.Id,
                                missionId,
                                stepKey));
                        progressPlan = PlanScenarioEventProgress(
                            client,
                            missionId,
                            scenarioId,
                            stepDefinition.ScenarioEventId ?? stepId,
                            unitOfWork);
                        committed = true;
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to record scenario step {scenarioId}:{stepId} for mission {missionId}: {error}");
                    return false;
                }

                progressPlan.Publish(client);
                return committed;
            }
        }

        internal bool TryEmitScenarioProgressEvent(
            Client client,
            uint missionId,
            uint scenarioId,
            uint scenarioEventId)
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

                var progressPlan = MissionProgressPublicationPlan.Empty;
                var committed = false;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission =
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id,
                                missionId);
                        if (durableMission?.MissionState != (uint)MissionState.Active)
                            throw new GameplayRejectionException(
                                "Durable mission is not active.");

                        progressPlan = PlanScenarioEventProgress(
                            client,
                            missionId,
                            scenarioId,
                            scenarioEventId,
                            unitOfWork);
                        committed = progressPlan.HasChanges;
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to emit scenario event {scenarioId}:{scenarioEventId} for mission {missionId}: {error}");
                    return false;
                }

                progressPlan.Publish(client);
                return committed;
            }
        }

        internal bool EvaluateDeadlines(Client client) =>
            _deadlineService.Evaluate(client);

        internal bool TryExecuteScenario(Client client, uint missionId, uint scenarioId) =>
            _scenarioService.TryExecute(client, missionId, scenarioId);

        internal bool TryExecuteFailureTransitionScenario(Client client, uint missionId, uint scenarioId) =>
            _scenarioService.TryExecuteFailureTransition(client, missionId, scenarioId);

        internal bool TickScenarios(Client client) =>
            _scenarioService.Tick(client);

        private static bool ShouldDeferBootcampDepartureScenario(
            Client client,
            uint missionId,
            uint scenarioId) =>
            client?.Player?.MapContextId == CharacterManager.BootcampPrivateMapContextId &&
            ((missionId == 1995 && scenarioId == 6) ||
             (missionId == 2005 && scenarioId == 5));

        private static void PublishStartedScenarios(
            Client client,
            uint missionId,
            IEnumerable<uint> scenarioIds,
            Func<Client, uint, uint, bool> startScenario)
        {
            foreach (var scenarioId in scenarioIds ?? Array.Empty<uint>())
            {
                if (ShouldDeferBootcampDepartureScenario(client, missionId, scenarioId))
                    continue;
                TryPublish(
                    () => startScenario?.Invoke(client, missionId, scenarioId),
                    $"mission {missionId} start scenario {scenarioId}");
            }
        }

        internal void RecordScenarioCreatureDeath(SpawnPool spawnPool) =>
            (_scenarioService as MissionScenarioService)?.RecordScenarioCreatureDeath(spawnPool);

        internal void RebuildScenarioRuntime(uint characterId, MapChannel mapChannel) =>
            _scenarioService.Rebuild(characterId, mapChannel);

        private static string CreateScenarioStepKey(uint scenarioId, uint stepId) =>
            $"scenario:{scenarioId}:step:{stepId}";

        private MissionProgressPublicationPlan PlanScenarioEventProgress(
            Client client,
            uint missionId,
            uint scenarioId,
            uint scenarioEventId,
            ICharUnitOfWork unitOfWork) =>
            PlanProgress(
                client,
                new[]
                {
                    MissionProgressEvent.Scenario(
                        missionId,
                        scenarioId,
                        scenarioEventId)
                },
                unitOfWork);

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
                if (!ArePrerequisitesSatisfied(client.Player, missionId, out var prerequisiteFailure))
                    return Reject($"Rejected mission {missionId}: {prerequisiteFailure}");
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
                        if (!ArePrerequisitesSatisfied(
                                client.Player,
                                missionId,
                                unitOfWork,
                                out prerequisiteFailure))
                            return;
                        if (MissionOutputsAlreadySatisfied(
                                client,
                                missionId,
                                unitOfWork,
                                out prerequisiteFailure))
                            return;
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
                        _deadlineService.SynchronizeMission(
                            unitOfWork,
                            client.Player.Id,
                            definition,
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id,
                                missionId),
                            unitOfWork.CharacterMissionProgress.GetTracked(
                                client.Player.Id,
                                missionId));
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
                    return Reject(string.IsNullOrWhiteSpace(prerequisiteFailure)
                        ? $"Rejected mission {missionId}: character {client.Player.Id} already has it."
                        : $"Rejected mission {missionId}: {prerequisiteFailure}");

                client.Player.Missions.Add(missionId, log);
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionGainedPacket(
                        missionId,
                        BuildPublishedMissionInfo(
                            client.Player,
                            definition,
                            log)));
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

                var progressPlan = MissionProgressPublicationPlan.Empty;
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
                        _deadlineService.SynchronizeMission(
                            unitOfWork,
                            client.Player.Id,
                            definition,
                            durableMission,
                            unitOfWork.CharacterMissionProgress.GetTracked(
                                client.Player.Id,
                                missionId));
                        progressPlan = PlanProgress(
                            client,
                            new[] { MissionProgressEvent.Mission(missionId) },
                            unitOfWork);
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
                TryPublish(
                    () => client.CallMethod(
                        client.Player.EntityId,
                        new MissionCompleteablePacket(missionId, false)),
                    $"mission {missionId} no longer completable");
                TryPublish(
                    () => client.CallMethod(
                        client.Player.EntityId,
                        new MissionCompletedPacket(missionId)),
                    $"mission {missionId} completed");
                progressPlan.Publish(client);
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
                var progressPlan = MissionProgressPublicationPlan.Empty;
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
                            progressPlan = PlanProgress(
                                client,
                                grant.CreateItemAcquisitionEvents(),
                                unitOfWork);
                            mission.MissionState = (uint)MissionState.Completed;
                            mission.Completeable = false;
                            _deadlineService.SynchronizeMission(
                                unitOfWork,
                                client.Player.Id,
                                definition,
                                mission,
                                unitOfWork.CharacterMissionProgress.GetTracked(
                                    client.Player.Id,
                                    missionId));
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
                    progressPlan.Publish(client);
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
                    !TryGetMatchingConversationTransition(
                        objectiveDefinition,
                        npc.Npc.NpcPackageId,
                        playerFlagId,
                        out var transition))
                    return Reject($"Rejected mission {missionId} objective {objectiveId}: completion binding does not match.");

                var npcPackageId = npc.Npc.NpcPackageId;
                var actionApplication = TransitionActionApplication.Empty;
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
                        actionApplication = ApplyTransitionActions(
                            objectiveId,
                            transition,
                            durableObjectives);

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
                        _deadlineService.SynchronizeMission(
                            unitOfWork,
                            client.Player.Id,
                            definition,
                            durableMission,
                            durableObjectives);
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to complete mission {missionId} objective {objectiveId} for character {client.Player.Id}: {error}");
                    return false;
                }

                foreach (var touchedObjectiveId in actionApplication.FinalObjectiveStates.Keys
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
                foreach (var successorId in actionApplication.RevealedObjectiveIds)
                    client.CallMethod(client.Player.EntityId,
                        new ObjectiveRevealedPacket(
                            missionId,
                            successorId,
                            BuildPublishedMissionInfo(
                                client.Player,
                                definition,
                                runtimeMission)));
                foreach (var successorId in actionApplication.ActivatedObjectiveIds)
                    client.CallMethod(client.Player.EntityId,
                        new ObjectiveActivatedPacket(missionId, successorId));
                if (completeable)
                    client.CallMethod(client.Player.EntityId,
                        new MissionCompleteablePacket(missionId, true));
                foreach (var scenarioId in actionApplication.StartScenarioIds)
                    if (!ShouldDeferBootcampDepartureScenario(client, missionId, scenarioId))
                        _scenarioService.TryExecute(client, missionId, scenarioId);
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

                var publicationPlan = MissionFailurePublicationPlan.Empty;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableMission =
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId);
                        var durableObjectives =
                            unitOfWork.CharacterMissionProgress.GetTracked(
                                client.Player.Id, missionId);
                        durableObjectives.TryGetValue(
                            objectiveId, out var durableObjective);
                        if (durableMission?.MissionState != (uint)MissionState.Active ||
                            durableObjective?.ObjectiveState !=
                            (byte)MissionObjectiveState.Incomplete)
                            throw new GameplayRejectionException(
                                "Durable mission objective cannot fail from its current state.");

                        durableObjective.ObjectiveState =
                            (byte)MissionObjectiveState.Failed;
                        var failMission = objectiveDefinition.IsRequired.Value;
                        var completeable = !failMission &&
                            definition.Objectives.Values
                                .Where(objective => objective.IsRequired.Value)
                                .All(objective =>
                                    durableObjectives.TryGetValue(
                                        objective.ObjectiveId,
                                        out var requiredObjective) &&
                                    requiredObjective.ObjectiveState ==
                                        (byte)MissionObjectiveState.Completed);
                        var completeableChanged =
                            durableMission.Completeable != completeable;
                        durableMission.Completeable = completeable;
                        if (failMission)
                            durableMission.MissionState = (uint)MissionState.Failed;
                        _deadlineService.SynchronizeMission(
                            unitOfWork,
                            client.Player.Id,
                            definition,
                            durableMission,
                            durableObjectives);
                        publicationPlan = new MissionFailurePublicationPlan(
                            missionId,
                            objectiveId,
                            failMission ? MissionState.Failed : MissionState.Active,
                            completeable,
                            completeableChanged,
                            publishMissionStatus:
                                objectiveDefinition.GetExecutableTransitionsOrLegacyDefault()
                                    .Any(transition =>
                                        transition.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed));
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to fail mission {missionId} objective {objectiveId}: {error}");
                    return false;
                }

                publicationPlan.Publish(client, this);
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
                        _deadlineService.SynchronizeMission(
                            unitOfWork,
                            client.Player.Id,
                            _loadedMissions[missionId],
                            durableMission,
                            unitOfWork.CharacterMissionProgress.GetTracked(
                                client.Player.Id,
                                missionId));
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

        internal bool TryPlanScenarioObjectiveAction(
            Client client,
            uint missionId,
            uint objectiveId,
            MissionScenarioStepKind actionKind,
            ICharUnitOfWork unitOfWork,
            MissionScenarioPlan scenarioPlan)
        {
            if (client?.Player == null ||
                unitOfWork == null ||
                scenarioPlan == null ||
                !TryGetOperationalMission(missionId, out var definition) ||
                !client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                runtimeMission.State != MissionState.Active ||
                !definition.Objectives.TryGetValue(objectiveId, out var objectiveDefinition) ||
                !runtimeMission.Objectives.TryGetValue(objectiveId, out var runtimeObjective))
                return false;

            var durableMission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                client.Player.Id,
                missionId);
            var durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
                client.Player.Id,
                missionId);
            if (durableMission?.MissionState != (uint)MissionState.Active ||
                !durableObjectives.TryGetValue(objectiveId, out var durableObjective))
                return false;

            switch (actionKind)
            {
                case MissionScenarioStepKind.RevealObjective:
                    if (durableObjective.ObjectiveState != (byte)MissionObjectiveState.Inactive)
                        return durableObjective.ObjectiveState ==
                            (byte)MissionObjectiveState.NotAssigned;

                    durableObjective.ObjectiveState = (byte)MissionObjectiveState.NotAssigned;
                    scenarioPlan.AddRuntimeConvergence(() =>
                        runtimeObjective.State = MissionObjectiveState.NotAssigned);
                    scenarioPlan.AddPublication(() =>
                        PublishMissionPacket(
                            client,
                            new ObjectiveRevealedPacket(
                                missionId,
                                objectiveId,
                                BuildPublishedMissionInfo(
                                    client.Player,
                                    definition,
                                    runtimeMission)),
                            $"mission {missionId} objective {objectiveId} revealed"));
                    return true;

                case MissionScenarioStepKind.ActivateObjective:
                    if (durableObjective.ObjectiveState is
                        (byte)MissionObjectiveState.Incomplete or
                        (byte)MissionObjectiveState.Completed or
                        (byte)MissionObjectiveState.Failed)
                        return true;
                    if (durableObjective.ObjectiveState is not
                        ((byte)MissionObjectiveState.Inactive) and not
                        ((byte)MissionObjectiveState.NotAssigned))
                        return false;

                    durableObjective.ObjectiveState = (byte)MissionObjectiveState.Incomplete;
                    scenarioPlan.AddRuntimeConvergence(() =>
                        runtimeObjective.State = MissionObjectiveState.Incomplete);
                    scenarioPlan.AddPublication(() =>
                        PublishMissionPacket(
                            client,
                            new ObjectiveActivatedPacket(missionId, objectiveId),
                            $"mission {missionId} objective {objectiveId} activated"));
                    return true;

                case MissionScenarioStepKind.CompleteObjective:
                    if (durableObjective.ObjectiveState != (byte)MissionObjectiveState.Incomplete ||
                        runtimeObjective.State != MissionObjectiveState.Incomplete)
                        return false;

                    var revealed = objectiveDefinition.RevealedObjectiveIds.ToArray();
                    var activated = objectiveDefinition.ActivatedObjectiveIds.ToArray();
                    var appliedRevealed = new List<uint>();
                    var appliedActivated = new List<uint>();
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
                        if (successor.ObjectiveState is
                            ((byte)MissionObjectiveState.Inactive) or
                            ((byte)MissionObjectiveState.NotAssigned))
                        {
                            successor.ObjectiveState = (byte)MissionObjectiveState.Incomplete;
                            appliedActivated.Add(successorId);
                        }
                    }

                    var completeable = definition.Objectives.Values
                        .Where(candidate => candidate.IsRequired.Value)
                        .All(candidate =>
                            durableObjectives.TryGetValue(candidate.ObjectiveId, out var required) &&
                            required.ObjectiveState == (byte)MissionObjectiveState.Completed);
                    var completeableChanged = durableMission.Completeable != completeable;
                    durableMission.Completeable = completeable;
                    var priorDeadline = unitOfWork.CharacterMissionDeadlines.Get(
                        client.Player.Id,
                        missionId);
                    var priorDeadlineState = priorDeadline?.State;
                    _deadlineService.SynchronizeMission(
                        unitOfWork,
                        client.Player.Id,
                        definition,
                        durableMission,
                        durableObjectives);
                    var currentDeadline = unitOfWork.CharacterMissionDeadlines.Get(
                        client.Player.Id,
                        missionId);
                    var deadlineBecameActive =
                        priorDeadlineState != CharacterMissionDeadlineState.Active &&
                        currentDeadline?.State == CharacterMissionDeadlineState.Active;

                    scenarioPlan.AddRuntimeConvergence(() =>
                    {
                        foreach (var touchedObjectiveId in revealed
                                     .Concat(activated)
                                     .Append(objectiveId)
                                     .Distinct())
                        {
                            var durable = durableObjectives[touchedObjectiveId];
                            var runtime = runtimeMission.Objectives[touchedObjectiveId];
                            runtime.State = (MissionObjectiveState)durable.ObjectiveState;
                            foreach (var counter in durable.Counters)
                                runtime.SetCounter(counter.CounterId, counter.CounterValue);
                            foreach (var counter in durable.ItemCounters)
                                runtime.SetItemCounter(counter.ItemClassId, counter.CounterValue);
                        }

                        runtimeMission.Completeable = completeable;
                    });
                    scenarioPlan.AddPublication(() =>
                    {
                        PublishMissionPacket(
                            client,
                            new ObjectiveCompletedPacket(missionId, objectiveId),
                            $"mission {missionId} objective {objectiveId} completed");
                        foreach (var successorId in revealed.Where(appliedRevealed.Contains))
                            PublishMissionPacket(
                                client,
                                new ObjectiveRevealedPacket(
                                    missionId,
                                    successorId,
                                    BuildPublishedMissionInfo(
                                        client.Player,
                                        definition,
                                        runtimeMission)),
                                $"mission {missionId} objective {successorId} revealed");
                        foreach (var successorId in appliedActivated)
                            PublishMissionPacket(
                                client,
                                new ObjectiveActivatedPacket(missionId, successorId),
                                $"mission {missionId} objective {successorId} activated");
                        if (completeableChanged && completeable)
                            PublishMissionPacket(
                                client,
                                new MissionCompleteablePacket(missionId, true),
                                $"mission {missionId} completable");
                        if (deadlineBecameActive)
                            PublishMissionStatus(
                                client,
                                missionId,
                                $"mission {missionId} status after deadline start");
                    });
                    return true;

                case MissionScenarioStepKind.FailObjective:
                    if (durableObjective.ObjectiveState != (byte)MissionObjectiveState.Incomplete)
                        return false;

                    durableObjective.ObjectiveState = (byte)MissionObjectiveState.Failed;
                    var failMission = objectiveDefinition.IsRequired.Value;
                    var nextCompleteable = !failMission &&
                        definition.Objectives.Values
                            .Where(candidate => candidate.IsRequired.Value)
                            .All(candidate =>
                                durableObjectives.TryGetValue(
                                    candidate.ObjectiveId,
                                    out var requiredObjective) &&
                                requiredObjective.ObjectiveState ==
                                    (byte)MissionObjectiveState.Completed);
                    var nextCompleteableChanged =
                        durableMission.Completeable != nextCompleteable;
                    durableMission.Completeable = nextCompleteable;
                    if (failMission)
                        durableMission.MissionState = (uint)MissionState.Failed;
                    _deadlineService.SynchronizeMission(
                        unitOfWork,
                        client.Player.Id,
                        definition,
                        durableMission,
                        durableObjectives);
                    scenarioPlan.AddFailurePlan(
                        new MissionFailurePublicationPlan(
                            missionId,
                            objectiveId,
                            failMission ? MissionState.Failed : MissionState.Active,
                            nextCompleteable,
                            nextCompleteableChanged,
                            publishMissionStatus:
                                objectiveDefinition.GetExecutableTransitionsOrLegacyDefault()
                                    .Any(transition =>
                                        transition.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed)));
                    return true;
            }

            return false;
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
                    !TryGetOperationalMission(missionId, out var definition) ||
                    !client.Player.Missions.TryGetValue(missionId, out var log) ||
                    log.State != MissionState.Active ||
                    missionId == InitiationMissionId)
                    return false;

                var authoredFailurePlan = MissionFailurePublicationPlan.Empty;
                var authoredFailure = false;
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

                        if (TryPlanAuthoredAbandonment(
                                client,
                                definition,
                                log,
                                unitOfWork,
                                durableMission,
                                out authoredFailurePlan))
                        {
                            authoredFailure = true;
                            return;
                        }

                        unitOfWork.CharacterMissions.Remove(client.Player.Id, missionId);
                        removed = true;
                    });
                    if (authoredFailure)
                    {
                        authoredFailurePlan.Publish(
                            client,
                            this,
                            TryExecuteScenario,
                            TryExecuteFailureTransitionScenario);
                        return true;
                    }
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

        private bool TryPlanAuthoredAbandonment(
            Client client,
            Mission definition,
            MissionLog runtimeMission,
            ICharUnitOfWork unitOfWork,
            CharacterMissionEntry durableMission,
            out MissionFailurePublicationPlan publicationPlan)
        {
            publicationPlan = MissionFailurePublicationPlan.Empty;
            if (client?.Player == null ||
                definition == null ||
                runtimeMission == null ||
                unitOfWork == null ||
                durableMission == null)
                return false;

            var deadline = unitOfWork.CharacterMissionDeadlines.Get(
                client.Player.Id,
                definition.MissionId);
            if (deadline?.State is not
                (CharacterMissionDeadlineState.Active or
                 CharacterMissionDeadlineState.Satisfied or
                 CharacterMissionDeadlineState.Cancelled))
                return false;

            var authoredFailure = definition.Objectives.Values
                .OrderBy(objective => objective.Ordinal)
                .ThenBy(objective => objective.ObjectiveId)
                .Select(objective => new
                {
                    Objective = objective,
                    Transition = objective.GetExecutableTransitionsOrLegacyDefault()
                        .Where(transition =>
                            transition.ToState == MissionObjectiveState.Failed &&
                            transition.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed)
                        .OrderBy(transition => transition.Sequence)
                        .ThenBy(transition => transition.TransitionId)
                        .FirstOrDefault()
                })
                .FirstOrDefault(candidate =>
                    candidate.Objective.IsRequired.Value &&
                    candidate.Transition != null);
            if (authoredFailure == null ||
                !runtimeMission.Objectives.TryGetValue(
                    authoredFailure.Objective.ObjectiveId,
                    out _))
                return false;

            var durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
                client.Player.Id,
                definition.MissionId);
            if (!durableObjectives.TryGetValue(
                    authoredFailure.Objective.ObjectiveId,
                    out var durableObjective))
                return false;

            durableObjective.ObjectiveState = (byte)MissionObjectiveState.Failed;
            durableMission.MissionState = (uint)MissionState.Failed;
            durableMission.Completeable = false;
            var failureActions = ApplyTransitionActions(
                authoredFailure.Objective.ObjectiveId,
                authoredFailure.Transition,
                durableObjectives);
            _deadlineService.SynchronizeMission(
                unitOfWork,
                client.Player.Id,
                definition,
                durableMission,
                durableObjectives);
            unitOfWork.CharacterMissionDeadlines.SetState(
                client.Player.Id,
                definition.MissionId,
                CharacterMissionDeadlineState.Cancelled);
            publicationPlan = new MissionFailurePublicationPlan(
                definition.MissionId,
                authoredFailure.Objective.ObjectiveId,
                MissionState.Failed,
                false,
                runtimeMission.Completeable,
                failureActions.StartScenarioIds);
            return true;
        }

        private bool MissionOutputsAlreadySatisfied(
            Client client,
            uint missionId,
            ICharUnitOfWork unitOfWork,
            out string failure)
        {
            failure = null;
            if (client?.Player == null || unitOfWork == null ||
                !_scenariosByMission.TryGetValue(missionId, out var scenarios))
                return false;

            var durableAccount = client.AccountEntry?.Id > 0
                ? unitOfWork.GameAccounts.Get(client.AccountEntry.Id)
                : null;
            foreach (var step in scenarios.Values
                         .OrderBy(scenario => scenario.ScenarioId)
                         .SelectMany(scenario => scenario.Steps.OrderBy(candidate => candidate.StepId)))
            {
                if (step.Kind == MissionScenarioStepKind.SetQualification &&
                    step.QualificationValue == MissionScenarioStepEntry.GrantedQualificationValue &&
                    step.TryGetQualificationKey(out var qualificationKey) &&
                    unitOfWork.CharacterQualifications.HasQualification(
                        client.Player.Id,
                        qualificationKey))
                {
                    failure = $"qualification {qualificationKey} is already granted.";
                    return true;
                }

                if (step.Kind == MissionScenarioStepKind.SetAccountSkipEntitlement &&
                    step.AccountSkipEntitlement == true &&
                    (durableAccount?.CanSkipBootcamp == true ||
                     client.AccountEntry?.CanSkipBootcamp == true))
                {
                    failure = "account bootcamp skip is already granted.";
                    return true;
                }
            }

            return false;
        }

        internal bool RecordProgress(Client client, MissionProgressEvent progress)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    client.AccountEntry == null ||
                    !HasProgressCandidate(client, progress))
                    return false;

                var plan = MissionProgressPublicationPlan.Empty;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                        plan = PlanProgress(
                            client,
                            new[] { progress },
                            unitOfWork));
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to record mission progress {progress.Kind}:{progress.SubjectId} " +
                        $"for character {client.Player.Id}: {error}");
                    return false;
                }

                plan.Publish(client);
                return plan.HasChanges;
            }
        }

        private bool HasProgressCandidate(
            Client client,
            MissionProgressEvent progress)
        {
            if (!Enum.IsDefined(
                    typeof(MissionProgressEventKind), progress.Kind) ||
                progress.SubjectId == 0 ||
                progress.Quantity == 0)
                return false;

            return _loadedMissions.Values
                .Where(definition => definition.IsOperational)
                .Any(mission =>
                    client.Player.Missions.TryGetValue(
                        mission.MissionId, out var runtimeMission) &&
                    runtimeMission.State == MissionState.Active &&
                    mission.Objectives.Values.Any(objective =>
                        objective.GetExecutableTransitionsOrLegacyDefault().Any(transition =>
                            transition.ProgressRule != null &&
                            transition.ProgressRule.Matches(progress) &&
                            CanAdvanceFromRuntime(
                                client.Player,
                                transition.ProgressRule)) &&
                        runtimeMission.Objectives.TryGetValue(
                            objective.ObjectiveId, out var runtimeObjective) &&
                        runtimeObjective.State ==
                            MissionObjectiveState.Incomplete));
        }

        internal MissionProgressPublicationPlan PlanProgress(
            Client client,
            IReadOnlyList<MissionProgressEvent> progressEvents,
            ICharUnitOfWork unitOfWork)
        {
            if (client == null ||
                unitOfWork == null ||
                !IsActivePlayer(client) ||
                client.AccountEntry == null)
                return MissionProgressPublicationPlan.Empty;

            var progresses = AggregateProgress(progressEvents);
            if (progresses.Count == 0)
                return MissionProgressPublicationPlan.Empty;

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
                    var matchingTransition = objective.GetExecutableTransitionsOrLegacyDefault()
                        .Where(transition =>
                            transition.ProgressRule != null &&
                            CanAdvanceFromRuntime(client.Player, transition.ProgressRule))
                        .Select(transition => new
                        {
                            Transition = transition,
                            Progress = progresses
                                .Where(transition.ProgressRule.Matches)
                                .Select(value => (MissionProgressEvent?)value)
                                .FirstOrDefault()
                        })
                        .Where(match => match.Progress.HasValue)
                        .OrderBy(match => match.Transition.Sequence)
                        .ThenBy(match => match.Transition.TransitionId)
                        .FirstOrDefault();
                    if (matchingTransition == null ||
                        !runtimeMission.Objectives.TryGetValue(
                            objective.ObjectiveId, out var runtimeObjective) ||
                        runtimeObjective.State != MissionObjectiveState.Incomplete)
                        continue;
                    candidates.Add(new ProgressCandidate(
                        mission,
                        runtimeMission,
                        objective,
                        runtimeObjective,
                        matchingTransition.Transition,
                        matchingTransition.Progress!.Value));
                }
            }
            if (candidates.Count == 0)
                return MissionProgressPublicationPlan.Empty;

            var durableCharacter = unitOfWork.Characters.Get(client.Player.Id);
            if (durableCharacter.AccountId != client.AccountEntry.Id)
                throw new GameplayRejectionException(
                    "Durable character owner changed.");

            var publications = new List<ProgressPublication>();
            var failurePlans = new List<MissionFailurePublicationPlan>();
            var completableMissions = new SortedSet<uint>();
            var missionStatusMissionIds = new SortedSet<uint>();
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
                var priorDeadline = unitOfWork.CharacterMissionDeadlines.Get(
                    client.Player.Id,
                    first.Definition.MissionId);
                var priorDeadlineState = priorDeadline?.State;
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

                    var rule = candidate.ExecutableTransition.ProgressRule;
                    var toState = candidate.ExecutableTransition.ToState ?? MissionObjectiveState.Completed;
                    if (rule.RuleType == MissionProgressRuleType.CompleteDistinctSet)
                    {
                        if (toState != MissionObjectiveState.Completed)
                            throw new GameplayRejectionException(
                                "Distinct progress transitions can only complete objectives in the current runtime.");
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
                        publications.Add(ProgressPublication.ForCompleted(
                            candidate,
                            ApplyTransitionActions(
                                candidate.ObjectiveDefinition.ObjectiveId,
                                candidate.ExecutableTransition,
                                durableObjectives)));
                        continue;
                    }

                    if (rule.RuleType == MissionProgressRuleType.IncrementExactCounter)
                    {
                        if (toState != MissionObjectiveState.Completed)
                            throw new GameplayRejectionException(
                                "Counter progress transitions can only complete objectives in the current runtime.");
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
                            candidate.Progress.Quantity,
                            rule.TargetValue.Value - runtimeValue);
                        durableCounter.CounterValue = value;
                        var completed = value == rule.TargetValue.Value;
                        if (completed)
                            durableObjective.ObjectiveState =
                                (byte)MissionObjectiveState.Completed;
                        var actionApplication = completed
                            ? ApplyTransitionActions(
                                candidate.ObjectiveDefinition.ObjectiveId,
                                candidate.ExecutableTransition,
                                durableObjectives)
                            : TransitionActionApplication.Empty;
                        publications.Add(ProgressPublication.Counter(
                            candidate, counterId, value, completed, actionApplication));
                        continue;
                    }

                    if (rule.RuleType ==
                        MissionProgressRuleType.IncrementExactItemCounter)
                    {
                        if (toState != MissionObjectiveState.Completed)
                            throw new GameplayRejectionException(
                                "Item counter progress transitions can only complete objectives in the current runtime.");
                        var itemClassId = rule.CounterId.Value;
                        if (!candidate.RuntimeObjective.ItemCounters.TryGetValue(
                            itemClassId, out var runtimeValue))
                            throw new GameplayRejectionException(
                                "Runtime mission item counter is missing.");
                        var durableCounter =
                            durableObjective.ItemCounters.SingleOrDefault(
                                counter => counter.ItemClassId == itemClassId);
                        if (durableCounter == null ||
                            durableCounter.CounterValue != runtimeValue ||
                            runtimeValue < rule.InitialValue.Value)
                            throw new GameplayRejectionException(
                                "Durable mission item counter is stale.");
                        if (runtimeValue >= rule.TargetValue.Value)
                            throw new GameplayRejectionException(
                                "Mission item counter cannot advance monotonically.");
                        var value = runtimeValue + Math.Min(
                            candidate.Progress.Quantity,
                            rule.TargetValue.Value - runtimeValue);
                        durableCounter.CounterValue = value;
                        var completed = value == rule.TargetValue.Value;
                        if (completed)
                            durableObjective.ObjectiveState =
                                (byte)MissionObjectiveState.Completed;
                        var actionApplication = completed
                            ? ApplyTransitionActions(
                                candidate.ObjectiveDefinition.ObjectiveId,
                                candidate.ExecutableTransition,
                                durableObjectives)
                            : TransitionActionApplication.Empty;
                        publications.Add(
                            ProgressPublication.ItemCounter(
                                candidate,
                                itemClassId,
                                value,
                                completed,
                                actionApplication));
                        continue;
                    }

                    if (toState == MissionObjectiveState.Failed)
                    {
                        durableObjective.ObjectiveState =
                            (byte)MissionObjectiveState.Failed;
                        var failMission = candidate.ObjectiveDefinition.IsRequired.Value;
                        var nextCompleteable = !failMission &&
                            first.Definition.Objectives.Values
                                .Where(objective => objective.IsRequired.Value)
                                .All(objective =>
                                    durableObjectives.TryGetValue(
                                        objective.ObjectiveId,
                                        out var requiredObjective) &&
                                    requiredObjective.ObjectiveState ==
                                        (byte)MissionObjectiveState.Completed);
                        var nextCompleteableChanged =
                            durableMission.Completeable != nextCompleteable;
                        durableMission.Completeable = nextCompleteable;
                        if (failMission)
                            durableMission.MissionState = (uint)MissionState.Failed;
                        var failureActions = ApplyTransitionActions(
                            candidate.ObjectiveDefinition.ObjectiveId,
                            candidate.ExecutableTransition,
                            durableObjectives);
                        _deadlineService.SynchronizeMission(
                            unitOfWork,
                            client.Player.Id,
                            first.Definition,
                            durableMission,
                            durableObjectives);
                        failurePlans.Add(
                            new MissionFailurePublicationPlan(
                                candidate.Definition.MissionId,
                                candidate.ObjectiveDefinition.ObjectiveId,
                                failMission ? MissionState.Failed : MissionState.Active,
                                nextCompleteable,
                                nextCompleteableChanged,
                                failureActions.StartScenarioIds,
                                publishMissionStatus:
                                    candidate.ExecutableTransition.ProgressRule?.Kind ==
                                    MissionProgressEventKind.DeadlineElapsed));
                        continue;
                    }

                    if (toState != MissionObjectiveState.Completed)
                    {
                        durableObjective.ObjectiveState = (byte)toState;
                        publications.Add(
                            ProgressPublication.ForTransition(
                                candidate,
                                toState,
                                ApplyTransitionActions(
                                    candidate.ObjectiveDefinition.ObjectiveId,
                                    candidate.ExecutableTransition,
                                    durableObjectives)));
                        continue;
                    }

                    durableObjective.ObjectiveState =
                        (byte)MissionObjectiveState.Completed;
                    publications.Add(ProgressPublication.ForCompleted(
                        candidate,
                        ApplyTransitionActions(
                            candidate.ObjectiveDefinition.ObjectiveId,
                            candidate.ExecutableTransition,
                            durableObjectives)));
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
                _deadlineService.SynchronizeMission(
                    unitOfWork,
                    client.Player.Id,
                    first.Definition,
                    durableMission,
                    durableObjectives);
                var currentDeadline = unitOfWork.CharacterMissionDeadlines.Get(
                    client.Player.Id,
                    first.Definition.MissionId);
                if (priorDeadlineState != CharacterMissionDeadlineState.Active &&
                    currentDeadline?.State == CharacterMissionDeadlineState.Active)
                    missionStatusMissionIds.Add(first.Definition.MissionId);
            }

            return new MissionProgressPublicationPlan(
                publications,
                failurePlans,
                completableMissions,
                missionStatusMissionIds,
                (progressClient, progressedMissionId, scenarioId) =>
                    _scenarioService.TryExecute(progressClient, progressedMissionId, scenarioId),
                (progressClient, progressedMissionId, scenarioId) =>
                    _scenarioService.TryExecuteFailureTransition(
                        progressClient,
                        progressedMissionId,
                        scenarioId),
                this);
        }

        private static IReadOnlyList<MissionProgressEvent> AggregateProgress(
            IReadOnlyList<MissionProgressEvent> progressEvents)
        {
            var valid = (progressEvents ?? Array.Empty<MissionProgressEvent>())
                .Where(progress =>
                    Enum.IsDefined(typeof(MissionProgressEventKind), progress.Kind) &&
                    progress.SubjectId != 0 &&
                    progress.Quantity != 0)
                .ToArray();
            if (valid.Length == 0)
                return Array.Empty<MissionProgressEvent>();

            var result = new List<MissionProgressEvent>();
            foreach (var group in valid.GroupBy(
                progress => (
                    progress.Kind,
                    progress.SubjectId,
                    progress.ScopeId,
                    progress.DetailId)))
            {
                uint quantity;
                try
                {
                    quantity = group.Aggregate(
                        0U,
                        (total, progress) => checked(total + progress.Quantity));
                }
                catch (OverflowException error)
                {
                    throw new GameplayRejectionException(
                        "Mission progress quantity exceeds the supported range.",
                        error);
                }

                result.Add(group.Key.Kind switch
                {
                    MissionProgressEventKind.ItemAcquired =>
                        MissionProgressEvent.ItemAcquired(
                            group.Key.SubjectId, quantity),
                    MissionProgressEventKind.ItemConsumed =>
                        MissionProgressEvent.ItemConsumed(
                            group.Key.SubjectId, quantity),
                    MissionProgressEventKind.AreaEntered =>
                        MissionProgressEvent.Area(
                            group.Key.ScopeId.GetValueOrDefault(),
                            group.Key.SubjectId),
                    MissionProgressEventKind.ItemEquipped =>
                        MissionProgressEvent.ItemEquipped(
                            group.Key.SubjectId,
                            group.Key.DetailId.GetValueOrDefault()),
                    MissionProgressEventKind.AbilityHit =>
                        MissionProgressEvent.AbilityHit(
                            group.Key.SubjectId,
                            group.Key.DetailId.GetValueOrDefault()),
                    MissionProgressEventKind.ScenarioEvent =>
                        MissionProgressEvent.Scenario(
                            group.Key.ScopeId.GetValueOrDefault(),
                            group.Key.DetailId.GetValueOrDefault(),
                            group.Key.SubjectId),
                    MissionProgressEventKind.DeadlineElapsed =>
                        MissionProgressEvent.Deadline(
                            group.Key.ScopeId.GetValueOrDefault(),
                            group.Key.SubjectId),
                    _ => group.First()
                });
            }
            return result;
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

        private static bool TryGetMatchingConversationTransition(
            MissionObjectiveDefinition objectiveDefinition,
            uint npcPackageId,
            uint playerFlagId,
            out MissionObjectiveExecutableTransition transition)
        {
            transition = objectiveDefinition.GetExecutableTransitionsOrLegacyDefault()
                .Where(candidate => candidate.Conversations.Any(conversation =>
                    conversation.Type == MissionObjectiveConversationType.Completion &&
                    conversation.NpcPackageId == npcPackageId &&
                    conversation.PlayerFlagId == playerFlagId))
                .OrderBy(candidate => candidate.Sequence)
                .ThenBy(candidate => candidate.TransitionId)
                .FirstOrDefault();
            return transition != null;
        }

        private static TransitionActionApplication ApplyTransitionActions(
            uint currentObjectiveId,
            MissionObjectiveExecutableTransition transition,
            IReadOnlyDictionary<uint, CharacterMissionObjectiveEntry> durableObjectives)
        {
            if (transition == null)
                return TransitionActionApplication.Empty;

            var revealed = new List<uint>();
            var activated = new List<uint>();
            var startedScenarios = new List<uint>();
            var finalStates = new Dictionary<uint, MissionObjectiveState>();

            foreach (var action in transition.Actions)
            {
                switch (action.Kind)
                {
                    case MissionActionKind.CompleteObjective:
                        if (action.TargetObjectiveId.HasValue &&
                            action.TargetObjectiveId.Value != currentObjectiveId)
                            throw new GameplayRejectionException(
                                $"Transition {transition.TransitionId} can only complete its current objective in the current runtime.");
                        break;

                    case MissionActionKind.RevealObjective:
                        if (!action.TargetObjectiveId.HasValue ||
                            !durableObjectives.TryGetValue(action.TargetObjectiveId.Value, out var revealedObjective))
                            throw new GameplayRejectionException(
                                "Configured revealed mission objective is missing.");
                        if (revealedObjective.ObjectiveState == (byte)MissionObjectiveState.Inactive)
                        {
                            revealedObjective.ObjectiveState = (byte)MissionObjectiveState.NotAssigned;
                            revealed.Add(action.TargetObjectiveId.Value);
                        }
                        finalStates[action.TargetObjectiveId.Value] =
                            (MissionObjectiveState)revealedObjective.ObjectiveState;

                        break;

                    case MissionActionKind.ActivateObjective:
                        if (!action.TargetObjectiveId.HasValue ||
                            !durableObjectives.TryGetValue(action.TargetObjectiveId.Value, out var activatedObjective))
                            throw new GameplayRejectionException(
                                "Configured activated mission objective is missing.");
                        if (activatedObjective.ObjectiveState is
                            ((byte)MissionObjectiveState.Incomplete) or
                            ((byte)MissionObjectiveState.Completed) or
                            ((byte)MissionObjectiveState.Failed))
                            break;
                        if (activatedObjective.ObjectiveState is not
                            ((byte)MissionObjectiveState.Inactive) and not
                            ((byte)MissionObjectiveState.NotAssigned))
                            throw new GameplayRejectionException(
                                "Configured activated mission objective state is invalid.");

                        activatedObjective.ObjectiveState = (byte)MissionObjectiveState.Incomplete;
                        activated.Add(action.TargetObjectiveId.Value);
                        finalStates[action.TargetObjectiveId.Value] =
                            (MissionObjectiveState)activatedObjective.ObjectiveState;
                        break;

                    case MissionActionKind.StartScenario:
                        if (action.ScenarioId.HasValue &&
                            !startedScenarios.Contains(action.ScenarioId.Value))
                            startedScenarios.Add(action.ScenarioId.Value);
                        break;
                }
            }

            return new TransitionActionApplication(finalStates, revealed, activated, startedScenarios);
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

        private bool ArePrerequisitesSatisfied(
            Manifestation player,
            uint missionId,
            out string failure)
        {
            failure = null;
            if (!_prerequisitesByMission.TryGetValue(missionId, out var prerequisites) ||
                prerequisites.Count == 0)
                return true;

            foreach (var prerequisite in prerequisites)
            {
                if (IsPrerequisiteSatisfied(player, prerequisite))
                    continue;

                failure = DescribePrerequisiteFailure(prerequisite);
                return false;
            }

            return true;
        }

        private bool ArePrerequisitesSatisfied(
            Manifestation player,
            uint missionId,
            ICharUnitOfWork unitOfWork,
            out string failure)
        {
            failure = null;
            if (!ArePrerequisitesSatisfied(player, missionId, out failure))
                return false;
            if (!_prerequisitesByMission.TryGetValue(missionId, out var prerequisites) ||
                prerequisites.Count == 0)
                return true;

            var durableCharacter = unitOfWork.Characters.Get(player.Id);
            foreach (var prerequisite in prerequisites)
            {
                if (IsPrerequisiteSatisfied(player, prerequisite, durableCharacter, unitOfWork))
                    continue;

                failure = DescribePrerequisiteFailure(prerequisite);
                return false;
            }

            return true;
        }

        private static bool IsPrerequisiteSatisfied(
            Manifestation player,
            MissionPrerequisiteDefinition prerequisite) =>
            prerequisite.Kind switch
            {
                MissionPrerequisiteKind.MissionCompleted =>
                    TryGetMissionLogState(player.Missions, prerequisite.RequiredMissionId, out var completedState) &&
                    IsMissionStateSatisfied(completedState, prerequisite.RequiredMissionStateValue),
                MissionPrerequisiteKind.MissionAccepted =>
                    TryGetMissionLogState(player.Missions, prerequisite.RequiredMissionId, out var acceptedState) &&
                    IsMissionStateSatisfied(acceptedState, prerequisite.RequiredMissionStateValue, requirePresenceOnly: true),
                MissionPrerequisiteKind.PlayerLevelAtLeast =>
                    prerequisite.RequiredLevel.HasValue &&
                    player.Level >= prerequisite.RequiredLevel.Value,
                MissionPrerequisiteKind.PlayerFlagValue =>
                    prerequisite.PlayerFlagId.HasValue &&
                    prerequisite.PlayerFlagValue.HasValue &&
                    player.PlayerFlags.TryGetValue(prerequisite.PlayerFlagId.Value, out var value) &&
                    value == prerequisite.PlayerFlagValue.Value,
                _ => false
            };

        private static bool IsPrerequisiteSatisfied(
            Manifestation player,
            MissionPrerequisiteDefinition prerequisite,
            CharacterEntry durableCharacter,
            ICharUnitOfWork unitOfWork) =>
            prerequisite.Kind switch
            {
                MissionPrerequisiteKind.MissionCompleted =>
                    TryGetDurableMissionState(unitOfWork, player.Id, prerequisite.RequiredMissionId, out var completedState) &&
                    IsMissionStateSatisfied(completedState, prerequisite.RequiredMissionStateValue),
                MissionPrerequisiteKind.MissionAccepted =>
                    TryGetDurableMissionState(unitOfWork, player.Id, prerequisite.RequiredMissionId, out var acceptedState) &&
                    IsMissionStateSatisfied(acceptedState, prerequisite.RequiredMissionStateValue, requirePresenceOnly: true),
                MissionPrerequisiteKind.PlayerLevelAtLeast =>
                    prerequisite.RequiredLevel.HasValue &&
                    durableCharacter != null &&
                    durableCharacter.Level >= prerequisite.RequiredLevel.Value,
                MissionPrerequisiteKind.PlayerFlagValue =>
                    IsPrerequisiteSatisfied(player, prerequisite),
                _ => false
            };

        private static bool TryGetMissionLogState(
            IReadOnlyDictionary<uint, MissionLog> missions,
            uint? missionId,
            out MissionState state)
        {
            if (missionId.HasValue &&
                missions.TryGetValue(missionId.Value, out var log))
            {
                state = log.State;
                return true;
            }

            state = default;
            return false;
        }

        private static bool TryGetDurableMissionState(
            ICharUnitOfWork unitOfWork,
            uint characterId,
            uint? missionId,
            out MissionState state)
        {
            if (missionId.HasValue)
            {
                var mission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                    characterId,
                    missionId.Value);
                if (mission != null &&
                    Enum.IsDefined(typeof(MissionState), (int)mission.MissionState))
                {
                    state = (MissionState)mission.MissionState;
                    return true;
                }
            }

            state = default;
            return false;
        }

        private static bool IsMissionStateSatisfied(
            MissionState state,
            byte? requiredStateValue,
            bool requirePresenceOnly = false)
        {
            if (requiredStateValue.HasValue)
            {
                if (!Enum.IsDefined(typeof(MissionState), (int)requiredStateValue.Value))
                    return false;
                return state == (MissionState)requiredStateValue.Value;
            }

            if (requirePresenceOnly)
                return true;
            return state is MissionState.Success or MissionState.Completed;
        }

        private static string DescribePrerequisiteFailure(
            MissionPrerequisiteDefinition prerequisite) =>
            prerequisite.Kind switch
            {
                MissionPrerequisiteKind.MissionCompleted =>
                    $"required mission {prerequisite.RequiredMissionId?.ToString() ?? "null"} is not in the required completed state.",
                MissionPrerequisiteKind.MissionAccepted =>
                    $"required mission {prerequisite.RequiredMissionId?.ToString() ?? "null"} is not currently accepted.",
                MissionPrerequisiteKind.PlayerLevelAtLeast =>
                    $"required level {prerequisite.RequiredLevel?.ToString() ?? "null"} is not satisfied.",
                MissionPrerequisiteKind.PlayerFlagValue =>
                    $"required player flag {prerequisite.PlayerFlagId?.ToString() ?? "null"} value {prerequisite.PlayerFlagValue?.ToString() ?? "null"} is not satisfied.",
                _ => $"unsupported prerequisite kind {(int)prerequisite.Kind}."
            };

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
                    if (mission.MissionGiver == creature.DbId &&
                        ArePrerequisitesSatisfied(player, mission.MissionId, out _))
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
            return MapInstanceScope.TryGetCreature(player?.MapChannel, npcEntityId, out npc);
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

        private void PublishMissionPacket(
            Client client,
            PythonPacket packet,
            string description)
        {
            TryPublish(
                () =>
                {
                    _beforeMissionPacketPublication?.Invoke(packet);
                    client.CallMethod(client.Player.EntityId, packet);
                },
                description);
        }

        internal sealed class MissionFailurePublicationPlan
        {
            internal static readonly MissionFailurePublicationPlan Empty =
                new(0, 0, MissionState.Active, false, false);

            private readonly uint _missionId;
            private readonly uint _objectiveId;
            private readonly MissionState _missionState;
            private readonly bool _completeable;
            private readonly bool _completeableChanged;
            private readonly bool _publishMissionStatus;
            internal uint MissionId => _missionId;
            internal IReadOnlyList<uint> StartScenarioIds { get; }

            internal MissionFailurePublicationPlan(
                uint missionId,
                uint objectiveId,
                MissionState missionState,
                bool completeable,
                bool completeableChanged,
                IEnumerable<uint> startScenarioIds = null,
                bool publishMissionStatus = false)
            {
                _missionId = missionId;
                _objectiveId = objectiveId;
                _missionState = missionState;
                _completeable = completeable;
                _completeableChanged = completeableChanged;
                _publishMissionStatus = publishMissionStatus;
                StartScenarioIds = Array.AsReadOnly(
                    (startScenarioIds ?? Array.Empty<uint>()).ToArray());
            }

            internal void Publish(
                Client client,
                MissionManager manager,
                Func<Client, uint, uint, bool> startScenario = null,
                Func<Client, uint, uint, bool> startFailureScenario = null)
            {
                if (!client.Player.Missions.TryGetValue(_missionId, out var mission) ||
                    !mission.Objectives.TryGetValue(_objectiveId, out var objective))
                    return;

                objective.State = MissionObjectiveState.Failed;
                mission.State = _missionState;
                mission.Completeable = _completeable;

                manager.PublishMissionPacket(
                    client,
                    new ObjectiveFailedPacket(_missionId, _objectiveId),
                    $"mission {_missionId} objective {_objectiveId} failed");
                if (_completeableChanged)
                    manager.PublishMissionPacket(
                        client,
                        new MissionCompleteablePacket(_missionId, _completeable),
                        $"mission {_missionId} completable after objective failure");
                if (_missionState == MissionState.Failed)
                    manager.PublishMissionPacket(
                        client,
                        new MissionFailedPacket(_missionId),
                        $"mission {_missionId} failed after objective failure");
                if (_publishMissionStatus)
                    manager.PublishMissionStatus(
                        client,
                        _missionId,
                        $"mission {_missionId} status after deadline change");

                MissionManager.PublishStartedScenarios(
                    client,
                    _missionId,
                    StartScenarioIds,
                    _missionState == MissionState.Failed
                        ? startFailureScenario
                        : startScenario);
            }
        }

        internal sealed class MissionProgressPublicationPlan
        {
            internal static readonly MissionProgressPublicationPlan Empty =
                new(
                    Array.Empty<ProgressPublication>(),
                    Array.Empty<MissionFailurePublicationPlan>(),
                    Array.Empty<uint>(),
                    Array.Empty<uint>(),
                    null,
                    null,
                    null);

            private readonly ProgressPublication[] _publications;
            private readonly MissionFailurePublicationPlan[] _failurePlans;
            private readonly uint[] _completableMissions;
            private readonly uint[] _missionStatusMissionIds;
            private readonly Func<Client, uint, uint, bool> _startScenario;
            private readonly Func<Client, uint, uint, bool> _startFailureScenario;
            private readonly MissionManager _manager;

            internal bool HasChanges => _publications.Length > 0 || _failurePlans.Length > 0;

            internal MissionProgressPublicationPlan(
                IEnumerable<ProgressPublication> publications,
                IEnumerable<MissionFailurePublicationPlan> failurePlans,
                IEnumerable<uint> completableMissions,
                IEnumerable<uint> missionStatusMissionIds,
                Func<Client, uint, uint, bool> startScenario,
                Func<Client, uint, uint, bool> startFailureScenario,
                MissionManager manager)
            {
                _publications = publications.ToArray();
                _failurePlans = failurePlans.ToArray();
                _completableMissions = completableMissions.ToArray();
                _missionStatusMissionIds = missionStatusMissionIds.ToArray();
                _startScenario = startScenario;
                _startFailureScenario = startFailureScenario;
                _manager = manager;
            }

            internal void Publish(Client client)
            {
                foreach (var publication in _publications.Where(
                    publication =>
                        publication.CounterId.HasValue &&
                        !publication.IsItemCounter))
                    if (TryGetRuntimeObjective(
                            client,
                            publication,
                            out var objective))
                        objective.SetCounter(
                            publication.CounterId.Value,
                            publication.CounterValue.Value);

                foreach (var publication in _publications.Where(
                    publication => publication.IsItemCounter))
                    if (TryGetRuntimeObjective(
                            client,
                            publication,
                            out var objective))
                        objective.SetItemCounter(
                            publication.CounterId.Value,
                            publication.CounterValue.Value);

                foreach (var publication in _publications)
                    if (TryGetRuntimeObjective(
                            client,
                            publication,
                            out var objective))
                    {
                        if (publication.ObjectiveState.HasValue)
                            objective.State = publication.ObjectiveState.Value;
                        if (client.Player.Missions.TryGetValue(
                                publication.MissionId,
                                out var mission))
                            foreach (var state in publication.FinalObjectiveStates)
                                if (mission.Objectives.TryGetValue(
                                        state.Key,
                                        out var successor))
                                    successor.State = state.Value;
                    }

                foreach (var missionId in _completableMissions)
                    if (client.Player.Missions.TryGetValue(
                            missionId, out var mission))
                        mission.Completeable = true;

                foreach (var publication in _publications.Where(
                    publication =>
                        publication.CounterId.HasValue &&
                        !publication.IsItemCounter))
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new UpdateObjectiveCounterPacket(
                                publication.MissionId,
                                publication.ObjectiveId,
                                publication.CounterId.Value,
                                publication.CounterValue.Value,
                                publication.InitialValue.Value,
                                publication.TargetValue.Value)),
                        $"mission {publication.MissionId} objective counter");

                foreach (var publication in _publications.Where(
                    publication => publication.IsItemCounter))
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new UpdateObjectiveItemCounterPacket(
                                publication.MissionId,
                                publication.ObjectiveId,
                                publication.CounterId.Value,
                                publication.CounterValue.Value,
                                publication.TargetValue.Value)),
                        $"mission {publication.MissionId} objective item counter");

                foreach (var publication in _publications.Where(
                    publication => publication.Completed))
                {
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new ObjectiveCompletedPacket(
                                publication.MissionId,
                                publication.ObjectiveId)),
                        $"mission {publication.MissionId} objective completion");
                }

                foreach (var publication in _publications)
                {
                    foreach (var objectiveId in publication.RevealedObjectiveIds)
                        TryPublish(
                            () => client.CallMethod(
                                client.Player.EntityId,
                                new ObjectiveRevealedPacket(
                                    publication.MissionId,
                                    objectiveId,
                                    _manager.BuildPublishedMissionInfo(
                                        client.Player,
                                        publication.Definition,
                                        publication.RuntimeMission))),
                            $"mission {publication.MissionId} objective {objectiveId} revealed");
                    foreach (var objectiveId in publication.ActivatedObjectiveIds)
                        TryPublish(
                            () => client.CallMethod(
                                client.Player.EntityId,
                                new ObjectiveActivatedPacket(
                                    publication.MissionId,
                                    objectiveId)),
                            $"mission {publication.MissionId} objective {objectiveId} activated");
                }

                foreach (var missionId in _missionStatusMissionIds)
                    _manager.PublishMissionStatus(
                        client,
                        missionId,
                        $"mission {missionId} status after deadline start");

                foreach (var missionId in _completableMissions)
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionCompleteablePacket(missionId, true)),
                        $"mission {missionId} completable");

                foreach (var failurePlan in _failurePlans)
                    failurePlan.Publish(
                        client,
                        _manager,
                        _startScenario,
                        _startFailureScenario);

                foreach (var publication in _publications)
                    MissionManager.PublishStartedScenarios(
                        client,
                        publication.MissionId,
                        publication.StartScenarioIds,
                        _startScenario);
            }

            private static bool TryGetRuntimeObjective(
                Client client,
                ProgressPublication publication,
                out MissionObjectiveLog objective)
            {
                objective = null;
                return client.Player.Missions.TryGetValue(
                        publication.MissionId, out var mission) &&
                    mission.Objectives.TryGetValue(
                        publication.ObjectiveId, out objective);
            }
        }

        internal readonly struct TransitionActionApplication
        {
            internal static readonly TransitionActionApplication Empty =
                new(
                    new Dictionary<uint, MissionObjectiveState>(),
                    Array.Empty<uint>(),
                    Array.Empty<uint>(),
                    Array.Empty<uint>());

            internal IReadOnlyDictionary<uint, MissionObjectiveState> FinalObjectiveStates { get; }
            internal IReadOnlyList<uint> RevealedObjectiveIds { get; }
            internal IReadOnlyList<uint> ActivatedObjectiveIds { get; }
            internal IReadOnlyList<uint> StartScenarioIds { get; }

            internal TransitionActionApplication(
                IReadOnlyDictionary<uint, MissionObjectiveState> finalObjectiveStates,
                IEnumerable<uint> revealedObjectiveIds,
                IEnumerable<uint> activatedObjectiveIds,
                IEnumerable<uint> startScenarioIds)
            {
                FinalObjectiveStates = new ReadOnlyDictionary<uint, MissionObjectiveState>(
                    new Dictionary<uint, MissionObjectiveState>(
                        finalObjectiveStates ?? new Dictionary<uint, MissionObjectiveState>()));
                RevealedObjectiveIds = Array.AsReadOnly(
                    (revealedObjectiveIds ?? Array.Empty<uint>()).ToArray());
                ActivatedObjectiveIds = Array.AsReadOnly(
                    (activatedObjectiveIds ?? Array.Empty<uint>()).ToArray());
                StartScenarioIds = Array.AsReadOnly(
                    (startScenarioIds ?? Array.Empty<uint>()).ToArray());
            }
        }

        internal readonly struct ProgressCandidate
        {
            internal Mission Definition { get; }
            internal MissionLog RuntimeMission { get; }
            internal MissionObjectiveDefinition ObjectiveDefinition { get; }
            internal MissionObjectiveLog RuntimeObjective { get; }
            internal MissionObjectiveExecutableTransition ExecutableTransition { get; }
            internal MissionProgressEvent Progress { get; }

            internal ProgressCandidate(
                Mission definition,
                MissionLog runtimeMission,
                MissionObjectiveDefinition objectiveDefinition,
                MissionObjectiveLog runtimeObjective,
                MissionObjectiveExecutableTransition executableTransition,
                MissionProgressEvent progress)
            {
                Definition = definition;
                RuntimeMission = runtimeMission;
                ObjectiveDefinition = objectiveDefinition;
                RuntimeObjective = runtimeObjective;
                ExecutableTransition = executableTransition;
                Progress = progress;
            }
        }

        internal readonly struct ProgressPublication
        {
            internal Mission Definition { get; }
            internal MissionLog RuntimeMission { get; }
            internal uint MissionId { get; }
            internal uint ObjectiveId { get; }
            internal MissionObjectiveState? ObjectiveState { get; }
            internal uint? CounterId { get; }
            internal uint? CounterValue { get; }
            internal uint? InitialValue { get; }
            internal uint? TargetValue { get; }
            internal bool Completed { get; }
            internal bool IsItemCounter { get; }
            internal IReadOnlyDictionary<uint, MissionObjectiveState> FinalObjectiveStates { get; }
            internal IReadOnlyList<uint> RevealedObjectiveIds { get; }
            internal IReadOnlyList<uint> ActivatedObjectiveIds { get; }
            internal IReadOnlyList<uint> StartScenarioIds { get; }

            private ProgressPublication(
                Mission definition,
                MissionLog runtimeMission,
                uint missionId,
                uint objectiveId,
                MissionObjectiveState? objectiveState,
                uint? counterId,
                uint? counterValue,
                uint? initialValue,
                uint? targetValue,
                bool completed,
                TransitionActionApplication actionApplication,
                bool isItemCounter = false)
            {
                Definition = definition;
                RuntimeMission = runtimeMission;
                MissionId = missionId;
                ObjectiveId = objectiveId;
                ObjectiveState = objectiveState;
                CounterId = counterId;
                CounterValue = counterValue;
                InitialValue = initialValue;
                TargetValue = targetValue;
                Completed = completed;
                IsItemCounter = isItemCounter;
                FinalObjectiveStates = new ReadOnlyDictionary<uint, MissionObjectiveState>(
                    new Dictionary<uint, MissionObjectiveState>(
                        actionApplication.FinalObjectiveStates));
                RevealedObjectiveIds = Array.AsReadOnly(actionApplication.RevealedObjectiveIds.ToArray());
                ActivatedObjectiveIds = Array.AsReadOnly(actionApplication.ActivatedObjectiveIds.ToArray());
                StartScenarioIds = Array.AsReadOnly(actionApplication.StartScenarioIds.ToArray());
            }

            internal static ProgressPublication ForCompleted(
                ProgressCandidate candidate,
                TransitionActionApplication actionApplication) =>
                new(
                    candidate.Definition,
                    candidate.RuntimeMission,
                    candidate.Definition.MissionId,
                    candidate.ObjectiveDefinition.ObjectiveId,
                    MissionObjectiveState.Completed,
                    null,
                    null,
                    null,
                    null,
                    true,
                    actionApplication);

            internal static ProgressPublication ForTransition(
                ProgressCandidate candidate,
                MissionObjectiveState objectiveState,
                TransitionActionApplication actionApplication) =>
                new(
                    candidate.Definition,
                    candidate.RuntimeMission,
                    candidate.Definition.MissionId,
                    candidate.ObjectiveDefinition.ObjectiveId,
                    objectiveState,
                    null,
                    null,
                    null,
                    null,
                    false,
                    actionApplication);

            internal static ProgressPublication Counter(
                ProgressCandidate candidate,
                uint counterId,
                uint counterValue,
                bool completed,
                TransitionActionApplication actionApplication) =>
                new(
                    candidate.Definition,
                    candidate.RuntimeMission,
                    candidate.Definition.MissionId,
                    candidate.ObjectiveDefinition.ObjectiveId,
                    completed ? MissionObjectiveState.Completed : null,
                    counterId,
                    counterValue,
                    candidate.ExecutableTransition.ProgressRule.InitialValue,
                    candidate.ExecutableTransition.ProgressRule.TargetValue,
                    completed,
                    actionApplication);

            internal static ProgressPublication ItemCounter(
                ProgressCandidate candidate,
                uint itemClassId,
                uint counterValue,
                bool completed,
                TransitionActionApplication actionApplication) =>
                new(
                    candidate.Definition,
                    candidate.RuntimeMission,
                    candidate.Definition.MissionId,
                    candidate.ObjectiveDefinition.ObjectiveId,
                    completed ? MissionObjectiveState.Completed : null,
                    itemClassId,
                    counterValue,
                    candidate.ExecutableTransition.ProgressRule.InitialValue,
                    candidate.ExecutableTransition.ProgressRule.TargetValue,
                    completed,
                    actionApplication,
                    isItemCounter: true);
        }
    }
}
