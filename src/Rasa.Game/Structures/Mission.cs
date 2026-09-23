using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Rasa.Structures
{
    using Data;
    using Structures.World;

    public sealed class Mission
    {
        public uint MissionId { get; }
        public string Name { get; }
        public uint? ClientNameTextId { get; }
        public uint? MissionGiver { get; }
        public uint? MissionReciver { get; }
        public uint? Level { get; }
        public byte? GroupType { get; }
        public byte? CategoryId { get; }
        public bool? Shareable { get; }
        public bool? RadioCompletable { get; }
        public IReadOnlyDictionary<uint, MissionObjectiveDefinition> Objectives { get; }
        public bool IsOperational { get; }
        public string OperationalDiagnostic { get; }

        public Mission(NpcMissionEntry mission, bool isOperational = false)
            : this(
                mission.Id,
                mission.Comment,
                null,
                mission.GiverId,
                mission.ReciverId,
                mission.Level,
                mission.GroupType,
                mission.CategoryId,
                mission.Shareable,
                mission.RadioCompleteable,
                Array.Empty<MissionObjectiveDefinition>(),
                isOperational)
        {
        }

        internal Mission(
            uint missionId,
            string name,
            uint? clientNameTextId,
            uint? missionGiver,
            uint? missionReciver,
            uint? level,
            byte? groupType,
            byte? categoryId,
            bool? shareable,
            bool? radioCompletable,
            IEnumerable<MissionObjectiveDefinition> objectives,
            bool enableOperational = false,
            string operationalDiagnostic = null)
        {
            MissionId = missionId;
            Name = name;
            ClientNameTextId = clientNameTextId;
            MissionGiver = missionGiver;
            MissionReciver = missionReciver;
            Level = level;
            GroupType = groupType;
            CategoryId = categoryId;
            Shareable = shareable;
            RadioCompletable = radioCompletable;
            var objectiveDictionary = (objectives ?? Array.Empty<MissionObjectiveDefinition>())
                .ToDictionary(objective => objective.ObjectiveId);
            Objectives = new ReadOnlyDictionary<uint, MissionObjectiveDefinition>(
                objectiveDictionary);
            var diagnostics = new List<string>();
            if (!enableOperational)
                diagnostics.Add("operational eligibility was not enabled");
            if (!MissionGiver.HasValue || !MissionReciver.HasValue || !Level.HasValue ||
                !GroupType.HasValue || !CategoryId.HasValue || !Shareable.HasValue ||
                !RadioCompletable.HasValue)
                diagnostics.Add("mission database metadata is incomplete");
            if (objectiveDictionary.Values.Any(objective => !objective.HasCompleteServerContract))
                diagnostics.Add("one or more objective server contracts are incomplete");
            if (objectiveDictionary.Values
                .Where(objective =>
                    objective.RevealedObjectiveIds != null &&
                    objective.ActivatedObjectiveIds != null)
                .SelectMany(objective =>
                    objective.RevealedObjectiveIds.Concat(objective.ActivatedObjectiveIds))
                .Any(objectiveId => !objectiveDictionary.ContainsKey(objectiveId)))
                diagnostics.Add("an objective successor is missing");
            if (!string.IsNullOrWhiteSpace(operationalDiagnostic))
                diagnostics.Add(operationalDiagnostic);

            IsOperational = diagnostics.Count == 0;
            OperationalDiagnostic = IsOperational
                ? null
                : string.Join("; ", diagnostics);
        }

        internal MissionInfo CreateInfo(
            MissionState state,
            bool completeable,
            IReadOnlyDictionary<uint, MissionObjectiveLog> objectiveLogs = null,
            Func<uint, uint?> objectiveTimeRemaining = null)
        {
            if (!IsOperational)
                throw new InvalidOperationException("Mission definition is not operational.");

            objectiveLogs ??= new Dictionary<uint, MissionObjectiveLog>();
            var objectives = new List<MissionObjective>();
            foreach (var definition in Objectives.Values.OrderBy(objective => objective.Ordinal.Value))
            {
                // Inactive means unrevealed; the native mission log renders every received row.
                if (!objectiveLogs.TryGetValue(definition.ObjectiveId, out var log) ||
                    log.State == MissionObjectiveState.Inactive)
                    continue;
                var objective = definition.CreateRuntime(log.State, log.Counters, log.ItemCounters);
                objective.TimeRemaining = objectiveTimeRemaining?.Invoke(definition.ObjectiveId);
                objectives.Add(objective);
            }

            return new MissionInfo
            {
                MissionState = state,
                Completeable = state == MissionState.Active && completeable,
                MissionConstantData = new MissionConstantData
                {
                    Level = Level.Value,
                    GroupType = GroupType.Value,
                    CategoryId = CategoryId.Value,
                    Shareable = Shareable.Value,
                    RadioCompletable = RadioCompletable.Value
                },
                ObjectivesList = objectives
            };
        }

        internal IReadOnlyDictionary<uint, MissionObjectiveLog> CreateInitialObjectiveLogs() =>
            Objectives.Values.ToDictionary(
                objective => objective.ObjectiveId,
                objective => new MissionObjectiveLog(
                    objective.ObjectiveId,
                    objective.InitialState.Value,
                    objective.Counters.ToDictionary(
                        counter => counter.Key,
                        counter => counter.Value.InitialValue),
                    objective.ItemCounters.ToDictionary(
                        counter => counter.Key,
                        counter => counter.Value.InitialValue)));

        internal Mission WithWorldMetadata(Mission worldDefinition) =>
            new(
                MissionId,
                Name,
                ClientNameTextId,
                worldDefinition?.MissionGiver,
                worldDefinition?.MissionReciver,
                worldDefinition?.Level,
                worldDefinition?.GroupType,
                worldDefinition?.CategoryId,
                worldDefinition?.Shareable,
                worldDefinition?.RadioCompletable,
                Objectives.Values,
                enableOperational: true);

        internal Mission DisableOperational(string diagnostic) =>
            new(
                MissionId,
                Name,
                ClientNameTextId,
                MissionGiver,
                MissionReciver,
                Level,
                GroupType,
                CategoryId,
                Shareable,
                RadioCompletable,
                Objectives.Values,
                enableOperational: true,
                operationalDiagnostic: diagnostic);
    }
}
