using JetBrains.Annotations;

namespace Rasa.Repositories.Char.CharacterStartingExperience
{
    using Structures.Char;

    public interface ICharacterStartingExperienceRepository
    {
        [CanBeNull]
        CharacterStartingExperienceEntry Get(uint characterId);
        void Add(CharacterStartingExperienceEntry entry);
        void SetState(uint characterId, CharacterStartingExperienceState state);
    }
}
