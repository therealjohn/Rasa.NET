using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Repositories.World;
    using Structures;
    using Structures.Missions;
    using Structures.World;

    internal sealed class MissionContentLoader
    {
        internal MissionContentSnapshot Load(IMissionContentRepository repository)
        {
            if (repository == null)
                return new MissionContentSnapshot(
                    new Dictionary<uint, MissionContentDefinition>(),
                    new Dictionary<uint, IReadOnlyList<string>>());

            var definitions = repository.GetDefinitions() ?? new List<MissionContentDefinitionEntry>();
            var objectives = repository.GetObjectives() ?? new List<MissionObjectiveDefinitionEntry>();
            var prerequisites = repository.GetPrerequisites() ?? new List<MissionPrerequisiteEntry>();
            var transitions = repository.GetTransitions() ?? new List<MissionObjectiveTransitionEntry>();
            var triggers = repository.GetTriggers() ?? new List<MissionTriggerEntry>();
            var actions = repository.GetActions() ?? new List<MissionActionEntry>();
            var rewards = repository.GetRewards() ?? new List<MissionRewardDefinitionEntry>();
            var rewardItems = repository.GetRewardItems() ?? new List<MissionRewardItemEntry>();
            var indicators = repository.GetIndicators() ?? new List<MissionIndicatorEntry>();
            var areas = repository.GetAreas() ?? new List<MissionAreaEntry>();
            var spawnGroups = repository.GetSpawnGroups() ?? new List<MissionSpawnGroupEntry>();
            var spawns = repository.GetSpawns() ?? new List<MissionSpawnEntry>();
            var scenarios = repository.GetScenarios() ?? new List<MissionScenarioEntry>();
            var scenarioSteps = repository.GetScenarioSteps() ?? new List<MissionScenarioStepEntry>();

            var revisionsByMission = definitions
                .GroupBy(entry => entry.MissionId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(entry => entry.ContentRevision)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray());

            var selectedDefinitions = new Dictionary<uint, MissionContentDefinition>();
            foreach (var missionGroup in definitions.GroupBy(entry => entry.MissionId).OrderBy(group => group.Key))
            {
                var selectedRevision = SelectRevision(missionGroup);
                var definition = missionGroup
                    .OrderBy(entry => entry.ContentRevision, StringComparer.Ordinal)
                    .First(entry => string.Equals(
                        entry.ContentRevision,
                        selectedRevision,
                        StringComparison.Ordinal));

                var missionObjectives = objectives
                    .Where(entry =>
                        entry.MissionId == definition.MissionId &&
                        string.Equals(entry.ContentRevision, selectedRevision, StringComparison.Ordinal))
                    .OrderBy(entry => entry.Ordinal)
                    .ThenBy(entry => entry.ObjectiveId)
                    .ToArray();
                var duplicateObjectiveIds = missionObjectives
                    .GroupBy(entry => entry.ObjectiveId)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .OrderBy(value => value)
                    .ToArray();
                var uniqueObjectives = missionObjectives
                    .GroupBy(entry => entry.ObjectiveId)
                    .ToDictionary(group => group.Key, group => group.First());

                var missionIndicators = indicators
                    .Where(entry =>
                        entry.MissionId == definition.MissionId &&
                        string.Equals(entry.ContentRevision, selectedRevision, StringComparison.Ordinal))
                    .GroupBy(entry => entry.ObjectiveId)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderBy(entry => entry.IndicatorId)
                            .Select(entry => new MissionIndicator
                            {
                                IndicatorId = entry.IndicatorId,
                                Position = new System.Numerics.Vector3(
                                    (float)entry.PosX,
                                    (float)entry.PosY,
                                    (float)entry.PosZ),
                                Radius = entry.Radius,
                                Show3DEffect = entry.Show3DEffect
                            })
                            .ToArray());

                var transitionDefinitions = BuildTransitions(
                    definition.MissionId,
                    selectedRevision,
                    transitions,
                    triggers,
                    actions);
                var objectiveDefinitions = BuildObjectives(
                    uniqueObjectives,
                    missionIndicators,
                    transitionDefinitions);

                var mission = new Mission(
                    definition.MissionId,
                    definition.Comment,
                    NormalizeTextId(definition.ClientNameTextId),
                    definition.GiverId,
                    definition.ReceiverId,
                    definition.Level,
                    definition.GroupType,
                    definition.CategoryId,
                    definition.Shareable,
                    definition.RadioCompleteable,
                    objectiveDefinitions.Values.OrderBy(objective => objective.ObjectiveId).ToArray(),
                    enableOperational: true);

                selectedDefinitions.Add(
                    definition.MissionId,
                    new MissionContentDefinition(
                        definition.MissionId,
                        selectedRevision,
                        definition.Requirement,
                        mission,
                        duplicateObjectiveIds,
                        prerequisites
                            .Where(entry =>
                                entry.MissionId == definition.MissionId &&
                                string.Equals(entry.ContentRevision, selectedRevision, StringComparison.Ordinal))
                            .OrderBy(entry => entry.PrerequisiteId)
                            .Select(entry => new MissionPrerequisiteDefinition(entry))
                            .ToArray(),
                        transitionDefinitions,
                        BuildRewards(definition.MissionId, selectedRevision, rewards, rewardItems),
                        areas
                            .Where(entry =>
                                entry.MissionId == definition.MissionId &&
                                string.Equals(entry.ContentRevision, selectedRevision, StringComparison.Ordinal))
                            .GroupBy(entry => entry.AreaId)
                            .ToDictionary(group => group.Key, group => new MissionAreaDefinition(group.First())),
                        BuildSpawnGroups(definition.MissionId, selectedRevision, spawnGroups, spawns),
                        BuildScenarios(definition.MissionId, selectedRevision, scenarios, scenarioSteps)));
            }

            return new MissionContentSnapshot(selectedDefinitions, revisionsByMission);
        }

        private static string SelectRevision(
            IGrouping<uint, MissionContentDefinitionEntry> missionGroup)
        {
            var revisions = missionGroup
                .Select(entry => entry.ContentRevision)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(value => value, StringComparer.Ordinal)
                .ToArray();
            var nonLegacy = revisions
                .Where(revision => !string.Equals(revision, "legacy", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (nonLegacy.Length > 0)
                return nonLegacy[0];
            return revisions[0];
        }

        private static IReadOnlyDictionary<uint, MissionObjectiveTransitionDefinition> BuildTransitions(
            uint missionId,
            string contentRevision,
            IEnumerable<MissionObjectiveTransitionEntry> transitions,
            IEnumerable<MissionTriggerEntry> triggers,
            IEnumerable<MissionActionEntry> actions)
        {
            var missionTriggers = triggers
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToLookup(entry => entry.TransitionId);
            var missionActions = actions
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToLookup(entry => entry.TransitionId);

            return transitions
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .GroupBy(entry => entry.TransitionId)
                .ToDictionary(
                    group => group.Key,
                    group => new MissionObjectiveTransitionDefinition(
                        group.First(),
                        missionTriggers[group.Key].Select(entry => new MissionTriggerDefinition(entry)).ToArray(),
                        missionActions[group.Key].Select(entry => new MissionActionDefinition(entry)).ToArray()));
        }

        private static IReadOnlyDictionary<uint, MissionObjectiveDefinition> BuildObjectives(
            IReadOnlyDictionary<uint, MissionObjectiveDefinitionEntry> objectives,
            IReadOnlyDictionary<uint, MissionIndicator[]> indicators,
            IReadOnlyDictionary<uint, MissionObjectiveTransitionDefinition> transitions)
        {
            var objectiveDefinitions = new Dictionary<uint, MissionObjectiveDefinition>();
            var transitionsByObjective = transitions.Values.ToLookup(transition => transition.ObjectiveId);
            foreach (var objectiveEntry in objectives.Values.OrderBy(entry => entry.Ordinal).ThenBy(entry => entry.ObjectiveId))
            {
                var revealTargets = new HashSet<uint>();
                var activateTargets = new HashSet<uint>();
                var conversations = new List<MissionObjectiveConversation>();
                var counters = new Dictionary<uint, MissionObjectiveCounterDefinition>();
                var itemCounters = new Dictionary<uint, MissionObjectiveItemCounterDefinition>();
                MissionProgressRule progressRule = null;

                foreach (var transition in transitionsByObjective[objectiveEntry.ObjectiveId]
                    .OrderBy(entry => entry.Sequence)
                    .ThenBy(entry => entry.TransitionId))
                {
                    foreach (var action in transition.Actions)
                    {
                        if (action.Kind == MissionActionKind.RevealObjective &&
                            action.TargetObjectiveId.HasValue)
                            revealTargets.Add(action.TargetObjectiveId.Value);
                        if (action.Kind == MissionActionKind.ActivateObjective &&
                            action.TargetObjectiveId.HasValue)
                            activateTargets.Add(action.TargetObjectiveId.Value);
                    }

                    foreach (var trigger in transition.Triggers
                        .Where(trigger => trigger.Kind == MissionTriggerKind.Conversation &&
                            trigger.NpcPackageId.HasValue &&
                            trigger.PlayerFlagId.HasValue))
                    {
                        conversations.Add(new MissionObjectiveConversation(
                            trigger.NpcPackageId.Value,
                            trigger.PlayerFlagId.Value,
                            MissionObjectiveConversationType.Completion));
                    }

                    if (progressRule == null &&
                        TryBuildProgressRule(
                            transition,
                            out var candidate,
                            out var derivedCounters,
                            out var derivedItemCounters))
                    {
                        progressRule = candidate;
                        foreach (var counter in derivedCounters)
                            counters[counter.Key] = counter.Value;
                        foreach (var counter in derivedItemCounters)
                            itemCounters[counter.Key] = counter.Value;
                    }
                }

                var counterTextIds = counters.Count == 0
                    ? new uint?[] { null, null, null }
                    : Enumerable.Range(
                            0,
                            Math.Max(
                                counters.Keys.Select(value => (int)value).DefaultIfEmpty(-1).Max() + 1,
                                3))
                        .Select(_ => (uint?)null)
                        .ToArray();

                objectiveDefinitions.Add(
                    objectiveEntry.ObjectiveId,
                    new MissionObjectiveDefinition(
                        objectiveEntry.ObjectiveId,
                        NormalizeTextId(objectiveEntry.ClientNameTextId),
                        NormalizeTextId(objectiveEntry.ClientBodyTextId),
                        counterTextIds,
                        objectiveEntry.Ordinal,
                        ParseObjectiveState(objectiveEntry.InitialState),
                        objectiveEntry.IsRequired,
                        counters,
                        itemCounters,
                        conversations,
                        revealTargets.OrderBy(value => value).ToArray(),
                        activateTargets.OrderBy(value => value).ToArray(),
                        indicators.TryGetValue(objectiveEntry.ObjectiveId, out var indicatorList)
                            ? indicatorList
                            : Array.Empty<MissionIndicator>(),
                        progressRule));
            }

            return objectiveDefinitions;
        }

        private static bool TryBuildProgressRule(
            MissionObjectiveTransitionDefinition transition,
            out MissionProgressRule rule,
            out IReadOnlyDictionary<uint, MissionObjectiveCounterDefinition> counters,
            out IReadOnlyDictionary<uint, MissionObjectiveItemCounterDefinition> itemCounters)
        {
            counters = new Dictionary<uint, MissionObjectiveCounterDefinition>();
            itemCounters = new Dictionary<uint, MissionObjectiveItemCounterDefinition>();
            rule = null;

            var progressTriggers = transition.Triggers
                .Where(trigger => trigger.Kind == MissionTriggerKind.ProgressEvent)
                .ToArray();
            if (progressTriggers.Length == 0)
                return false;
            if (progressTriggers.Any(trigger => !trigger.TryGetEventKind(out _)))
                return false;
            if (progressTriggers.Length == 1)
            {
                var trigger = progressTriggers[0];
                trigger.TryGetEventKind(out var kind);
                if (!trigger.SubjectId.HasValue || trigger.SubjectId.Value == 0)
                    return false;
                if (kind == MissionProgressEventKind.ItemAcquired ||
                    kind == MissionProgressEventKind.ItemConsumed)
                {
                    if (!trigger.InitialValue.HasValue || !trigger.TargetValue.HasValue)
                        return false;
                    itemCounters = new Dictionary<uint, MissionObjectiveItemCounterDefinition>
                    {
                        [trigger.SubjectId.Value] = new(
                            trigger.SubjectId.Value,
                            trigger.InitialValue.Value,
                            trigger.TargetValue.Value)
                    };
                    rule = MissionProgressRule.IncrementItemCounterOnExactSubject(
                        kind,
                        trigger.SubjectId.Value,
                        trigger.InitialValue.Value,
                        trigger.TargetValue.Value);
                    return true;
                }

                if (trigger.CounterId.HasValue &&
                    trigger.InitialValue.HasValue &&
                    trigger.TargetValue.HasValue)
                {
                    counters = new Dictionary<uint, MissionObjectiveCounterDefinition>
                    {
                        [trigger.CounterId.Value] = new(
                            trigger.CounterId.Value,
                            trigger.InitialValue.Value,
                            trigger.TargetValue.Value)
                    };
                    rule = MissionProgressRule.IncrementCounterOnExactSubject(
                        kind,
                        trigger.SubjectId.Value,
                        trigger.CounterId.Value,
                        trigger.InitialValue.Value,
                        trigger.TargetValue.Value);
                    return true;
                }

                if (kind == MissionProgressEventKind.WaypointAcquired ||
                    kind == MissionProgressEventKind.LogosAcquired)
                {
                    rule = MissionProgressRule.CompleteWhenAllDistinctSubjectsObserved(
                        kind,
                        new HashSet<uint> { trigger.SubjectId.Value });
                    return true;
                }

                rule = MissionProgressRule.CompleteOnExactSubject(
                    kind,
                    trigger.SubjectId.Value);
                return true;
            }

            if (!progressTriggers.All(trigger =>
                    trigger.SubjectId.HasValue &&
                    trigger.SubjectId.Value != 0 &&
                    !trigger.CounterId.HasValue &&
                    !trigger.InitialValue.HasValue &&
                    !trigger.TargetValue.HasValue))
                return false;
            progressTriggers[0].TryGetEventKind(out var aggregateKind);
            if (aggregateKind != MissionProgressEventKind.WaypointAcquired &&
                aggregateKind != MissionProgressEventKind.LogosAcquired)
                return false;
            if (!progressTriggers.All(trigger =>
                    trigger.TryGetEventKind(out var kind) &&
                    kind == aggregateKind))
                return false;

            rule = MissionProgressRule.CompleteWhenAllDistinctSubjectsObserved(
                aggregateKind,
                new HashSet<uint>(progressTriggers.Select(trigger => trigger.SubjectId.Value)));
            return true;
        }

        private static IReadOnlyDictionary<uint, MissionAuthoringRewardDefinition> BuildRewards(
            uint missionId,
            string contentRevision,
            IEnumerable<MissionRewardDefinitionEntry> rewards,
            IEnumerable<MissionRewardItemEntry> rewardItems)
        {
            var itemsLookup = rewardItems
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToLookup(entry => entry.RewardId);
            return rewards
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToDictionary(
                    entry => entry.RewardId,
                    entry => new MissionAuthoringRewardDefinition(
                        entry,
                        itemsLookup[entry.RewardId]
                            .Where(item => item.Kind == MissionRewardItemKind.Fixed)
                            .Select(item => new MissionAuthoringRewardItemDefinition(item))
                            .ToArray(),
                        itemsLookup[entry.RewardId]
                            .Where(item => item.Kind == MissionRewardItemKind.Selectable)
                            .Select(item => new MissionAuthoringRewardItemDefinition(item))
                            .ToArray()));
        }

        private static IReadOnlyDictionary<uint, MissionSpawnGroupDefinition> BuildSpawnGroups(
            uint missionId,
            string contentRevision,
            IEnumerable<MissionSpawnGroupEntry> spawnGroups,
            IEnumerable<MissionSpawnEntry> spawns)
        {
            var spawnLookup = spawns
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToLookup(entry => entry.SpawnGroupId);
            return spawnGroups
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToDictionary(
                    entry => entry.SpawnGroupId,
                    entry => new MissionSpawnGroupDefinition(
                        entry,
                        spawnLookup[entry.SpawnGroupId]
                            .Select(spawn => new MissionSpawnDefinition(spawn))
                            .ToArray()));
        }

        private static IReadOnlyDictionary<uint, MissionScenarioDefinition> BuildScenarios(
            uint missionId,
            string contentRevision,
            IEnumerable<MissionScenarioEntry> scenarios,
            IEnumerable<MissionScenarioStepEntry> scenarioSteps)
        {
            var stepLookup = scenarioSteps
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToLookup(entry => entry.ScenarioId);
            return scenarios
                .Where(entry =>
                    entry.MissionId == missionId &&
                    string.Equals(entry.ContentRevision, contentRevision, StringComparison.Ordinal))
                .ToDictionary(
                    entry => entry.ScenarioId,
                    entry => new MissionScenarioDefinition(
                        entry,
                        stepLookup[entry.ScenarioId]
                            .Select(step => new MissionScenarioStepDefinition(step))
                            .ToArray()));
        }

        private static uint? NormalizeTextId(uint value) =>
            value == 0 ? null : value;

        private static MissionObjectiveState? ParseObjectiveState(byte value) =>
            Enum.IsDefined(typeof(MissionObjectiveState), (int)value)
                ? (MissionObjectiveState)value
                : null;
    }
}
