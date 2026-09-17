namespace Rasa.Packets.MapChannel.Client
{
    using Data;
    using Memory;

    public class LevelSkillsPacket : ClientPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.LevelSkills;
        
        public int ListLenght { get; set; }
        public int[] SkillIds { get; set; }
        public int[] SkillLevels { get; set; }

        public override void Read(PythonReader pr)
        {
            if (pr.ReadTuple() != 1)
                throw new System.IO.InvalidDataException("Training requires one skill list.");
            ListLenght = pr.ReadList();
            if (ListLenght < 1 || ListLenght > 73)
                throw new System.IO.InvalidDataException("Training exceeds the 73-entry skill catalogue.");
            SkillIds = new int[ListLenght];
            SkillLevels = new int[ListLenght];
            for (var i = 0; i < ListLenght; i++)
            {
                if (pr.ReadTuple() != 2)
                    throw new System.IO.InvalidDataException("Training entries require a skill ID and rank.");
                SkillIds[i] = pr.ReadInt();
                SkillLevels[i] = pr.ReadInt();
            }
        }
    }
}
