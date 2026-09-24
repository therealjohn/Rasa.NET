using System;
using System.Linq;

namespace Rasa.Game.Missions.Persistence
{
    using Data;
    using Managers;
    using global::Rasa.Missions.Scenes;
    using Repositories.Char;
    using Structures;
    using Structures.Char;
    using Structures.World;

    internal sealed class SceneCharacterAdapter
    {
        private readonly MissionApplication _missions;
        private readonly ManifestationManager _manifestations;
        internal SceneCharacterAdapter(MissionApplication missions, ManifestationManager manifestations)
        { _missions = missions; _manifestations = manifestations; }

        internal void Apply(Client client, SceneRun run, CharacterIntent intent,
            ICharUnitOfWork unit, MissionScenarioPlan publication)
        {
            if (client?.Player?.Id != run.OwnerCharacterId)
                throw new GameplayRejectionException($"Run {run.Id} has no authoritative character adapter.");
            switch (intent)
            {
                case GrantRewardIntent reward:
                    if (!_missions.GetRewardPackages(reward.MissionId).TryGetValue(reward.RewardId, out var definition))
                        throw new GameplayRejectionException($"Unknown reward package {reward.MissionId}:{reward.RewardId}.");
                    var character = unit.Characters.Get(client.Player.Id);
                    if (character.AccountId != client.AccountEntry?.Id ||
                        !_manifestations.ValidateProgressionForClient(client))
                        throw new GameplayRejectionException("Scene reward character ownership/progression changed.");
                    var grant = definition.CreateScenarioGrant(_missions.BeforeRewardItemPublication);
                    publication.AddRewardGrant(grant);
                    grant.PlanAndSave(client, character, unit, _manifestations);
                    publication.AddProgressPlan(_missions.PlanProgress(client, grant.CreateItemAcquisitionEvents(), unit));
                    break;
                case ObjectiveIntent objective:
                    var action = objective.State switch
                    {
                        MissionObjectiveState.NotAssigned => MissionScenarioStepKind.RevealObjective,
                        MissionObjectiveState.Incomplete => MissionScenarioStepKind.ActivateObjective,
                        MissionObjectiveState.Completed => MissionScenarioStepKind.CompleteObjective,
                        MissionObjectiveState.Failed => MissionScenarioStepKind.FailObjective,
                        _ => throw new GameplayRejectionException("Unsupported authored objective state.")
                    };
                    if (!_missions.TryPlanScenarioObjectiveAction(client, objective.MissionId, objective.ObjectiveId,
                        action, unit, publication))
                        throw new GameplayRejectionException($"Cannot apply scene objective {objective.MissionId}:{objective.ObjectiveId}.");
                    break;
                case GrantAbilityIntent ability:
                    var learned = unit.CharacterSkills.GetCharacterSkills(client.Player.Id)
                        .SingleOrDefault(skill => skill.SkillId == ability.SkillId);
                    if (learned != null && learned.SkillLevel >= ability.Level)
                        break;
                    unit.CharacterSkills.AddOrUpdate(client.Player.Id, ability.SkillId, (int)ability.AbilityId, ability.Level);
                    if (ability.Slot.HasValue)
                        unit.CharacterAbilityDrawers.AddOrUpdate(client.Player.Id, ability.Slot.Value,
                            (int)ability.AbilityId, ability.Level);
                    publication.AddRuntimeConvergence(() =>
                    {
                        client.Player.Skills[(SkillId)ability.SkillId] =
                            new SkillsData((SkillId)ability.SkillId, (int)ability.AbilityId, ability.Level);
                        if (ability.Slot.HasValue)
                            client.Player.Abilities[ability.Slot.Value] =
                                new AbilityDrawerData(ability.Slot.Value, (int)ability.AbilityId, ability.Level);
                    });
                    publication.AddPublication(() =>
                    {
                        client.CallMethod(client.Player.EntityId,
                            new Packets.MapChannel.Server.SkillsPacket(client.Player.Skills));
                        if (ability.Slot.HasValue)
                            client.CallMethod(client.Player.EntityId,
                                new Packets.MapChannel.Server.AbilityDrawerPacket(client.Player.Abilities));
                    });
                    break;
                case SetQualificationIntent qualification:
                    if (!Enum.IsDefined(typeof(CharacterQualificationKey), qualification.Qualification))
                        throw new GameplayRejectionException("Unknown scene qualification.");
                    var key = (CharacterQualificationKey)qualification.Qualification;
                    if (!qualification.Present)
                        unit.CharacterQualifications.Remove(client.Player.Id, key);
                    else if (!unit.CharacterQualifications.HasQualification(client.Player.Id, key))
                        unit.CharacterQualifications.Add(new CharacterQualificationEntry(client.Player.Id, key));
                    var startingExperienceCompleted = MissionRequirementFactsAdapter
                        .HasCompletedStartingExperience(unit, client.Player.Id);
                    publication.AddRuntimeConvergence(() =>
                        client.Player.StartingExperienceCompleted = startingExperienceCompleted);
                    break;
                case SetEntitlementIntent entitlement:
                    unit.GameAccounts.UpdateCanSkipBootcamp(client.AccountEntry.Id, entitlement.Enabled);
                    publication.AddRuntimeConvergence(client.ReloadGameAccountEntry);
                    break;
                case MissionDeadlineIntent deadline:
                    var existingDeadline = unit.CharacterMissionDeadlines.Get(client.Player.Id, deadline.MissionId);
                    if (deadline.Kind == DeadlineIntentKind.Start)
                        unit.CharacterMissionDeadlines.AddOrUpdate(client.Player.Id, deadline.MissionId,
                            DateTime.UtcNow.AddMilliseconds(deadline.Milliseconds), CharacterMissionDeadlineState.Active);
                    else if (existingDeadline?.State == CharacterMissionDeadlineState.Active)
                        unit.CharacterMissionDeadlines.SetState(client.Player.Id, deadline.MissionId,
                            deadline.Kind == DeadlineIntentKind.Satisfy
                                ? CharacterMissionDeadlineState.Satisfied : CharacterMissionDeadlineState.Cancelled);
                    if (deadline.Kind != DeadlineIntentKind.Start)
                        _missions.Scenes.EndDeadline(unit, run, deadline.MissionId);
                    var itemPublication = MissionInventory.Plan(client, unit, _missions, deadline.MissionId);
                    if (itemPublication != null)
                        publication.AddRuntimeConvergence(() => itemPublication(client));
                    publication.AddPublication(() => _missions.PublishMissionStatus(client, deadline.MissionId,
                        $"scene {run.Id} deadline {deadline.Kind}"));
                    break;
                default:
                    throw new GameplayRejectionException($"Unsupported character intent {intent.GetType().Name}.");
            }
        }
    }
}
