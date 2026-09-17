using System.Collections.Generic;
using System.Linq;

namespace Rasa.Packets.MapChannel.Server
{
    using Data;
    using Memory;
    using Structures;

    public class SkillsPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.Skills;

        private readonly (SkillId Id, int Rank)[] _skills;
        
        public SkillsPacket(Dictionary<SkillId, SkillsData> skillsData)
        {
            _skills = skillsData.Values.Select(skill => (skill.SkillId, skill.SkillLevel)).ToArray();
        }

        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(1);
            pw.WriteList(_skills.Length);
            foreach(var entry in _skills)
            {
                pw.WriteTuple(2);
                pw.WriteInt((int)entry.Id);
                pw.WriteInt(entry.Rank);
            }
        }
    }
}
