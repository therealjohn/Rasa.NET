namespace Rasa.Packets.LootDispenser.Client
{
    using Data;
    using Memory;

    public class RequestCorpseLootingPacket : ClientPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.RequestCorpseLooting;

        public ulong EntityId { get; set; }

        public override void Read(PythonReader pr)
        {
            if (pr.ReadTuple() != 1)
                throw new System.IO.InvalidDataException("RequestCorpseLooting requires one entity ID.");
            EntityId = pr.ReadULong();
        }
    }
}
