using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Repositories.Char;
    using Repositories.Char.CharacterMissionProgress;
    using Repositories.UnitOfWork;
    using Structures;

    internal static class BootcampReinforcementsCompatibility
    {
        private const uint MissionId = 1995;
        private const uint SearchObjectiveId = 2;
        private const uint LegacySurvivorObjectiveId = 10;

        internal static CharacterMissionProgressSnapshot NormalizeSnapshot(
            uint characterId,
            CharacterMissionProgressSnapshot progress,
            IGameUnitOfWorkFactory factory,
            MissionManager manager)
        {
            if (!progress.Missions.TryGetValue(MissionId, out var legacy) ||
                !legacy.Objectives.ContainsKey(LegacySurvivorObjectiveId))
                return progress;

            using var unit = factory.CreateChar();
            unit.ExecuteTransaction(() => NormalizeDurable(characterId, unit, manager));
            return unit.CharacterMissionProgress.Get(characterId);
        }

        internal static void NormalizeDurable(uint characterId, ICharUnitOfWork unit, MissionManager manager)
        {
            if (!manager.TryGetOperationalMission(MissionId, out var definition) ||
                definition.Objectives.ContainsKey(LegacySurvivorObjectiveId))
                return;

            var objectives = unit.CharacterMissionProgress.GetTracked(characterId, MissionId);
            if (!objectives.TryGetValue(LegacySurvivorObjectiveId, out var survivor))
                return;

            var mission = unit.CharacterMissions.GetByCharacterAndMission(characterId, MissionId);
            // The old area step completed 2 before exposing 10. Preserve the conversation
            // without replaying scenario or timer outputs.
            if (mission == null || !MissionManager.IsPublishedState((MissionState)mission.MissionState) ||
                !objectives.TryGetValue(SearchObjectiveId, out var search) ||
                objectives.Count != 5 ||
                definition.Objectives.Keys.Except(objectives.Keys).Any() ||
                objectives.Values.Any(objective =>
                    objective.ObjectiveState < (byte)MissionObjectiveState.Incomplete ||
                    objective.ObjectiveState > (byte)MissionObjectiveState.Inactive ||
                    objective.Counters.Count != 0 || objective.ItemCounters.Count != 0) ||
                (survivor.ObjectiveState != (byte)MissionObjectiveState.Inactive &&
                 search.ObjectiveState != (byte)MissionObjectiveState.Completed))
            {
                var message = $"Cannot migrate legacy mission {MissionId} for character {characterId}: inconsistent objective progress; saved data was preserved.";
                Logger.WriteLog(LogType.Error, message);
                throw new GameplayRejectionException(message);
            }

            var mergedState = survivor.ObjectiveState != (byte)MissionObjectiveState.Inactive
                ? survivor.ObjectiveState
                : search.ObjectiveState == (byte)MissionObjectiveState.Completed
                    ? (byte)MissionObjectiveState.Incomplete
                    : search.ObjectiveState;
            var completeable = definition.Objectives.Values.Where(objective => objective.IsRequired.Value)
                .All(objective =>
                    (objective.ObjectiveId == SearchObjectiveId ? mergedState : objectives[objective.ObjectiveId].ObjectiveState)
                    == (byte)MissionObjectiveState.Completed);
            if (mission.MissionState == (uint)MissionState.Active && mission.Completeable != completeable)
            {
                var message = $"Cannot migrate legacy mission {MissionId} for character {characterId}: inconsistent completion state; saved data was preserved.";
                Logger.WriteLog(LogType.Error, message);
                throw new GameplayRejectionException(message);
            }
            search.ObjectiveState = mergedState;
            unit.CharacterMissionProgress.Remove(characterId, MissionId, LegacySurvivorObjectiveId);
        }

        internal static void NormalizeProgress(
            Manifestation player, IGameUnitOfWorkFactory factory, MissionManager manager, MissionProgressEvent progress)
        {
            if (player == null || !player.Missions.TryGetValue(MissionId, out var mission) ||
                mission.State != MissionState.Active ||
                !mission.Objectives.ContainsKey(LegacySurvivorObjectiveId) ||
                !manager.TryGetOperationalMission(MissionId, out var definition) ||
                !definition.Objectives.Values
                    .SelectMany(objective => objective.GetExecutableTransitionsOrLegacyDefault())
                    .Any(transition => transition.ProgressRule?.Matches(progress) == true))
                return;

            NormalizeRuntime(player, factory, manager);
        }

        internal static void NormalizeRuntime(
            Manifestation player, IGameUnitOfWorkFactory factory, MissionManager manager, uint? requestedMissionId = null)
        {
            if ((requestedMissionId.HasValue && requestedMissionId.Value != MissionId) ||
                player == null || !player.Missions.TryGetValue(MissionId, out var mission) ||
                !mission.Objectives.ContainsKey(LegacySurvivorObjectiveId) ||
                !manager.TryGetOperationalMission(MissionId, out var definition) ||
                definition.Objectives.ContainsKey(LegacySurvivorObjectiveId))
                return;

            using var unit = factory.CreateChar();
            MissionLog recovered = null;
            unit.ExecuteTransaction(() =>
            {
                NormalizeDurable(player.Id, unit, manager);
                var row = unit.CharacterMissions.GetByCharacterAndMission(player.Id, MissionId);
                if (row == null ||
                    !manager.TryHydrateMission(
                        player.Id, row, unit.CharacterMissionProgress.Get(player.Id, MissionId), out recovered))
                    throw new GameplayRejectionException(
                        $"Cannot hydrate migrated mission {MissionId} for character {player.Id}; saved data was preserved.");
            });
            player.Missions[MissionId] = recovered;
        }
    }
}
