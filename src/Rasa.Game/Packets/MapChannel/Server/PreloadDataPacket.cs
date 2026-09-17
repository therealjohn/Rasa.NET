using System.Collections.Generic;
using System.Linq;

namespace Rasa.Packets.MapChannel.Server
{
    using Data;
    using Memory;
    using Structures;

    public class PreloadDataPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.PreloadData;

        public ulong WeaponId { get; }
        private readonly (int Ability, uint Rank)[] _abilities;
        
        public PreloadDataPacket(ulong weaponId, Dictionary<int, AbilityDrawerData> abilitiesList)
        {
            WeaponId = weaponId;
            _abilities = abilitiesList.Values.Select(ability => (ability.AbilityId, ability.AbilityLevel)).ToArray();
        }

        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(2);
            pw.WriteULong(WeaponId);
            pw.WriteList(_abilities.Length);
            foreach (var ability in _abilities)
            {
                pw.WriteTuple(2);
                pw.WriteInt(ability.Ability);
                pw.WriteUInt(ability.Rank);
            }
        }
    }
}
