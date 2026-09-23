using System;
using System.Collections.Generic;
using System.Linq;
using Rasa.Missions.Runtime;
using Rasa.Repositories.Char;
using Rasa.Structures;
using Rasa.Game.Missions.Persistence;

namespace Rasa.Game.Missions.Integration
{
    internal sealed class MissionRequirementService
    {
        private readonly MissionRequirementEvaluator _evaluator;
        private readonly MissionRequirementFactsAdapter _facts;
        internal MissionRequirementService(MissionRequirementEvaluator evaluator = null,
            MissionRequirementFactsAdapter facts = null)
        { _evaluator = evaluator ?? new MissionRequirementEvaluator(); _facts = facts ?? new MissionRequirementFactsAdapter(); }

        internal bool Evaluate(Manifestation player, MissionRequirement requirement, ICharUnitOfWork unit = null) =>
            requirement == null || _evaluator.Evaluate(requirement, _facts.Read(player, _evaluator.RequiredFacts(requirement), unit));

        internal void Validate(Mission mission) => Validate(mission.MissionId, mission.Objectives.Keys,
            mission.Requirement, mission.TurnInRequirement, mission.ObjectiveRequirements);

        internal void Validate(uint missionId, IEnumerable<uint> objectiveIds, MissionRequirement admission,
            MissionRequirement turnIn, IReadOnlyDictionary<uint, MissionRequirement> objectiveRequirements)
        {
            if (objectiveRequirements == null)
                throw new MissionRuleException($"Mission {missionId} has no objective requirement bindings.");
            var knownObjectives = objectiveIds.ToHashSet();
            foreach (var pair in objectiveRequirements)
                if (!knownObjectives.Contains(pair.Key))
                    throw new MissionRuleException($"Mission {missionId} binds a requirement to missing objective {pair.Key}.");
            foreach (var requirement in objectiveRequirements.Values.Append(admission).Append(turnIn))
                foreach (var fact in _evaluator.RequiredFacts(requirement))
                    if (!MissionRequirementFactsAdapter.Supported.Contains(fact))
                        throw new MissionRuleException($"Mission {missionId} requires unsupported Game fact {fact}.");
        }
    }
}
