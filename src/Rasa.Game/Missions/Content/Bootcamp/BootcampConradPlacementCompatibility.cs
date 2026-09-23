using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Rasa.Game.Missions.Content.Bootcamp
{
    using Data;
    using global::Rasa.Missions.Scenes;
    using Structures;

    internal static class BootcampConradPlacementCompatibility
    {
        private const uint MissionId = 1995;
        private const string Revision = "deployment_11";
        private const string Role = "bootcamp-conrad-corpse";
        private static readonly ScenePosition LegacyPosition = new(-102.4f, 86.20677f, 66.8f);
        private static readonly Vector3 LegacyMarker = new(-102.4f, 86.10937f, 66.8f);
        private static readonly ScenePosition CorpsePosition = new(-99, 86.41823f, 74);
        private static readonly Vector3 GroundPosition = new(-99, 86.32086f, 74);

        internal static void Apply(IDictionary<uint, Mission> missions,
            IReadOnlyDictionary<uint, MissionSceneDocument> scenes,
            IEnumerable<MissionExperienceDocument> experiences)
        {
            if (!missions.TryGetValue(MissionId, out var mission) || !mission.IsOperational ||
                mission.ContentRevision != Revision ||
                !scenes.TryGetValue(MissionId, out var scene) ||
                scene.Script != "bootcamp.reinforcements" || scene.StateVersion != 1 ||
                !scene.Actors.TryGetValue(Role, out var actor) || !IsLegacyActor(actor) ||
                !mission.Objectives.TryGetValue(3, out var objective))
                return;

            var experience = experiences.SingleOrDefault(candidate =>
                candidate.Key == "bootcamp" && candidate.Revision == Revision &&
                candidate.MapContextId == 1985 && candidate.PrivatePerCharacter &&
                candidate.Scene.Script == "bootcamp.experience" && candidate.Scene.StateVersion == 1);
            if (experience == null ||
                !experience.Scene.Actors.TryGetValue(Role, out var sharedActor) || !IsLegacyActor(sharedActor))
                return;
            var marker = objective.Indicators.SingleOrDefault(candidate => candidate.IndicatorId == 436);
            if (marker == null || marker.Position != LegacyMarker)
                return;

            // Keep published bindings and saved scene identities immutable. Only the loaded
            // legacy projection moves out of the trench wall, including its experience-owned actor.
            var correctedObjective = new MissionObjectiveDefinition(
                objective.ObjectiveId, objective.ClientNameTextId, objective.ClientBodyTextId,
                objective.ClientCounterTextIds, objective.Ordinal, objective.InitialState, objective.IsRequired,
                objective.Counters, objective.ItemCounters, objective.Conversations,
                objective.RevealedObjectiveIds, objective.ActivatedObjectiveIds,
                objective.Indicators.Select(indicator => indicator.IndicatorId == marker.IndicatorId
                    ? new MissionIndicator
                    {
                        IndicatorId = indicator.IndicatorId, Position = GroundPosition,
                        Radius = indicator.Radius, Show3DEffect = indicator.Show3DEffect
                    } : indicator),
                objective.ProgressRule, objective.ExecutableTransitions, objective.Requirement, objective.CreditPolicy);
            missions[MissionId] = new Mission(
                mission.MissionId, mission.Name, mission.ClientNameTextId, mission.MissionGiver, mission.MissionReciver,
                mission.Level, mission.GroupType, mission.CategoryId, mission.Shareable, mission.RadioCompletable,
                mission.Objectives.Values.Select(candidate => candidate.ObjectiveId == objective.ObjectiveId
                    ? correctedObjective : candidate),
                mission.IsOperational, mission.OperationalDiagnostic, mission.ContentRevision,
                mission.Requirement, mission.TurnInRequirement, mission.ObjectiveRequirements);
            scene.Actors[Role] = actor with { Position = CorpsePosition };
            experience.Scene.Actors[Role] = sharedActor with { Position = CorpsePosition };
            Logger.WriteLog(LogType.Initialize,
                "Applied Bootcamp Conrad placement compatibility at (-99, 86.41823, 74); published content and saved progress are unchanged.");
        }

        private static bool IsLegacyActor(SceneActorDefinition actor) =>
            actor.Role == Role && actor.SharedKey == Role && actor.Kind == SceneActorKind.Object &&
            actor.TemplateId == 24990 && actor.Position == LegacyPosition && actor.Orientation == 0 &&
            actor.InitialObjectState == (uint)UseObjectState.TdStateClosed;
    }
}
