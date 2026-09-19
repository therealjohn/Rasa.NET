using System.Collections.Generic;
using System.Linq;

namespace Rasa.Repositories.Char.CharacterMissionScenario
{
    using Context.Char;
    using Structures.Char;

    public class CharacterMissionScenarioRepository : ICharacterMissionScenarioRepository
    {
        private readonly CharContext _charContext;

        public CharacterMissionScenarioRepository(CharContext charContext)
        {
            _charContext = charContext;
        }

        public IReadOnlyList<CharacterMissionScenarioStepEntry> Get(uint characterId, uint missionId)
        {
            var query = _charContext.CreateNoTrackingQuery(_charContext.CharacterMissionScenarioStepEntries);
            return query
                .Where(entry => entry.CharacterId == characterId && entry.MissionId == missionId)
                .OrderBy(entry => entry.StepKey)
                .ToList();
        }

        public bool HasStep(uint characterId, uint missionId, string stepKey) =>
            _charContext.CharacterMissionScenarioStepEntries.Any(entry =>
                entry.CharacterId == characterId &&
                entry.MissionId == missionId &&
                entry.StepKey == stepKey);

        public void Add(CharacterMissionScenarioStepEntry entry)
        {
            _charContext.CharacterMissionScenarioStepEntries.Add(entry);
            _charContext.SaveChanges();
        }
    }
}
