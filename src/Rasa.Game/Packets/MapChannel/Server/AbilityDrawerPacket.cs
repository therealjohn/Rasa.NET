using System.Collections.Generic;
using System.Linq;

namespace Rasa.Packets.MapChannel.Server
{
    using Data;
    using Memory;
    using Structures;

    public class AbilityDrawerPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.AbilityDrawer;

        private readonly (int Slot, int Ability, uint Rank)[] _abilities;
        public IReadOnlyDictionary<int, AbilityDrawerData> Abilities => _abilities.ToDictionary(
            entry => entry.Slot, entry => new AbilityDrawerData(entry.Slot, entry.Ability, entry.Rank));

        public AbilityDrawerPacket(Dictionary<int, AbilityDrawerData> abilities)
        {
            _abilities = abilities.Select(entry => (entry.Key, entry.Value.AbilityId, entry.Value.AbilityLevel)).ToArray();
        }

        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(1);
            pw.WriteDictionary(_abilities.Length);
            foreach (var entry in _abilities)
            {
                pw.WriteInt(entry.Slot); // slotId
                pw.WriteTuple(3);
                pw.WriteInt(entry.Ability);     // abilityId
                pw.WriteUInt(entry.Rank);  // abilityLevel
                pw.WriteNoneStruct();                   // itemId ( unknown purpose ) <<= c++  krssrb =>> if you drag 'n' drop usable iteme from inventory
            }
        }
    }
}
