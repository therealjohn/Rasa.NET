using System;
using System.Collections.Generic;
using System.Linq;
using Rasa.Data;
using Rasa.Game.Missions.Content;
using Rasa.Missions.Scenes;
using Rasa.Missions.Definitions;
using Rasa.Structures.World;

namespace Rasa.MissionTool
{
    internal static class BootcampPackMigration
    {
        internal static MissionPackDocument Upgrade(IReadOnlyList<MissionPackDocument> packs)
        {
            var scripts = new Dictionary<uint, string>
            {
                [1990] = "bootcamp.initiation", [1992] = "bootcamp.gearing-up",
                [1994] = "bootcamp.capture-the-flag", [1995] = "bootcamp.reinforcements",
                [2005] = "bootcamp.bomb-retry"
            };
            foreach (var pack in packs)
            {
                pack.Scene = new MissionSceneDocument { Script = scripts[pack.Definition.MissionId] };
                if (pack.Definition.MissionId is 1995 or 2005)
                    pack.Scene.Requirement = new Rasa.Missions.Runtime.NotRequirement(
                        new Rasa.Missions.Runtime.CustomRequirement("character.starting-experience-completed"));
                foreach (var spawn in pack.Spawns)
                    for (var number = 0; number < spawn.Quantity; number++)
                    {
                        var role = SpawnRole(spawn, number);
                        var forean = pack.Definition.MissionId == 1994 && spawn.SpawnGroupId == 1;
                        var shared = forean ? $"bootcamp-forean-{spawn.SpawnId}-{number}" :
                            pack.Definition.MissionId == 1994 && spawn.CreatureId == 510207 ? "bootcamp-youngblood" : null;
                        pack.Scene.Actors.Add(role, new SceneActorDefinition(role, SceneActorKind.Creature,
                            spawn.CreatureId, new ScenePosition((float)spawn.PosX, (float)spawn.PosY, (float)spawn.PosZ),
                            spawn.Rotation, SharedKey: shared, MissionId: pack.Definition.MissionId,
                            GroupId: spawn.SpawnGroupId, SpawnId: spawn.SpawnId,
                            FollowOffset: forean ? new ScenePosition(((int)spawn.SpawnId - 2) * 1.5f, 0, -2) : null));
                    }
                foreach (var step in pack.ScenarioSteps.Where(step => step.Kind == MissionScenarioStepKind.SpawnDynamicObject))
                {
                    var crate = step.EntityClassId == 29877;
                    pack.Scene.Actors[step.DynamicObjectKey] = new SceneActorDefinition(step.DynamicObjectKey,
                        SceneActorKind.Object, step.EntityClassId.Value,
                        new ScenePosition((float)step.PosX.Value, (float)step.PosY.Value, (float)step.PosZ.Value),
                        step.Orientation.Value, step.InitialInteractionEnabled ?? true,
                        (uint)(crate || step.EntityClassId == 24990 ? UseObjectState.TdStateClosed :
                            step.EntityClassId == 24586 ? UseObjectState.DoorStateClosed : UseObjectState.IdStateActive),
                        crate ? 1992U : null, crate ? 58U : null, crate ? 1U : null,
                        SharedKey: step.DynamicObjectKey, WindupMilliseconds: step.DelayMilliseconds);
                }
                foreach (var scenario in pack.Scenarios)
                    pack.Scene.Names[scenario.Name.Substring(scenario.Name.LastIndexOf('-') + 1)] = scenario.ScenarioId;
            }
            var gearing = packs.Single(pack => pack.Definition.MissionId == 1992);
            var capture = packs.Single(pack => pack.Definition.MissionId == 1994);
            var reinforcement = packs.Single(pack => pack.Definition.MissionId == 1995);
            var retry = packs.Single(pack => pack.Definition.MissionId == 2005);
            retry.Scene.Actors["bootcamp-dropship-debris"] = reinforcement.Scene.Actors["bootcamp-dropship-debris"];
            var positions = new[] { new ScenePosition(386, 120, 184.7f), new ScenePosition(380, 120, 186), new ScenePosition(375, 120, 186) };
            for (var index = 0; index < positions.Length; index++)
            {
                var role = $"practice-{index}";
                gearing.Scene.Actors[role] = new SceneActorDefinition(role, SceneActorKind.PracticeTarget, 29365,
                    positions[index], InitialObjectState: (uint)UseObjectState.StateNull, SharedKey: $"bootcamp-{role}");
            }
            foreach (var pack in packs)
            {
                foreach (var scenario in pack.Scenarios)
                {
                    var sequence = new SceneSequenceDocument();
                    pack.Scene.Sequences.Add(scenario.ScenarioId, sequence);
                    foreach (var step in pack.ScenarioSteps.Where(step => step.ScenarioId == scenario.ScenarioId).OrderBy(step => step.Sequence))
                        Translate(pack, step, sequence);
                }
            }
            retry.Scene.Sequences[0] = new SceneSequenceDocument
            {
                World = new()
                {
                    new EnsureActorIntent("retry-wreck", "bootcamp-dropship-debris"),
                    new SetInteractionIntent("retry-enable-wreck", "bootcamp-dropship-debris", true)
                }
            };
            var experience = new MissionExperienceDocument
            {
                Key = "bootcamp", Revision = "deployment_11", MapContextId = 1985, PrivatePerCharacter = true,
                Scene = new MissionSceneDocument { Script = "bootcamp.experience" },
                MissionTriggers = new()
                {
                    new(1992, "Accepted", 1), new(1994, "Accepted", 2), new(1994, "Rewarded", 3)
                }
            };
            var initial = new SceneSequenceDocument();
            experience.ActorPolicies[510207] = new ActorGameplayPolicy
            { Invulnerable = true, DefenseRadius = 18, DefenseTargetTag = "bootcamp-thrax" };
            foreach (var creatureId in new uint[] { 510210, 510216, 510221, 510222, 510223, 510224, 510225, 510226 })
                experience.ActorPolicies[creatureId] = new ActorGameplayPolicy
                {
                    Tags = new[] { "bootcamp-thrax" }, RewardScenarioKills = true, TrackParticipation = true,
                    Loot = new AuthoredLootProfile(new[]
                    {
                        new LootDrop(41666, 100, 1, 1), new LootDrop(28, 55, 12, 24),
                        new LootDrop(56, 30, 8, 16), new LootDrop(44917, 15, 1, 1), new LootDrop(41665, 25, 1, 1)
                    })
                };
            experience.Scene.Sequences[0] = initial;
            var crateRole = gearing.Scene.Actors["bootcamp-equipment-crate"] with { InitiallyInteractable = false };
            experience.Scene.Actors.Add(crateRole.Role, crateRole);
            initial.World.Add(new EnsureActorIntent("stage-equipment-crate", crateRole.Role));
            foreach (var target in gearing.Scene.Actors.Values.Where(actor => actor.Kind == SceneActorKind.PracticeTarget))
            {
                experience.Scene.Actors.Add(target.Role, target);
                initial.World.Add(new EnsureActorIntent("stage-" + target.Role, target.Role));
            }
            foreach (var actor in capture.Scene.Actors.Values.Where(actor => actor.GroupId == 1))
            {
                experience.Scene.Actors.Add(actor.Role, actor);
                initial.World.Add(new EnsureActorIntent("stage-" + actor.Role, actor.Role));
            }
            foreach (var actor in packs.SelectMany(pack => pack.Scene.Actors.Values)
                .Where(actor => actor.SharedKey != null))
                experience.Scene.Actors.TryAdd(actor.Role, actor);
            experience.Scene.Actors.Add("mcallister", new SceneActorDefinition("mcallister", SceneActorKind.PublicSpawn, 510203));
            experience.Scene.Routes.Add("mcallister-departure",
                new SceneRoute("mcallister-departure", new[] { new SceneWaypoint(new ScenePosition(400, 120, 150), 2.175) },
                    ResumeAtDestination: true));
            experience.Scene.Sequences[1] = new SceneSequenceDocument
            {
                World = new()
                {
                    new EnsureActorIntent("ensure-mcallister", "mcallister"),
                    new RunRouteIntent("mcallister-departure", "mcallister", "mcallister-departure")
                }
            };
            experience.Scene.Sequences[2] = new SceneSequenceDocument();
            experience.Scene.Sequences[3] = new SceneSequenceDocument();
            foreach (var actor in capture.Scene.Actors.Values.Where(actor => actor.GroupId == 1))
            {
                experience.Scene.Sequences[2].World.Add(new EnsureActorIntent("escort-ensure-" + actor.Role, actor.Role));
                experience.Scene.Sequences[2].World.Add(new FollowActorIntent("escort-start-" + actor.Role, actor.Role, 0));
                experience.Scene.Sequences[3].World.Add(new FollowActorIntent("escort-stop-" + actor.Role, actor.Role, 0, false));
            }
            return new MissionPackDocument
            { Definition = null, Release = packs[0].Release, Enabled = true, Experience = experience };
        }

        private static string SpawnRole(MissionSpawnEntry spawn, int number) =>
            $"group-{spawn.SpawnGroupId}-spawn-{spawn.SpawnId}-{number}";
        private static string Key(MissionScenarioStepEntry step) => $"sequence-{step.ScenarioId}-step-{step.StepId}";

        private static void Translate(MissionPackDocument pack, MissionScenarioStepEntry step, SceneSequenceDocument sequence)
        {
            var id = pack.Definition.MissionId;
            var key = Key(step);
            var group = pack.Scene.Actors.Values.Where(actor => actor.GroupId == step.SpawnGroupId).ToArray();
            switch (step.Kind)
            {
                case MissionScenarioStepKind.SpawnGroup:
                    sequence.World.AddRange(group.Select(actor => new EnsureActorIntent(key + "-" + actor.Role, actor.Role)));
                    break;
                case MissionScenarioStepKind.DespawnGroup:
                    sequence.World.AddRange(group.Select(actor => new RemoveActorIntent(key + "-" + actor.Role, actor.Role)));
                    break;
                case MissionScenarioStepKind.EscortSpawnGroup:
                    sequence.World.AddRange(group.Select(actor => new FollowActorIntent(key + "-" + actor.Role, actor.Role, 0)));
                    break;
                case MissionScenarioStepKind.SpawnDynamicObject:
                    sequence.World.Add(new EnsureActorIntent(key, step.DynamicObjectKey));
                    sequence.World.Add(new SetInteractionIntent(key + "-availability", step.DynamicObjectKey, step.InitialInteractionEnabled ?? true));
                    break;
                case MissionScenarioStepKind.DespawnDynamicObject:
                    sequence.World.Add(new RemoveActorIntent(key, step.DynamicObjectKey));
                    break;
                case MissionScenarioStepKind.EnableInteraction:
                case MissionScenarioStepKind.DisableInteraction:
                    var targets = pack.Scene.Actors.Values.Where(actor => step.EntityClassId.HasValue
                        ? actor.TemplateId == step.EntityClassId &&
                            actor.Kind is SceneActorKind.Object or SceneActorKind.PracticeTarget
                        : actor.GroupId == step.SpawnGroupId && actor.SpawnId == step.SpawnId).ToArray();
                    if (targets.Length == 0)
                        throw new InvalidOperationException($"Mission {id} step {key} has no exact actor binding.");
                    foreach (var actor in targets)
                    {
                        sequence.World.Add(new SetInteractionIntent(key + "-" + actor.Role, actor.Role,
                            step.Kind == MissionScenarioStepKind.EnableInteraction, IfPresent: true));
                    }
                    break;
                case MissionScenarioStepKind.GrantRewardPackage:
                    sequence.Character.Add(new GrantRewardIntent(key, id, step.RewardId.Value));
                    break;
                case MissionScenarioStepKind.GrantSkillAbility:
                    sequence.Character.Add(new GrantAbilityIntent(key, step.SkillId.Value, step.AbilityId.Value, step.SkillLevel.Value, step.AbilitySlot));
                    break;
                case MissionScenarioStepKind.PlayTutorial:
                    sequence.World.Add(new PresentationIntent(key, PresentationKind.Tutorial, step.TutorialId.Value));
                    sequence.World.Add(new PresentationIntent(key + "-audio", PresentationKind.Audio, step.AudioSetId.GetValueOrDefault()));
                    break;
                case MissionScenarioStepKind.RevealObjective:
                case MissionScenarioStepKind.ActivateObjective:
                case MissionScenarioStepKind.CompleteObjective:
                case MissionScenarioStepKind.FailObjective:
                    sequence.Character.Add(new ObjectiveIntent(key, id, step.TargetObjectiveId.Value, step.Kind switch
                    {
                        MissionScenarioStepKind.RevealObjective => MissionObjectiveState.NotAssigned,
                        MissionScenarioStepKind.ActivateObjective => MissionObjectiveState.Incomplete,
                        MissionScenarioStepKind.CompleteObjective => MissionObjectiveState.Completed,
                        _ => MissionObjectiveState.Failed
                    }));
                    break;
                case MissionScenarioStepKind.StartDeadline:
                case MissionScenarioStepKind.CancelDeadline:
                case MissionScenarioStepKind.SatisfyDeadline:
                    sequence.Character.Add(new MissionDeadlineIntent(key, id, step.Kind switch
                    {
                        MissionScenarioStepKind.StartDeadline => DeadlineIntentKind.Start,
                        MissionScenarioStepKind.CancelDeadline => DeadlineIntentKind.Cancel,
                        _ => DeadlineIntentKind.Satisfy
                    }, step.DelayMilliseconds.GetValueOrDefault()));
                    break;
                case MissionScenarioStepKind.ScheduleScenario:
                    sequence.Timers.Add(new SequenceTimer(key, step.DelayMilliseconds.Value, step.TargetScenarioId.Value));
                    break;
                case MissionScenarioStepKind.ResetAttempt:
                    foreach (var scheduled in pack.ScenarioSteps.Where(candidate =>
                        candidate.Kind == MissionScenarioStepKind.ScheduleScenario &&
                        (candidate.TargetScenarioId == step.TargetScenarioId || candidate.ScenarioId == step.TargetScenarioId)))
                        sequence.Timers.Add(new SequenceTimer(Key(scheduled), 0, scheduled.TargetScenarioId.Value, Cancel: true));
                    foreach (var spawn in pack.ScenarioSteps.Where(candidate =>
                        candidate.ScenarioId == step.TargetScenarioId && candidate.Kind == MissionScenarioStepKind.SpawnGroup))
                        sequence.World.AddRange(pack.Scene.Actors.Values.Where(actor => actor.GroupId == spawn.SpawnGroupId)
                            .Select(actor => new RemoveActorIntent(key + "-" + actor.Role, actor.Role)));
                    break;
                case MissionScenarioStepKind.EmitScenarioEvent:
                    sequence.Signals.Add(new SceneMissionSignal(id, step.ScenarioId, step.ScenarioEventId.Value));
                    break;
                case MissionScenarioStepKind.TransferPlayer:
                    sequence.World.Add(new TransferIntent(key, step.MapContextId.Value,
                        new ScenePosition((float)step.PosX.Value, (float)step.PosY.Value, (float)step.PosZ.Value), step.Orientation.Value));
                    break;
                case MissionScenarioStepKind.SetQualification:
                    sequence.Character.Add(new SetQualificationIntent(key, (byte)step.QualificationKey.Value, step.QualificationValue == 1));
                    break;
                case MissionScenarioStepKind.SetAccountSkipEntitlement:
                    sequence.Character.Add(new SetEntitlementIntent(key, step.AccountSkipEntitlement.Value));
                    break;
                default:
                    throw new InvalidOperationException($"Bootcamp migration does not support {step.Kind}.");
            }
            sequence.Timers = sequence.Timers.GroupBy(timer => timer.Name).Select(grouping => grouping.Last()).ToList();
        }
    }
}
