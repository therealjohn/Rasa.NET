using System;
using System.Collections.Generic;
using System.Linq;
using Rasa.Missions.Content;
using Rasa.Missions.Runtime;
using Rasa.Missions.Scenes;

namespace Rasa.Game.Missions.Content
{
    internal static class MissionSceneValidation
    {
        internal static void Validate(uint missionId, string revision, MissionSceneDefinition scene, IEnumerable<uint> objectives)
        {
            var objectiveIds = objectives.ToHashSet();
            new Integration.MissionRequirementService().Validate(missionId, objectiveIds,
                scene.Requirement, scene.TurnInRequirement, scene.ObjectiveRequirements);
            var scripts = new SceneScriptRegistry();
            if (scene.Script != null && !scripts.TryResolve(scene.Script, scene.StateVersion, out _))
                throw new MissionRuleException($"Mission {missionId}: migrated script {scene.Script}/{scene.StateVersion} is unavailable in this server.");
            foreach (var actor in scene.Actors)
                if (actor.Key != actor.Value.Role || actor.Value.TemplateId == 0 ||
                    actor.Value.Kind != SceneActorKind.PublicSpawn && actor.Value.Position == null ||
                    !double.IsFinite(actor.Value.Orientation) ||
                    actor.Value.Position is { } position &&
                        (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)))
                    throw new MissionRuleException($"Mission {missionId}: invalid actor role {actor.Key}.");
            if (scene.PublicEncounter is { } encounter &&
                (encounter.MissionId != missionId || encounter.ScriptKey != scene.Script ||
                 !scene.Actors.TryGetValue(encounter.Role, out var publicActor) ||
                 publicActor.Kind != SceneActorKind.PublicSpawn || publicActor.TemplateId != encounter.SpawnId ||
                 encounter.OwnerLossPolicy is not ("Reset" or "Wait" or "Continue")))
                throw new MissionRuleException($"Mission {missionId}: public encounter does not match its actor/script.");
            foreach (var route in scene.Routes)
                if (route.Key != route.Value.Key || route.Value.Points.Count == 0 ||
                    !float.IsFinite(route.Value.Speed) || route.Value.Speed <= 0 ||
                    route.Value.Points.Any(point => point.Position == null ||
                        !float.IsFinite(point.Position.X) || !float.IsFinite(point.Position.Y) ||
                        !float.IsFinite(point.Position.Z) || !double.IsFinite(point.Orientation)))
                    throw new MissionRuleException($"Mission {missionId}: invalid route {route.Key}.");
            if (scene.Credit.Keys.Any(id => !objectiveIds.Contains(id)))
                throw new MissionRuleException($"Mission {missionId}: credit policy names an unknown objective.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var runtime = new SceneRuntime(scripts);
            foreach (var sequence in scene.Sequences)
            {
                var run = new SceneRun("validation", revision, "data.sequence", 1, 1, 0, "{}", SceneStatus.Running, 1, missionId);
                var result = runtime.Evaluate(run, scene.Bindings(revision),
                    new SceneObservation(SceneEventKind.Signal, 1, SequenceId: sequence.Key), DateTime.UnixEpoch);
                if (!result.Accepted)
                    throw new MissionRuleException($"Mission {missionId}, sequence {sequence.Key}: {result.Rejection}");
                foreach (var key in sequence.Value.World.Select(intent => intent.OperationKey)
                    .Concat(sequence.Value.Character.Select(intent => intent.OperationKey)))
                    if (!keys.Add(key))
                        throw new MissionRuleException($"Mission {missionId}: duplicate operation {key}.");
                foreach (var timer in sequence.Value.Timers)
                    if (!timer.Cancel && !scene.Sequences.ContainsKey(timer.SequenceId))
                        throw new MissionRuleException($"Mission {missionId}: timer {timer.Name} has no target sequence.");
            }
        }
    }
}
