using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Repositories.Char;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;

    public sealed class MissionDeadlineService
    {
        private static MissionDeadlineService _instance;
        private static readonly object InstanceLock = new();
        private readonly Func<IGameUnitOfWorkFactory> _gameUnitOfWorkFactory;
        private readonly Func<MissionManager> _missionManager;
        private readonly Func<DateTime> _utcNow;

        internal static MissionDeadlineService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        _instance ??= new MissionDeadlineService();
                    }
                }

                return _instance;
            }
        }

        internal MissionDeadlineService(
            Func<IGameUnitOfWorkFactory> gameUnitOfWorkFactory = null,
            Func<MissionManager> missionManager = null,
            Func<DateTime> utcNow = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory ?? (() => Server.GameUnitOfWorkFactory);
            _missionManager = missionManager ?? (() => MissionManager.Instance);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal bool Evaluate(Client client)
        {
            var missionManager = _missionManager();
            var unitOfWorkFactory = _gameUnitOfWorkFactory();
            if (client == null || missionManager == null || unitOfWorkFactory == null)
                return false;

            lock (client.SyncRoot)
            {
                if (client.AccountEntry == null ||
                    client.Player == null ||
                    client.Player.MapChannel == null ||
                    client.State != ClientState.Ingame)
                    return false;

                var progressPlan = MissionManager.MissionProgressPublicationPlan.Empty;
                try
                {
                    using var unitOfWork = unitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        SynchronizeClient(unitOfWork, missionManager, client);
                        var expiredEvents = CollectExpiredEvents(
                            unitOfWork,
                            missionManager,
                            client);
                        if (expiredEvents.Count == 0)
                            return;

                        progressPlan = missionManager.PlanProgress(
                            client,
                            expiredEvents,
                            unitOfWork);
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to evaluate mission deadlines for character {client.Player.Id}: {error}");
                    return false;
                }

                progressPlan.Publish(client);
                return progressPlan.HasChanges;
            }
        }

        internal void SynchronizeMission(
            ICharUnitOfWork unitOfWork,
            uint characterId,
            Mission definition,
            CharacterMissionEntry durableMission,
            IReadOnlyDictionary<uint, CharacterMissionObjectiveEntry> durableObjectives)
        {
            if (unitOfWork == null || definition == null || durableMission == null)
                return;
            if (!definition.Objectives.Values.Any(objective =>
                    objective.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed))
                return;

            var activeDeadline = GetActiveDeadlineObjective(
                definition,
                durableMission,
                durableObjectives,
                requireSingle: true);
            var existing = unitOfWork.CharacterMissionDeadlines.Get(
                characterId,
                definition.MissionId);
            if (activeDeadline.HasValue)
            {
                if (existing == null)
                {
                    unitOfWork.CharacterMissionDeadlines.Add(
                        new CharacterMissionDeadlineEntry(
                            characterId,
                            definition.MissionId,
                            _utcNow().AddSeconds(activeDeadline.Value.Rule.DurationSeconds.Value),
                            CharacterMissionDeadlineState.Active));
                }

                return;
            }

            if (existing?.State != CharacterMissionDeadlineState.Active)
                return;

            var completedDeadlineObjective = definition.Objectives.Values.Any(objective =>
                objective.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed &&
                durableObjectives.TryGetValue(objective.ObjectiveId, out var durableObjective) &&
                durableObjective.ObjectiveState == (byte)MissionObjectiveState.Completed);
            unitOfWork.CharacterMissionDeadlines.SetState(
                characterId,
                definition.MissionId,
                completedDeadlineObjective
                    ? CharacterMissionDeadlineState.Satisfied
                    : CharacterMissionDeadlineState.Cancelled);
        }

        private void SynchronizeClient(
            ICharUnitOfWork unitOfWork,
            MissionManager missionManager,
            Client client)
        {
            foreach (var missionLog in client.Player.Missions.Values
                         .OrderBy(mission => mission.MissionId))
            {
                if (!missionManager.LoadedMissions.TryGetValue(
                        missionLog.MissionId,
                        out var definition) ||
                    !definition.IsOperational)
                    continue;

                var durableMission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                    client.Player.Id,
                    missionLog.MissionId);
                if (durableMission == null)
                    continue;

                var durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
                    client.Player.Id,
                    missionLog.MissionId);
                SynchronizeMission(
                    unitOfWork,
                    client.Player.Id,
                    definition,
                    durableMission,
                    durableObjectives);
            }
        }

        private IReadOnlyList<MissionProgressEvent> CollectExpiredEvents(
            ICharUnitOfWork unitOfWork,
            MissionManager missionManager,
            Client client)
        {
            var now = _utcNow();
            var expired = new List<MissionProgressEvent>();
            foreach (var deadline in unitOfWork.CharacterMissionDeadlines.Get(client.Player.Id)
                         .Where(entry =>
                             entry.State == CharacterMissionDeadlineState.Active &&
                             entry.DueAtUtc <= now)
                         .OrderBy(entry => entry.MissionId))
            {
                if (!missionManager.LoadedMissions.TryGetValue(
                        deadline.MissionId,
                        out var definition) ||
                    !definition.IsOperational ||
                    !client.Player.Missions.TryGetValue(
                        deadline.MissionId,
                        out var runtimeMission) ||
                    runtimeMission.State != MissionState.Active)
                {
                    unitOfWork.CharacterMissionDeadlines.SetState(
                        client.Player.Id,
                        deadline.MissionId,
                        CharacterMissionDeadlineState.Cancelled);
                    continue;
                }

                var durableMission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                    client.Player.Id,
                    deadline.MissionId);
                var durableObjectives = unitOfWork.CharacterMissionProgress.GetTracked(
                    client.Player.Id,
                    deadline.MissionId);
                var activeDeadline = GetActiveDeadlineObjective(
                    definition,
                    durableMission,
                    durableObjectives,
                    requireSingle: false);
                if (!activeDeadline.HasValue)
                {
                    unitOfWork.CharacterMissionDeadlines.SetState(
                        client.Player.Id,
                        deadline.MissionId,
                        CharacterMissionDeadlineState.Cancelled);
                    continue;
                }

                unitOfWork.CharacterMissionDeadlines.SetState(
                    client.Player.Id,
                    deadline.MissionId,
                    CharacterMissionDeadlineState.Expired);
                expired.Add(
                    MissionProgressEvent.Deadline(
                        deadline.MissionId,
                        activeDeadline.Value.ObjectiveId));
            }

            return expired;
        }

        private static (uint ObjectiveId, MissionProgressRule Rule)? GetActiveDeadlineObjective(
            Mission definition,
            CharacterMissionEntry durableMission,
            IReadOnlyDictionary<uint, CharacterMissionObjectiveEntry> durableObjectives,
            bool requireSingle)
        {
            if (definition == null ||
                durableMission?.MissionState != (uint)MissionState.Active)
                return null;

            var active = definition.Objectives.Values
                .Where(objective =>
                    objective.ProgressRule?.Kind == MissionProgressEventKind.DeadlineElapsed &&
                    durableObjectives.TryGetValue(objective.ObjectiveId, out var durableObjective) &&
                    durableObjective.ObjectiveState == (byte)MissionObjectiveState.Incomplete)
                .Select(objective => (objective.ObjectiveId, objective.ProgressRule))
                .ToArray();
            if (active.Length == 0)
                return null;
            if (requireSingle && active.Length > 1)
                throw new GameplayRejectionException(
                    $"Mission {definition.MissionId} has multiple active deadline objectives.");
            return active[0];
        }
    }
}
