using System;
using System.Collections.Generic;
using System.Linq;
using Rasa.Missions.Runtime;
using Rasa.Missions.Scenes;

namespace Rasa.Game.Missions.Content
{
    public static class MissionPackValidation
    {
        public static List<string> ValidateBindings(IReadOnlyList<MissionPackDocument> packs, ClientBindingManifest client)
        {
            var errors = new List<string>();
            var ids = new HashSet<uint>();
            var scripts = new SceneScriptRegistry();
            var runtime = new SceneRuntime(scripts);
            var requirements = new Integration.MissionRequirementService();
            foreach (var pack in packs)
            {
                var experience = pack.Experience;
                var id = experience == null ? pack.Definition.MissionId : 0;
                var revision = experience?.Revision ?? pack.Definition.ContentRevision;
                if (pack.SchemaVersion != 1 || string.IsNullOrWhiteSpace(pack.Release) || pack.Release.Length > 32 ||
                    string.IsNullOrWhiteSpace(revision) || revision.Length > 32)
                    errors.Add($"Mission {id}: invalid schema/release/revision.");
                if (!ids.Add(id))
                    errors.Add($"Mission {id}: duplicate release binding.");
                if (pack.Enabled && pack.Synthetic)
                    errors.Add($"Mission {id}: synthetic examples cannot be activated.");
                if (experience != null && (string.IsNullOrWhiteSpace(experience.Key) ||
                    !experience.PrivatePerCharacter || experience.MapContextId == 0))
                    errors.Add("Starting experiences require an explicit key and per-character private map.");
                if (pack.Enabled && experience == null)
                {
                    if (!client.Missions.TryGetValue(id, out var known) || known.NameTextId != pack.Definition.ClientNameTextId ||
                        pack.Objectives.Any(objective => !known.Objectives.TryGetValue(objective.ObjectiveId, out var binding) ||
                            binding.NameTextId != objective.ClientNameTextId || binding.BodyTextId != objective.ClientBodyTextId))
                        errors.Add($"Mission {id}: unknown or changed native-client mission/objective/text binding.");
                    if (pack.Evidence.Count == 0)
                        errors.Add($"Mission {id}: active content requires source evidence.");
                }
                foreach (var row in pack.Rows())
                {
                    var type = row.GetType();
                    if ((uint)type.GetProperty("MissionId").GetValue(row) != id ||
                        (string)type.GetProperty("ContentRevision").GetValue(row) != revision)
                        errors.Add($"Mission {id}: {type.Name} escapes its mission/revision.");
                }
                var scene = experience?.Scene ?? pack.Scene;
                if (scene == null)
                    continue;
                try
                {
                    requirements.Validate(id, pack.Objectives.Select(objective => objective.ObjectiveId),
                        scene.Requirement, scene.TurnInRequirement, scene.ObjectiveRequirements);
                }
                catch (MissionRuleException error)
                {
                    errors.Add($"Mission {id}: invalid requirement binding: {error.Message}");
                }
                if (scene.Recovery is not ("RestoreCheckpoint" or "RestartAttempt" or "Fail"))
                    errors.Add($"Mission {id}: missing/unsupported recovery policy.");
                if (scene.Script != null && !scripts.TryResolve(scene.Script, scene.StateVersion, out _))
                    errors.Add($"Mission {id}: missing script {scene.Script}/{scene.StateVersion}.");
                foreach (var actor in scene.Actors)
                    if (actor.Key != actor.Value.Role || actor.Value.TemplateId == 0 ||
                        actor.Value.Kind != SceneActorKind.PublicSpawn && actor.Value.Position == null)
                        errors.Add($"Mission {id}: invalid actor role {actor.Key}.");
                foreach (var route in scene.Routes)
                    if (route.Key != route.Value.Key || route.Value.Points.Count == 0 ||
                        !float.IsFinite(route.Value.Speed) || route.Value.Speed <= 0 ||
                        route.Value.Points.Any(point => point.Position == null ||
                            !float.IsFinite(point.Position.X) || !float.IsFinite(point.Position.Y) ||
                            !float.IsFinite(point.Position.Z) || !double.IsFinite(point.Orientation)))
                        errors.Add($"Mission {id}: invalid route {route.Key}.");
                foreach (var rule in scene.Credit)
                {
                    if (!pack.Objectives.Any(objective => objective.ObjectiveId == rule.Key) ||
                        rule.Value.Mode != MissionCreditMode.Personal && pack.Triggers.Any(trigger =>
                            trigger.ObjectiveId == rule.Key && trigger.EventKind != (byte)Data.MissionProgressEventKind.CreatureKilled &&
                            trigger.EventKind != (byte)Data.MissionProgressEventKind.ScenarioEvent))
                        errors.Add($"Mission {id}: objective {rule.Key} cannot share personal actions.");
                }
                var operationKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var sequence in scene.Sequences)
                {
                    var run = new SceneRun("validation", revision, "data.sequence", 1,
                        1, 0, "{}", SceneStatus.Running, 1, id);
                    var result = runtime.Evaluate(run, scene.Bindings(run.Release),
                        new SceneObservation(SceneEventKind.Signal, 1, SequenceId: sequence.Key), DateTime.UnixEpoch);
                    if (!result.Accepted)
                        errors.Add($"Mission {id}, sequence {sequence.Key}: {result.Rejection}");
                    foreach (var key in sequence.Value.World.Select(intent => intent.OperationKey)
                        .Concat(sequence.Value.Character.Select(intent => intent.OperationKey)))
                        if (!operationKeys.Add(key))
                            errors.Add($"Mission {id}: duplicate operation binding {key}.");
                    foreach (var timer in sequence.Value.Timers)
                        if (!timer.Cancel && !scene.Sequences.ContainsKey(timer.SequenceId))
                            errors.Add($"Mission {id}: timer {timer.Name} has no target sequence {timer.SequenceId}.");
                }
            }
            if (packs.Select(pack => pack.Release).Distinct(StringComparer.Ordinal).Count() > 1)
                errors.Add("A publication must contain one complete, explicitly named release.");
            return errors;
        }
    }
}
