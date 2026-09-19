using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rasa.Structures.Char
{
    public enum CharacterQualificationKey : byte
    {
        BootcampComplete = 1
    }

    [Table(TableName)]
    public class CharacterQualificationEntry
    {
        public const string TableName = "character_qualification";

        public CharacterQualificationEntry()
        {
        }

        public CharacterQualificationEntry(uint characterId, CharacterQualificationKey qualificationKey)
        {
            CharacterId = characterId;
            QualificationKey = qualificationKey;
        }

        [Column("character_id")]
        [Required]
        public uint CharacterId { get; set; }

        [Column("qualification_key")]
        [Required]
        public CharacterQualificationKey QualificationKey { get; set; }

        public CharacterEntry Character { get; set; }
    }
}
