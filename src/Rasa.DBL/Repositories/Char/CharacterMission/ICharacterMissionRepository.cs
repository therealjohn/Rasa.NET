using System.Collections.Generic;

namespace Rasa.Repositories.Char.CharacterMission
{
    using Structures.Char;

    public interface ICharacterMissionRepository
    {
        IReadOnlyList<CharacterMissionEntry> Get(uint characterId);
        CharacterMissionEntry Get(uint characterId, uint missionId);
        void Add(CharacterMissionEntry entry);
        void SetCompletable(uint characterId, uint missionId, bool value);
        void SetState(uint characterId, uint missionId, uint state);
        void Remove(uint characterId, uint missionId);
    }
}
