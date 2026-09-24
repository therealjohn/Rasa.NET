using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Rasa.Missions.Runtime;
using ProgressCandidate = Rasa.Missions.Runtime.MissionProgressCandidate;
using System.Text.Json;
using Rasa.Game.Missions.Content;
using Rasa.Repositories.World;
using Rasa.Game.Missions.Persistence;

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

    public class MissionApplication
    {
        private readonly MissionContentCatalog _catalog;
        private readonly MissionJournalAdapter _journal;
        private readonly MissionProtocolAdapter _protocol;
        private MissionRuntime _runtime => _catalog.Runtime;
        private readonly Game.Missions.Integration.MissionRequirementService _requirements = new();
        private static MissionApplication _instance;
        private static readonly object InstanceLock = new();
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly ManifestationManager _manifestationManager;
        private readonly Action<Item> _beforeRewardItemPublication;
        private readonly Action<PythonPacket> _beforeMissionPacketPublication;
        private readonly MissionDeadlineService _deadlineService;
        private readonly IMissionSceneHost _scenarioService;
        private readonly Func<DateTime> _utcNow;

        public IReadOnlyDictionary<uint, Mission> LoadedMissions => _catalog.View;
        internal int LastRuleEvaluations => _runtime.LastRuleEvaluations;
        internal long TotalRuleEvaluations => _runtime.TotalRuleEvaluations;
        public Game.Missions.World.PublicActorLeaseService PublicActors { get; }
        internal Game.Missions.SceneApplication Scenes { get; }
        internal Game.Missions.GroupCreditService Credit { get; }
        internal Game.Missions.MissionObjectConversations ObjectConversations { get; }
        internal Rasa.Missions.Definitions.ActorPolicyCatalog ActorPolicies { get; } = new();
        internal IMissionSceneHost ScenarioService => _scenarioService;
        internal Action<Item> BeforeRewardItemPublication => _beforeRewardItemPublication;
        internal MissionValidationReport LatestValidationReport { get; private set; } =
            new MissionValidationReport(
                Array.Empty<MissionValidationDiagnostic>(),
                Array.Empty<uint>());

        internal IReadOnlyList<string> Inspect(uint characterId)
        {
            using var unit = _gameUnitOfWorkFactory.CreateChar();
            var store = unit.CharacterMissions.Runtime;
            var lines = new List<string>
            {
                $"Character {characterId}: {unit.CharacterMissions.Count(characterId)} journal slots; {store.History(characterId).Count} outcomes."
            };
            foreach (var assignment in unit.CharacterMissions.Get(characterId))
                lines.Add($"Mission {assignment.MissionId}@{assignment.ContentRevision}: assignment={assignment.AssignmentId} generation={assignment.Generation} version={assignment.Version} state={assignment.MissionState}.");
            foreach (var scene in store.ScenesForCharacter(characterId).Take(50))
            {
                lines.Add($"Scene {scene.RunId}: {scene.ScriptKey}/{scene.StateVersion}@{scene.Release} generation={scene.Generation} version={scene.Version} {scene.Status}; checkpoint={scene.Checkpoint}.");
                foreach (var lease in store.Leases(scene.RunId))
                    lines.Add($"Actor {lease.ActorRole}: map={lease.MapKey} spawn={lease.SpawnKey} owner={lease.RunId} generation={lease.Generation} {lease.State}.");
                foreach (var effect in store.Effects(scene.RunId).Where(effect => effect.Status is "Pending" or "Failed").Take(10))
                    lines.Add($"Operation {effect.OperationKey}: {effect.Status}; {effect.Failure}");
            }
            return lines;
        }

        public static MissionApplication Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new MissionApplication(Server.GameUnitOfWorkFactory);
                    }
                }

                return _instance;
            }
        }

        private MissionApplication(IGameUnitOfWorkFactory gameUnitOfWorkFactory)
            : this(gameUnitOfWorkFactory, new Dictionary<uint, Mission>())
        {
        }

        public MissionApplication(
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

        internal MissionApplication(
            IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            IReadOnlyDictionary<uint, Mission> definitions,
            IReadOnlyDictionary<uint, MissionRewardDefinition> rewardDefinitions,
            ManifestationManager manifestationManager,
            Action<Item> beforeRewardItemPublication = null,
            Action<PythonPacket> beforeMissionPacketPublication = null,
            MissionDeadlineService deadlineService = null,
            IMissionSceneHost scenarioService = null,
            Func<DateTime> utcNow = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            PublicActors = new Game.Missions.World.PublicActorLeaseService(gameUnitOfWorkFactory, () => this,
                runId => Scenes?.LeaseReset(runId));
            _catalog = new MissionContentCatalog(gameUnitOfWorkFactory, definitions, rewardDefinitions);
            _journal = new MissionJournalAdapter(_catalog);
            _manifestationManager = manifestationManager;
            _beforeRewardItemPublication = beforeRewardItemPublication;
            _beforeMissionPacketPublication = beforeMissionPacketPublication;
            _utcNow = utcNow ?? deadlineService?.Clock ?? (() => DateTime.UtcNow);
            _protocol = new MissionProtocolAdapter(gameUnitOfWorkFactory, _catalog, _journal, _utcNow, beforeMissionPacketPublication);
            _deadlineService = deadlineService ?? new MissionDeadlineService(
                () => _gameUnitOfWorkFactory,
                () => this,
                _utcNow);
            _scenarioService = scenarioService ?? new MissionSceneHost(
                () => _gameUnitOfWorkFactory,
                () => this,
                _manifestationManager);
            Scenes = new Game.Missions.SceneApplication(_gameUnitOfWorkFactory, this, _manifestationManager, utcNow: _utcNow);
            Credit = new Game.Missions.GroupCreditService(_gameUnitOfWorkFactory, this);
            ObjectConversations = new Game.Missions.MissionObjectConversations(_gameUnitOfWorkFactory, this);
            Game.Missions.Integration.MissionRuntimeComposition.Bind(_catalog, Scenes, PublicActors, ActorPolicies);
        }

        internal MissionValidationReport LoadMissions()
        {
            var report = _catalog.Load();
            Game.Missions.Integration.MissionRuntimeComposition.Bind(_catalog, Scenes, PublicActors, ActorPolicies);
            LatestValidationReport = report;
            return report;
        }

        internal void Hydrate(Manifestation player, IReadOnlyList<CharacterMissionEntry> rows,
            CharacterMissionProgressSnapshot progress) => _journal.Hydrate(player, rows, progress);
        internal void Hydrate(Manifestation player, IReadOnlyList<CharacterMissionEntry> rows) => _journal.Hydrate(player, rows);
        internal void HydrateAndClearInvalid(Manifestation player, ICharUnitOfWork unit) => _journal.HydrateAndClearInvalid(player, unit);
        internal bool TryHydrateMission(uint characterId, CharacterMissionEntry row,
            CharacterMissionProgressSnapshot progress, out MissionLog mission) =>
            _journal.TryHydrateMission(characterId, row, progress, out mission);

        public IReadOnlyDictionary<uint, MissionInfo> BuildStatusSnapshot(Manifestation player) =>
            _protocol.BuildStatusSnapshot(player);
        internal void PublishMissionStatus(Client client, uint missionId, string description) =>
            _protocol.PublishMissionStatus(client, missionId, description);
        internal MissionInfo BuildPublishedMissionInfo(Manifestation player, Mission definition, MissionLog mission) =>
            _protocol.BuildPublishedMissionInfo(player, definition, mission);
        internal void PublishAnnouncementAudio(Client client, uint missionId, uint greetingId) =>
            _protocol.PublishAnnouncementAudio(client, missionId, greetingId);
        public void PublishInitialState(Client client)
        {
            Scenes.Resume(client);
            _protocol.PublishInitialState(client);
        }

        internal void RefreshNpcConversationStatuses(Client client)
        {
            var player = client?.Player;
            var map = player?.MapChannel;
            if (map == null || client.State != ClientState.Ingame ||
                !CellManager.Instance.IsInWorld(client))
                return;

            var npcs = CellManager.CellsIn(map, player.Cells)
                .SelectMany(cell => cell.CreatureList)
                .Where(creature => creature.Npc != null)
                .Distinct()
                .ToArray();
            foreach (var npc in npcs)
                if (MapInstanceScope.TryGetCreature(map, npc.EntityId, out var current) &&
                    ReferenceEquals(current, npc))
                    TryPublish(
                        () => NpcManager.Instance.UpdateConversationStatus(client, npc, this),
                        $"NPC {npc.EntityId} conversation status after mission progress");
        }

        internal void OfferRadioMission(Client client, uint missionId)
        {
            if (!IsActivePlayer(client) ||
                client.Player.Missions.ContainsKey(missionId))
                return;

            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
            if (unitOfWork.CharacterMissions.GetByCharacterAndMission(client.Player.Id, missionId) != null ||
                !ArePrerequisitesSatisfied(client.Player, missionId, unitOfWork, out _))
                return;

            if (!TryGetOperationalMission(missionId, out var definition))
            {
                Logger.WriteLog(LogType.Error,
                    $"Unable to offer radio mission {missionId} to character {client.Player.Id}: definition is not operational.");
                return;
            }

            var offer = _protocol.BuildOfferInfo(definition);
            PublishMissionPacket(client, new DispenseRadioMissionPacket(missionId, offer, true),
                $"radio mission {missionId} offer");
        }

        internal bool TryGetAreaDefinition(
            uint missionId,
            uint areaId,
            out MissionAreaDefinition area)
        {
            if (_catalog.Areas.TryGetValue(missionId, out var areas) &&
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
            if (_catalog.SpawnGroups.TryGetValue(missionId, out var spawnGroups) &&
                spawnGroups.TryGetValue(spawnGroupId, out spawnGroup))
                return true;

            spawnGroup = null;
            return false;
        }

        internal bool TryGetScenarioDefinitions(
            uint missionId,
            out IReadOnlyDictionary<uint, MissionScenarioDefinition> scenarios) =>
            _catalog.Scenarios.TryGetValue(missionId, out scenarios);

        internal bool TryGetScenarioDefinition(
            uint missionId,
            uint scenarioId,
            out MissionScenarioDefinition scenario)
        {
            if (_catalog.Scenarios.TryGetValue(missionId, out var scenarios) &&
                scenarios.TryGetValue(scenarioId, out scenario))
                return true;

            scenario = null;
            return false;
        }

        internal IReadOnlyDictionary<uint, MissionRewardDefinition> GetRewardPackages(uint missionId) =>
            _catalog.RewardPackages.TryGetValue(missionId, out var rewards)
                ? rewards
                : new Dictionary<uint, MissionRewardDefinition>();

        private bool TryGetScenarioStepDefinition(
            uint missionId,
            uint scenarioId,
            uint stepId,
            out MissionScenarioStepDefinition step)
        {
            step = null;
            if (!_catalog.Scenarios.TryGetValue(missionId, out var scenarios) ||
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

        private bool ShouldStartScenarioAutomatically(uint missionId, uint scenarioId) =>
            TryGetScenarioDefinition(missionId, scenarioId, out var scenario) &&
            scenario.StartPolicy != MissionScenarioStartPolicy.PlayerTriggered;

        internal void PublishStartedScenarios(
            Client client,
            uint missionId,
            IEnumerable<uint> scenarioIds,
            Func<Client, uint, uint, bool> startScenario)
        {
            foreach (var scenarioId in scenarioIds ?? Array.Empty<uint>())
            {
                if (!ShouldStartScenarioAutomatically(missionId, scenarioId))
                    continue;
                TryPublish(
                    () => startScenario?.Invoke(client, missionId, scenarioId),
                    $"mission {missionId} start scenario {scenarioId}");
            }
        }

        internal void RecordScenarioCreatureDeath(SpawnPool spawnPool) =>
            (_scenarioService as MissionSceneHost)?.RecordScenarioCreatureDeath(spawnPool);

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

        public bool TryAcceptNpcMission(Client client, ulong npcEntityId, uint missionId) =>
            TryAcceptMission(client, missionId, npcEntityId);

        internal bool TryAcceptRadioMission(
            Client client,
            uint missionId,
            Func<Manifestation, uint, ICharUnitOfWork, bool> canAccept) =>
            TryAcceptMission(client, missionId, null, canAccept);

        private bool TryAcceptMission(
            Client client,
            uint missionId,
            ulong? npcEntityId,
            Func<Manifestation, uint, ICharUnitOfWork, bool> canAcceptRadio = null)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client))
                    return Reject($"Rejected mission {missionId}: character is not active in the world.");
                if (!TryGetOperationalMission(missionId, out var definition))
                    return Reject($"Rejected mission {missionId}: definition is not operational.");
                if (npcEntityId.HasValue)
                {
                    if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId.Value, out var npc))
                        return Reject($"Rejected mission {missionId}: NPC entity {npcEntityId} is not in the current map instance.");
                    if (npc.Npc == null || npc.DbId != definition.MissionGiver)
                        return Reject($"Rejected mission {missionId}: NPC {npc.DbId} is not its authoritative giver.");
                }
                else if (canAcceptRadio == null)
                    return Reject($"Rejected radio mission {missionId}: no admission policy is available.");
                if (!ArePrerequisitesSatisfied(client.Player, missionId, out var prerequisiteFailure))
                    return Reject($"Rejected mission {missionId}: {prerequisiteFailure}");
                if (client.Player.Missions.ContainsKey(missionId))
                    return Reject($"Rejected mission {missionId}: character {client.Player.Id} already has it.");

                if (!PublicActors.TryPrepare(client, missionId, definition.ContentRevision,
                        out var reservation, out var reservationFailure))
                    return Reject($"Rejected mission {missionId}: {reservationFailure}");
                using var admission = reservation;
                var objectiveLogs = definition.CreateInitialObjectiveLogs();
                var initialCompleteable = MissionRuntime.IsComplete(definition,
                    objectiveLogs.ToDictionary(entry => entry.Key, entry => entry.Value.State));
                var log = new MissionLog(
                    missionId,
                    MissionState.Active,
                    initialCompleteable,
                    objectiveLogs);
                var accepted = false;
                var durableLogFull = false;
                Action<Client> inventoryPublication = null;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        if (!npcEntityId.HasValue &&
                            !canAcceptRadio(client.Player, missionId, unitOfWork))
                        {
                            prerequisiteFailure = "the character is not eligible for this radio mission offer.";
                            return;
                        }
                        if (MissionRuntime.Admit(true, false, false,
                                unitOfWork.CharacterMissions.Count(client.Player.Id)).Rejection ==
                            MissionRejection.JournalFull)
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
                        if (unitOfWork.CharacterMissions.Runtime.HasHistory(client.Player.Id, missionId) ||
                            unitOfWork.CharacterMissions.GetByCharacterAndMission(
                                client.Player.Id, missionId) != null)
                            return;

                        unitOfWork.CharacterMissions.Add(new CharacterMissionEntry(
                            client.Player.Id,
                            missionId,
                            (uint)MissionState.Active)
                        {
                            Completeable = initialCompleteable,
                            ContentRevision = definition.ContentRevision
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
                        var assignment = unitOfWork.CharacterMissions.GetByCharacterAndMission(client.Player.Id, missionId);
                        if (admission != null)
                            admission.Persist(unitOfWork, assignment);
                        else
                            Scenes.PrepareAssignment(unitOfWork, client, assignment);
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
                        inventoryPublication = MissionInventory.Plan(client, unitOfWork, this, missionId);
                        accepted = true;
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
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

                admission?.Commit();
                inventoryPublication?.Invoke(client);
                client.Player.Missions.Add(missionId, log);
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionGainedPacket(
                        missionId,
                        BuildPublishedMissionInfo(
                            client.Player,
                            definition,
                            log)));

                TryPublish(() => _scenarioService.OnMissionAccepted(client, missionId),
                    $"mission {missionId} acceptance world state");
                TryPublish(() => Scenes.MissionChanged(client, missionId, "Accepted"),
                    $"mission {missionId} experience acceptance");
                _protocol.PublishAudio(client, missionId, Rasa.Missions.Content.MissionAudioEvent.Accepted);

                // ClassifyNpcConversation's "dispensable" list is recomputed here, so the giver's
                // available-mission icon would otherwise keep showing what it showed when this
                // NPC first became visible (CreatePhysicalEntityOnClient's one-time snapshot) -
                // nothing previously refreshed it after a mission state actually changed.
                RefreshNpcConversationStatuses(client);
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
                // Older turn-ins persisted Success before claiming rewards.
                var alreadySuccessful = client.Player != null &&
                    client.Player.Missions.TryGetValue(missionId, out var mission) &&
                    mission.State == MissionState.Success;
                return TryGrantNpcMission(
                    client,
                    npcEntityId,
                    missionId,
                    selectionIndex,
                    rating,
                    alreadySuccessful ? MissionState.Success : MissionState.Active,
                    requireCompletable: !alreadySuccessful,
                    publishCompleted: !alreadySuccessful);
            }
        }

        internal bool TryCompleteNpcMission(
            Client client,
            ulong npcEntityId,
            uint missionId,
            int selectionIndex)
            => TryCompleteNpcMission(
                client,
                npcEntityId,
                missionId,
                selectionIndex,
                null);

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
                if (!_catalog.Rewards.TryGetValue(missionId, out var rewardDefinition))
                    return Reject($"Rejected mission {missionId} turn-in: no approved reward definition is loaded.");
                if (rating.HasValue)
                    return Reject($"Rejected mission {missionId} turn-in: ratings are not supported.");
                if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var npc))
                    return Reject($"Rejected mission {missionId} turn-in: NPC entity {npcEntityId} is not in the current map instance.");
                if (npc.Npc == null || npc.DbId != definition.MissionReciver)
                    return Reject($"Rejected mission {missionId} turn-in: NPC {npc.DbId} is not its authoritative receiver.");
                client.Player.Missions.TryGetValue(missionId, out var runtimeMission);
                if (!MissionRuntime.CanTurnIn(runtimeMission, expectedState, requireCompletable).Accepted)
                    return Reject($"Rejected mission {missionId} turn-in: runtime mission is not in the required state.");
                if (!_requirements.Evaluate(client.Player, definition.TurnInRequirement))
                    return Reject($"Rejected mission {missionId} turn-in: authored turn-in requirement is not satisfied.");
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
                            if (!_requirements.Evaluate(client.Player, definition.TurnInRequirement, unitOfWork))
                                throw new GameplayRejectionException("Durable turn-in requirement is not satisfied.");
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
                            var progressEvents = grant.CreateItemAcquisitionEvents();
                            if (publishCompleted)
                                progressEvents = new[] { MissionProgressEvent.Mission(missionId) }
                                    .Concat(progressEvents).ToArray();
                            progressPlan = PlanProgress(
                                client,
                                progressEvents,
                                unitOfWork);
                            mission.MissionState = (uint)MissionState.Completed;
                            mission.Completeable = false;
                            mission.Version++;
                            unitOfWork.CharacterMissions.Runtime.Archive(mission, _utcNow());
                            unitOfWork.CharacterMissions.Runtime.Add(new MissionReceiptEntry
                            {
                                OwnerId = mission.AssignmentId, Generation = 0,
                                OperationKey = "mission-reward", Kind = "Grant", CreatedAtUtc = _utcNow()
                            });
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
                    client.Player.MissionHistory[missionId] = MissionState.Completed;
                    grant.ConvergeRuntime(client);
                    if (publishCompleted)
                    {
                        PublishMissionPacket(
                            client,
                            new MissionCompleteablePacket(missionId, false),
                            $"mission {missionId} no longer completable");
                        PublishMissionPacket(
                            client,
                            new MissionCompletedPacket(missionId),
                            $"mission {missionId} completed");
                    }
                    progressPlan.Publish(client);
                    grant.Publish(client, _manifestationManager);
                    TryPublish(() => Scenes.MissionChanged(client, missionId, "Rewarded"),
                        $"mission {missionId} experience outcome");
                    TryPublish(
                        () => client.CallMethod(
                            client.Player.EntityId,
                            new MissionRewardedPacket(missionId)),
                        $"mission {missionId} rewarded");
                    _protocol.PublishAudio(client, missionId, Rasa.Missions.Content.MissionAudioEvent.Completed);
                    RefreshNpcConversationStatuses(client);
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
            if (EntityManager.Instance.TryGetObject(npcEntityId, out var conversationObject) &&
                conversationObject.MissionConversation != null)
                return ObjectConversations.Complete(client, npcEntityId, missionId, objectiveId, playerFlagId);

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
                if (!_requirements.Evaluate(client.Player, definition.ObjectiveRequirements.GetValueOrDefault(objectiveId)))
                    return Reject($"Rejected mission {missionId} objective {objectiveId}: authored objective requirement is not satisfied.");
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
                        if (!_requirements.Evaluate(client.Player, definition.ObjectiveRequirements.GetValueOrDefault(objectiveId), unitOfWork))
                            throw new GameplayRejectionException("Durable objective requirement is not satisfied.");
                        actionApplication = ApplyTransitionActions(
                            objectiveId,
                            transition,
                            durableObjectives, unitOfWork, client.Player.Id);
                        QueueStartedScenarios(client, missionId, actionApplication.StartScenarioIds, unitOfWork);

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
                {
                    client.CallMethod(client.Player.EntityId,
                        new MissionCompleteablePacket(missionId, true));
                    TryPublish(() => Scenes.MissionChanged(client, missionId, "Completeable"),
                        $"mission {missionId} experience ready state");
                }
                foreach (var scenarioId in actionApplication.StartScenarioIds)
                    if (ShouldStartScenarioAutomatically(missionId, scenarioId))
                        _scenarioService.TryExecute(client, missionId, scenarioId);
                foreach (var flagChange in actionApplication.PlayerFlagChanges)
                    client.Player.PlayerFlags[flagChange.FlagId] = flagChange.Value;
                foreach (var spawnGroupId in actionApplication.ActivateSpawnGroupIds)
                    _scenarioService.TryActivateSpawnGroup(client, missionId, spawnGroupId);
                if (actionApplication.ShownIndicatorIds.Count > 0)
                    PublishMissionStatus(client, missionId, $"mission {missionId} status after indicator reveal");
                RefreshNpcConversationStatuses(client);

                // See the matching comment in MissionProgressPublicationPlan.Publish: this is the
                // only way an ObjectiveState-gated transition elsewhere in the mission gets a
                // chance to fire when this NPC conversation is what actually moved the objective.
                foreach (var touchedObjectiveId in actionApplication.FinalObjectiveStates.Keys
                    .Append(objectiveId)
                    .Distinct())
                    RecordProgress(
                        client,
                        MissionProgressEvent.ObjectiveState(
                            missionId,
                            touchedObjectiveId,
                            durableObjectives[touchedObjectiveId].ObjectiveState));
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
                                        transition.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed),
                            inventoryPublication: MissionInventory.Plan(client, unitOfWork, this, missionId));
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

                Action<Client> inventoryPublication = null;
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
                            _catalog.Missions[missionId],
                            durableMission,
                            unitOfWork.CharacterMissionProgress.GetTracked(
                                client.Player.Id,
                                missionId));
                        inventoryPublication = MissionInventory.Plan(client, unitOfWork, this, missionId);
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
                inventoryPublication?.Invoke(client);
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
                    var requirement = definition.ObjectiveRequirements.GetValueOrDefault(objectiveId);
                    if (!_requirements.Evaluate(client.Player, requirement) ||
                        !_requirements.Evaluate(client.Player, requirement, unitOfWork))
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
                        {
                            PublishMissionPacket(
                                client,
                                new MissionCompleteablePacket(missionId, true),
                                $"mission {missionId} completable");
                            TryPublish(() => Scenes.MissionChanged(client, missionId, "Completeable"),
                                $"mission {missionId} experience ready state");
                        }
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
                                        transition.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed),
                            inventoryPublication: MissionInventory.Plan(client, unitOfWork, this, missionId)));
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
                if (runtimeMission.State is MissionState.Success or MissionState.Failed or MissionState.Completed)
                    client.Player.MissionHistory[missionId] = runtimeMission.State;
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
                    _catalog.Abandonment.GetValueOrDefault(
                        missionId,
                        MissionAbandonmentPolicy.Allowed) == MissionAbandonmentPolicy.Prohibited)
                    return false;

                var authoredFailurePlan = MissionFailurePublicationPlan.Empty;
                var authoredFailure = false;
                IReadOnlyList<string> cancelledScenes = Array.Empty<string>();
                Action<Client> inventoryPublication = null;
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

                        cancelledScenes = Scenes.CancelAssignment(unitOfWork, durableMission);
                        unitOfWork.CharacterMissions.Remove(client.Player.Id, missionId);
                        inventoryPublication = MissionInventory.Plan(client, unitOfWork, this, missionId);
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
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to abandon mission {missionId} for character {client.Player.Id}: {error.Message}");
                    return false;
                }

                client.Player.Missions.Remove(missionId);
                inventoryPublication?.Invoke(client);
                Scenes.CompleteAssignmentCancellation(cancelledScenes);
                PublishMissionPacket(client, new MissionDiscardedPacket(missionId), $"mission {missionId} abandoned");
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
                durableObjectives, unitOfWork, client.Player.Id);
            _deadlineService.SynchronizeMission(
                unitOfWork,
                client.Player.Id,
                definition,
                durableMission,
                durableObjectives);
            if (deadline != null)
            {
                unitOfWork.CharacterMissionDeadlines.SetState(
                    client.Player.Id,
                    definition.MissionId,
                    CharacterMissionDeadlineState.Cancelled);
            }
            publicationPlan = new MissionFailurePublicationPlan(
                definition.MissionId,
                authoredFailure.Objective.ObjectiveId,
                MissionState.Failed,
                false,
                runtimeMission.Completeable,
                failureActions.StartScenarioIds,
                inventoryPublication: MissionInventory.Plan(client, unitOfWork, this, definition.MissionId),
                flags: failureActions.PlayerFlagChanges.Count > 0 ? unitOfWork.CharacterFlags.Get(client.Player.Id) : null);
            QueueStartedScenarios(client, definition.MissionId, failureActions.StartScenarioIds, unitOfWork);
            return true;
        }

        internal bool HasPlayerTriggeredScenario(uint missionId) =>
            _catalog.Scenarios.TryGetValue(missionId, out var scenarios) &&
            scenarios.Values.Any(scenario =>
                scenario.StartPolicy == MissionScenarioStartPolicy.PlayerTriggered);

        internal bool RecordProgress(Client client, MissionProgressEvent progress)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    client.AccountEntry == null)
                    return false;

                var plan = MissionProgressPublicationPlan.Empty;
                try
                {
                    if (!HasProgressCandidate(client, progress))
                        return false;
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

        internal IReadOnlyList<ProgressCandidate> ProgressCandidates(Client client, MissionProgressEvent progress) =>
            client?.Player == null ? Array.Empty<ProgressCandidate>() :
                _runtime.SelectCandidates(client.Player.Missions, new[] { progress },
                    client.Player.GainedWaypoints.Select(entry => entry.WaypointId).ToHashSet(),
                    client.Player.Logos.ToHashSet());

        internal bool IsObjectiveEligibleAtEvent(Client client, uint missionId, uint objectiveId, ICharUnitOfWork unit) =>
            TryGetOperationalMission(missionId, out var mission) && mission.Objectives.ContainsKey(objectiveId) &&
            _requirements.Evaluate(client.Player, mission.ObjectiveRequirements.GetValueOrDefault(objectiveId), unit);

        private bool HasProgressCandidate(
            Client client,
            MissionProgressEvent progress)
        {
            if (!Enum.IsDefined(
                    typeof(MissionProgressEventKind), progress.Kind) ||
                progress.SubjectId == 0 ||
                progress.Quantity == 0)
                return false;

            return _runtime.SelectCandidates(client.Player.Missions, new[] { progress },
                client.Player.GainedWaypoints.Select(entry => entry.WaypointId).ToHashSet(),
                client.Player.Logos.ToHashSet()).Count > 0;
        }

        internal MissionProgressPublicationPlan PlanProgress(
            Client client,
            IReadOnlyList<MissionProgressEvent> progressEvents,
            ICharUnitOfWork unitOfWork,
            IReadOnlySet<(uint MissionId, uint ObjectiveId)> allowedObjectives = null) =>
            PlanProgressCore(client, progressEvents, unitOfWork, allowedObjectives, requirementsFrozen: false);

        internal MissionProgressPublicationPlan PlanFrozenProgress(
            Client client,
            IReadOnlyList<MissionProgressEvent> progressEvents,
            ICharUnitOfWork unitOfWork,
            IReadOnlySet<(uint MissionId, uint ObjectiveId)> eligibleObjectives) =>
            PlanProgressCore(client, progressEvents, unitOfWork,
                eligibleObjectives ?? throw new ArgumentNullException(nameof(eligibleObjectives)), requirementsFrozen: true);

        private MissionProgressPublicationPlan PlanProgressCore(
            Client client,
            IReadOnlyList<MissionProgressEvent> progressEvents,
            ICharUnitOfWork unitOfWork,
            IReadOnlySet<(uint MissionId, uint ObjectiveId)> allowedObjectives,
            bool requirementsFrozen)
        {
            if (client == null ||
                unitOfWork == null ||
                !IsActivePlayer(client) ||
                client.AccountEntry == null)
                return MissionProgressPublicationPlan.Empty;

            var candidates = _runtime.SelectCandidates(client.Player.Missions, progressEvents,
                client.Player.GainedWaypoints.Select(entry => entry.WaypointId).ToHashSet(),
                client.Player.Logos.ToHashSet());
            if (!requirementsFrozen)
                candidates = candidates.Where(candidate => _requirements.Evaluate(client.Player,
                    candidate.Definition.ObjectiveRequirements.GetValueOrDefault(candidate.ObjectiveDefinition.ObjectiveId))).ToArray();
            if (allowedObjectives != null)
                candidates = candidates.Where(candidate => allowedObjectives.Contains(
                    (candidate.Definition.MissionId, candidate.ObjectiveDefinition.ObjectiveId))).ToArray();
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

                    if (!requirementsFrozen && !_requirements.Evaluate(client.Player, candidate.Definition.ObjectiveRequirements
                            .GetValueOrDefault(candidate.ObjectiveDefinition.ObjectiveId), unitOfWork))
                        throw new GameplayRejectionException("Durable objective requirement is not satisfied.");
                    var rule = candidate.ExecutableTransition.ProgressRule;
                    IReadOnlySet<uint> durableSubjects = null;
                    if (rule.RuleType == MissionProgressRuleType.CompleteDistinctSet)
                    {
                        durableSubjects = rule.Kind == MissionProgressEventKind.WaypointAcquired
                            ? durableWaypointIds ??= unitOfWork.CharacterTeleporters.Get(client.Player.Id)
                                .Select(entry => entry.WaypointId).ToHashSet()
                            : durableLogosIds ??= unitOfWork.CharacterLogoses.GetLogos(client.Player.Id).ToHashSet();
                    }
                    var decision = MissionRuntime.Evaluate(candidate,
                        new MissionObjectiveLog(durableObjective.ObjectiveId,
                            (MissionObjectiveState)durableObjective.ObjectiveState,
                            durableObjective.Counters.ToDictionary(entry => entry.CounterId, entry => entry.CounterValue),
                            durableObjective.ItemCounters.ToDictionary(entry => entry.ItemClassId, entry => entry.CounterValue)),
                        durableSubjects);
                    if (decision.CounterId.HasValue)
                    {
                        if (decision.IsItemCounter)
                            durableObjective.ItemCounters.Single(entry => entry.ItemClassId == decision.CounterId.Value)
                                .CounterValue = decision.CounterValue.Value;
                        else
                            durableObjective.Counters.Single(entry => entry.CounterId == decision.CounterId.Value)
                                .CounterValue = decision.CounterValue.Value;
                        var completed = decision.State == MissionObjectiveState.Completed;
                        if (completed)
                            durableObjective.ObjectiveState = (byte)MissionObjectiveState.Completed;
                        var actions = completed
                            ? ApplyTransitionActions(candidate.ObjectiveDefinition.ObjectiveId,
                                candidate.ExecutableTransition, durableObjectives, unitOfWork, client.Player.Id)
                            : TransitionActionApplication.Empty;
                        publications.Add(decision.IsItemCounter
                            ? ProgressPublication.ItemCounter(candidate, decision.CounterId.Value,
                                decision.CounterValue.Value, completed, actions)
                            : ProgressPublication.Counter(candidate, decision.CounterId.Value,
                                decision.CounterValue.Value, completed, actions));
                        continue;
                    }
                    var toState = decision.State.Value;

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
                            durableObjectives, unitOfWork, client.Player.Id);
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
                                    MissionProgressEventKind.DeadlineElapsed,
                                changesFlags: failureActions.PlayerFlagChanges.Count > 0));
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
                                    durableObjectives, unitOfWork, client.Player.Id)));
                        continue;
                    }

                    durableObjective.ObjectiveState =
                        (byte)MissionObjectiveState.Completed;
                    publications.Add(ProgressPublication.ForCompleted(
                        candidate,
                        ApplyTransitionActions(
                            candidate.ObjectiveDefinition.ObjectiveId,
                            candidate.ExecutableTransition,
                            durableObjectives, unitOfWork, client.Player.Id)));
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

            foreach (var publication in publications)
                QueueStartedScenarios(client, publication.MissionId, publication.StartScenarioIds, unitOfWork);
            foreach (var failure in failurePlans)
                QueueStartedScenarios(client, failure.MissionId, failure.StartScenarioIds, unitOfWork);
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
                this,
                (progressClient, progressedMissionId, spawnGroupId) =>
                    _scenarioService.TryActivateSpawnGroup(
                        progressClient,
                        progressedMissionId,
                        spawnGroupId),
                MissionInventory.Plan(client, unitOfWork, this),
                flags: publications.Any(publication => publication.PlayerFlagChanges.Count > 0) ||
                    failurePlans.Any(failure => failure.ChangesFlags)
                    ? unitOfWork.CharacterFlags.Get(client.Player.Id) : null);
        }

        private void QueueStartedScenarios(Client client, uint missionId, IEnumerable<uint> scenarioIds,
            ICharUnitOfWork unit)
        {
            foreach (var scenarioId in scenarioIds.Where(id => ShouldStartScenarioAutomatically(missionId, id)))
                Scenes.QueueSequence(unit, client, missionId, scenarioId);
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
            IReadOnlyDictionary<uint, CharacterMissionObjectiveEntry> durableObjectives,
            ICharUnitOfWork unitOfWork, uint characterId)
        {
            if (transition == null)
                return TransitionActionApplication.Empty;

            var decision = MissionActionPlanner.Decide(currentObjectiveId, transition,
                durableObjectives.ToDictionary(entry => entry.Key, entry => (MissionObjectiveState)entry.Value.ObjectiveState));
            foreach (var state in decision.States)
                durableObjectives[state.Key].ObjectiveState = (byte)state.Value;
            var revealed = decision.Revealed;
            var activated = decision.Activated;
            var startedScenarios = new List<uint>();
            var ambientConversations = new List<AmbientConversationRequest>();
            var finalStates = decision.States;
            var shownIndicators = new List<uint>();
            var playerFlagChanges = new List<PlayerFlagChange>();
            var activatedSpawnGroups = new List<uint>();

            foreach (var action in decision.Effects)
            {
                switch (action.Kind)
                {
                    case MissionActionKind.StartScenario:
                        if (action.ScenarioId.HasValue &&
                            !startedScenarios.Contains(action.ScenarioId.Value))
                            startedScenarios.Add(action.ScenarioId.Value);
                        break;

                    case MissionActionKind.ShowAmbientConversation:
                        if (!action.NpcPackageId.HasValue)
                            throw new GameplayRejectionException(
                                $"Transition {transition.TransitionId} ambient conversation action is missing its greeting id.");
                        ambientConversations.Add(new AmbientConversationRequest(
                            action.NpcPackageId.Value));
                        break;

                    case MissionActionKind.ShowIndicator:
                        if (!action.IndicatorId.HasValue)
                            throw new GameplayRejectionException(
                                $"Transition {transition.TransitionId} show indicator action is missing its indicator id.");
                        shownIndicators.Add(action.IndicatorId.Value);
                        break;

                    case MissionActionKind.SetPlayerFlag:
                        if (!action.PlayerFlagId.HasValue || !action.PlayerFlagValue.HasValue)
                            throw new GameplayRejectionException(
                                $"Transition {transition.TransitionId} set player flag action is missing its flag id or value.");
                        if (!CharacterFlagIds.IsMissionFlag(action.PlayerFlagId.Value))
                            throw new GameplayRejectionException("Mission flag actions cannot write zero or reserved server flag IDs.");
                        unitOfWork.CharacterFlags.Set(characterId, action.PlayerFlagId.Value, action.PlayerFlagValue.Value);
                        playerFlagChanges.Add(new PlayerFlagChange(
                            action.PlayerFlagId.Value,
                            action.PlayerFlagValue.Value));
                        break;

                    case MissionActionKind.ActivateSpawnGroup:
                        if (!action.SpawnGroupId.HasValue)
                            throw new GameplayRejectionException(
                                $"Transition {transition.TransitionId} activate spawn group action is missing its spawn group id.");
                        activatedSpawnGroups.Add(action.SpawnGroupId.Value);
                        break;
                }
            }

            return new TransitionActionApplication(
                finalStates,
                revealed,
                activated,
                startedScenarios,
                ambientConversations,
                shownIndicators,
                playerFlagChanges,
                activatedSpawnGroups);
        }

        internal static bool IsPublishedState(MissionState state) =>
            state == MissionState.Active ||
            state == MissionState.Success ||
            state == MissionState.Failed ||
            state == MissionState.Completed;

        internal bool TryGetRewardInfo(uint missionId, out RewardInfo rewardInfo)
        {
            if (_catalog.Rewards.TryGetValue(missionId, out var definition))
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
            if (_catalog.Missions.TryGetValue(missionId, out var definition) &&
                !_requirements.Evaluate(player, definition.Requirement))
            {
                failure = "authored admission requirements are not satisfied.";
                return false;
            }
            if (!_catalog.Prerequisites.TryGetValue(missionId, out var prerequisites) ||
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
            if (_catalog.Missions.TryGetValue(missionId, out var definition) && definition.Requirement != null)
            {
                if (!_requirements.Evaluate(player, definition.Requirement, unitOfWork))
                {
                    failure = "durable admission requirements are not satisfied.";
                    return false;
                }
            }
            if (!_catalog.Prerequisites.TryGetValue(missionId, out var prerequisites) ||
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
                    (TryGetMissionLogState(player.Missions, prerequisite.RequiredMissionId, out var completedState) ||
                     prerequisite.RequiredMissionId.HasValue &&
                     player.MissionHistory.TryGetValue(prerequisite.RequiredMissionId.Value, out completedState)) &&
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
                    prerequisite.PlayerFlagId.HasValue && prerequisite.PlayerFlagValue.HasValue &&
                    unitOfWork.CharacterFlags.HasValue(player.Id, prerequisite.PlayerFlagId.Value, prerequisite.PlayerFlagValue.Value),
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
                var history = unitOfWork.CharacterMissions.Runtime.History(characterId)
                    .SingleOrDefault(entry => entry.MissionId == missionId.Value);
                if (history != null)
                {
                    state = (MissionState)history.Outcome;
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

            foreach (var mission in _runtime.ForNpc(creature.DbId, creature.Npc.NpcPackageId))
            {
                if (!mission.IsOperational)
                    continue;
                if (!player.Missions.TryGetValue(mission.MissionId, out var log))
                {
                    if (player.MissionHistory.TryGetValue(mission.MissionId, out var outcome) &&
                        outcome is MissionState.Success or MissionState.Completed)
                        continue;
                    if (mission.MissionGiver == creature.DbId &&
                        ArePrerequisitesSatisfied(player, mission.MissionId, out _))
                        dispensable.Add(
                            mission.MissionId,
                            _protocol.BuildOfferInfo(mission, MissionState.Active));
                    continue;
                }

                if (log.State == MissionState.Active)
                {
                    if (log.Completeable &&
                        mission.MissionReciver == creature.DbId &&
                        _requirements.Evaluate(player, mission.TurnInRequirement))
                    {
                        var completionReward = _catalog.Rewards.TryGetValue(
                            mission.MissionId, out var definition)
                            ? definition.CreateInfo()
                            : new RewardInfo();
                        completeable.Add(mission.MissionId, completionReward);
                        continue;
                    }

                    foreach (var objective in mission.Objectives.Values)
                    {
                        if (!log.Objectives.TryGetValue(objective.ObjectiveId, out var objectiveLog) ||
                            objectiveLog.State != MissionObjectiveState.Incomplete ||
                            !_requirements.Evaluate(player, mission.ObjectiveRequirements.GetValueOrDefault(objective.ObjectiveId)))
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
                    _requirements.Evaluate(player, mission.TurnInRequirement) &&
                    TryGetRewardInfo(mission.MissionId, out var reward))
                {
                    completeable.Add(mission.MissionId, reward);
                }
            }

            return new MissionConversationState(
                dispensable,
                objectives,
                completeable,
                rewardable);
        }

        internal bool TryGetOperationalMission(uint missionId, out Mission mission)
        {
            if (_catalog.Missions.TryGetValue(missionId, out mission) && mission.IsOperational)
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

        internal void PublishMissionPacket(Client client, PythonPacket packet, string description) =>
            _protocol.PublishMissionPacket(client, packet, description);

        internal readonly struct TransitionActionApplication
        {
            internal static readonly TransitionActionApplication Empty =
                new(
                    new Dictionary<uint, MissionObjectiveState>(),
                    Array.Empty<uint>(),
                    Array.Empty<uint>(),
                    Array.Empty<uint>(),
                    Array.Empty<AmbientConversationRequest>(),
                    Array.Empty<uint>(),
                    Array.Empty<PlayerFlagChange>(),
                    Array.Empty<uint>());

            internal IReadOnlyDictionary<uint, MissionObjectiveState> FinalObjectiveStates { get; }
            internal IReadOnlyList<uint> RevealedObjectiveIds { get; }
            internal IReadOnlyList<uint> ActivatedObjectiveIds { get; }
            internal IReadOnlyList<uint> StartScenarioIds { get; }
            internal IReadOnlyList<AmbientConversationRequest> AmbientConversationRequests { get; }
            internal IReadOnlyList<uint> ShownIndicatorIds { get; }
            internal IReadOnlyList<PlayerFlagChange> PlayerFlagChanges { get; }
            internal IReadOnlyList<uint> ActivateSpawnGroupIds { get; }

            internal TransitionActionApplication(
                IReadOnlyDictionary<uint, MissionObjectiveState> finalObjectiveStates,
                IEnumerable<uint> revealedObjectiveIds,
                IEnumerable<uint> activatedObjectiveIds,
                IEnumerable<uint> startScenarioIds,
                IEnumerable<AmbientConversationRequest> ambientConversationRequests = null,
                IEnumerable<uint> shownIndicatorIds = null,
                IEnumerable<PlayerFlagChange> playerFlagChanges = null,
                IEnumerable<uint> activateSpawnGroupIds = null)
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
                AmbientConversationRequests = Array.AsReadOnly(
                    (ambientConversationRequests ?? Array.Empty<AmbientConversationRequest>()).ToArray());
                ShownIndicatorIds = Array.AsReadOnly(
                    (shownIndicatorIds ?? Array.Empty<uint>()).ToArray());
                PlayerFlagChanges = Array.AsReadOnly(
                    (playerFlagChanges ?? Array.Empty<PlayerFlagChange>()).ToArray());
                ActivateSpawnGroupIds = Array.AsReadOnly(
                    (activateSpawnGroupIds ?? Array.Empty<uint>()).ToArray());
            }
        }

        /// <summary>A player_flag_id/player_flag_value pair applied by a SetPlayerFlag action.</summary>
        internal readonly struct PlayerFlagChange
        {
            internal uint FlagId { get; }
            internal uint Value { get; }

            internal PlayerFlagChange(uint flagId, uint value)
            {
                FlagId = flagId;
                Value = value;
            }
        }

        /// <summary>
        /// A one-way conversation to force open on the client as a side effect of a transition,
        /// via ForceConverse - which needs no NPC entity, unlike Completion-type conversations.
        /// GreetingId is a client npcgreetinglanguage text id.
        /// </summary>
        internal readonly struct AmbientConversationRequest
        {
            internal uint GreetingId { get; }

            internal AmbientConversationRequest(uint greetingId)
            {
                GreetingId = greetingId;
            }
        }


    }
}
