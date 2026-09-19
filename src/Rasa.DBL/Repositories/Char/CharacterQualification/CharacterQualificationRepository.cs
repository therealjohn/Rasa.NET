using System.Collections.Generic;
using System.Linq;

namespace Rasa.Repositories.Char.CharacterQualification
{
    using Context.Char;
    using Structures.Char;

    public class CharacterQualificationRepository : ICharacterQualificationRepository
    {
        private readonly CharContext _charContext;

        public CharacterQualificationRepository(CharContext charContext)
        {
            _charContext = charContext;
        }

        public IReadOnlyList<CharacterQualificationEntry> Get(uint characterId)
        {
            var query = _charContext.CreateNoTrackingQuery(_charContext.CharacterQualificationEntries);
            return query.Where(entry => entry.CharacterId == characterId).ToList();
        }

        public bool HasQualification(uint characterId, CharacterQualificationKey qualificationKey) =>
            _charContext.CharacterQualificationEntries.Any(entry =>
                entry.CharacterId == characterId &&
                entry.QualificationKey == qualificationKey);

        public void Add(CharacterQualificationEntry entry)
        {
            _charContext.CharacterQualificationEntries.Add(entry);
            _charContext.SaveChanges();
        }

        public void Remove(uint characterId, CharacterQualificationKey qualificationKey)
        {
            var entry = _charContext.CharacterQualificationEntries.SingleOrDefault(candidate =>
                candidate.CharacterId == characterId &&
                candidate.QualificationKey == qualificationKey);
            if (entry == null)
                return;

            _charContext.CharacterQualificationEntries.Remove(entry);
            _charContext.SaveChanges();
        }
    }
}
