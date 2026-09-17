namespace Rasa.Packets.MapChannel.Client
{
    using Data;
    using Memory;

    public class RequestSwapAbilitySlotsPacket : ClientPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.RequestSwapAbilitySlots;
        
        public int FromSlot { get; set; }
        public int ToSlot { get; set; }

        public override void Read(PythonReader pr)
        {
            if (pr.ReadTuple() != 2)
                throw new System.IO.InvalidDataException("Drawer swap requires two slots.");
            var source = pr.ReadLong();
            if (source < int.MinValue || source > int.MaxValue)
                throw new System.IO.InvalidDataException("Drawer source slot exceeds the integer range.");
            FromSlot = (int)source;
            ToSlot = pr.ReadInt();
        }
    }
}
