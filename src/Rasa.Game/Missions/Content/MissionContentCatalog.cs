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
        internal Dictionary<uint, MissionSceneDocument> SceneBindings { get; } = new();
        internal List<MissionExperienceDocument> Experiences { get; } = new();
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
                            "legacy npc_mission rows stay inactive: source-only objectives and rewards require an explicit validated release");
                foreach (var recovered in MissionDefinitionCatalog.CreateRecoveredInactiveDefinitions())
                {
                    Missions.TryGetValue(recovered.Key, out var world);
                    Missions[recovered.Key] = recovered.Value.WithWorldMetadata(world);
                }
                Report = new MissionValidationReport(Array.Empty<MissionValidationDiagnostic>(), Array.Empty<uint>());
            }
            else
            {
                MissionActiveReleaseEntry release = null;
                IReadOnlyDictionary<uint, string> selected = null;
                if (unit.MissionContent is IReleasedMissionContentRepository released)
                {
                    release = released.GetActiveRelease() ?? throw new InvalidOperationException(
                        "No active mission release. Validate and publish content with Rasa.MissionTool before startup.");
                    selected = released.GetReleaseMembers(release.ReleaseName).Where(member => member.Enabled)
                        .ToDictionary(member => member.MissionId, member => member.ContentRevision);
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
                if (unit.MissionContent is IReleasedMissionContentRepository content)
                {
                    foreach (var binding in content.GetSceneBindings().Where(binding =>
                        selected.TryGetValue(binding.MissionId, out var revision) && revision == binding.ContentRevision))
                    {
                        var document = JsonSerializer.Deserialize<MissionSceneDocument>(binding.Bindings, MissionPackCodec.Options)
                            ?? throw new InvalidOperationException($"Mission {binding.MissionId} has no binding document.");
                        if (document.Script != binding.ScriptKey || document.StateVersion != binding.StateVersion)
                            throw new InvalidOperationException($"Mission {binding.MissionId} has inconsistent script/version bindings.");
                        SceneBindings.Add(binding.MissionId, document);
                        Missions[binding.MissionId] = Missions[binding.MissionId].WithPolicies(document.Credit, document.Requirement,
                            document.TurnInRequirement, document.ObjectiveRequirements);
                    }
                    Experiences.AddRange(content.GetExperiences(release.ReleaseName).Select(entry =>
                        JsonSerializer.Deserialize<MissionExperienceDocument>(entry.Bindings, MissionPackCodec.Options)
                        ?? throw new InvalidOperationException($"Experience {entry.ExperienceKey} has no binding document.")));
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
