using System;
using System.Collections.Generic;
using System.Linq;
using Rasa.Missions.Runtime;
using Rasa.Missions.Scenes;
using Rasa.Structures.World;

namespace Rasa.Game.Missions.Content
{
    public sealed class MissionPackDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public string Release { get; set; } = "";
        public bool Enabled { get; set; }
        public bool Synthetic { get; set; }
        public MissionContentDefinitionEntry Definition { get; set; } = new();
        public List<MissionPrerequisiteEntry> Prerequisites { get; set; } = new();
        public List<MissionObjectiveDefinitionEntry> Objectives { get; set; } = new();
        public List<MissionObjectiveTransitionEntry> Transitions { get; set; } = new();
        public List<MissionTriggerEntry> Triggers { get; set; } = new();
        public List<MissionActionEntry> Actions { get; set; } = new();
        public List<MissionRewardDefinitionEntry> Rewards { get; set; } = new();
        public List<MissionRewardItemEntry> RewardItems { get; set; } = new();
        public List<MissionIndicatorEntry> Indicators { get; set; } = new();
        public List<MissionAreaEntry> Areas { get; set; } = new();
        public List<MissionSpawnGroupEntry> SpawnGroups { get; set; } = new();
        public List<MissionSpawnEntry> Spawns { get; set; } = new();
        public List<MissionScenarioEntry> Scenarios { get; set; } = new();
        public List<MissionScenarioStepEntry> ScenarioSteps { get; set; } = new();
        public List<MissionEvidenceEntry> Evidence { get; set; } = new();
        public MissionSceneDocument Scene { get; set; }
        public MissionExperienceDocument Experience { get; set; }

        internal IEnumerable<object> Rows() => Experience != null ? Array.Empty<object>() : new object[] { Definition }
            .Concat(Prerequisites).Concat(Objectives).Concat(Transitions).Concat(Triggers)
            .Concat(Actions).Concat(Rewards).Concat(RewardItems).Concat(Indicators).Concat(Areas)
            .Concat(SpawnGroups).Concat(Spawns).Concat(Scenarios).Concat(ScenarioSteps).Concat(Evidence);
    }

    public sealed class MissionSceneDocument
    {
        public string Script { get; set; }
        public int StateVersion { get; set; } = 1;
        public string Recovery { get; set; } = "RestoreCheckpoint";
        public Dictionary<string, SceneActorDefinition> Actors { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, SceneRoute> Routes { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<uint, SceneSequenceDocument> Sequences { get; set; } = new();
        public Dictionary<string, uint> Names { get; set; } = new();
        public Dictionary<uint, MissionCreditPolicy> Credit { get; set; } = new();
        public MissionRequirement Requirement { get; set; }
        public MissionRequirement TurnInRequirement { get; set; }
        public Dictionary<uint, MissionRequirement> ObjectiveRequirements { get; set; } = new();
        public World.PublicEncounterBinding PublicEncounter { get; set; }

        public SceneBindings Bindings(string revision) => new(revision, Actors, Routes,
            Sequences.ToDictionary(entry => entry.Key, entry => new SceneSequence(
                entry.Value.World, entry.Value.Character, entry.Value.Signals, entry.Value.Timers)), Names);
    }

    public sealed class MissionExperienceDocument
    {
        public string Key { get; set; } = "";
        public string Revision { get; set; } = "";
        public uint MapContextId { get; set; }
        public bool PrivatePerCharacter { get; set; }
        public MissionSceneDocument Scene { get; set; } = new();
        public List<ExperienceMissionTrigger> MissionTriggers { get; set; } = new();
        public Dictionary<uint, global::Rasa.Missions.Definitions.ActorGameplayPolicy> ActorPolicies { get; set; } = new();
    }
    public sealed record ExperienceMissionTrigger(uint MissionId, string Event, uint SequenceId);

    public sealed class SceneSequenceDocument
    {
        public List<WorldIntent> World { get; set; } = new();
        public List<CharacterIntent> Character { get; set; } = new();
        public List<SceneMissionSignal> Signals { get; set; } = new();
        public List<SequenceTimer> Timers { get; set; } = new();
    }

    public sealed class ClientBindingManifest
    {
        public string Source { get; set; } = "";
        public Dictionary<uint, ClientMissionBinding> Missions { get; set; } = new();
    }
    public sealed class ClientMissionBinding
    {
        public uint NameTextId { get; set; }
        public Dictionary<uint, ClientObjectiveBinding> Objectives { get; set; } = new();
    }
    public sealed record ClientObjectiveBinding(uint NameTextId, uint BodyTextId);
}
