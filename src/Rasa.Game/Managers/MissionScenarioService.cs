using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.MapChannel.Server;
    using Packets.Manifestation.Server;
    using Repositories.Char;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;
    using Structures.Missions;
    using Structures.World;

    public interface IMissionScenarioService
    {
        bool TryExecute(Client client, uint missionId, uint scenarioId);
        bool TryExecuteFailureTransition(Client client, uint missionId, uint scenarioId);
        bool TryActivateSpawnGroup(Client client, uint missionId, uint spawnGroupId);
        void OnMissionAccepted(Client client, uint missionId);
        bool Tick(Client client);
        void Rebuild(uint characterId, MapChannel mapChannel);
        void Release(uint characterId, MapChannel mapChannel);
    }

    internal sealed class MissionScenarioService : IMissionScenarioService
    {
        private readonly Func<IGameUnitOfWorkFactory> _gameUnitOfWorkFactory;
        private readonly Func<MissionManager> _missionManager;
        private readonly ManifestationManager _manifestationManager;
        private readonly Func<MapChannelManager> _maps;
        private readonly Func<CreatureManager> _creatures;
        private readonly Func<DynamicObjectManager> _objects;
        private readonly Func<CommunicatorManager> _communicator;
        private readonly Func<DateTime> _utcNow;
        private readonly object _runtimeSyncRoot = new();
        private readonly Dictionary<MapChannel, RuntimeRegistry> _runtimeByMap = new();

        internal MissionScenarioService(
            Func<IGameUnitOfWorkFactory> gameUnitOfWorkFactory,
            Func<MissionManager> missionManager,
            ManifestationManager manifestationManager,
            Func<MapChannelManager> maps = null,
            Func<CreatureManager> creatures = null,
            Func<DynamicObjectManager> objects = null,
            Func<CommunicatorManager> communicator = null,
            Func<DateTime> utcNow = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory ?? (() => Server.GameUnitOfWorkFactory);
            _missionManager = missionManager ?? (() => MissionManager.Instance);
            _manifestationManager = manifestationManager ?? ManifestationManager.Instance;
            _maps = maps ?? (() => MapChannelManager.Instance);
            _creatures = creatures ?? (() => CreatureManager.Instance);
            _objects = objects ?? (() => DynamicObjectManager.Instance);
            _communicator = communicator ?? (() => CommunicatorManager.Instance);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public bool TryExecute(Client client, uint missionId, uint scenarioId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                return TryExecuteCore(client, missionId, scenarioId, null);
            }
        }

        public bool TryExecuteFailureTransition(Client client, uint missionId, uint scenarioId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                return TryExecuteCore(
                    client,
                    missionId,
                    scenarioId,
                    pendingSchedule: null,
                    allowFailedMission: true);
            }
        }

        /// <summary>
        /// Activates a spawn group directly from a transition's ActivateSpawnGroup action, bypassing
        /// the scenario-step machinery entirely (no durable completed-step tracking, no attempt key).
        /// Unlike a scenario's own SpawnGroup step, this spawn is not restored by Rebuild on relog -
        /// content that needs that durability should route the spawn through a scenario instead.
        /// </summary>
        public bool TryActivateSpawnGroup(Client client, uint missionId, uint spawnGroupId)
        {
            if (client?.Player == null)
                return false;

            lock (client.SyncRoot)
            {
                var manager = _missionManager();
                if (manager == null ||
                    client.State != ClientState.Ingame ||
                    client.PendingTransfer != null ||
                    !manager.TryGetSpawnGroupDefinition(missionId, spawnGroupId, out var spawnGroup))
                    return false;

                var mapChannel = ResolveMap(
                    client.Player,
                    spawnGroup.MapContextId,
                    client.Player.MapChannel);
                if (mapChannel == null)
                    return false;

                EnsureSpawnGroupRuntime(
                    mapChannel,
                    spawnGroup,
                    BuildSpawnGroupRuntimeKey(
                        client.Player.Id,
                        missionId,
                        spawnGroup.ContentRevision,
                        "transition",
                        spawnGroupId));
                return true;
            }
        }

        public bool Tick(Client client)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                var manager = _missionManager();
                if (manager == null ||
                    client.Player == null ||
                    client.State != ClientState.Ingame ||
                    client.PendingTransfer != null)
                    return false;

                if (client.Player.Missions.ContainsKey(BootcampEquipmentCrateLoot.MissionId))
                    EnsureBootcampAlisterDestination(client.Player.Id, client.Player.MapChannel);

                foreach (var mission in client.Player.Missions.Values
                             .Where(mission => mission.State == MissionState.Active)
                             .OrderBy(mission => mission.MissionId))
                {
                    using var unitOfWork = _gameUnitOfWorkFactory().CreateChar();
                    var rows = unitOfWork.CharacterMissionScenario.Get(client.Player.Id, mission.MissionId);
                    var scheduled = ParseState(rows).ScheduledSteps
                        .Where(state => state.DueAtUtc.HasValue && state.DueAtUtc.Value <= _utcNow())
                        .OrderBy(state => state.DueAtUtc)
                        .ThenBy(state => state.TargetScenarioId)
                        .ToArray();
                    foreach (var pending in scheduled)
                        if (TryExecuteCore(
                                client,
                                mission.MissionId,
                                pending.TargetScenarioId.GetValueOrDefault(),
                                pending))
                            return true;
                }

                return false;
            }
        }

        public void Rebuild(uint characterId, MapChannel mapChannel)
        {
            if (characterId == 0 || mapChannel == null)
                return;

            var manager = _missionManager();
            if (manager == null)
                return;

            EnsureBootcampEquipmentCrate(characterId, mapChannel, manager);
            using var unitOfWork = _gameUnitOfWorkFactory().CreateChar();
            var missions = unitOfWork.CharacterMissions.Get(characterId)
                .Where(mission =>
                    mission.MissionState == (uint)MissionState.Active ||
                    mission.MissionState == (uint)MissionState.Failed ||
                    ((mission.MissionState == (uint)MissionState.Success ||
                      mission.MissionState == (uint)MissionState.Completed) &&
                     mission.MissionId == BootcampEquipmentCrateLoot.MissionId &&
                     mapChannel.MapInfo?.MapContextId == 1985))
                .OrderBy(mission => mission.MissionId)
                .ToArray();
            if (missions.Any(mission => mission.MissionId == BootcampEquipmentCrateLoot.MissionId))
                EnsureBootcampAlisterDestination(characterId, mapChannel);

            foreach (var durableMission in missions)
            {
                if (!manager.TryGetScenarioDefinitions(
                        durableMission.MissionId,
                        out var scenarios))
                    continue;

                var rows = unitOfWork.CharacterMissionScenario.Get(characterId, durableMission.MissionId);
                var state = ParseState(rows);
                foreach (var scenario in scenarios.Values.OrderBy(scenario => scenario.ScenarioId))
                    foreach (var step in scenario.Steps)
                    {
                        if ((durableMission.MissionState == (uint)MissionState.Success ||
                             durableMission.MissionState == (uint)MissionState.Completed) &&
                            !IsRewardCrateSpawn(durableMission.MissionId, step))
                            continue;

                        var key = MissionScenarioStepState.CreateCompletedKey(
                            scenario.ScenarioId,
                            step.StepId,
                            step.AttemptKey);
                        if (!state.CompletedSteps.ContainsKey(key))
                            continue;

                        ApplyRebuildStep(
                            characterId,
                            durableMission.MissionId,
                            step,
                            mapChannel,
                            manager,
                            state);
                    }
            }
        }

        public void OnMissionAccepted(Client client, uint missionId)
        {
            if (missionId == BootcampEquipmentCrateLoot.MissionId &&
                client.Player.MapChannel?.MapInfo?.MapContextId == 1985)
                Rebuild(client.Player.Id, client.Player.MapChannel);
        }

        public void Release(uint characterId, MapChannel mapChannel)
        {
            if (mapChannel == null)
                return;

            lock (_runtimeSyncRoot)
                _runtimeByMap.Remove(mapChannel);
        }

        private bool TryExecuteCore(
            Client client,
            uint missionId,
            uint scenarioId,
            MissionScenarioStepState? pendingSchedule,
            bool allowFailedMission = false)
        {
            var manager = _missionManager();
            if (manager == null ||
                client.Player == null ||
                client.State != ClientState.Ingame ||
                client.PendingTransfer != null ||
                !manager.TryGetOperationalMission(missionId, out var missionDefinition) ||
                !manager.TryGetScenarioDefinition(missionId, scenarioId, out var scenario) ||
                !client.Player.Missions.TryGetValue(missionId, out var runtimeMission) ||
                !IsMissionStateEligible(runtimeMission.State, allowFailedMission))
                return false;

            using var plan = new MissionScenarioPlan();
            var committed = false;
            try
            {
                using var unitOfWork = _gameUnitOfWorkFactory().CreateChar();
                unitOfWork.ExecuteTransaction(() =>
                {
                    var durableMission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                        client.Player.Id,
                        missionId);
                    var durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
                        client.Player.Id,
                        missionId);
                    if (!IsMissionStateEligible(
                            durableMission?.MissionState is uint durableState &&
                            Enum.IsDefined(typeof(MissionState), (int)durableState)
                                ? (MissionState?)durableState
                                : null,
                            allowFailedMission))
                        throw new GameplayRejectionException("Durable mission is not active.");

                    var state = ParseState(
                        unitOfWork.CharacterMissionScenario.Get(client.Player.Id, missionId));
                    if (pendingSchedule.HasValue &&
                        !state.ScheduledSteps.Any(current => current.StepKey == pendingSchedule.Value.StepKey))
                        return;

                    var mapChannel = client.Player.MapChannel;
                    var rewardPackages = manager.GetRewardPackages(missionId);
                    var context = new MissionActionContext(
                        client,
                        manager,
                        _manifestationManager,
                        unitOfWork,
                        missionDefinition,
                        durableMission,
                        durableObjectives,
                        scenario,
                        rewardPackages,
                        state.CompletedSteps,
                        state.ScheduledSteps,
                        _utcNow(),
                        mapChannel,
                        plan);

                    foreach (var step in scenario.Steps)
                    {
                        var key = MissionScenarioStepState.CreateCompletedKey(
                            scenario.ScenarioId,
                            step.StepId,
                            step.AttemptKey);
                        if (state.CompletedSteps.ContainsKey(key))
                            continue;

                        PlanStep(context, step);
                        if (!plan.StepKeysToAdd.Contains(key))
                            plan.StepKeysToAdd.Add(key);
                    }

                    if (pendingSchedule.HasValue)
                        plan.ExactStepKeysToRemove.Add(pendingSchedule.Value.StepKey);

                    ApplyDurableState(client.Player.Id, missionId, unitOfWork, plan);
                    committed = plan.HasChanges;
                });
            }
            catch (Exception error) when (GameplayRejectionException.IsExpected(error))
            {
                Logger.WriteLog(
                    LogType.Error,
                    $"Unable to execute mission scenario {missionId}:{scenarioId} for character {client.Player.Id}: {error}");
                return false;
            }

            if (!committed)
                return false;

            plan.ApplyRuntime(client, _manifestationManager, manager);
            return true;
        }

        private static bool IsMissionStateEligible(
            MissionState? missionState,
            bool allowFailedMission) =>
            missionState == MissionState.Active ||
            (allowFailedMission && missionState == MissionState.Failed);

        private static void ApplyDurableState(
            uint characterId,
            uint missionId,
            ICharUnitOfWork unitOfWork,
            MissionScenarioPlan plan)
        {
            foreach (var stepKey in plan.StepKeyPrefixesToRemove.Distinct(StringComparer.Ordinal))
                unitOfWork.CharacterMissionScenario.RemoveByPrefix(
                    characterId,
                    missionId,
                    stepKey);
            foreach (var durablePrefix in plan.DurableKeyPrefixesToRemove.Distinct(StringComparer.Ordinal))
                unitOfWork.CharacterMissionScenario.RemoveByPrefix(
                    characterId,
                    missionId,
                    durablePrefix);
            foreach (var stepKey in plan.ExactStepKeysToRemove.Distinct(StringComparer.Ordinal))
                unitOfWork.CharacterMissionScenario.Remove(
                    characterId,
                    missionId,
                    stepKey);
            foreach (var stepKey in plan.StepKeysToAdd.Distinct(StringComparer.Ordinal))
                unitOfWork.CharacterMissionScenario.Add(
                    new CharacterMissionScenarioStepEntry(
                        characterId,
                        missionId,
                        stepKey));
        }

        private void PlanStep(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            switch (step.Kind)
            {
                case MissionScenarioStepKind.SpawnGroup:
                    PlanSpawnGroup(context, step);
                    return;

                case MissionScenarioStepKind.DespawnGroup:
                    PlanDespawnGroup(context, step);
                    return;

                case MissionScenarioStepKind.SpawnDynamicObject:
                    PlanSpawnDynamicObject(context, step);
                    return;

                case MissionScenarioStepKind.DespawnDynamicObject:
                    PlanDespawnDynamicObject(context, step);
                    return;

                case MissionScenarioStepKind.EscortSpawnGroup:
                    PlanEscortSpawnGroup(context, step);
                    return;

                case MissionScenarioStepKind.EnableInteraction:
                    PlanInteraction(context, step, true);
                    return;

                case MissionScenarioStepKind.DisableInteraction:
                    PlanInteraction(context, step, false);
                    return;

                case MissionScenarioStepKind.RevealObjective:
                case MissionScenarioStepKind.ActivateObjective:
                case MissionScenarioStepKind.CompleteObjective:
                case MissionScenarioStepKind.FailObjective:
                    if (!step.TargetObjectiveId.HasValue ||
                        !context.MissionManager.TryPlanScenarioObjectiveAction(
                            context.Client,
                            context.MissionDefinition.MissionId,
                            step.TargetObjectiveId.Value,
                            step.Kind,
                            context.UnitOfWork,
                            context.Plan))
                        throw new GameplayRejectionException(
                            $"Unable to apply scenario objective action {step.Kind}.");
                    return;

                case MissionScenarioStepKind.StartDeadline:
                    PlanDeadline(context, step, CharacterMissionDeadlineState.Active);
                    return;

                case MissionScenarioStepKind.CancelDeadline:
                    PlanCancelDeadline(context);
                    return;

                case MissionScenarioStepKind.SatisfyDeadline:
                    PlanSatisfyDeadline(context);
                    return;

                case MissionScenarioStepKind.GrantRewardPackage:
                    PlanRewardPackage(context, step);
                    return;

                case MissionScenarioStepKind.GrantSkillAbility:
                    PlanSkillAbility(context, step);
                    return;

                case MissionScenarioStepKind.PlayTutorial:
                    PlanTutorial(context, step);
                    return;

                case MissionScenarioStepKind.ScheduleScenario:
                    PlanScheduleScenario(context, step);
                    return;

                case MissionScenarioStepKind.ResetAttempt:
                    PlanResetAttempt(context, step);
                    return;

                case MissionScenarioStepKind.EmitScenarioEvent:
                    PlanScenarioEvent(context, step);
                    return;

                case MissionScenarioStepKind.TransferPlayer:
                    PlanTransfer(context, step);
                    return;

                case MissionScenarioStepKind.SetQualification:
                    PlanQualification(context, step);
                    return;

                case MissionScenarioStepKind.SetAccountSkipEntitlement:
                    PlanAccountSkipEntitlement(context, step);
                    return;

                default:
                    throw new GameplayRejectionException(
                        $"Unsupported scenario step kind {step.Kind}.");
            }
        }

        private void PlanSpawnGroup(MissionActionContext context, MissionScenarioStepDefinition step)
        {
            if (!step.SpawnGroupId.HasValue ||
                !context.MissionManager.TryGetSpawnGroupDefinition(
                    context.MissionDefinition.MissionId,
                    step.SpawnGroupId.Value,
                    out var spawnGroup))
                throw new GameplayRejectionException("Scenario spawn group is missing.");

            var mapChannel = ResolveMap(
                context.Client.Player,
                spawnGroup.MapContextId,
                context.MapChannel,
                context.UnitOfWork);
            if (mapChannel == null)
                throw new GameplayRejectionException("Scenario spawn group map is unavailable.");

            context.Plan.AddRuntimeConvergence(() =>
                EnsureSpawnGroupRuntime(
                    mapChannel,
                    spawnGroup,
                    BuildSpawnGroupRuntimeKey(
                        context.Client.Player.Id,
                        context.MissionDefinition.MissionId,
                        spawnGroup.ContentRevision,
                        step.AttemptKey,
                        spawnGroup.SpawnGroupId),
                    step.AttemptKey));
        }

        private void PlanDespawnGroup(MissionActionContext context, MissionScenarioStepDefinition step)
        {
            if (!step.SpawnGroupId.HasValue ||
                !context.MissionManager.TryGetSpawnGroupDefinition(
                    context.MissionDefinition.MissionId,
                    step.SpawnGroupId.Value,
                    out var spawnGroup))
                throw new GameplayRejectionException("Scenario spawn group is missing.");

            var mapChannel = ResolveMap(
                context.Client.Player,
                spawnGroup.MapContextId,
                context.MapChannel,
                context.UnitOfWork);
            if (mapChannel == null)
                throw new GameplayRejectionException("Scenario spawn group map is unavailable.");

            context.Plan.AddRuntimeConvergence(() =>
                RemoveSpawnGroupRuntime(
                    mapChannel,
                    BuildSpawnGroupRuntimeKey(
                        context.Client.Player.Id,
                        context.MissionDefinition.MissionId,
                        spawnGroup.ContentRevision,
                        step.AttemptKey,
                        spawnGroup.SpawnGroupId)));
            context.Plan.DurableKeyPrefixesToRemove.Add(
                MissionScenarioStepState.CreateSpawnDeathGroupPrefix(
                    spawnGroup.SpawnGroupId,
                    step.AttemptKey));
        }

        private void PlanSpawnDynamicObject(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (string.IsNullOrWhiteSpace(step.DynamicObjectKey) ||
                !step.EntityClassId.HasValue ||
                !step.PosX.HasValue ||
                !step.PosY.HasValue ||
                !step.PosZ.HasValue ||
                !step.Orientation.HasValue)
                throw new GameplayRejectionException("Scenario dynamic object step is incomplete.");

            var position = new Vector3(
                (float)step.PosX.Value,
                (float)step.PosY.Value,
                (float)step.PosZ.Value);
            var enabled = step.InitialInteractionEnabled ?? true;
            context.Plan.AddRuntimeConvergence(() =>
            {
                EnsureScenarioDynamicObject(
                    context.MapChannel,
                    BuildDynamicObjectRuntimeKey(
                        context.Client.Player.Id,
                        context.MissionDefinition.MissionId,
                        step.ContentRevision,
                        step.AttemptKey,
                        step.DynamicObjectKey),
                    (EntityClasses)step.EntityClassId.Value,
                    position,
                    step.Orientation.Value,
                    enabled,
                    step.DelayMilliseconds,
                    IsRewardCrateSpawn(context.MissionDefinition.MissionId, step)
                        ? BootcampEquipmentCrateLoot
                        : null);
            });
        }

        private const uint BootcampEquipmentCrateEntityClassId = 29877;
        private static readonly MissionLootSource BootcampEquipmentCrateLoot = new(1992, 1, 58);
        private const uint BootcampAlisterCreatureId = 510203;
        private static readonly Vector3 BootcampAlisterDestination = new(400, 120, 150);
        private const double BootcampAlisterOrientation = 2.175;

        private static bool IsRewardCrateSpawn(uint missionId, MissionScenarioStepDefinition step) =>
            missionId == BootcampEquipmentCrateLoot.MissionId &&
            step.Kind == MissionScenarioStepKind.SpawnDynamicObject &&
            step.EntityClassId == BootcampEquipmentCrateEntityClassId;

        private void EnsureBootcampEquipmentCrate(
            uint characterId, MapChannel mapChannel, MissionManager manager)
        {
            if (mapChannel.MapInfo?.MapContextId != 1985 ||
                !mapChannel.IsPrivateInstance || mapChannel.OwnerCharacterId != characterId ||
                !manager.TryGetScenarioDefinitions(BootcampEquipmentCrateLoot.MissionId, out var scenarios))
                return;

            var step = scenarios.Values.SelectMany(scenario => scenario.Steps)
                .SingleOrDefault(step => IsRewardCrateSpawn(BootcampEquipmentCrateLoot.MissionId, step));
            if (step == null)
                return;

            EnsureScenarioDynamicObject(
                mapChannel,
                BuildDynamicObjectRuntimeKey(characterId, BootcampEquipmentCrateLoot.MissionId,
                    step.ContentRevision, step.AttemptKey, step.DynamicObjectKey),
                (EntityClasses)step.EntityClassId.Value,
                new Vector3((float)step.PosX.Value, (float)step.PosY.Value, (float)step.PosZ.Value),
                step.Orientation.Value,
                false,
                step.DelayMilliseconds,
                BootcampEquipmentCrateLoot);
        }

        private static void EnsureBootcampAlisterDestination(uint characterId, MapChannel mapChannel)
        {
            if (mapChannel?.MapInfo?.MapContextId != 1985 ||
                !mapChannel.IsPrivateInstance || mapChannel.OwnerCharacterId != characterId)
                return;

            foreach (var pool in mapChannel.SpawnPools.Where(pool => pool.DbId == BootcampAlisterCreatureId))
            {
                pool.Position = BootcampAlisterDestination;
                pool.Rotation = (float)BootcampAlisterOrientation;
            }

            foreach (var alister in mapChannel.MapCellInfo.Cells.Values
                         .SelectMany(cell => cell.CreatureList).Distinct()
                         .Where(creature => creature.DbId == BootcampAlisterCreatureId &&
                                            creature.SpawnPool?.DbId == BootcampAlisterCreatureId))
                BehaviorManager.Instance.SetActionScriptedMove(
                    mapChannel, alister, BootcampAlisterDestination, BootcampAlisterOrientation);
        }

        private void PlanDespawnDynamicObject(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (string.IsNullOrWhiteSpace(step.DynamicObjectKey))
                throw new GameplayRejectionException("Scenario dynamic object key is missing.");

            context.Plan.AddRuntimeConvergence(() =>
                RemoveScenarioDynamicObject(
                    context.MapChannel,
                    BuildDynamicObjectRuntimeKey(
                        context.Client.Player.Id,
                        context.MissionDefinition.MissionId,
                        step.ContentRevision,
                        step.AttemptKey,
                        step.DynamicObjectKey)));
        }

        private void PlanEscortSpawnGroup(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (!step.SpawnGroupId.HasValue ||
                !context.MissionManager.TryGetSpawnGroupDefinition(
                    context.MissionDefinition.MissionId,
                    step.SpawnGroupId.Value,
                    out var spawnGroup))
                throw new GameplayRejectionException("Scenario escort spawn group is missing.");
            if (spawnGroup.SpawnPolicy != MissionSpawnGroupPolicy.ScenarioControlled)
                throw new GameplayRejectionException("Scenario escort spawn group must be scenario controlled.");

            var mapChannel = ResolveMap(
                context.Client.Player,
                spawnGroup.MapContextId,
                context.MapChannel,
                context.UnitOfWork);
            if (mapChannel == null)
                throw new GameplayRejectionException("Scenario escort spawn group map is unavailable.");

            context.Plan.AddRuntimeConvergence(() =>
                EnsureSpawnGroupEscortRuntime(
                    mapChannel,
                    BuildSpawnGroupRuntimeKey(
                        context.Client.Player.Id,
                        context.MissionDefinition.MissionId,
                        spawnGroup.ContentRevision,
                        step.AttemptKey,
                        spawnGroup.SpawnGroupId),
                    context.Client.Player.Id,
                    context.Client.Player.EntityId));
        }

        private void PlanInteraction(
            MissionActionContext context,
            MissionScenarioStepDefinition step,
            bool enabled)
        {
            context.Plan.AddRuntimeConvergence(() =>
            {
                var map = context.MapChannel;
                if (map == null)
                    return;

                foreach (var dynamicObject in FindInteractionObjects(map, step))
                    _objects().SetScenarioInteractionEnabled(map, dynamicObject, enabled);
                foreach (var creature in FindInteractionCreatures(map, step))
                    _creatures().SetScenarioInteractionEnabled(map, creature, enabled);
            });
        }

        private static void PlanDeadline(
            MissionActionContext context,
            MissionScenarioStepDefinition step,
            CharacterMissionDeadlineState state)
        {
            if (!step.DelayMilliseconds.HasValue)
                throw new GameplayRejectionException("Scenario deadline delay is missing.");

            var dueAt = context.UtcNow.AddMilliseconds(step.DelayMilliseconds.Value);
            context.UnitOfWork.CharacterMissionDeadlines.AddOrUpdate(
                context.Client.Player.Id,
                context.MissionDefinition.MissionId,
                dueAt,
                state);
            context.Plan.AddPublication(() =>
                context.MissionManager.PublishMissionStatus(
                    context.Client,
                    context.MissionDefinition.MissionId,
                    $"mission {context.MissionDefinition.MissionId} status after deadline start"));
        }

        private static void PlanCancelDeadline(MissionActionContext context)
        {
            var existing = context.UnitOfWork.CharacterMissionDeadlines.Get(
                context.Client.Player.Id,
                context.MissionDefinition.MissionId);
            if (existing?.State == CharacterMissionDeadlineState.Active)
            {
                context.UnitOfWork.CharacterMissionDeadlines.SetState(
                    context.Client.Player.Id,
                    context.MissionDefinition.MissionId,
                    CharacterMissionDeadlineState.Cancelled);
                context.Plan.AddPublication(() =>
                    context.MissionManager.PublishMissionStatus(
                        context.Client,
                        context.MissionDefinition.MissionId,
                        $"mission {context.MissionDefinition.MissionId} status after deadline cancel"));
            }
        }

        private static void PlanSatisfyDeadline(MissionActionContext context)
        {
            var existing = context.UnitOfWork.CharacterMissionDeadlines.Get(
                context.Client.Player.Id,
                context.MissionDefinition.MissionId);
            if (existing?.State == CharacterMissionDeadlineState.Active)
            {
                context.UnitOfWork.CharacterMissionDeadlines.SetState(
                    context.Client.Player.Id,
                    context.MissionDefinition.MissionId,
                    CharacterMissionDeadlineState.Satisfied);
                context.Plan.AddPublication(() =>
                    context.MissionManager.PublishMissionStatus(
                        context.Client,
                        context.MissionDefinition.MissionId,
                        $"mission {context.MissionDefinition.MissionId} status after deadline satisfaction"));
            }
        }

        private void PlanRewardPackage(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (!step.RewardId.HasValue ||
                !context.RewardPackages.TryGetValue(step.RewardId.Value, out var rewardDefinition))
                throw new GameplayRejectionException("Scenario reward package is missing.");

            if (!_manifestationManager.ValidateProgressionForClient(context.Client))
                throw new GameplayRejectionException("Client progression cannot accept a scenario reward.");

            var character = context.UnitOfWork.Characters.Get(context.Client.Player.Id);
            if (character?.AccountId != context.Client.AccountEntry?.Id)
                throw new GameplayRejectionException("Durable character owner changed.");

            var grant = rewardDefinition.CreateScenarioGrant(
                context.MissionManager.BeforeRewardItemPublication);
            grant.PlanAndSave(
                context.Client,
                character,
                context.UnitOfWork,
                _manifestationManager);
            context.Plan.AddRewardGrant(grant);
            context.Plan.AddProgressPlan(
                context.MissionManager.PlanProgress(
                    context.Client,
                    grant.CreateItemAcquisitionEvents(),
                    context.UnitOfWork));
        }

        private static void PlanSkillAbility(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (!step.SkillId.HasValue ||
                !step.AbilityId.HasValue ||
                !step.SkillLevel.HasValue)
                throw new GameplayRejectionException("Scenario skill grant is incomplete.");

            context.UnitOfWork.CharacterSkills.AddOrUpdate(
                context.Client.Player.Id,
                step.SkillId.Value,
                (int)step.AbilityId.Value,
                step.SkillLevel.Value);
            if (step.AbilitySlot.HasValue)
                context.UnitOfWork.CharacterAbilityDrawers.AddOrUpdate(
                    context.Client.Player.Id,
                    step.AbilitySlot.Value,
                    (int)step.AbilityId.Value,
                    step.SkillLevel.Value);

            context.Plan.AddRuntimeConvergence(() =>
            {
                context.Client.Player.Skills[(SkillId)step.SkillId.Value] =
                    new SkillsData(
                        (SkillId)step.SkillId.Value,
                        (int)step.AbilityId.Value,
                        step.SkillLevel.Value);
                if (step.AbilitySlot.HasValue)
                    context.Client.Player.Abilities[step.AbilitySlot.Value] =
                        new AbilityDrawerData(
                            step.AbilitySlot.Value,
                            (int)step.AbilityId.Value,
                            step.SkillLevel.Value);
            });
            context.Plan.AddPublication(() =>
            {
                MissionManager.TryPublish(
                    () => context.Client.CallMethod(
                        context.Client.Player.EntityId,
                        new SkillsPacket(context.Client.Player.Skills)),
                    $"scenario skill grant {step.SkillId.Value}");
                if (step.AbilitySlot.HasValue)
                    MissionManager.TryPublish(
                        () => context.Client.CallMethod(
                            context.Client.Player.EntityId,
                            new AbilityDrawerPacket(context.Client.Player.Abilities)),
                        $"scenario ability slot {step.AbilitySlot.Value}");
            });
        }

        private void PlanTutorial(MissionActionContext context, MissionScenarioStepDefinition step)
        {
            if (!step.TryGetTutorialId(out var tutorialId))
                throw new GameplayRejectionException("Scenario tutorial id is invalid.");

            context.Plan.AddPublication(() =>
            {
                MissionManager.TryPublish(
                    () => _communicator().DisplayPlayerTutorial(context.Client, tutorialId),
                    $"scenario tutorial {tutorialId}");
                MissionManager.TryPublish(
                    () => _communicator().PlayTutorialAudio(context.Client, step.AudioSetId),
                    $"scenario tutorial audio {step.AudioSetId?.ToString() ?? "none"}");
            });
        }

        private static void PlanScheduleScenario(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (!step.TargetScenarioId.HasValue || !step.DelayMilliseconds.HasValue)
                throw new GameplayRejectionException("Scenario schedule step is incomplete.");

            var dueAtUtc = context.UtcNow.AddMilliseconds(step.DelayMilliseconds.Value);
            context.Plan.StepKeysToAdd.Add(
                MissionScenarioStepState.CreateScheduledKey(
                    context.Scenario.ScenarioId,
                    step.StepId,
                    step.TargetScenarioId.Value,
                    dueAtUtc,
                    step.AttemptKey));
        }

        private void PlanResetAttempt(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            var completedToRemove = context.CompletedSteps.Values
                .Where(state => MatchesResetTarget(state, step))
                .ToArray();
            var scheduledToRemove = context.ScheduledSteps
                .Where(state => MatchesResetTarget(state, step))
                .ToArray();

            foreach (var state in completedToRemove.Concat(scheduledToRemove))
                context.Plan.ExactStepKeysToRemove.Add(state.StepKey);

            foreach (var state in completedToRemove)
                PlanRuntimeReset(context, state);
        }

        private static void PlanScenarioEvent(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (!step.ScenarioEventId.HasValue)
                throw new GameplayRejectionException("Scenario event id is missing.");

            context.Plan.AddPostCommit(() =>
                context.MissionManager.TryEmitScenarioProgressEvent(
                    context.Client,
                    context.MissionDefinition.MissionId,
                    context.Scenario.ScenarioId,
                    step.ScenarioEventId.Value));
        }

        private void PlanTransfer(MissionActionContext context, MissionScenarioStepDefinition step)
        {
            if (!step.MapContextId.HasValue ||
                !step.PosX.HasValue ||
                !step.PosY.HasValue ||
                !step.PosZ.HasValue ||
                !step.Orientation.HasValue)
                throw new GameplayRejectionException("Scenario transfer step is incomplete.");

            var destinationMap = ResolveMap(
                context.Client.Player,
                step.MapContextId.Value,
                null,
                context.UnitOfWork);
            if (destinationMap == null)
                throw new GameplayRejectionException("Scenario transfer destination is unavailable.");

            var position = new Vector3(
                (float)step.PosX.Value,
                (float)step.PosY.Value,
                (float)step.PosZ.Value);
            context.Plan.AddPublication(() =>
                MissionManager.TryPublish(
                    () => _maps().ChangeMap(
                        context.Client,
                        destinationMap,
                        position,
                        (float)step.Orientation.Value),
                    $"scenario transfer to {step.MapContextId.Value}"));
        }

        private static void PlanQualification(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (!step.TryGetQualificationKey(out var qualificationKey) ||
                !step.QualificationValue.HasValue)
                throw new GameplayRejectionException("Scenario qualification step is incomplete.");

            if (step.QualificationValue.Value == MissionScenarioStepEntry.RemovedQualificationValue)
            {
                context.UnitOfWork.CharacterQualifications.Remove(
                    context.Client.Player.Id,
                    qualificationKey);
                return;
            }

            if (!context.UnitOfWork.CharacterQualifications.HasQualification(
                    context.Client.Player.Id,
                    qualificationKey))
            {
                context.UnitOfWork.CharacterQualifications.Add(
                    new CharacterQualificationEntry(
                        context.Client.Player.Id,
                        qualificationKey));
            }
        }

        private static void PlanAccountSkipEntitlement(
            MissionActionContext context,
            MissionScenarioStepDefinition step)
        {
            if (!step.AccountSkipEntitlement.HasValue)
                throw new GameplayRejectionException("Scenario account entitlement step is incomplete.");

            context.UnitOfWork.GameAccounts.UpdateCanSkipBootcamp(
                context.Client.AccountEntry.Id,
                step.AccountSkipEntitlement.Value);
            context.Plan.AddRuntimeConvergence(() =>
            {
                context.Client.ReloadGameAccountEntry();
            });
        }

        private void ApplyRebuildStep(
            uint characterId,
            uint missionId,
            MissionScenarioStepDefinition step,
            MapChannel mapChannel,
            MissionManager manager,
            ScenarioState state)
        {
            switch (step.Kind)
            {
                case MissionScenarioStepKind.SpawnGroup:
                    if (step.SpawnGroupId.HasValue &&
                        manager.TryGetSpawnGroupDefinition(
                            missionId,
                            step.SpawnGroupId.Value,
                            out var spawnGroup) &&
                        spawnGroup.MapContextId == mapChannel.MapInfo.MapContextId)
                    {
                        EnsureSpawnGroupRuntime(
                            mapChannel,
                            spawnGroup,
                            BuildSpawnGroupRuntimeKey(
                                characterId,
                                missionId,
                                spawnGroup.ContentRevision,
                                step.AttemptKey,
                                spawnGroup.SpawnGroupId),
                            step.AttemptKey,
                            state);
                    }
                    return;

                case MissionScenarioStepKind.DespawnGroup:
                    if (step.SpawnGroupId.HasValue)
                        RemoveSpawnGroupRuntime(
                            mapChannel,
                            BuildSpawnGroupRuntimeKey(
                                characterId,
                                missionId,
                                step.ContentRevision,
                                step.AttemptKey,
                                step.SpawnGroupId.Value));
                    return;

                case MissionScenarioStepKind.SpawnDynamicObject:
                    if (string.IsNullOrWhiteSpace(step.DynamicObjectKey) ||
                        !step.EntityClassId.HasValue ||
                        !step.PosX.HasValue ||
                        !step.PosY.HasValue ||
                        !step.PosZ.HasValue ||
                        !step.Orientation.HasValue)
                        return;
                    {
                        EnsureScenarioDynamicObject(
                            mapChannel,
                            BuildDynamicObjectRuntimeKey(
                                characterId,
                                missionId,
                                step.ContentRevision,
                                step.AttemptKey,
                                step.DynamicObjectKey),
                            (EntityClasses)step.EntityClassId.Value,
                            new Vector3(
                                (float)step.PosX.Value,
                                (float)step.PosY.Value,
                                (float)step.PosZ.Value),
                            step.Orientation.Value,
                            step.InitialInteractionEnabled ?? true,
                            step.DelayMilliseconds,
                            IsRewardCrateSpawn(missionId, step) ? BootcampEquipmentCrateLoot : null);
                    }
                    return;

                case MissionScenarioStepKind.DespawnDynamicObject:
                    if (!string.IsNullOrWhiteSpace(step.DynamicObjectKey))
                        RemoveScenarioDynamicObject(
                            mapChannel,
                            BuildDynamicObjectRuntimeKey(
                                characterId,
                                missionId,
                                step.ContentRevision,
                                step.AttemptKey,
                                step.DynamicObjectKey));
                    return;

                case MissionScenarioStepKind.EscortSpawnGroup:
                    if (step.SpawnGroupId.HasValue &&
                        manager.TryGetSpawnGroupDefinition(
                            missionId,
                            step.SpawnGroupId.Value,
                            out var escortGroup) &&
                        escortGroup.MapContextId == mapChannel.MapInfo.MapContextId &&
                        escortGroup.SpawnPolicy == MissionSpawnGroupPolicy.ScenarioControlled)
                    {
                        EnsureSpawnGroupEscortRuntime(
                            mapChannel,
                            BuildSpawnGroupRuntimeKey(
                                characterId,
                                missionId,
                                escortGroup.ContentRevision,
                                step.AttemptKey,
                                escortGroup.SpawnGroupId),
                            characterId,
                            0);
                    }
                    return;

                case MissionScenarioStepKind.EnableInteraction:
                    foreach (var dynamicObject in FindInteractionObjects(mapChannel, step))
                        _objects().SetScenarioInteractionEnabled(mapChannel, dynamicObject, true);
                    foreach (var creature in FindInteractionCreatures(mapChannel, step))
                        _creatures().SetScenarioInteractionEnabled(mapChannel, creature, true);
                    return;

                case MissionScenarioStepKind.DisableInteraction:
                    foreach (var dynamicObject in FindInteractionObjects(mapChannel, step))
                        _objects().SetScenarioInteractionEnabled(mapChannel, dynamicObject, false);
                    foreach (var creature in FindInteractionCreatures(mapChannel, step))
                        _creatures().SetScenarioInteractionEnabled(mapChannel, creature, false);
                    return;
            }
        }

        private void EnsureSpawnGroupRuntime(
            MapChannel mapChannel,
            MissionSpawnGroupDefinition spawnGroup,
            string runtimeKey,
            string attemptKey = null,
            ScenarioState state = null)
        {
            if (mapChannel == null)
                return;

            var registry = GetRegistry(mapChannel);
            if (!registry.SpawnGroupsByKey.TryGetValue(runtimeKey, out var pools))
            {
                pools = mapChannel.SpawnPools
                    .Where(pool => string.Equals(pool.ScenarioKey, runtimeKey, StringComparison.Ordinal))
                    .ToList();
                if (pools.Count == 0)
                {
                    pools = new List<SpawnPool>();
                    foreach (var spawn in spawnGroup.Spawns)
                    {
                        var pool = new SpawnPool
                        {
                            DbId = spawn.SpawnId,
                            ScenarioKey = runtimeKey,
                            ScenarioMissionId = spawnGroup.MissionId,
                            ScenarioGroupId = spawnGroup.SpawnGroupId,
                            ScenarioAttemptKey = attemptKey,
                            ScenarioOwnerCharacterId = ExtractOwnerCharacterId(runtimeKey),
                            SpawnPolicy = spawnGroup.SpawnPolicy,
                            MapContextId = mapChannel.MapInfo.MapContextId,
                            RuntimeMapChannel = mapChannel,
                            Position = spawn.Position,
                            Rotation = spawn.Rotation,
                            RespawnTime = spawnGroup.RespawnSeconds.GetValueOrDefault() * 1000L,
                            UpdateTimer = spawnGroup.RespawnSeconds.GetValueOrDefault() * 1000L,
                            SpawnSlot = new List<SpawnPoolSlot>
                            {
                                new SpawnPoolSlot(
                                    spawn.CreatureId,
                                    checked((short)spawn.Quantity),
                                    checked((short)spawn.Quantity))
                            }
                        };
                        pools.Add(pool);
                    }
                }

                registry.SpawnGroupsByKey[runtimeKey] = pools;
            }

            foreach (var pool in pools)
            {
                if (!mapChannel.SpawnPools.Contains(pool))
                    mapChannel.SpawnPools.Add(pool);

                var desired = pool.SpawnSlot.Sum(slot => slot.CountMin);
                var deaths = spawnGroup.SpawnPolicy == MissionSpawnGroupPolicy.ScenarioControlled
                    ? state?.GetSpawnDeathCount(
                        spawnGroup.SpawnGroupId,
                        pool.DbId,
                        attemptKey) ?? 0U
                    : 0U;
                desired = Math.Max(0, desired - checked((int)Math.Min((uint)desired, deaths)));
                var existing = mapChannel.MapCellInfo.Cells.Values
                    .SelectMany(cell => cell.CreatureList)
                    .Count(creature => creature.SpawnPool == pool);
                var template = pool.SpawnSlot.FirstOrDefault();
                if (template == null)
                    continue;

                for (var index = existing; index < desired; index++)
                {
                    var creature = _creatures().CreateScenarioCreature(
                        pool,
                        template.CreatureId,
                        pool.Position,
                        pool.Rotation);
                    if (creature != null)
                        CellManager.Instance.AddToWorld(mapChannel, creature);
                }
            }
        }

        private void EnsureSpawnGroupEscortRuntime(
            MapChannel mapChannel,
            string runtimeKey,
            uint ownerCharacterId,
            ulong followTargetEntityId)
        {
            if (mapChannel == null ||
                string.IsNullOrWhiteSpace(runtimeKey) ||
                ownerCharacterId == 0)
                return;

            var registry = GetRegistry(mapChannel);
            if (!registry.SpawnGroupsByKey.TryGetValue(runtimeKey, out var pools))
                pools = mapChannel.SpawnPools
                    .Where(pool => string.Equals(pool.ScenarioKey, runtimeKey, StringComparison.Ordinal))
                    .ToList();
            if (pools.Count == 0)
                return;

            foreach (var pool in pools)
            {
                pool.FollowOwnerCharacterId = ownerCharacterId;
                pool.FollowTargetEntityId = followTargetEntityId;
            }

            foreach (var creature in mapChannel.MapCellInfo.Cells.Values
                         .SelectMany(cell => cell.CreatureList)
                         .Distinct()
                         .Where(creature => pools.Contains(creature.SpawnPool))
                         .ToArray())
                BehaviorManager.Instance.SetActionFollow(creature, followTargetEntityId);
        }

        private void RemoveSpawnGroupRuntime(MapChannel mapChannel, string runtimeKey)
        {
            if (mapChannel == null || string.IsNullOrWhiteSpace(runtimeKey))
                return;

            var registry = GetRegistry(mapChannel);
            if (!registry.SpawnGroupsByKey.TryGetValue(runtimeKey, out var pools))
                pools = mapChannel.SpawnPools
                    .Where(pool => string.Equals(pool.ScenarioKey, runtimeKey, StringComparison.Ordinal))
                    .ToList();

            foreach (var creature in mapChannel.MapCellInfo.Cells.Values
                         .SelectMany(cell => cell.CreatureList)
                         .Distinct()
                         .Where(creature => pools.Contains(creature.SpawnPool))
                         .ToArray())
                CellManager.Instance.RemoveCreatureFromWorld(mapChannel, creature);

            foreach (var pool in pools)
                mapChannel.SpawnPools.Remove(pool);
            registry.SpawnGroupsByKey.Remove(runtimeKey);
        }

        private DynamicObject EnsureScenarioDynamicObject(
            MapChannel mapChannel,
            string runtimeKey,
            EntityClasses entityClassId,
            Vector3 position,
            double rotation,
            bool enabled,
            uint? windupTime,
            MissionLootSource lootSource)
        {
            if (mapChannel == null || string.IsNullOrWhiteSpace(runtimeKey))
                return null;

            var registry = GetRegistry(mapChannel);
            if (!registry.DynamicObjectsByKey.TryGetValue(runtimeKey, out var existing))
            {
                existing = mapChannel.DynamicObjects.SingleOrDefault(candidate =>
                    string.Equals(candidate.ScenarioKey, runtimeKey, StringComparison.Ordinal));
                if (existing != null)
                    registry.DynamicObjectsByKey[runtimeKey] = existing;
            }

            if (existing != null &&
                MapInstanceScope.Contains(mapChannel, existing))
            {
                if (existing.LootDispenserEntityId == 0)
                    _objects().SetScenarioInteractionEnabled(mapChannel, existing, enabled);
                return existing;
            }

            var dynamicObject = _objects().CreateScenarioDynamicObject(
                mapChannel,
                entityClassId,
                position,
                rotation,
                runtimeKey,
                enabled,
                windupTime);
            dynamicObject.MissionLootSource = lootSource;
            if (lootSource != null)
                dynamicObject.StateId = UseObjectState.TdStateClosed;
            mapChannel.DynamicObjects.Add(dynamicObject);
            CellManager.Instance.AddToWorld(mapChannel, dynamicObject);

            // DynamicObjectManager.DynamicObjectWorker sweeps every entry in
            // mapChannel.DynamicObjects once a tick and re-adds any with IsInWorld still false
            // once its (default zero) RespawnTime counts down past zero - which, for an object
            // never given that flag, is true on the very next tick, and every tick after. That
            // second AddToWorld call re-registers the same entity id and throws (caught and
            // logged per-tick in Server.MainLoop, so the crash itself was silent and endless: a
            // scenario prop that survived more than about a second past creation - which
            // nothing forced before a real Lootable window existed to wait on - jammed the whole
            // map channel's tick from that point on, every second, indefinitely). Persistent
            // objects (logos shrines, dropship pads) set this themselves once actually spawned;
            // a scenario object never went through that path at all.
            dynamicObject.IsInWorld = true;

            registry.DynamicObjectsByKey[runtimeKey] = dynamicObject;
            return dynamicObject;
        }

        private void RemoveScenarioDynamicObject(MapChannel mapChannel, string runtimeKey)
        {
            if (mapChannel == null || string.IsNullOrWhiteSpace(runtimeKey))
                return;

            var registry = GetRegistry(mapChannel);
            if (!registry.DynamicObjectsByKey.TryGetValue(runtimeKey, out var dynamicObject))
                dynamicObject = mapChannel.DynamicObjects.SingleOrDefault(candidate =>
                    string.Equals(candidate.ScenarioKey, runtimeKey, StringComparison.Ordinal));
            if (dynamicObject == null)
                return;

            CellManager.Instance.RemoveFromWorld(mapChannel, dynamicObject);
            mapChannel.DynamicObjects.Remove(dynamicObject);
            registry.DynamicObjectsByKey.Remove(runtimeKey);
        }

        private IEnumerable<DynamicObject> FindInteractionObjects(
            MapChannel mapChannel,
            MissionScenarioStepDefinition step)
        {
            if (mapChannel == null)
                return Array.Empty<DynamicObject>();

            if (step.EntityClassId.HasValue)
                return mapChannel.DynamicObjects
                    .Concat(mapChannel.ControlPoints.Values)
                    .Concat(mapChannel.FootLockers.Values)
                    .Concat(mapChannel.Teleporters.Values)
                    .Concat(mapChannel.Kraftwerks.Values)
                    .Where(dynamicObject => (uint)dynamicObject.EntityClassId == step.EntityClassId.Value)
                    .Distinct()
                    .ToArray();

            return Array.Empty<DynamicObject>();
        }

        private IEnumerable<Creature> FindInteractionCreatures(
            MapChannel mapChannel,
            MissionScenarioStepDefinition step)
        {
            if (mapChannel == null || !step.SpawnGroupId.HasValue || !step.SpawnId.HasValue)
                return Array.Empty<Creature>();

            return mapChannel.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList)
                .Distinct()
                .Where(creature =>
                    creature.SpawnPool?.ScenarioGroupId == step.SpawnGroupId.Value &&
                    creature.SpawnPool.DbId == step.SpawnId.Value)
                .ToArray();
        }

        private MapChannel ResolveMap(
            Manifestation player,
            uint contextId,
            MapChannel currentMap,
            ICharUnitOfWork unitOfWork = null)
        {
            if (player?.Id > 0)
            {
                var owned = _maps().FindOwnedPrivateInstance(contextId, player.Id);
                if (owned != null)
                    return owned;
                if (ShouldUseOwnedPrivateInstance(player, contextId, currentMap, unitOfWork))
                    return _maps().GetOrCreatePrivateInstance(contextId, player.Id);
            }

            if (currentMap?.MapInfo?.MapContextId == contextId)
                return currentMap;
            return _maps().FindByContextId(contextId);
        }

        private static bool MatchesResetTarget(
            MissionScenarioStepState state,
            MissionScenarioStepDefinition step)
        {
            if (!string.IsNullOrWhiteSpace(step.AttemptKey))
                return string.Equals(state.AttemptKey, step.AttemptKey, StringComparison.Ordinal);
            if (step.TargetScenarioId.HasValue)
                return state.ScenarioId == step.TargetScenarioId.Value;
            return false;
        }

        private void PlanRuntimeReset(
            MissionActionContext context,
            MissionScenarioStepState state)
        {
            if (!context.MissionManager.TryGetScenarioDefinition(
                    context.MissionDefinition.MissionId,
                    state.ScenarioId,
                    out var scenario))
                return;

            var completedStep = scenario.Steps.SingleOrDefault(candidate => candidate.StepId == state.StepId);
            if (completedStep == null)
                return;

            switch (completedStep.Kind)
            {
                case MissionScenarioStepKind.SpawnGroup:
                    if (!completedStep.SpawnGroupId.HasValue ||
                        !context.MissionManager.TryGetSpawnGroupDefinition(
                            context.MissionDefinition.MissionId,
                            completedStep.SpawnGroupId.Value,
                            out var spawnGroup))
                        return;
                    var spawnMap = ResolveMap(
                        context.Client.Player,
                        spawnGroup.MapContextId,
                        context.MapChannel,
                        context.UnitOfWork);
                    context.Plan.AddRuntimeConvergence(() =>
                        RemoveSpawnGroupRuntime(
                            spawnMap,
                            BuildSpawnGroupRuntimeKey(
                                context.Client.Player.Id,
                                context.MissionDefinition.MissionId,
                                spawnGroup.ContentRevision,
                                state.AttemptKey,
                                spawnGroup.SpawnGroupId)));
                    context.Plan.DurableKeyPrefixesToRemove.Add(
                        MissionScenarioStepState.CreateSpawnDeathGroupPrefix(
                            spawnGroup.SpawnGroupId,
                            state.AttemptKey));
                    return;

                case MissionScenarioStepKind.SpawnDynamicObject:
                    if (string.IsNullOrWhiteSpace(completedStep.DynamicObjectKey))
                        return;
                    context.Plan.AddRuntimeConvergence(() =>
                        RemoveScenarioDynamicObject(
                            context.MapChannel,
                            BuildDynamicObjectRuntimeKey(
                                context.Client.Player.Id,
                                context.MissionDefinition.MissionId,
                                completedStep.ContentRevision,
                                state.AttemptKey,
                                completedStep.DynamicObjectKey)));
                    return;
            }
        }

        private static string BuildSpawnGroupRuntimeKey(
            uint ownerCharacterId,
            uint missionId,
            string contentRevision,
            string attemptKey,
            uint spawnGroupId) =>
            $"owner:{ownerCharacterId}:mission:{missionId}:revision:{contentRevision}:attempt:{NormalizeRuntimeKeyToken(attemptKey)}:spawn:{spawnGroupId}";

        private static string BuildDynamicObjectRuntimeKey(
            uint ownerCharacterId,
            uint missionId,
            string contentRevision,
            string attemptKey,
            string dynamicObjectKey) =>
            $"owner:{ownerCharacterId}:mission:{missionId}:revision:{contentRevision}:attempt:{NormalizeRuntimeKeyToken(attemptKey)}:object:{dynamicObjectKey}";

        private static string NormalizeRuntimeKeyToken(string value) =>
            string.IsNullOrWhiteSpace(value) ? "-" : value;

        private bool ShouldUseOwnedPrivateInstance(
            Manifestation player,
            uint contextId,
            MapChannel currentMap,
            ICharUnitOfWork unitOfWork)
        {
            if (player?.Id == 0)
                return false;
            if (currentMap?.MapInfo?.MapContextId == contextId &&
                currentMap.IsPrivateInstance &&
                currentMap.OwnerCharacterId == player.Id)
                return true;

            return _maps().FindOwnedPrivateInstance(contextId, player.Id) != null;
        }

        private RuntimeRegistry GetRegistry(MapChannel mapChannel)
        {
            lock (_runtimeSyncRoot)
            {
                if (_runtimeByMap.TryGetValue(mapChannel, out var registry))
                    return registry;
                registry = new RuntimeRegistry();
                _runtimeByMap.Add(mapChannel, registry);
                return registry;
            }
        }

        private static ScenarioState ParseState(IReadOnlyList<CharacterMissionScenarioStepEntry> rows)
        {
            var completed = new Dictionary<string, MissionScenarioStepState>(StringComparer.Ordinal);
            var scheduled = new List<MissionScenarioStepState>();
            var spawnDeaths = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (var row in rows ?? Array.Empty<CharacterMissionScenarioStepEntry>())
            {
                if (!MissionScenarioStepState.TryParse(row.StepKey, out var state))
                    continue;
                if (state.Kind == MissionScenarioStepStateKind.CompletedStep)
                    completed[row.StepKey] = state;
                else if (state.Kind == MissionScenarioStepStateKind.ScheduledScenario)
                    scheduled.Add(state);
                else if (state.Kind == MissionScenarioStepStateKind.SpawnDeathCount &&
                         state.SpawnGroupId.HasValue &&
                         state.SpawnId.HasValue &&
                         state.DeathCount.HasValue)
                {
                    spawnDeaths[MissionScenarioStepState.CreateSpawnDeathPrefix(
                        state.SpawnGroupId.Value,
                        state.SpawnId.Value,
                        state.AttemptKey)] = state.DeathCount.Value;
                }
            }

            return new ScenarioState(completed, scheduled, spawnDeaths);
        }

        internal void RecordScenarioCreatureDeath(SpawnPool spawnPool)
        {
            if (spawnPool?.SpawnPolicy != MissionSpawnGroupPolicy.ScenarioControlled ||
                !spawnPool.ScenarioGroupId.HasValue ||
                spawnPool.ScenarioOwnerCharacterId == 0 ||
                spawnPool.ScenarioMissionId == 0)
                return;

            using var unitOfWork = _gameUnitOfWorkFactory().CreateChar();
            unitOfWork.ExecuteTransaction(() =>
            {
                var state = ParseState(unitOfWork.CharacterMissionScenario.Get(
                    spawnPool.ScenarioOwnerCharacterId,
                    spawnPool.ScenarioMissionId));
                var prefix = MissionScenarioStepState.CreateSpawnDeathPrefix(
                    spawnPool.ScenarioGroupId.Value,
                    spawnPool.DbId,
                    spawnPool.ScenarioAttemptKey);
                var existingDeaths = state.SpawnDeathCounts.TryGetValue(prefix, out var deathCount)
                    ? deathCount
                    : 0U;
                var spawnTotal = (uint)Math.Max(0, spawnPool.SpawnSlot?.Sum(slot => slot.CountMin) ?? 0);
                var nextDeaths = spawnTotal == 0
                    ? existingDeaths + 1
                    : Math.Min(spawnTotal, existingDeaths + 1);
                unitOfWork.CharacterMissionScenario.RemoveByPrefix(
                    spawnPool.ScenarioOwnerCharacterId,
                    spawnPool.ScenarioMissionId,
                    prefix);
                unitOfWork.CharacterMissionScenario.Add(
                    new CharacterMissionScenarioStepEntry(
                        spawnPool.ScenarioOwnerCharacterId,
                        spawnPool.ScenarioMissionId,
                        MissionScenarioStepState.CreateSpawnDeathKey(
                            spawnPool.ScenarioGroupId.Value,
                            spawnPool.DbId,
                            nextDeaths,
                            spawnPool.ScenarioAttemptKey)));
            });
        }

        private static uint ExtractOwnerCharacterId(string runtimeKey)
        {
            if (string.IsNullOrWhiteSpace(runtimeKey))
                return 0;

            const string ownerPrefix = "owner:";
            const string separator = ":mission:";
            if (!runtimeKey.StartsWith(ownerPrefix, StringComparison.Ordinal))
                return 0;

            var separatorIndex = runtimeKey.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex <= ownerPrefix.Length)
                return 0;

            return uint.TryParse(
                runtimeKey.Substring(ownerPrefix.Length, separatorIndex - ownerPrefix.Length),
                out var ownerCharacterId)
                ? ownerCharacterId
                : 0;
        }

        private sealed class RuntimeRegistry
        {
            internal Dictionary<string, List<SpawnPool>> SpawnGroupsByKey { get; } =
                new(StringComparer.Ordinal);
            internal Dictionary<string, DynamicObject> DynamicObjectsByKey { get; } =
                new(StringComparer.Ordinal);
        }

        private sealed class ScenarioState
        {
            internal ScenarioState(
                IReadOnlyDictionary<string, MissionScenarioStepState> completedSteps,
                IReadOnlyList<MissionScenarioStepState> scheduledSteps,
                IReadOnlyDictionary<string, uint> spawnDeathCounts)
            {
                CompletedSteps = completedSteps;
                ScheduledSteps = scheduledSteps;
                SpawnDeathCounts = spawnDeathCounts;
            }

            internal IReadOnlyDictionary<string, MissionScenarioStepState> CompletedSteps { get; }
            internal IReadOnlyList<MissionScenarioStepState> ScheduledSteps { get; }
            internal IReadOnlyDictionary<string, uint> SpawnDeathCounts { get; }

            internal uint GetSpawnDeathCount(
                uint spawnGroupId,
                uint spawnId,
                string attemptKey)
            {
                var prefix = MissionScenarioStepState.CreateSpawnDeathPrefix(
                    spawnGroupId,
                    spawnId,
                    attemptKey);
                return SpawnDeathCounts.TryGetValue(prefix, out var deathCount)
                    ? deathCount
                    : 0;
            }
        }
    }
}
