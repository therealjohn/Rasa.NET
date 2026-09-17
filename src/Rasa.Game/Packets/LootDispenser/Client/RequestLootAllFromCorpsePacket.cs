namespace Rasa.Packets.LootDispenser.Client
{
    using Data;
    using Memory;

    public class RequestLootAllFromCorpsePacket : ClientPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.RequestLootAllFromCorpse;

        public ulong EntityId { get; set; }
        public bool AutoLootOnly { get; set; }

        public override void Read(PythonReader pr)
        {
            if (pr.ReadTuple() != 2)
                throw new System.IO.InvalidDataException("RequestLootAllFromCorpse requires an entity ID and auto-loot flag.");
            EntityId = pr.ReadULong();
            AutoLootOnly = pr.ReadBool();
        }
    }
}
