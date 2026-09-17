using System.Collections.Generic;
using System.Linq;

namespace Rasa.Repositories.Char.CharacterMission
{
    using Context.Char;
    using Structures.Char;
    public class CharacterMissionRepository : ICharacterMissionRepository
    {
        private readonly CharContext _charContext;

        public CharacterMissionRepository(CharContext charContext)
        {
            _charContext = charContext;
        }

        public IReadOnlyList<CharacterMissionEntry> Get(uint characterId)
        {
            var query = _charContext.CreateNoTrackingQuery(_charContext.CharacterMissionEntries);
            return query.Where(entry => entry.CharacterId == characterId).ToList();
        }

        public CharacterMissionEntry Get(uint characterId, uint missionId)
        {
            return _charContext.CharacterMissionEntries.SingleOrDefault(
                entry => entry.CharacterId == characterId && entry.MissionId == missionId);
        }

        public void Add(CharacterMissionEntry entry)
        {
            _charContext.CharacterMissionEntries.Add(entry);
            _charContext.SaveChanges();
        }

        public void SetCompletable(uint characterId, uint missionId, bool value)
        {
            var mission = Get(characterId, missionId);
            mission.Completeable = value;
            _charContext.SaveChanges();
        }

        public void SetState(uint characterId, uint missionId, uint state)
        {
            var mission = Get(characterId, missionId);
            mission.MissionState = state;
            _charContext.SaveChanges();
        }

        public void Remove(uint characterId, uint missionId)
        {
            var mission = Get(characterId, missionId);
            if (mission == null)
                return;

            _charContext.CharacterMissionEntries.Remove(mission);
            _charContext.SaveChanges();
        }
    }
}
