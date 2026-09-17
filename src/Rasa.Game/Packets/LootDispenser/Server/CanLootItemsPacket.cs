using System.Collections.Generic;
using System.Linq;

namespace Rasa.Packets.LootDispenser.Server
{
    using Data;
    using Memory;
    using Structures;

    public class CanLootItemsPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.CanLootItems;

        public bool CanLootItems { get; set; }
        public List<LootItem> LootItems = new List<LootItem>();
        
        public CanLootItemsPacket(bool canLootItems, List<LootItem> lootItems)
        {
            CanLootItems = canLootItems;
            LootItems = lootItems.Select(item => new LootItem(item)).ToList();
        }
        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(2);
            pw.WriteBool(CanLootItems);
            if (CanLootItems)
            {
                pw.WriteDictionary(LootItems.Count);
                foreach (var item in LootItems)
                {
                    pw.WriteULong(item.EntityId);
                    pw.WriteTuple(1);
                    pw.WriteBool(CanLootItems);
                }
            }
            else
                pw.WriteNoneStruct();
        }
    }
}
