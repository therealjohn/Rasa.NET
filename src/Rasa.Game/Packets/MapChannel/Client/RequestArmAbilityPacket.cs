namespace Rasa.Packets.MapChannel.Client
{
    using Data;
    using Memory;

    public class RequestArmAbilityPacket : ClientPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.RequestArmAbility;

        public int AbilityDrawerSlot { get; set; }

        public override void Read(PythonReader pr)
        {
            if (pr.ReadTuple() != 1)
                throw new System.IO.InvalidDataException("Drawer selection requires one slot.");
            AbilityDrawerSlot = pr.ReadInt();
        }
    }
}
