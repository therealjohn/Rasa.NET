using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Repositories.World;
    using Structures.Missions;
    using Structures.World;

    internal sealed class MissionContentValidator
    {
        internal MissionValidationReport Validate(
            MissionContentSnapshot snapshot,
            IWorldUnitOfWork unitOfWork)
        {
            var diagnostics = new List<MissionValidationDiagnostic>();
            var requiredMissionIds = snapshot.Definitions.Values
                .Where(definition => definition.Requirement == MissionContentRequirement.Required)
                .Select(definition => definition.MissionId)
                .ToArray();

            var references = CreateReferences(unitOfWork);
            foreach (var revisions in snapshot.RevisionsByMission.OrderBy(entry => entry.Key))
            {
                var authoredRevisions = revisions.Value
                    .Where(revision => !string.Equals(revision, "legacy", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (authoredRevisions.Length > 1)
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "duplicate-mission-id",
                        $"multiple authored content revisions are present ({string.Join(", ", authoredRevisions)}); keep one active revision per mission.",
                        revisions.Key,
                        snapshot.Definitions.TryGetValue(revisions.Key, out var selected)
                            ? selected.ContentRevision
                            : authoredRevisions[0]));
                }
            }

            foreach (var definition in snapshot.Definitions.Values.OrderBy(definition => definition.MissionId))
            {
                ValidateMission(snapshot, definition, references, diagnostics);
            }

            var report = new MissionValidationReport(diagnostics, requiredMissionIds);
            var inactiveRequired = snapshot.Definitions.Values
                .Where(definition => definition.Requirement == MissionContentRequirement.Required)
                .Where(definition =>
                    definition.Prerequisites.Any(prerequisite =>
                        prerequisite.RequiredMissionId.HasValue &&
                        snapshot.Definitions.TryGetValue(prerequisite.RequiredMissionId.Value, out var target) &&
                        report.HasErrorsForMission(target.MissionId)))
                .SelectMany(definition =>
                    definition.Prerequisites
                        .Where(prerequisite =>
                            prerequisite.RequiredMissionId.HasValue &&
                            snapshot.Definitions.TryGetValue(prerequisite.RequiredMissionId.Value, out var target) &&
                            report.HasErrorsForMission(target.MissionId))
                        .Select(prerequisite => new MissionValidationDiagnostic(
                            "required-chain-inactive",
                            $"required prerequisite mission {prerequisite.RequiredMissionId.Value} is inactive; fix that mission or mark the chain optional.",
                            definition.MissionId,
                            definition.ContentRevision)))
                .ToArray();
            if (inactiveRequired.Length == 0)
                return report;
            return new MissionValidationReport(report.Diagnostics.Concat(inactiveRequired), requiredMissionIds);
        }

        private static MissionContentReferenceSet CreateReferences(IWorldUnitOfWork unitOfWork)
        {
            var itemTemplateClasses = (unitOfWork?.Equipment?.GetItemTemplateClasses() ??
                new List<ItemTemplateItemClassEntry>())
                .ToDictionary(entry => entry.ItemTemplateId, entry => entry.ItemClass);
            var creatureClasses = (unitOfWork?.Creatures?.Get() ?? new List<CreatureEntry>())
                .ToDictionary(entry => entry.Id, entry => entry.ClassId);
            return new MissionContentReferenceSet(
                (unitOfWork?.NpcPackages?.Get() ?? new List<NpcPackageEntry>()).Select(entry => entry.Id),
                itemTemplateClasses,
                (unitOfWork?.EntityClasses?.Get() ?? new List<EntityClassEntry>()).Select(entry => entry.Id),
                creatureClasses,
                (unitOfWork?.MapInfos?.Get() ?? new List<MapInfoEntry>()).Select(entry => entry.Id));
        }

        private static void ValidateMission(
            MissionContentSnapshot snapshot,
            MissionContentDefinition definition,
            MissionContentReferenceSet references,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            if (!definition.Mission.ClientNameTextId.HasValue)
            {
                diagnostics.Add(new MissionValidationDiagnostic(
                    "missing-client-text",
                    "mission client_name_text_id is missing; author a non-zero binding.",
                    definition.MissionId,
                    definition.ContentRevision));
            }

            if (definition.Objectives.Count == 0)
            {
                diagnostics.Add(new MissionValidationDiagnostic(
                    "missing-objective-id",
                    "mission has no authored objectives; add at least one objective row.",
                    definition.MissionId,
                    definition.ContentRevision));
            }

            foreach (var duplicateObjectiveId in definition.DuplicateObjectiveIds)
            {
                diagnostics.Add(new MissionValidationDiagnostic(
                    "duplicate-objective-id",
                    $"objective {duplicateObjectiveId} is defined more than once in the selected revision.",
                    definition.MissionId,
                    definition.ContentRevision,
                    duplicateObjectiveId));
            }

            foreach (var objective in definition.Objectives.Values.OrderBy(objective => objective.ObjectiveId))
            {
                if (!objective.ClientNameTextId.HasValue || !objective.ClientBodyTextId.HasValue)
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "missing-client-text",
                        "objective text bindings are incomplete; populate client_name_text_id and client_body_text_id.",
                        definition.MissionId,
                        definition.ContentRevision,
                        objective.ObjectiveId));
                }
            }

            ValidateTransitions(definition, references, diagnostics);
            ValidateReferences(snapshot, definition, references, diagnostics);
            ValidateObjectiveGraph(definition, diagnostics);
            ValidateRewards(definition, references, diagnostics);
            ValidateAreas(definition, references, diagnostics);
            ValidateSpawnGroups(definition, references, diagnostics);
            ValidateScenarios(definition, diagnostics);
        }

        private static void ValidateTransitions(
            MissionContentDefinition definition,
            MissionContentReferenceSet references,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            foreach (var transition in definition.Transitions.Values.OrderBy(transition => transition.TransitionId))
            {
                foreach (var trigger in transition.Triggers)
                {
                    if (!trigger.HasDefinedKind())
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "unsupported-trigger",
                            $"trigger kind {(int)trigger.Kind} is unknown; use a supported discriminator.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            trigger.TriggerId));
                        continue;
                    }

                    if (trigger.Kind == MissionTriggerKind.Conversation &&
                        (!trigger.NpcPackageId.HasValue ||
                            !references.NpcPackageIds.Contains(trigger.NpcPackageId.Value)))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "missing-npc-package",
                            $"conversation trigger references missing npc_package {trigger.NpcPackageId?.ToString() ?? "null"}.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            trigger.TriggerId));
                    }

                    if (trigger.Kind == MissionTriggerKind.AreaEntered &&
                        (!trigger.AreaId.HasValue ||
                            !definition.Areas.ContainsKey(trigger.AreaId.Value)))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "missing-area",
                            $"area trigger references missing mission_area {trigger.AreaId?.ToString() ?? "null"}.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            trigger.TriggerId));
                    }

                    if (trigger.Kind == MissionTriggerKind.TimerElapsed &&
                        (!trigger.DurationSeconds.HasValue || trigger.DurationSeconds.Value == 0))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "invalid-delay",
                            "timer trigger must declare a positive duration_seconds value.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            trigger.TriggerId));
                    }
                }

                foreach (var action in transition.Actions)
                {
                    if (!action.HasDefinedKind())
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "unsupported-action",
                            $"action kind {(int)action.Kind} is unknown; use a supported discriminator.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            actionId: action.ActionId));
                        continue;
                    }

                    if (action.Kind is MissionActionKind.RevealObjective or MissionActionKind.ActivateObjective or MissionActionKind.CompleteObjective)
                    {
                        if (!action.TargetObjectiveId.HasValue ||
                            !definition.Objectives.ContainsKey(action.TargetObjectiveId.Value))
                        {
                            diagnostics.Add(new MissionValidationDiagnostic(
                                "missing-target",
                                $"action references missing objective {action.TargetObjectiveId?.ToString() ?? "null"}.",
                                definition.MissionId,
                                definition.ContentRevision,
                                transition.ObjectiveId,
                                transition.TransitionId,
                                actionId: action.ActionId));
                        }
                    }

                    if (action.Kind == MissionActionKind.GrantReward &&
                        (!action.RewardId.HasValue || !definition.Rewards.ContainsKey(action.RewardId.Value)))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "missing-reward",
                            $"grant reward action references missing reward {action.RewardId?.ToString() ?? "null"}.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            actionId: action.ActionId));
                    }

                    if (action.Kind == MissionActionKind.ActivateSpawnGroup &&
                        (!action.SpawnGroupId.HasValue || !definition.SpawnGroups.ContainsKey(action.SpawnGroupId.Value)))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "missing-spawn-group",
                            $"activate spawn group action references missing spawn group {action.SpawnGroupId?.ToString() ?? "null"}.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            actionId: action.ActionId));
                    }

                    if (action.Kind == MissionActionKind.StartScenario &&
                        (!action.ScenarioId.HasValue || !definition.Scenarios.ContainsKey(action.ScenarioId.Value)))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "missing-scenario",
                            $"start scenario action references missing scenario {action.ScenarioId?.ToString() ?? "null"}.",
                            definition.MissionId,
                            definition.ContentRevision,
                            transition.ObjectiveId,
                            transition.TransitionId,
                            actionId: action.ActionId));
                    }
                }
            }
        }

        private static void ValidateReferences(
            MissionContentSnapshot snapshot,
            MissionContentDefinition definition,
            MissionContentReferenceSet references,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            foreach (var prerequisite in definition.Prerequisites)
            {
                if (prerequisite.RequiredMissionId.HasValue &&
                    snapshot.Definitions.TryGetValue(prerequisite.RequiredMissionId.Value, out var target) &&
                    !string.Equals(target.ContentRevision, definition.ContentRevision, StringComparison.Ordinal))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "cross-revision-reference",
                        $"prerequisite mission {prerequisite.RequiredMissionId.Value} resolves to content revision {target.ContentRevision}; keep required chains on one revision.",
                        definition.MissionId,
                        definition.ContentRevision));
                }
            }

            foreach (var objective in definition.Objectives.Values)
            {
                var rule = objective.ProgressRule;
                if (rule != null &&
                    rule.Kind == MissionProgressEventKind.MissionCompleted)
                {
                    var targetMissionId = rule.Subjects.Single();
                    if (snapshot.Definitions.TryGetValue(targetMissionId, out var target) &&
                        !string.Equals(target.ContentRevision, definition.ContentRevision, StringComparison.Ordinal))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "cross-revision-reference",
                            $"mission completed progress rule references mission {targetMissionId} on revision {target.ContentRevision}; keep authored mission references on one revision.",
                            definition.MissionId,
                            definition.ContentRevision,
                            objective.ObjectiveId));
                    }
                }
            }
        }

        private static void ValidateObjectiveGraph(
            MissionContentDefinition definition,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            var revealEdges = definition.Objectives.Values
                .ToDictionary(
                    objective => objective.ObjectiveId,
                    objective => objective.RevealedObjectiveIds.ToArray());
            var activateEdges = definition.Objectives.Values
                .ToDictionary(
                    objective => objective.ObjectiveId,
                    objective => objective.ActivatedObjectiveIds.ToArray());

            var initialObjectives = definition.Objectives.Values
                .Where(objective => objective.InitialState == MissionObjectiveState.Incomplete)
                .Select(objective => objective.ObjectiveId)
                .ToHashSet();
            if (definition.Objectives.Count > 0 && initialObjectives.Count == 0)
            {
                diagnostics.Add(new MissionValidationDiagnostic(
                    "invalid-objective-graph",
                    "mission has no initially incomplete objective; author one entry point.",
                    definition.MissionId,
                    definition.ContentRevision));
            }

            foreach (var objective in definition.Objectives.Values.OrderBy(objective => objective.ObjectiveId))
            {
                if (objective.InitialState == MissionObjectiveState.Inactive &&
                    activateEdges.Values.SelectMany(ids => ids).Contains(objective.ObjectiveId) &&
                    !revealEdges.Values.SelectMany(ids => ids).Contains(objective.ObjectiveId))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "invalid-objective-graph",
                        "inactive objectives must be revealed before they are activated.",
                        definition.MissionId,
                        definition.ContentRevision,
                        objective.ObjectiveId));
                }
            }

            var graph = definition.Objectives.Values.ToDictionary(
                objective => objective.ObjectiveId,
                objective => objective.RevealedObjectiveIds.Concat(objective.ActivatedObjectiveIds).Distinct().ToArray());
            var visiting = new HashSet<uint>();
            var visited = new HashSet<uint>();
            foreach (var node in graph.Keys.OrderBy(value => value))
            {
                if (DetectCycle(node, graph, visiting, visited))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "transition-cycle",
                        "reveal/activate actions create a cycle; break the successor loop.",
                        definition.MissionId,
                        definition.ContentRevision,
                        node));
                    break;
                }
            }
        }

        private static bool DetectCycle(
            uint node,
            IReadOnlyDictionary<uint, uint[]> graph,
            ISet<uint> visiting,
            ISet<uint> visited)
        {
            if (visited.Contains(node))
                return false;
            if (!visiting.Add(node))
                return true;
            if (graph.TryGetValue(node, out var edges))
            {
                foreach (var edge in edges)
                {
                    if (graph.ContainsKey(edge) && DetectCycle(edge, graph, visiting, visited))
                        return true;
                }
            }

            visiting.Remove(node);
            visited.Add(node);
            return false;
        }

        private static void ValidateRewards(
            MissionContentDefinition definition,
            MissionContentReferenceSet references,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            foreach (var reward in definition.Rewards.Values.OrderBy(reward => reward.RewardId))
            {
                foreach (var item in reward.FixedItems.Concat(reward.SelectableItems))
                {
                    if (!references.ItemTemplateClasses.TryGetValue(item.ItemTemplateId, out var entityClassId))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "missing-item-template",
                            $"reward item {item.ItemId} references missing item template {item.ItemTemplateId}.",
                            definition.MissionId,
                            definition.ContentRevision));
                        continue;
                    }

                    if (!references.EntityClassIds.Contains(entityClassId))
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "missing-entity-class",
                            $"item template {item.ItemTemplateId} resolves to missing entity class {entityClassId}.",
                            definition.MissionId,
                            definition.ContentRevision));
                    }

                    if (item.Quantity == 0)
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "invalid-quantity",
                            $"reward item {item.ItemId} must have a positive quantity.",
                            definition.MissionId,
                            definition.ContentRevision));
                    }
                }

                var hasSelectable = reward.SelectableItems.Count > 0;
                if ((reward.SelectionCount == 0 && hasSelectable) ||
                    (reward.SelectionCount > 0 && !hasSelectable))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "invalid-reward-selection",
                        "reward selection_count must match the presence of selectable reward items.",
                        definition.MissionId,
                        definition.ContentRevision));
                }
            }
        }

        private static void ValidateAreas(
            MissionContentDefinition definition,
            MissionContentReferenceSet references,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            foreach (var area in definition.Areas.Values.OrderBy(area => area.AreaId))
            {
                if (!references.MapContextIds.Contains(area.MapContextId))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "missing-map-context",
                        $"mission area {area.AreaId} references missing map_context {area.MapContextId}.",
                        definition.MissionId,
                        definition.ContentRevision));
                }

                if (area.Shape == MissionAreaShape.Sphere &&
                    (!area.Radius.HasValue || area.Radius.Value <= 0))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "invalid-radius",
                        $"mission area {area.AreaId} must have a positive radius.",
                        definition.MissionId,
                        definition.ContentRevision));
                }
            }

            foreach (var objective in definition.Objectives.Values.OrderBy(objective => objective.ObjectiveId))
            {
                foreach (var indicator in objective.Indicators.Where(indicator => indicator.Radius <= 0))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "invalid-radius",
                        $"indicator {indicator.IndicatorId} must have a positive radius.",
                        definition.MissionId,
                        definition.ContentRevision,
                        objective.ObjectiveId));
                }
            }
        }

        private static void ValidateSpawnGroups(
            MissionContentDefinition definition,
            MissionContentReferenceSet references,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            foreach (var spawnGroup in definition.SpawnGroups.Values.OrderBy(group => group.SpawnGroupId))
            {
                if (!references.MapContextIds.Contains(spawnGroup.MapContextId))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "missing-map-context",
                        $"spawn group {spawnGroup.SpawnGroupId} references missing map_context {spawnGroup.MapContextId}.",
                        definition.MissionId,
                        definition.ContentRevision));
                }

                if (spawnGroup.AreaId.HasValue &&
                    !definition.Areas.ContainsKey(spawnGroup.AreaId.Value))
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "missing-area",
                        $"spawn group {spawnGroup.SpawnGroupId} references missing mission_area {spawnGroup.AreaId.Value}.",
                        definition.MissionId,
                        definition.ContentRevision));
                }

                if (spawnGroup.RespawnSeconds.HasValue &&
                    spawnGroup.RespawnSeconds.Value == 0)
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "invalid-delay",
                        $"spawn group {spawnGroup.SpawnGroupId} must use a positive respawn_seconds value when present.",
                        definition.MissionId,
                        definition.ContentRevision));
                }

                foreach (var spawn in spawnGroup.Spawns)
                {
                    if (spawn.Quantity == 0)
                    {
                        diagnostics.Add(new MissionValidationDiagnostic(
                            "invalid-quantity",
                            $"spawn {spawn.SpawnId} must have a positive quantity.",
                            definition.MissionId,
                            definition.ContentRevision));
                    }

                    if (references.CreatureClasses.TryGetValue(spawn.CreatureId, out var creatureClassId))
                    {
                        if (!references.EntityClassIds.Contains(creatureClassId))
                        {
                            diagnostics.Add(new MissionValidationDiagnostic(
                                "missing-entity-class",
                                $"spawn creature {spawn.CreatureId} resolves to missing entity class {creatureClassId}.",
                                definition.MissionId,
                                definition.ContentRevision));
                        }
                    }
                }
            }
        }

        private static void ValidateScenarios(
            MissionContentDefinition definition,
            ICollection<MissionValidationDiagnostic> diagnostics)
        {
            foreach (var scenario in definition.Scenarios.Values)
            {
                if (scenario.Steps.Count == 0)
                {
                    diagnostics.Add(new MissionValidationDiagnostic(
                        "missing-scenario",
                        $"scenario {scenario.ScenarioId} has no steps; author at least one step.",
                        definition.MissionId,
                        definition.ContentRevision));
                }
            }
        }

        private sealed class MissionContentReferenceSet
        {
            public HashSet<uint> NpcPackageIds { get; }
            public IReadOnlyDictionary<uint, uint> ItemTemplateClasses { get; }
            public HashSet<uint> EntityClassIds { get; }
            public IReadOnlyDictionary<uint, uint> CreatureClasses { get; }
            public HashSet<uint> MapContextIds { get; }

            public MissionContentReferenceSet(
                IEnumerable<uint> npcPackageIds,
                IReadOnlyDictionary<uint, uint> itemTemplateClasses,
                IEnumerable<uint> entityClassIds,
                IReadOnlyDictionary<uint, uint> creatureClasses,
                IEnumerable<uint> mapContextIds)
            {
                NpcPackageIds = new HashSet<uint>(npcPackageIds ?? Array.Empty<uint>());
                ItemTemplateClasses = itemTemplateClasses ??
                    new Dictionary<uint, uint>();
                EntityClassIds = new HashSet<uint>(entityClassIds ?? Array.Empty<uint>());
                CreatureClasses = creatureClasses ?? new Dictionary<uint, uint>();
                MapContextIds = new HashSet<uint>(mapContextIds ?? Array.Empty<uint>());
            }
        }
    }
}
