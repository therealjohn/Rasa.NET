using System.Collections.Generic;
using System.Linq;

namespace Rasa.Packets.LootDispenser.Server
{
    using Data;
    using Memory;
    using Structures;

    public class LootInfoPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.LootInfo;

        public List<LootItem> LootItems = new List<LootItem>();
        
        public LootInfoPacket(List<LootItem> lootItems)
        {
            LootItems = lootItems.Select(item => new LootItem(item)).ToList();
        }
        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(1);
            pw.WriteDictionary(LootItems.Count);
            foreach(var item in LootItems)
            {
                pw.WriteULong(item.EntityId);
                pw.WriteTuple(4);
                pw.WriteUInt(item.ItemTemplateId);
                pw.WriteUInt(item.ItemQuantity);
                pw.WriteULong(item.ActorId);
                if (item.PartyId > 0)
                    pw.WriteUInt(item.PartyId);
                else
                    pw.WriteNoneStruct();
            }
        }
    }
}
