using System.Collections.Generic;

namespace Rasa.Repositories.Char.CharacterQualification
{
    using Structures.Char;

    public interface ICharacterQualificationRepository
    {
        IReadOnlyList<CharacterQualificationEntry> Get(uint characterId);
        bool HasQualification(uint characterId, CharacterQualificationKey qualificationKey);
        void Add(CharacterQualificationEntry entry);
        void Remove(uint characterId, CharacterQualificationKey qualificationKey);
    }
}
