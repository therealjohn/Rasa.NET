using Rasa.Missions.Content;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Rasa.Managers
{
    using Game.Missions.Content;
    using Rasa.Missions.Runtime;
    using Repositories.UnitOfWork;
    using Repositories.World;
    using Structures;
    using Structures.Missions;
    using Structures.World;

    internal sealed class MissionContentCatalog
    {
        private readonly IGameUnitOfWorkFactory _factory;
        internal Dictionary<uint, Mission> Missions { get; }
        internal IReadOnlyDictionary<uint, Mission> View { get; }
        internal Dictionary<uint, MissionRewardDefinition> Rewards { get; }
        internal Dictionary<uint, IReadOnlyDictionary<uint, MissionRewardDefinition>> RewardPackages { get; } = new();
        internal Dictionary<uint, MissionAbandonmentPolicy> Abandonment { get; } = new();
        internal Dictionary<uint, IReadOnlyList<MissionPrerequisiteDefinition>> Prerequisites { get; } = new();
        internal Dictionary<uint, IReadOnlyDictionary<uint, MissionAreaDefinition>> Areas { get; } = new();
        internal Dictionary<uint, IReadOnlyDictionary<uint, MissionSpawnGroupDefinition>> SpawnGroups { get; } = new();
        internal Dictionary<uint, IReadOnlyDictionary<uint, MissionScenarioDefinition>> Scenarios { get; } = new();
        internal Dictionary<uint, MissionSceneDefinition> SceneBindings { get; } = new();
        internal List<MissionExperienceDefinition> Experiences { get; } = new();
        internal MissionRuntime Runtime { get; private set; }
        internal MissionValidationReport Report { get; private set; } =
            new(Array.Empty<MissionValidationDiagnostic>(), Array.Empty<uint>());

        internal MissionContentCatalog(IGameUnitOfWorkFactory factory, IReadOnlyDictionary<uint, Mission> definitions,
            IReadOnlyDictionary<uint, MissionRewardDefinition> rewards)
        {
            _factory = factory;
            Missions = definitions.ToDictionary(entry => entry.Key, entry => entry.Value);
            View = new ReadOnlyDictionary<uint, Mission>(Missions);
            Rewards = new Dictionary<uint, MissionRewardDefinition>(rewards ?? new Dictionary<uint, MissionRewardDefinition>());
            Runtime = new MissionRuntime(Missions.Values);
        }

        internal bool TryGetOperational(uint id, out Mission mission) =>
            Missions.TryGetValue(id, out mission) && mission.IsOperational;

        internal MissionValidationReport Load()
        {
            using var unit = _factory.CreateWorld();
            Missions.Clear(); Rewards.Clear(); RewardPackages.Clear(); Abandonment.Clear();
            Prerequisites.Clear(); Areas.Clear(); SpawnGroups.Clear(); Scenarios.Clear();
            SceneBindings.Clear(); Experiences.Clear();
            if (unit.MissionContent == null)
            {
                foreach (var row in unit.NpcMissions.Get())
                    Missions[row.Id] = new Mission(row.Id, row.Comment, null, row.GiverId, row.ReciverId,
                        row.Level, row.GroupType, row.CategoryId, row.Shareable, row.RadioCompleteable,
                        Array.Empty<MissionObjectiveDefinition>()).DisableOperational(
                            "legacy npc_mission rows stay inactive until an enabled, complete definition is installed by migrations");
                foreach (var recovered in MissionDefinitionCatalog.CreateRecoveredInactiveDefinitions())
                {
                    Missions.TryGetValue(recovered.Key, out var world);
                    Missions[recovered.Key] = recovered.Value.WithWorldMetadata(world);
                }
                Report = new MissionValidationReport(Array.Empty<MissionValidationDiagnostic>(), Array.Empty<uint>());
            }
            else
            {
                IReadOnlyDictionary<uint, string> selected = null;
                if (unit.MissionContent is IMigratedMissionContentRepository migrated)
                {
                    var enabled = migrated.GetEnabledDefinitions();
                    if (enabled.Count == 0)
                        throw new InvalidOperationException("No enabled mission definitions. Apply the required World data migrations before starting Game.");
                    if (enabled.GroupBy(entry => entry.MissionId).Any(group => group.Count() != 1))
                        throw new InvalidOperationException("Mission migrations enabled more than one definition for a mission.");
                    selected = enabled.ToDictionary(entry => entry.MissionId, entry => entry.ContentRevision);
                }
                var snapshot = new MissionContentLoader().Load(unit.MissionContent, selected);
                Report = new MissionContentValidator().Validate(snapshot, unit);
                foreach (var entry in MissionDefinitionCatalog.CreateDefinitions(snapshot, Report))
                    Missions[entry.Key] = entry.Value;
                foreach (var entry in MissionDefinitionCatalog.CreateRewardDefinitions(snapshot, Report))
                    Rewards[entry.Key] = entry.Value;
                foreach (var entry in MissionDefinitionCatalog.CreateRewardPackages(snapshot, Report))
                    RewardPackages[entry.Key] = entry.Value;
                foreach (var definition in snapshot.Definitions.Values)
                {
                    Abandonment[definition.MissionId] = definition.AbandonmentPolicy;
                    Prerequisites[definition.MissionId] = definition.Prerequisites;
                    Areas[definition.MissionId] = definition.Areas;
                    SpawnGroups[definition.MissionId] = definition.SpawnGroups;
                    Scenarios[definition.MissionId] = definition.Scenarios;
                }
                if (unit.MissionContent is IMigratedMissionContentRepository content)
                {
                    var publicSpawnIds = (unit.Spawnpools
                        ?? throw new InvalidOperationException("Migrated mission validation requires the World spawn repository."))
                        .Get().Select(entry => entry.Id).ToHashSet();
                    foreach (var binding in content.GetSceneBindings().Where(binding =>
                        selected.TryGetValue(binding.MissionId, out var revision) && revision == binding.ContentRevision))
                    {
                        var document = JsonSerializer.Deserialize<MissionSceneDefinition>(binding.Bindings, MissionContentCodec.Options)
                            ?? throw new InvalidOperationException($"Mission {binding.MissionId} has no binding document.");
                        if (document.Script != binding.ScriptKey || document.StateVersion != binding.StateVersion)
                            throw new InvalidOperationException($"Mission {binding.MissionId} has inconsistent script/version bindings.");
                        MissionSceneValidation.Validate(binding.MissionId, binding.ContentRevision, document,
                            Missions[binding.MissionId].Objectives.Keys);
                        foreach (var actor in document.Actors.Values.Where(actor =>
                            actor.Kind == Rasa.Missions.Scenes.SceneActorKind.PublicSpawn))
                            if (!publicSpawnIds.Contains(actor.TemplateId))
                                throw new MissionRuleException($"Mission {binding.MissionId}: migrated public spawn {actor.TemplateId} does not exist.");
                        foreach (var credit in document.Credit.Where(entry => entry.Value.Mode != MissionCreditMode.Personal))
                            if (Missions[binding.MissionId].Objectives[credit.Key].GetExecutableTransitionsOrLegacyDefault()
                                .Any(transition => transition.ProgressRule?.Kind is not
                                    (Data.MissionProgressEventKind.CreatureKilled or Data.MissionProgressEventKind.ScenarioEvent)))
                                throw new MissionRuleException($"Mission {binding.MissionId}: objective {credit.Key} cannot share personal actions.");
                        SceneBindings.Add(binding.MissionId, document);
                        Missions[binding.MissionId] = Missions[binding.MissionId].WithPolicies(document.Credit, document.Requirement,
                            document.TurnInRequirement, document.ObjectiveRequirements);
                    }
                    foreach (var mission in snapshot.Definitions.Values.Where(definition => definition.Scenarios.Count > 0))
                        if (!SceneBindings.TryGetValue(mission.MissionId, out var scene) || string.IsNullOrWhiteSpace(scene.Script))
                            throw new MissionRuleException($"Mission {mission.MissionId} requires a migrated scene script binding.");
                    foreach (var entry in content.GetExperiences())
                    {
                        var experience = JsonSerializer.Deserialize<MissionExperienceDefinition>(entry.Bindings, MissionContentCodec.Options)
                            ?? throw new InvalidOperationException($"Experience {entry.ExperienceKey} has no binding definition.");
                        if (experience.Key != entry.ExperienceKey || experience.MapContextId != entry.MapContextId ||
                            !experience.PrivatePerCharacter || string.IsNullOrWhiteSpace(experience.Revision))
                            throw new InvalidOperationException($"Experience {entry.ExperienceKey} has inconsistent migrated bindings.");
                        MissionSceneValidation.Validate(0, experience.Revision, experience.Scene, Array.Empty<uint>());
                        foreach (var trigger in experience.MissionTriggers)
                            if (!Missions.ContainsKey(trigger.MissionId) ||
                                !experience.Scene.Sequences.ContainsKey(trigger.SequenceId) ||
                                trigger.Event is not ("Accepted" or "Rewarded" or "Completeable" or "Departing"))
                                throw new MissionRuleException($"Experience {experience.Key}: invalid mission trigger {trigger.MissionId}/{trigger.Event}.");
                        foreach (var actor in experience.Scene.Actors.Values.Where(actor =>
                            actor.Kind == Rasa.Missions.Scenes.SceneActorKind.PublicSpawn))
                            if (!publicSpawnIds.Contains(actor.TemplateId))
                                throw new MissionRuleException($"Experience {experience.Key}: migrated public spawn {actor.TemplateId} does not exist.");
                        foreach (var actor in experience.Scene.Actors.Values.Where(actor => actor.Conversation != null))
                            if (!Missions.TryGetValue(actor.Conversation.MissionId, out var mission) ||
                                !mission.Objectives.ContainsKey(actor.Conversation.ObjectiveId))
                                throw new MissionRuleException($"Experience {experience.Key}: object conversation has an unknown mission objective.");
                        Experiences.Add(experience);
                    }
                }
            }
            Runtime = new MissionRuntime(Missions.Values);
            var requirements = new Game.Missions.Integration.MissionRequirementService();
            foreach (var mission in Missions.Values.Where(mission => mission.IsOperational))
                requirements.Validate(mission);
            return Report;
        }
    }
}
